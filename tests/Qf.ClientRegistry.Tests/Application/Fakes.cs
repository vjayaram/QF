using Qf.ClientRegistry.Application.Abstractions;
using Qf.ClientRegistry.Domain;

namespace Qf.ClientRegistry.Tests.Application;

/// <summary>In-memory repository that mimics the tenant query filter.</summary>
internal sealed class InMemoryClientRepository(Guid tenantId) : IClientRepository
{
    public List<Client> Store { get; } = [];

    private IEnumerable<Client> Visible => Store.Where(c => c.TenantId == tenantId);

    public Task<Client?> GetAsync(Guid clientId, CancellationToken cancellationToken) =>
        Task.FromResult(Visible.FirstOrDefault(c => c.Id == clientId));

    public Task<bool> MrnExistsAsync(MedicalRecordNumber mrn, CancellationToken cancellationToken) =>
        Task.FromResult(Visible.Any(c => c.Mrn == mrn));

    public Task<Client?> FindByLegacyIdentityAsync(LegacyIdentity identity, CancellationToken cancellationToken) =>
        Task.FromResult(Visible.FirstOrDefault(c => c.LegacyIdentities.Contains(identity)));

    public Task<IReadOnlyList<Client>> FindByDateOfBirthAsync(DateOnly dateOfBirth, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<Client>>([.. Visible.Where(c => c.DateOfBirth == dateOfBirth && c.Status != ClientStatus.Merged)]);

    public void Add(Client client) => Store.Add(client);
}

internal sealed class RecordingReviewInbox : IMatchReviewInbox
{
    public List<MatchReviewItem> Items { get; } = [];

    public Task EnqueueAsync(MatchReviewItem item, CancellationToken cancellationToken)
    {
        Items.Add(item);
        return Task.CompletedTask;
    }
}
