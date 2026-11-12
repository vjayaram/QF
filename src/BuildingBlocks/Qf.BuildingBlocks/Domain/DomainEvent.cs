namespace Qf.BuildingBlocks.Domain;

public interface IDomainEvent
{
    Guid EventId { get; }

    DateTimeOffset OccurredAt { get; }
}

/// <summary>
/// Base for generated domain events. EventId becomes the Service Bus MessageId,
/// so broker-side duplicate detection makes publishing idempotent.
/// </summary>
public abstract record DomainEvent : IDomainEvent
{
    public Guid EventId { get; init; } = Guid.CreateVersion7();

    public DateTimeOffset OccurredAt { get; init; }
}
