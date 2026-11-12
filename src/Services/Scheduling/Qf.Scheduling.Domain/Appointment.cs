using Qf.BuildingBlocks.Domain;

namespace Qf.Scheduling.Domain;

/// <summary>
/// Hand-written behaviour for the Appointment aggregate. The lifecycle (which status may follow
/// which) is declared in models/scheduling.model.json; the time-based rules are here.
/// </summary>
public sealed partial class Appointment
{
    /// <summary>Front desk may check a client in up to an hour before the session.</summary>
    public static readonly TimeSpan EarlyCheckInWindow = TimeSpan.FromMinutes(60);

    /// <summary>A client is not a no-show until 15 minutes after the start time.</summary>
    public static readonly TimeSpan NoShowGracePeriod = TimeSpan.FromMinutes(15);

    public bool IsActive => Status is AppointmentStatus.Scheduled or AppointmentStatus.CheckedIn;

    /// <param name="providerAppointments">
    /// The provider's appointments that intersect the requested slot. The caller loads them under a
    /// per-provider lock so the double-booking check cannot race.
    /// </param>
    public static Result<Appointment> Schedule(
        Guid tenantId,
        Guid clientId,
        Guid providerId,
        TimeSlot slot,
        ServiceType serviceType,
        VisitModality modality,
        IReadOnlyCollection<Appointment> providerAppointments,
        TimeProvider clock)
    {
        ArgumentNullException.ThrowIfNull(slot);
        ArgumentNullException.ThrowIfNull(providerAppointments);
        ArgumentNullException.ThrowIfNull(clock);

        var now = clock.GetUtcNow();
        var errors = new List<Error>();
        if (tenantId == Guid.Empty)
        {
            errors.Add(Error.Validation("Appointment.TenantId.Required", "A tenant is required."));
        }

        if (clientId == Guid.Empty)
        {
            errors.Add(Error.Validation("Appointment.ClientId.Required", "A client is required."));
        }

        if (providerId == Guid.Empty)
        {
            errors.Add(Error.Validation("Appointment.ProviderId.Required", "A provider is required."));
        }

        if (!Enum.IsDefined(serviceType))
        {
            errors.Add(Error.Validation("Appointment.ServiceType.Invalid", "Service type is not valid."));
        }

        if (!Enum.IsDefined(modality))
        {
            errors.Add(Error.Validation("Appointment.Modality.Invalid", "Visit modality is not valid."));
        }

        if (slot.Start <= now)
        {
            errors.Add(Error.Rule("Appointment.Slot.InPast", "Appointments must be scheduled in the future."));
        }

        if (errors.Count > 0)
        {
            return Result<Appointment>.Failure(errors);
        }

        var conflict = providerAppointments.FirstOrDefault(a =>
            a.ProviderId == providerId
            && a.IsActive
            && a.Slot.Overlaps(slot)
            && !IsSameGroupSession(a, serviceType, slot));
        if (conflict is not null)
        {
            return Error.Conflict(
                "Appointment.ProviderDoubleBooked",
                "The provider already has an appointment that overlaps this time.");
        }

        var appointment = new Appointment
        {
            Id = Guid.CreateVersion7(now),
            TenantId = tenantId,
            ClientId = clientId,
            ProviderId = providerId,
            Slot = slot,
            ServiceType = serviceType,
            Modality = modality,
            Status = AppointmentStatus.Scheduled,
        };
        appointment.Raise(new AppointmentScheduled(appointment.Id, tenantId, clientId, providerId, slot.Start, slot.End) { OccurredAt = now });
        return appointment;
    }

    public Result CheckIn(TimeProvider clock)
    {
        ArgumentNullException.ThrowIfNull(clock);
        var now = clock.GetUtcNow();
        if (Status == AppointmentStatus.Scheduled && (now < Slot.Start - EarlyCheckInWindow || now >= Slot.End))
        {
            return Result.Failure(Error.Rule(
                "Appointment.CheckIn.OutsideWindow",
                $"Check-in opens {EarlyCheckInWindow.TotalMinutes} minutes before the start and closes at the end of the appointment."));
        }

        var moved = TransitionStatusTo(AppointmentStatus.CheckedIn);
        if (moved.IsSuccess)
        {
            CheckedInAt = now;
            Raise(new AppointmentCheckedIn(Id, TenantId) { OccurredAt = now });
        }

        return moved;
    }

    public Result Complete(TimeProvider clock)
    {
        ArgumentNullException.ThrowIfNull(clock);
        var now = clock.GetUtcNow();
        var moved = TransitionStatusTo(AppointmentStatus.Completed);
        if (moved.IsSuccess)
        {
            CompletedAt = now;
            Raise(new AppointmentCompleted(Id, TenantId, ServiceType) { OccurredAt = now });
        }

        return moved;
    }

    public Result Cancel(CancellationReason reason, TimeProvider clock)
    {
        ArgumentNullException.ThrowIfNull(clock);
        if (!Enum.IsDefined(reason))
        {
            return Result.Failure(Error.Validation("Appointment.CancellationReason.Invalid", "A valid cancellation reason is required."));
        }

        var moved = TransitionStatusTo(AppointmentStatus.Cancelled);
        if (moved.IsSuccess)
        {
            CancellationReason = reason;
            Raise(new AppointmentCancelled(Id, TenantId, reason) { OccurredAt = clock.GetUtcNow() });
        }

        return moved;
    }

    public Result MarkNoShow(TimeProvider clock)
    {
        ArgumentNullException.ThrowIfNull(clock);
        var now = clock.GetUtcNow();
        if (now < Slot.Start + NoShowGracePeriod)
        {
            return Result.Failure(Error.Rule(
                "Appointment.NoShow.TooEarly",
                $"A client can be marked as a no-show {NoShowGracePeriod.TotalMinutes} minutes after the start time."));
        }

        var moved = TransitionStatusTo(AppointmentStatus.NoShow);
        if (moved.IsSuccess)
        {
            Raise(new AppointmentMarkedNoShow(Id, TenantId) { OccurredAt = now });
        }

        return moved;
    }

    /// <summary>
    /// Reaction to Client Registry's ClientMerged event. Applies in every status: history must
    /// follow the surviving client record too. Idempotent for redelivered messages.
    /// </summary>
    public Result ReassignClient(Guid fromClientId, Guid toClientId, TimeProvider clock)
    {
        ArgumentNullException.ThrowIfNull(clock);
        if (toClientId == Guid.Empty)
        {
            return Result.Failure(Error.Validation("Appointment.ClientId.Required", "A client is required."));
        }

        if (ClientId == toClientId)
        {
            return Result.Success();
        }

        if (ClientId != fromClientId)
        {
            return Result.Failure(Error.Conflict("Appointment.Reassign.UnexpectedClient", "The appointment does not belong to the merged client."));
        }

        ClientId = toClientId;
        Raise(new AppointmentClientReassigned(Id, TenantId, fromClientId, toClientId) { OccurredAt = clock.GetUtcNow() });
        return Result.Success();
    }

    /// <summary>Group therapy: several clients share one provider in one identical slot.</summary>
    private static bool IsSameGroupSession(Appointment existing, ServiceType requested, TimeSlot slot) =>
        requested == ServiceType.GroupTherapy
        && existing.ServiceType == ServiceType.GroupTherapy
        && existing.Slot.Start == slot.Start
        && existing.Slot.End == slot.End;
}
