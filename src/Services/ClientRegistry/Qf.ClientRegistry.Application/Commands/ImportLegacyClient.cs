using Qf.BuildingBlocks.Application;
using Qf.BuildingBlocks.Domain;
using Qf.ClientRegistry.Application.Abstractions;
using Qf.ClientRegistry.Domain;

namespace Qf.ClientRegistry.Application.Commands;

/// <summary>
/// A client record as it exists in an acquired EHR, already translated into registry language
/// by the anti-corruption layer. This is the only shape legacy data takes inside the new platform.
/// </summary>
public sealed record LegacyClientSnapshot(
    LegacySystem SourceSystem,
    string ExternalId,
    string? Mrn,
    string GivenName,
    string FamilyName,
    string? MiddleName,
    DateOnly DateOfBirth,
    bool IsActive);

public sealed record ImportLegacyClientCommand(LegacyClientSnapshot Snapshot) : ICommand<ImportOutcome>;

public enum ImportDecision
{
    AlreadyLinked = 1,
    LinkedToExisting = 2,
    Registered = 3,
    QueuedForReview = 4,
}

public sealed record ImportOutcome(ImportDecision Decision, Guid? ClientId);

/// <summary>
/// Strangler-fig migration step, run per record by the sync worker (CDC from each legacy EHR).
/// Idempotent: replaying the same snapshot yields AlreadyLinked and changes nothing.
/// </summary>
public sealed class ImportLegacyClientHandler(
    IClientRepository clients,
    IMatchReviewInbox reviewQueue,
    IUnitOfWork unitOfWork,
    ITenantContext tenant,
    TimeProvider clock) : ICommandHandler<ImportLegacyClientCommand, ImportOutcome>
{
    public async Task<Result<ImportOutcome>> HandleAsync(ImportLegacyClientCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        var s = command.Snapshot;

        var identity = LegacyIdentity.Create(s.SourceSystem, s.ExternalId);
        if (identity.IsFailure)
        {
            return Result<ImportOutcome>.Failure(identity.Errors);
        }

        var existing = await clients.FindByLegacyIdentityAsync(identity.Value, cancellationToken).ConfigureAwait(false);
        if (existing is not null)
        {
            return new ImportOutcome(ImportDecision.AlreadyLinked, existing.Id);
        }

        var candidates = await clients.FindByDateOfBirthAsync(s.DateOfBirth, cancellationToken).ConfigureAwait(false);
        var scored = candidates
            .Select(c => (Client: c, Confidence: ClientMatchingPolicy.Evaluate(c, s.Mrn, s.GivenName, s.FamilyName, s.DateOfBirth)))
            .Where(x => x.Confidence != MatchConfidence.None)
            .ToList();

        var certain = scored.Where(x => x.Confidence == MatchConfidence.Certain).Select(x => x.Client).ToList();
        if (certain.Count == 1)
        {
            var linked = certain[0].LinkLegacyIdentity(identity.Value, clock);
            if (linked.IsFailure)
            {
                return Result<ImportOutcome>.Failure(linked.Errors);
            }

            await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            return new ImportOutcome(ImportDecision.LinkedToExisting, certain[0].Id);
        }

        if (scored.Count > 0)
        {
            await reviewQueue.EnqueueAsync(
                new MatchReviewItem(tenant.TenantId, s.SourceSystem, s.ExternalId, [.. scored.Select(x => x.Client.Id)]),
                cancellationToken).ConfigureAwait(false);
            return new ImportOutcome(ImportDecision.QueuedForReview, null);
        }

        return await RegisterAsync(s, identity.Value, cancellationToken).ConfigureAwait(false);
    }

    private async Task<Result<ImportOutcome>> RegisterAsync(LegacyClientSnapshot s, LegacyIdentity identity, CancellationToken cancellationToken)
    {
        // Legacy records without an MRN get a deterministic one derived from the source key.
        var mrnText = string.IsNullOrWhiteSpace(s.Mrn) ? DeriveMrn(s) : s.Mrn;
        var mrn = MedicalRecordNumber.Create(mrnText);
        var name = PersonName.Create(s.GivenName, s.FamilyName, s.MiddleName);
        if (mrn.IsFailure || name.IsFailure)
        {
            return Result<ImportOutcome>.Failure(mrn.Errors.Concat(name.Errors));
        }

        if (await clients.MrnExistsAsync(mrn.Value, cancellationToken).ConfigureAwait(false))
        {
            return Error.Conflict("Client.Mrn.Duplicate", "MRN already belongs to a different client; route to review.");
        }

        var registered = Client.Register(tenant.TenantId, mrn.Value, name.Value, s.DateOfBirth, clock);
        if (registered.IsFailure)
        {
            return Result<ImportOutcome>.Failure(registered.Errors);
        }

        var client = registered.Value;
        var outcome = client.LinkLegacyIdentity(identity, clock);
        if (outcome.IsSuccess && !s.IsActive)
        {
            outcome = client.Deactivate(clock);
        }

        if (outcome.IsFailure)
        {
            return Result<ImportOutcome>.Failure(outcome.Errors);
        }

        clients.Add(client);
        await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return new ImportOutcome(ImportDecision.Registered, client.Id);
    }

    internal static string DeriveMrn(LegacyClientSnapshot s)
    {
        var prefix = s.SourceSystem switch
        {
            LegacySystem.CareLogic => "CL",
            LegacySystem.Credible => "CR",
            LegacySystem.InSync => "IS",
            _ => "LG",
        };
        var key = new string([.. s.ExternalId.Where(char.IsAsciiLetterOrDigit).Select(char.ToUpperInvariant)]);
        var mrn = $"{prefix}-{key}";
        return mrn.Length <= 32 ? mrn : mrn[..32];
    }
}
