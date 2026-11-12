using Microsoft.EntityFrameworkCore;
using Qf.BuildingBlocks.Application;
using Qf.BuildingBlocks.Infrastructure.Persistence;
using Qf.ClientRegistry.Domain;

namespace Qf.ClientRegistry.Infrastructure.Persistence;

public sealed class ClientRegistryDbContext(
    DbContextOptions<ClientRegistryDbContext> options,
    ITenantContext tenant,
    ClientRegistryEventMapper mapper) : QfDbContext(options, tenant, mapper)
{
    public DbSet<Client> Clients => Set<Client>();

    public DbSet<MatchReviewRecord> MatchReviews => Set<MatchReviewRecord>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        ClientRegistryModelConfiguration.ApplyGeneratedConfigurations(modelBuilder);

        // Tenant isolation layer 1 of 2 (layer 2 is Azure SQL row-level security).
        modelBuilder.Entity<Client>().HasQueryFilter(c => c.TenantId == CurrentTenantId);
        modelBuilder.Entity<MatchReviewRecord>(b =>
        {
            b.ToTable("MatchReviews");
            b.HasKey(x => x.Id);
            b.Property(x => x.SourceSystem).HasConversion<string>().HasMaxLength(32);
            b.Property(x => x.ExternalId).HasMaxLength(64);
            b.Property(x => x.CandidateClientIds).HasMaxLength(2000);
            b.HasQueryFilter(x => x.TenantId == CurrentTenantId);
        });
    }
}

/// <summary>Probable duplicate awaiting a data steward. Candidate ids are stored as a comma-separated list.</summary>
public sealed class MatchReviewRecord
{
    public Guid Id { get; init; }

    public Guid TenantId { get; init; }

    public LegacySystem SourceSystem { get; init; }

    public string ExternalId { get; init; } = string.Empty;

    public string CandidateClientIds { get; init; } = string.Empty;

    public DateTimeOffset CreatedAt { get; init; }

    public DateTimeOffset? ResolvedAt { get; set; }
}
