using Qf.Scheduling.Application.Abstractions;
using Qf.Scheduling.Domain;

namespace Qf.Scheduling.Tests.Application;

internal sealed class InMemoryAppointmentRepository : IAppointmentRepository
{
    public List<Appointment> Store { get; } = [];

    public Task<Appointment?> GetAsync(Guid appointmentId, CancellationToken cancellationToken) =>
        Task.FromResult(Store.FirstOrDefault(a => a.Id == appointmentId));

    public Task<IReadOnlyList<Appointment>> GetForProviderAsync(Guid providerId, DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<Appointment>>([.. Store.Where(a => a.ProviderId == providerId && a.IsActive && a.Slot.Start < to && a.Slot.End > from)]);

    public Task<IReadOnlyList<Appointment>> GetForClientAsync(Guid clientId, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<Appointment>>([.. Store.Where(a => a.ClientId == clientId)]);

    public void Add(Appointment appointment) => Store.Add(appointment);
}

internal sealed class RecordingLock : IProviderScheduleLock
{
    public List<Guid> Acquired { get; } = [];

    public int Released { get; private set; }

    public Task<IAsyncDisposable> AcquireAsync(Guid providerId, CancellationToken cancellationToken)
    {
        Acquired.Add(providerId);
        return Task.FromResult<IAsyncDisposable>(new Release(this));
    }

    private sealed class Release(RecordingLock owner) : IAsyncDisposable
    {
        public ValueTask DisposeAsync()
        {
            owner.Released++;
            return ValueTask.CompletedTask;
        }
    }
}

internal sealed class InMemoryInbox : IProcessedMessageStore
{
    private readonly HashSet<Guid> _seen = [];

    public Task<bool> TryMarkProcessedAsync(Guid messageId, CancellationToken cancellationToken) => Task.FromResult(_seen.Add(messageId));
}
