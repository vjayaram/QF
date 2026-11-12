using Qf.BuildingBlocks.Application;
using Qf.BuildingBlocks.Domain;
using Qf.ClientRegistry.Application.Abstractions;
using Qf.ClientRegistry.Domain;

namespace Qf.ClientRegistry.Application.Commands;

public sealed record RegisterClientCommand(
    string Mrn,
    string GivenName,
    string FamilyName,
    string? MiddleName,
    DateOnly DateOfBirth) : ICommand<Guid>;

public sealed class RegisterClientHandler(
    IClientRepository clients,
    IUnitOfWork unitOfWork,
    ITenantContext tenant,
    TimeProvider clock) : ICommandHandler<RegisterClientCommand, Guid>
{
    public async Task<Result<Guid>> HandleAsync(RegisterClientCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var mrn = MedicalRecordNumber.Create(command.Mrn);
        var name = PersonName.Create(command.GivenName, command.FamilyName, command.MiddleName);
        if (mrn.IsFailure || name.IsFailure)
        {
            return Result<Guid>.Failure(mrn.Errors.Concat(name.Errors));
        }

        if (await clients.MrnExistsAsync(mrn.Value, cancellationToken).ConfigureAwait(false))
        {
            return Error.Conflict("Client.Mrn.Duplicate", "A client with this MRN already exists.");
        }

        var client = Client.Register(tenant.TenantId, mrn.Value, name.Value, command.DateOfBirth, clock);
        if (client.IsFailure)
        {
            return Result<Guid>.Failure(client.Errors);
        }

        clients.Add(client.Value);
        await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return client.Value.Id;
    }
}
