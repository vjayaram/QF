using Azure.Messaging.ServiceBus;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Qf.BuildingBlocks.Infrastructure.Persistence;

namespace Qf.BuildingBlocks.Infrastructure.Messaging;

public sealed class OutboxRelayOptions
{
    public required string Topic { get; init; }

    public TimeSpan PollInterval { get; init; } = TimeSpan.FromSeconds(2);

    public int BatchSize { get; init; } = 50;

    public int MaxAttempts { get; init; } = 20;
}

/// <summary>
/// Relays committed outbox rows to a Service Bus topic. Safe with multiple replicas: the topic has
/// duplicate detection on, and MessageId = event id, so a row sent twice is delivered once.
/// </summary>
public sealed partial class OutboxRelay<TContext>(
    IServiceScopeFactory scopes,
    ServiceBusClient client,
    OutboxRelayOptions options,
    TimeProvider clock,
    ILogger<OutboxRelay<TContext>> logger) : BackgroundService
    where TContext : QfDbContext
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var sender = client.CreateSender(options.Topic);
        await using (sender.ConfigureAwait(false))
        {
            using var timer = new PeriodicTimer(options.PollInterval, clock);
            while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false))
            {
                try
                {
                    await RelayBatchAsync(sender, stoppingToken).ConfigureAwait(false);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    LogRelayFailed(ex);
                }
            }
        }
    }

    private async Task RelayBatchAsync(ServiceBusSender sender, CancellationToken cancellationToken)
    {
        using var scope = scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TContext>();
        var batch = await db.OutboxMessages
            .Where(m => m.ProcessedAt == null && m.Attempts < options.MaxAttempts)
            .OrderBy(m => m.OccurredAt)
            .Take(options.BatchSize)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        foreach (var message in batch)
        {
            try
            {
                var sbMessage = new ServiceBusMessage(BinaryData.FromString(message.Payload))
                {
                    MessageId = message.Id.ToString(),
                    Subject = message.Subject,
                    ContentType = "application/json",
                };
                sbMessage.ApplicationProperties["tenantId"] = message.TenantId.ToString();
                await sender.SendMessageAsync(sbMessage, cancellationToken).ConfigureAwait(false);
                message.ProcessedAt = clock.GetUtcNow();
            }
            catch (ServiceBusException ex)
            {
                message.Attempts++;
                message.LastError = ex.Message.Length > 2000 ? ex.Message[..2000] : ex.Message;
                LogSendFailed(ex, message.Id, message.Attempts);
            }
        }

        if (batch.Count > 0)
        {
            await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "Outbox relay iteration failed")]
    private partial void LogRelayFailed(Exception exception);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Outbox message {MessageId} failed to send (attempt {Attempt})")]
    private partial void LogSendFailed(Exception exception, Guid messageId, int attempt);
}
