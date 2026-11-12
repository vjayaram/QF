using Qf.BuildingBlocks.Domain;

namespace Qf.ClientRegistry.Domain;

/// <summary>
/// Hand-written behaviour for the Client aggregate. State, enums and events are generated
/// from models/client-registry.model.json; invariants and use-case rules live here.
/// </summary>
public sealed partial class Client
{
    private static readonly DateOnly EarliestPlausibleBirthDate = new(1900, 1, 1);

    public static Result<Client> Register(
        Guid tenantId,
        MedicalRecordNumber mrn,
        PersonName name,
        DateOnly dateOfBirth,
        TimeProvider clock)
    {
        ArgumentNullException.ThrowIfNull(mrn);
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(clock);

        var now = clock.GetUtcNow();
        var errors = new List<Error>();
        if (tenantId == Guid.Empty)
        {
            errors.Add(Error.Validation("Client.TenantId.Required", "A tenant is required."));
        }

        if (dateOfBirth > DateOnly.FromDateTime(now.UtcDateTime))
        {
            errors.Add(Error.Validation("Client.DateOfBirth.InFuture", "Date of birth cannot be in the future."));
        }

        if (dateOfBirth < EarliestPlausibleBirthDate)
        {
            errors.Add(Error.Validation("Client.DateOfBirth.Implausible", "Date of birth is before 1900."));
        }

        if (errors.Count > 0)
        {
            return Result<Client>.Failure(errors);
        }

        var client = new Client
        {
            Id = Guid.CreateVersion7(now),
            TenantId = tenantId,
            Mrn = mrn,
            Name = name,
            DateOfBirth = dateOfBirth,
            Status = ClientStatus.Active,
        };
        client.Raise(new ClientRegistered(client.Id, tenantId) { OccurredAt = now });
        return client;
    }

    /// <summary>
    /// Records where this client lives in an acquired EHR. One identity per source system:
    /// two records in the same legacy system for one person is a duplicate to be merged there first.
    /// </summary>
    public Result LinkLegacyIdentity(LegacyIdentity identity, TimeProvider clock)
    {
        ArgumentNullException.ThrowIfNull(identity);
        ArgumentNullException.ThrowIfNull(clock);

        if (Status == ClientStatus.Merged)
        {
            return Result.Failure(Error.Rule("Client.Merged", "A merged client cannot be changed; use the surviving record."));
        }

        if (_legacyIdentities.Contains(identity))
        {
            return Result.Success(); // idempotent: replaying a migration batch is safe
        }

        if (_legacyIdentities.Any(i => i.SourceSystem == identity.SourceSystem))
        {
            return Result.Failure(Error.Conflict(
                "Client.LegacyIdentity.SourceAlreadyLinked",
                $"Client is already linked to a {identity.SourceSystem} record."));
        }

        _legacyIdentities.Add(identity);
        Raise(new LegacyIdentityLinked(Id, TenantId, identity.SourceSystem) { OccurredAt = clock.GetUtcNow() });
        return Result.Success();
    }

    public Result Rename(PersonName newName)
    {
        ArgumentNullException.ThrowIfNull(newName);
        if (Status == ClientStatus.Merged)
        {
            return Result.Failure(Error.Rule("Client.Merged", "A merged client cannot be changed; use the surviving record."));
        }

        Name = newName;
        return Result.Success();
    }

    public Result Deactivate(TimeProvider clock)
    {
        ArgumentNullException.ThrowIfNull(clock);
        var moved = TransitionStatusTo(ClientStatus.Inactive);
        if (moved.IsSuccess)
        {
            Raise(new ClientDeactivated(Id, TenantId) { OccurredAt = clock.GetUtcNow() });
        }

        return moved;
    }

    public Result Reactivate(TimeProvider clock)
    {
        ArgumentNullException.ThrowIfNull(clock);
        var moved = TransitionStatusTo(ClientStatus.Active);
        if (moved.IsSuccess)
        {
            Raise(new ClientReactivated(Id, TenantId) { OccurredAt = clock.GetUtcNow() });
        }

        return moved;
    }

    /// <summary>
    /// Folds this (duplicate) client into <paramref name="survivor"/>. Legacy identities move to the
    /// survivor so every acquired EHR record still resolves. Downstream contexts (Scheduling, Billing)
    /// react to <see cref="ClientMerged"/> and re-point their references.
    /// </summary>
    public Result MergeInto(Client survivor, TimeProvider clock)
    {
        ArgumentNullException.ThrowIfNull(survivor);
        ArgumentNullException.ThrowIfNull(clock);

        if (survivor.Id == Id)
        {
            return Result.Failure(Error.Rule("Client.Merge.Self", "A client cannot be merged into itself."));
        }

        if (survivor.TenantId != TenantId)
        {
            return Result.Failure(Error.Forbidden("Client.Merge.CrossTenant", "Clients from different tenants cannot be merged."));
        }

        if (survivor.Status == ClientStatus.Merged)
        {
            return Result.Failure(Error.Rule("Client.Merge.SurvivorMerged", "The surviving client has itself been merged."));
        }

        var conflicting = _legacyIdentities
            .Where(i => survivor._legacyIdentities.Any(s => s.SourceSystem == i.SourceSystem && s != i))
            .Select(i => i.SourceSystem)
            .ToList();
        if (conflicting.Count > 0)
        {
            return Result.Failure(Error.Conflict(
                "Client.Merge.LegacyConflict",
                $"Both clients have different records in: {string.Join(", ", conflicting)}. Resolve in the source EHR first."));
        }

        var moved = TransitionStatusTo(ClientStatus.Merged);
        if (moved.IsFailure)
        {
            return moved;
        }

        var now = clock.GetUtcNow();
        foreach (var identity in _legacyIdentities.Where(i => !survivor._legacyIdentities.Contains(i)))
        {
            survivor._legacyIdentities.Add(identity);
            survivor.Raise(new LegacyIdentityLinked(survivor.Id, TenantId, identity.SourceSystem) { OccurredAt = now });
        }

        _legacyIdentities.Clear();
        MergedIntoClientId = survivor.Id;
        Raise(new ClientMerged(Id, survivor.Id, TenantId) { OccurredAt = now });
        return Result.Success();
    }
}
