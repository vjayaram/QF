namespace Qf.BuildingBlocks.Infrastructure.Persistence;

/// <summary>
/// Integration event written in the same transaction as the aggregate change (transactional outbox),
/// then relayed to Service Bus by <see cref="Messaging.OutboxRelay{TContext}"/>. Guarantees that a committed
/// change is always published and an uncommitted one never is.
/// </summary>
public sealed class OutboxMessage
{
    public Guid Id { get; init; }

    public Guid TenantId { get; init; }

    public string Subject { get; init; } = string.Empty;

    public string Payload { get; init; } = string.Empty;

    public DateTimeOffset OccurredAt { get; init; }

    public DateTimeOffset? ProcessedAt { get; set; }

    public int Attempts { get; set; }

    public string? LastError { get; set; }
}

/// <summary>Ids of integration events already handled by this service (idempotent consumer).</summary>
public sealed class InboxMessage
{
    public Guid Id { get; init; }

    public DateTimeOffset ProcessedAt { get; init; }
}
