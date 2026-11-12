using Microsoft.EntityFrameworkCore;
using Qf.ClientRegistry.Application.Abstractions;
using Qf.ClientRegistry.Domain;

namespace Qf.ClientRegistry.Infrastructure.Persistence;

public sealed class ClientRepository(ClientRegistryDbContext db) : IClientRepository
{
    public Task<Client?> GetAsync(Guid clientId, CancellationToken cancellationToken) =>
        db.Clients.FirstOrDefaultAsync(c => c.Id == clientId, cancellationToken);

    public Task<bool> MrnExistsAsync(MedicalRecordNumber mrn, CancellationToken cancellationToken) =>
        db.Clients.AnyAsync(c => c.Mrn == mrn, cancellationToken);

    public Task<Client?> FindByLegacyIdentityAsync(LegacyIdentity identity, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(identity);
        return db.Clients.FirstOrDefaultAsync(
            c => c.LegacyIdentities.Any(i => i.SourceSystem == identity.SourceSystem && i.ExternalId == identity.ExternalId),
            cancellationToken);
    }

    public async Task<IReadOnlyList<Client>> FindByDateOfBirthAsync(DateOnly dateOfBirth, CancellationToken cancellationToken) =>
        await db.Clients.Where(c => c.DateOfBirth == dateOfBirth && c.Status != ClientStatus.Merged)
            .Take(200)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

    public void Add(Client client) => db.Clients.Add(client);
}

public sealed class MatchReviewInbox(ClientRegistryDbContext db, TimeProvider clock) : IMatchReviewInbox
{
    public async Task EnqueueAsync(MatchReviewItem item, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(item);
        db.MatchReviews.Add(new MatchReviewRecord
        {
            Id = Guid.CreateVersion7(),
            TenantId = item.TenantId,
            SourceSystem = item.SourceSystem,
            ExternalId = item.ExternalId,
            CandidateClientIds = string.Join(',', item.CandidateClientIds),
            CreatedAt = clock.GetUtcNow(),
        });
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }
}
