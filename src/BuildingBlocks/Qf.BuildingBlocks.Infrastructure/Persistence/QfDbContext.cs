using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Qf.BuildingBlocks.Application;
using Qf.BuildingBlocks.Domain;

namespace Qf.BuildingBlocks.Infrastructure.Persistence;

/// <summary>
/// Base DbContext for every service: unit of work, tenant scoping and the transactional outbox.
/// Database schema is owned by the SQL Database Project (DACPAC), not by EF migrations.
/// </summary>
public abstract class QfDbContext(DbContextOptions options, ITenantContext tenant, IIntegrationEventMapper mapper)
    : DbContext(options), IUnitOfWork
{
    private static readonly JsonSerializerOptions PayloadOptions = new(JsonSerializerDefaults.Web);

    public DbSet<OutboxMessage> OutboxMessages => Set<OutboxMessage>();

    public DbSet<InboxMessage> InboxMessages => Set<InboxMessage>();

    /// <summary>Referenced by global query filters; EF re-evaluates it per context instance.</summary>
    protected Guid CurrentTenantId => tenant.TenantId;

    Task IUnitOfWork.SaveChangesAsync(CancellationToken cancellationToken) => SaveChangesAsync(cancellationToken);

    public override async Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
    {
        StageIntegrationEvents();
        var written = await base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken).ConfigureAwait(false);

        // If a use case opened a transaction (e.g. the scheduling lock), the unit of work commits it.
        if (Database.CurrentTransaction is { } transaction)
        {
            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        }

        return written;
    }

    public override int SaveChanges(bool acceptAllChangesOnSuccess) =>
        throw new NotSupportedException("Use SaveChangesAsync; synchronous I/O is not allowed.");

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);
        modelBuilder.Entity<OutboxMessage>(b =>
        {
            b.ToTable("OutboxMessages");
            b.HasKey(x => x.Id);
            b.Property(x => x.Subject).HasMaxLength(128).IsRequired();
            b.Property(x => x.Payload).IsRequired();
            b.Property(x => x.LastError).HasMaxLength(2000);
        });
        modelBuilder.Entity<InboxMessage>(b =>
        {
            b.ToTable("InboxMessages");
            b.HasKey(x => x.Id);
        });
    }

    private void StageIntegrationEvents()
    {
        var aggregates = ChangeTracker.Entries<IHasDomainEvents>()
            .Select(e => e.Entity)
            .Where(a => a.DomainEvents.Count > 0)
            .ToList();

        foreach (var aggregate in aggregates)
        {
            foreach (var domainEvent in aggregate.DomainEvents)
            {
                if (mapper.Map(domainEvent) is not { } mapped)
                {
                    continue;
                }

                OutboxMessages.Add(new OutboxMessage
                {
                    Id = mapped.Payload.EventId,
                    TenantId = mapped.Payload.TenantId,
                    Subject = mapped.Subject,
                    Payload = JsonSerializer.Serialize(mapped.Payload, mapped.Payload.GetType(), PayloadOptions),
                    OccurredAt = mapped.Payload.OccurredAt,
                });
            }

            aggregate.ClearDomainEvents();
        }
    }
}
