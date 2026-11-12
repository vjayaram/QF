using Qf.ClientRegistry.Domain;

namespace Qf.ClientRegistry.Application.Abstractions;

/// <summary>Aggregate repository. All queries are implicitly scoped to the caller's tenant.</summary>
public interface IClientRepository
{
    Task<Client?> GetAsync(Guid clientId, CancellationToken cancellationToken);

    Task<bool> MrnExistsAsync(MedicalRecordNumber mrn, CancellationToken cancellationToken);

    Task<Client?> FindByLegacyIdentityAsync(LegacyIdentity identity, CancellationToken cancellationToken);

    /// <summary>Blocking key for matching: candidates share a date of birth.</summary>
    Task<IReadOnlyList<Client>> FindByDateOfBirthAsync(DateOnly dateOfBirth, CancellationToken cancellationToken);

    void Add(Client client);
}

/// <summary>Probable duplicates are queued for a data steward instead of being merged automatically.</summary>
public interface IMatchReviewInbox
{
    Task EnqueueAsync(MatchReviewItem item, CancellationToken cancellationToken);
}

public sealed record MatchReviewItem(Guid TenantId, LegacySystem SourceSystem, string ExternalId, IReadOnlyList<Guid> CandidateClientIds);
