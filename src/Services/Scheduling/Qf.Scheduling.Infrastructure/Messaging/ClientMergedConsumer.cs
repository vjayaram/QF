using System.Text.Json;
using Azure.Messaging.ServiceBus;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Qf.BuildingBlocks.Application;
using Qf.Contracts;
using Qf.Contracts.ClientRegistry;
using Qf.Scheduling.Application.Commands;
using Qf.Scheduling.Infrastructure.Persistence;

namespace Qf.Scheduling.Infrastructure.Messaging;

/// <summary>
/// Conformist downstream of Client Registry: when two clients are merged, appointments follow the survivor.
/// Peek-lock with manual completion; failures are abandoned and retried, then dead-lettered after
/// the subscription's max delivery count (alerted on in Azure Monitor).
/// </summary>
public sealed partial class ClientMergedConsumer(
    ServiceBusClient client,
    IServiceScopeFactory scopes,
    ILogger<ClientMergedConsumer> logger) : BackgroundService
{
    public const string Subscription = "scheduling";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var processor = client.CreateProcessor(Topics.ClientRegistryEvents, Subscription, new ServiceBusProcessorOptions
        {
            AutoCompleteMessages = false,
            MaxConcurrentCalls = 4,
            PrefetchCount = 0,
        });
        await using (processor.ConfigureAwait(false))
        {
            processor.ProcessMessageAsync += HandleAsync;
            processor.ProcessErrorAsync += args =>
            {
                LogProcessorError(args.Exception, args.ErrorSource.ToString());
                return Task.CompletedTask;
            };

            await processor.StartProcessingAsync(stoppingToken).ConfigureAwait(false);
            try
            {
                await Task.Delay(Timeout.Infinite, stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // shutting down
            }

            await processor.StopProcessingAsync(CancellationToken.None).ConfigureAwait(false);
        }
    }

    private async Task HandleAsync(ProcessMessageEventArgs args)
    {
        if (args.Message.Subject != Subjects.ClientMerged)
        {
            await args.CompleteMessageAsync(args.Message, args.CancellationToken).ConfigureAwait(false);
            return;
        }

        var evt = args.Message.Body.ToObjectFromJson<ClientMergedV1>(JsonOptions);
        if (evt is null || evt.TenantId == Guid.Empty)
        {
            await args.DeadLetterMessageAsync(args.Message, "InvalidPayload", "ClientMerged.v1 body could not be read.", args.CancellationToken).ConfigureAwait(false);
            return;
        }

        using var scope = scopes.CreateScope();
        scope.ServiceProvider.GetRequiredService<TenantScope>().Set(evt.TenantId, "svc:scheduling-consumer");
        var db = scope.ServiceProvider.GetRequiredService<SchedulingDbContext>();
        var handler = scope.ServiceProvider.GetRequiredService<ICommandHandler<ReassignMergedClientCommand, int>>();

        var strategy = db.Database.CreateExecutionStrategy();
        var result = await strategy.ExecuteAsync(
            () => handler.HandleAsync(new ReassignMergedClientCommand(evt.EventId, evt.SourceClientId, evt.SurvivorClientId), args.CancellationToken)).ConfigureAwait(false);

        if (result.IsFailure)
        {
            await args.DeadLetterMessageAsync(args.Message, result.Errors[0].Code, result.Errors[0].Message, args.CancellationToken).ConfigureAwait(false);
            return;
        }

        LogReassigned(result.Value, evt.EventId);
        await args.CompleteMessageAsync(args.Message, args.CancellationToken).ConfigureAwait(false);
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "Service Bus processor error from {Source}")]
    private partial void LogProcessorError(Exception exception, string source);

    [LoggerMessage(Level = LogLevel.Information, Message = "Reassigned {Count} appointments for merge event {EventId}")]
    private partial void LogReassigned(int count, Guid eventId);
}
