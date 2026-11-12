using Qf.BuildingBlocks.Application;
using Qf.BuildingBlocks.Domain;
using Qf.ClientRegistry.Application.Abstractions;

namespace Qf.ClientRegistry.Application.Commands;

/// <summary>Data-steward action after reviewing a probable duplicate.</summary>
public sealed record MergeClientsCommand(Guid SourceClientId, Guid SurvivorClientId) : ICommand<Unit>;

public sealed class MergeClientsHandler(
    IClientRepository clients,
    IUnitOfWork unitOfWork,
    TimeProvider clock) : ICommandHandler<MergeClientsCommand, Unit>
{
    public async Task<Result<Unit>> HandleAsync(MergeClientsCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var source = await clients.GetAsync(command.SourceClientId, cancellationToken).ConfigureAwait(false);
        var survivor = await clients.GetAsync(command.SurvivorClientId, cancellationToken).ConfigureAwait(false);
        if (source is null || survivor is null)
        {
            return Error.NotFound("Client.NotFound", "Both clients must exist.");
        }

        var merged = source.MergeInto(survivor, clock);
        if (merged.IsFailure)
        {
            return Result<Unit>.Failure(merged.Errors);
        }

        // One transaction: both aggregates plus the ClientMerged outbox row commit atomically.
        await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return Unit.Value;
    }
}
