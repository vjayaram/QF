using Qf.BuildingBlocks.Application;
using Qf.BuildingBlocks.Domain;
using Qf.ClientRegistry.Application.Abstractions;
using Qf.ClientRegistry.Domain;

namespace Qf.ClientRegistry.Application.Commands;

public sealed record LinkLegacyIdentityCommand(Guid ClientId, LegacySystem SourceSystem, string ExternalId) : ICommand<Unit>;

public sealed class LinkLegacyIdentityHandler(
    IClientRepository clients,
    IUnitOfWork unitOfWork,
    TimeProvider clock) : ICommandHandler<LinkLegacyIdentityCommand, Unit>
{
    public async Task<Result<Unit>> HandleAsync(LinkLegacyIdentityCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var identity = LegacyIdentity.Create(command.SourceSystem, command.ExternalId);
        if (identity.IsFailure)
        {
            return Result<Unit>.Failure(identity.Errors);
        }

        var client = await clients.GetAsync(command.ClientId, cancellationToken).ConfigureAwait(false);
        if (client is null)
        {
            return Error.NotFound("Client.NotFound", "Client was not found.");
        }

        var owner = await clients.FindByLegacyIdentityAsync(identity.Value, cancellationToken).ConfigureAwait(false);
        if (owner is not null && owner.Id != client.Id)
        {
            return Error.Conflict("Client.LegacyIdentity.LinkedElsewhere", "This legacy record is already linked to another client.");
        }

        var linked = client.LinkLegacyIdentity(identity.Value, clock);
        if (linked.IsFailure)
        {
            return Result<Unit>.Failure(linked.Errors);
        }

        await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return Unit.Value;
    }
}
