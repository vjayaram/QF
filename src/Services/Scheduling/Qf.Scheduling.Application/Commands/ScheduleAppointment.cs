using Qf.BuildingBlocks.Application;
using Qf.BuildingBlocks.Domain;
using Qf.Scheduling.Application.Abstractions;
using Qf.Scheduling.Domain;

namespace Qf.Scheduling.Application.Commands;

public sealed record ScheduleAppointmentCommand(
    Guid ClientId,
    Guid ProviderId,
    DateTimeOffset Start,
    DateTimeOffset End,
    ServiceType ServiceType,
    VisitModality Modality) : ICommand<Guid>;

public sealed class ScheduleAppointmentHandler(
    IAppointmentRepository appointments,
    IProviderScheduleLock scheduleLock,
    IUnitOfWork unitOfWork,
    ITenantContext tenant,
    TimeProvider clock) : ICommandHandler<ScheduleAppointmentCommand, Guid>
{
    public async Task<Result<Guid>> HandleAsync(ScheduleAppointmentCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var slot = TimeSlot.Create(command.Start, command.End);
        if (slot.IsFailure)
        {
            return Result<Guid>.Failure(slot.Errors);
        }

        var providerLock = await scheduleLock.AcquireAsync(command.ProviderId, cancellationToken).ConfigureAwait(false);
        await using (providerLock.ConfigureAwait(false))
        {
            var existing = await appointments
                .GetForProviderAsync(command.ProviderId, slot.Value.Start, slot.Value.End, cancellationToken)
                .ConfigureAwait(false);

            var appointment = Appointment.Schedule(
                tenant.TenantId,
                command.ClientId,
                command.ProviderId,
                slot.Value,
                command.ServiceType,
                command.Modality,
                existing,
                clock);
            if (appointment.IsFailure)
            {
                return Result<Guid>.Failure(appointment.Errors);
            }

            appointments.Add(appointment.Value);
            await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            return appointment.Value.Id;
        }
    }
}
