using Qf.Scheduling.Application.Commands;
using Qf.Scheduling.Domain;
using Qf.Scheduling.Tests.Domain;
using Qf.TestKit;

namespace Qf.Scheduling.Tests.Application;

public class HandlerTests
{
    private readonly FakeClock _clock = new();
    private readonly InMemoryAppointmentRepository _appointments = new();
    private readonly RecordingLock _lock = new();
    private readonly FakeUnitOfWork _uow = new();

    private ScheduleAppointmentHandler Scheduler() => new(_appointments, _lock, _uow, new FakeTenant(FakeTenant.A), _clock);

    private ScheduleAppointmentCommand Command(int hoursFromNow, Guid? client = null) =>
        new(client ?? AppointmentTests.ClientA, AppointmentTests.Provider, _clock.Now.AddHours(hoursFromNow), _clock.Now.AddHours(hoursFromNow).AddMinutes(50),
            ServiceType.IndividualTherapy, VisitModality.InPerson);

    [Fact]
    public async Task Schedule_TakesProviderLock_SavesAndReleases()
    {
        var result = await Scheduler().HandleAsync(Command(24), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(AppointmentTests.Provider, Assert.Single(_lock.Acquired));
        Assert.Equal(1, _lock.Released);
        Assert.Equal(1, _uow.Saves);
        Assert.Equal(result.Value, Assert.Single(_appointments.Store).Id);
    }

    [Fact]
    public async Task Schedule_SecondOverlappingBooking_IsRejected_AndLockStillReleased()
    {
        await Scheduler().HandleAsync(Command(24), CancellationToken.None);

        var second = await Scheduler().HandleAsync(Command(24, AppointmentTests.ClientB), CancellationToken.None);

        Assert.Equal("Appointment.ProviderDoubleBooked", Assert.Single(second.Errors).Code);
        Assert.Equal(2, _lock.Released);
        Assert.Single(_appointments.Store);
    }

    [Fact]
    public async Task Schedule_InvalidSlot_FailsBeforeLocking()
    {
        var bad = Command(24) with { End = _clock.Now.AddHours(23) };

        var result = await Scheduler().HandleAsync(bad, CancellationToken.None);

        Assert.Equal("TimeSlot.End.BeforeStart", Assert.Single(result.Errors).Code);
        Assert.Empty(_lock.Acquired);
    }

    [Fact]
    public async Task ChangeStatus_HandlesEachVerb()
    {
        var appointment = AppointmentTests.Book(_clock, AppointmentTests.SlotAt(_clock, 1));
        _appointments.Add(appointment);
        var handler = new ChangeAppointmentStatusHandler(_appointments, _uow, _clock);

        var noReason = await handler.HandleAsync(new(appointment.Id, LifecycleAction.Cancel), CancellationToken.None);
        var checkIn = await handler.HandleAsync(new(appointment.Id, LifecycleAction.CheckIn), CancellationToken.None);
        var noShow = await handler.HandleAsync(new(appointment.Id, LifecycleAction.MarkNoShow), CancellationToken.None);
        var complete = await handler.HandleAsync(new(appointment.Id, LifecycleAction.Complete), CancellationToken.None);
        var missing = await handler.HandleAsync(new(Guid.NewGuid(), LifecycleAction.CheckIn), CancellationToken.None);

        Assert.Equal("Appointment.CancellationReason.Required", Assert.Single(noReason.Errors).Code);
        Assert.True(checkIn.IsSuccess);
        Assert.True(noShow.IsFailure);
        Assert.True(complete.IsSuccess);
        Assert.Equal("Appointment.NotFound", Assert.Single(missing.Errors).Code);
        Assert.Equal(AppointmentStatus.Completed, appointment.Status);
        Assert.Equal(2, _uow.Saves);
    }

    [Fact]
    public async Task ChangeStatus_CancelWithReason()
    {
        var appointment = AppointmentTests.Book(_clock, AppointmentTests.SlotAt(_clock, 5));
        _appointments.Add(appointment);

        var result = await new ChangeAppointmentStatusHandler(_appointments, _uow, _clock)
            .HandleAsync(new(appointment.Id, LifecycleAction.Cancel, CancellationReason.ProviderUnavailable), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(AppointmentStatus.Cancelled, appointment.Status);
    }

    [Fact]
    public async Task ReassignMergedClient_MovesEveryAppointment_Once()
    {
        _appointments.Add(AppointmentTests.Book(_clock, AppointmentTests.SlotAt(_clock, 24)));
        _appointments.Add(AppointmentTests.Book(_clock, AppointmentTests.SlotAt(_clock, 48)));
        _appointments.Add(AppointmentTests.Book(_clock, AppointmentTests.SlotAt(_clock, 72), client: AppointmentTests.ClientB));
        var handler = new ReassignMergedClientHandler(_appointments, new InMemoryInbox(), _uow, _clock);
        var messageId = Guid.NewGuid();
        var survivor = Guid.NewGuid();

        var first = await handler.HandleAsync(new(messageId, AppointmentTests.ClientA, survivor), CancellationToken.None);
        var redelivery = await handler.HandleAsync(new(messageId, AppointmentTests.ClientA, survivor), CancellationToken.None);

        Assert.Equal(2, first.Value);
        Assert.Equal(0, redelivery.Value);
        Assert.Equal(2, _appointments.Store.Count(a => a.ClientId == survivor));
        Assert.Equal(1, _uow.Saves);
    }
}
