namespace Qf.BuildingBlocks.Domain;

public interface IHasDomainEvents
{
    IReadOnlyCollection<IDomainEvent> DomainEvents { get; }

    void ClearDomainEvents();
}

/// <summary>Every aggregate belongs to exactly one tenant (a behavioral health agency).</summary>
public interface ITenantScoped
{
    Guid TenantId { get; }
}

/// <summary>
/// Consistency boundary. All changes go through the root; the root records domain events,
/// which persistence writes to the transactional outbox in the same database transaction.
/// Optimistic concurrency (rowversion) is a persistence concern and is mapped as a shadow property.
/// </summary>
public abstract class AggregateRoot<TId> : Entity<TId>, IHasDomainEvents, ITenantScoped
    where TId : notnull
{
    private readonly List<IDomainEvent> _domainEvents = [];

    public Guid TenantId { get; protected set; }

    public IReadOnlyCollection<IDomainEvent> DomainEvents => _domainEvents.AsReadOnly();

    public void ClearDomainEvents() => _domainEvents.Clear();

    protected void Raise(IDomainEvent domainEvent)
    {
        ArgumentNullException.ThrowIfNull(domainEvent);
        _domainEvents.Add(domainEvent);
    }
}
