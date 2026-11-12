using Qf.BuildingBlocks.Domain;
using Qf.Scheduling.Domain;
using Qf.TestKit;

namespace Qf.Scheduling.Tests.Domain;

public class AppointmentTests
{
    internal static readonly Guid Provider = Guid.Parse("9e9e9e9e-0000-4000-8000-000000000009");
    internal static readonly Guid ClientA = Guid.Parse("c1c1c1c1-0000-4000-8000-000000000001");
    internal static readonly Guid ClientB = Guid.Parse("c2c2c2c2-0000-4000-8000-000000000002");

    private readonly FakeClock _clock = new();

    internal static TimeSlot SlotAt(FakeClock clock, int hoursFromNow, int minutes = 50) =>
        TimeSlot.Create(clock.Now.AddHours(hoursFromNow), clock.Now.AddHours(hoursFromNow).AddMinutes(minutes)).Value;

    internal static Appointment Book(
        FakeClock clock,
        TimeSlot slot,
        ServiceType type = ServiceType.IndividualTherapy,
        Guid? client = null,
        IReadOnlyCollection<Appointment>? existing = null)
    {
        var appointment = Appointment.Schedule(FakeTenant.A, client ?? ClientA, Provider, slot, type, VisitModality.InPerson, existing ?? [], clock).Value;
        appointment.ClearDomainEvents();
        return appointment;
    }

    [Fact]
    public void Schedule_CreatesScheduledAppointment_AndRaisesEvent()
    {
        var slot = SlotAt(_clock, 24);

        var result = Appointment.Schedule(FakeTenant.A, ClientA, Provider, slot, ServiceType.Intake, VisitModality.Telehealth, [], _clock);

        Assert.True(result.IsSuccess);
        Assert.Equal(AppointmentStatus.Scheduled, result.Value.Status);
        var evt = Assert.IsType<AppointmentScheduled>(Assert.Single(result.Value.DomainEvents));
        Assert.Equal(slot.Start, evt.Start);
        Assert.Equal(ClientA, evt.ClientId);
    }

    [Fact]
    public void Schedule_ValidatesIdsEnumsAndTime()
    {
        var past = TimeSlot.Create(_clock.Now.AddHours(-2), _clock.Now.AddHours(-1)).Value;

        var result = Appointment.Schedule(Guid.Empty, Guid.Empty, Guid.Empty, past, (ServiceType)99, (VisitModality)0, [], _clock);

        var codes = result.Errors.Select(e => e.Code).ToList();
        Assert.Contains("Appointment.TenantId.Required", codes);
        Assert.Contains("Appointment.ClientId.Required", codes);
        Assert.Contains("Appointment.ProviderId.Required", codes);
        Assert.Contains("Appointment.ServiceType.Invalid", codes);
        Assert.Contains("Appointment.Modality.Invalid", codes);
        Assert.Contains("Appointment.Slot.InPast", codes);
    }

    [Fact]
    public void Schedule_RejectsProviderDoubleBooking()
    {
        var existing = Book(_clock, SlotAt(_clock, 24));
        var overlapping = TimeSlot.Create(existing.Slot.Start.AddMinutes(30), existing.Slot.End.AddMinutes(30)).Value;

        var result = Appointment.Schedule(FakeTenant.A, ClientB, Provider, overlapping, ServiceType.IndividualTherapy, VisitModality.InPerson, [existing], _clock);

        var error = Assert.Single(result.Errors);
        Assert.Equal("Appointment.ProviderDoubleBooked", error.Code);
        Assert.Equal(ErrorKind.Conflict, error.Kind);
    }

    [Fact]
    public void Schedule_AllowsBackToBackAndIgnoresCancelled()
    {
        var first = Book(_clock, SlotAt(_clock, 24, 60));
        var cancelled = Book(_clock, SlotAt(_clock, 26));
        cancelled.Cancel(CancellationReason.ClientRequest, _clock);

        var backToBack = Appointment.Schedule(FakeTenant.A, ClientB, Provider, TimeSlot.Create(first.Slot.End, first.Slot.End.AddMinutes(50)).Value,
            ServiceType.IndividualTherapy, VisitModality.InPerson, [first, cancelled], _clock);
        var overCancelled = Appointment.Schedule(FakeTenant.A, ClientB, Provider, cancelled.Slot,
            ServiceType.IndividualTherapy, VisitModality.InPerson, [first, cancelled], _clock);

        Assert.True(backToBack.IsSuccess);
        Assert.True(overCancelled.IsSuccess);
    }

    [Fact]
    public void Schedule_AllowsSeveralClientsInTheSameGroupSession()
    {
        var slot = SlotAt(_clock, 48, 90);
        var group = Book(_clock, slot, ServiceType.GroupTherapy);

        var sameGroup = Appointment.Schedule(FakeTenant.A, ClientB, Provider, slot, ServiceType.GroupTherapy, VisitModality.InPerson, [group], _clock);
        var shiftedGroup = Appointment.Schedule(FakeTenant.A, ClientB, Provider,
            TimeSlot.Create(slot.Start.AddMinutes(15), slot.End.AddMinutes(15)).Value, ServiceType.GroupTherapy, VisitModality.InPerson, [group], _clock);
        var individual = Appointment.Schedule(FakeTenant.A, ClientB, Provider, slot, ServiceType.IndividualTherapy, VisitModality.InPerson, [group], _clock);

        Assert.True(sameGroup.IsSuccess);
        Assert.True(shiftedGroup.IsFailure);
        Assert.True(individual.IsFailure);
    }

    [Fact]
    public void CheckIn_OnlyWithinWindow()
    {
        var appointment = Book(_clock, SlotAt(_clock, 3));

        Assert.Equal("Appointment.CheckIn.OutsideWindow", Assert.Single(appointment.CheckIn(_clock).Errors).Code);

        _clock.Advance(TimeSpan.FromHours(2.5));
        Assert.True(appointment.CheckIn(_clock).IsSuccess);
        Assert.Equal(AppointmentStatus.CheckedIn, appointment.Status);
        Assert.Equal(_clock.Now, appointment.CheckedInAt);
        Assert.IsType<AppointmentCheckedIn>(Assert.Single(appointment.DomainEvents));
    }

    [Fact]
    public void CheckIn_ClosesAtEndOfAppointment()
    {
        var appointment = Book(_clock, SlotAt(_clock, 1));
        _clock.Advance(TimeSpan.FromHours(2));

        Assert.Equal("Appointment.CheckIn.OutsideWindow", Assert.Single(appointment.CheckIn(_clock).Errors).Code);
    }

    [Fact]
    public void Complete_RequiresCheckIn()
    {
        var appointment = Book(_clock, SlotAt(_clock, 1));

        Assert.Equal("Appointment.Status.InvalidTransition", Assert.Single(appointment.Complete(_clock).Errors).Code);

        appointment.CheckIn(_clock);
        _clock.Advance(TimeSpan.FromHours(1.5));
        Assert.True(appointment.Complete(_clock).IsSuccess);
        Assert.Equal(_clock.Now, appointment.CompletedAt);
        var completed = Assert.IsType<AppointmentCompleted>(appointment.DomainEvents.Last());
        Assert.Equal(ServiceType.IndividualTherapy, completed.ServiceType);
    }

    [Fact]
    public void Cancel_RequiresValidReason_AndIsTerminal()
    {
        var appointment = Book(_clock, SlotAt(_clock, 24));

        Assert.Equal("Appointment.CancellationReason.Invalid", Assert.Single(appointment.Cancel((CancellationReason)0, _clock).Errors).Code);
        Assert.True(appointment.Cancel(CancellationReason.Weather, _clock).IsSuccess);
        Assert.Equal(CancellationReason.Weather, appointment.CancellationReason);
        Assert.False(appointment.IsActive);
        Assert.True(appointment.Cancel(CancellationReason.Other, _clock).IsFailure);
        Assert.True(appointment.CheckIn(_clock).IsFailure);
    }

    [Fact]
    public void NoShow_OnlyAfterGracePeriod()
    {
        var appointment = Book(_clock, SlotAt(_clock, 1));
        _clock.Advance(TimeSpan.FromMinutes(70));

        Assert.Equal("Appointment.NoShow.TooEarly", Assert.Single(appointment.MarkNoShow(_clock).Errors).Code);

        _clock.Advance(TimeSpan.FromMinutes(10));
        Assert.True(appointment.MarkNoShow(_clock).IsSuccess);
        Assert.Equal(AppointmentStatus.NoShow, appointment.Status);
    }

    [Fact]
    public void ReassignClient_FollowsMerge_AndIsIdempotent()
    {
        var appointment = Book(_clock, SlotAt(_clock, 24));

        Assert.True(appointment.ReassignClient(ClientA, ClientB, _clock).IsSuccess);
        Assert.True(appointment.ReassignClient(ClientA, ClientB, _clock).IsSuccess);

        Assert.Equal(ClientB, appointment.ClientId);
        var evt = Assert.IsType<AppointmentClientReassigned>(Assert.Single(appointment.DomainEvents));
        Assert.Equal(ClientA, evt.FromClientId);
    }

    [Fact]
    public void ReassignClient_RejectsUnexpectedSourceAndEmptyTarget()
    {
        var appointment = Book(_clock, SlotAt(_clock, 24));

        Assert.Equal("Appointment.Reassign.UnexpectedClient", Assert.Single(appointment.ReassignClient(Guid.NewGuid(), ClientB, _clock).Errors).Code);
        Assert.Equal("Appointment.ClientId.Required", Assert.Single(appointment.ReassignClient(ClientA, Guid.Empty, _clock).Errors).Code);
    }

    [Fact]
    public void Lifecycle_MatchesTheModel()
    {
        Assert.Equal(new[] { AppointmentStatus.CheckedIn, AppointmentStatus.Cancelled, AppointmentStatus.NoShow }, AppointmentStatusTransitions.AllowedFrom(AppointmentStatus.Scheduled));
        Assert.Empty(AppointmentStatusTransitions.AllowedFrom(AppointmentStatus.Completed));
        Assert.Empty(AppointmentStatusTransitions.AllowedFrom(AppointmentStatus.NoShow));
    }
}
