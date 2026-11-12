using Qf.BuildingBlocks.Application;
using Qf.BuildingBlocks.Domain;
using Qf.Scheduling.Application.Abstractions;

namespace Qf.Scheduling.Application.Commands;

/// <summary>Raised by the Service Bus consumer for Client Registry's ClientMerged.v1 event.</summary>
public sealed record ReassignMergedClientCommand(Guid MessageId, Guid SourceClientId, Guid SurvivorClientId) : ICommand<int>;

public sealed class ReassignMergedClientHandler(
    IAppointmentRepository appointments,
    IProcessedMessageStore inbox,
    IUnitOfWork unitOfWork,
    TimeProvider clock) : ICommandHandler<ReassignMergedClientCommand, int>
{
    public async Task<Result<int>> HandleAsync(ReassignMergedClientCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        if (!await inbox.TryMarkProcessedAsync(command.MessageId, cancellationToken).ConfigureAwait(false))
        {
            return 0; // duplicate delivery
        }

        var affected = await appointments.GetForClientAsync(command.SourceClientId, cancellationToken).ConfigureAwait(false);
        foreach (var appointment in affected)
        {
            var moved = appointment.ReassignClient(command.SourceClientId, command.SurvivorClientId, clock);
            if (moved.IsFailure)
            {
                return Result<int>.Failure(moved.Errors);
            }
        }

        // Inbox row and reassignments commit together, so a crash cannot half-apply the event.
        await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return affected.Count;
    }
}
