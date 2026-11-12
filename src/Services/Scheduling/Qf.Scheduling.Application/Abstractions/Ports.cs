using Qf.Scheduling.Domain;

namespace Qf.Scheduling.Application.Abstractions;

public interface IAppointmentRepository
{
    Task<Appointment?> GetAsync(Guid appointmentId, CancellationToken cancellationToken);

    /// <summary>Active appointments for a provider that intersect [from, to).</summary>
    Task<IReadOnlyList<Appointment>> GetForProviderAsync(Guid providerId, DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken);

    Task<IReadOnlyList<Appointment>> GetForClientAsync(Guid clientId, CancellationToken cancellationToken);

    void Add(Appointment appointment);
}

/// <summary>
/// Serialises bookings per provider so the overlap check and insert cannot interleave.
/// Azure SQL implementation: sp_getapplock scoped to the transaction.
/// </summary>
public interface IProviderScheduleLock
{
    Task<IAsyncDisposable> AcquireAsync(Guid providerId, CancellationToken cancellationToken);
}

/// <summary>Inbox for at-least-once delivery: remembers processed integration event ids.</summary>
public interface IProcessedMessageStore
{
    Task<bool> TryMarkProcessedAsync(Guid messageId, CancellationToken cancellationToken);
}
