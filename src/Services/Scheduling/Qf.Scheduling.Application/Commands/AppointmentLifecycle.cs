using Qf.BuildingBlocks.Application;
using Qf.BuildingBlocks.Domain;
using Qf.Scheduling.Application.Abstractions;
using Qf.Scheduling.Domain;

namespace Qf.Scheduling.Application.Commands;

public enum LifecycleAction
{
    CheckIn = 1,
    Complete = 2,
    Cancel = 3,
    MarkNoShow = 4,
}

public sealed record ChangeAppointmentStatusCommand(
    Guid AppointmentId,
    LifecycleAction Action,
    CancellationReason? Reason = null) : ICommand<Unit>;

/// <summary>One handler for the lifecycle verbs: load, apply the domain method, save.</summary>
public sealed class ChangeAppointmentStatusHandler(
    IAppointmentRepository appointments,
    IUnitOfWork unitOfWork,
    TimeProvider clock) : ICommandHandler<ChangeAppointmentStatusCommand, Unit>
{
    public async Task<Result<Unit>> HandleAsync(ChangeAppointmentStatusCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var appointment = await appointments.GetAsync(command.AppointmentId, cancellationToken).ConfigureAwait(false);
        if (appointment is null)
        {
            return Error.NotFound("Appointment.NotFound", "Appointment was not found.");
        }

        var result = command.Action switch
        {
            LifecycleAction.CheckIn => appointment.CheckIn(clock),
            LifecycleAction.Complete => appointment.Complete(clock),
            LifecycleAction.Cancel when command.Reason is { } reason => appointment.Cancel(reason, clock),
            LifecycleAction.Cancel => Result.Failure(Error.Validation("Appointment.CancellationReason.Required", "A cancellation reason is required.")),
            LifecycleAction.MarkNoShow => appointment.MarkNoShow(clock),
            _ => Result.Failure(Error.Validation("Appointment.Action.Invalid", "Unknown action.")),
        };
        if (result.IsFailure)
        {
            return Result<Unit>.Failure(result.Errors);
        }

        await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return Unit.Value;
    }
}
