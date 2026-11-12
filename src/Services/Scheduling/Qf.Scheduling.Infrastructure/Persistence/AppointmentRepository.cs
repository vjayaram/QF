using System.Data;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Qf.BuildingBlocks.Infrastructure.Persistence;
using Qf.Scheduling.Application.Abstractions;
using Qf.Scheduling.Domain;

namespace Qf.Scheduling.Infrastructure.Persistence;

public sealed class AppointmentRepository(SchedulingDbContext db) : IAppointmentRepository
{
    public Task<Appointment?> GetAsync(Guid appointmentId, CancellationToken cancellationToken) =>
        db.Appointments.FirstOrDefaultAsync(a => a.Id == appointmentId, cancellationToken);

    public async Task<IReadOnlyList<Appointment>> GetForProviderAsync(Guid providerId, DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken) =>
        await db.Appointments
            .Where(a => a.ProviderId == providerId
                && (a.Status == AppointmentStatus.Scheduled || a.Status == AppointmentStatus.CheckedIn)
                && a.Slot.Start < to
                && a.Slot.End > from)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

    public async Task<IReadOnlyList<Appointment>> GetForClientAsync(Guid clientId, CancellationToken cancellationToken) =>
        await db.Appointments.Where(a => a.ClientId == clientId).ToListAsync(cancellationToken).ConfigureAwait(false);

    public void Add(Appointment appointment) => db.Appointments.Add(appointment);
}

/// <summary>
/// Per-provider exclusive application lock held for the life of the booking transaction.
/// The unit of work commits the transaction (releasing the lock); disposing without commit rolls back.
/// Callers run the whole use case inside the EF execution strategy so transient faults retry as a unit.
/// </summary>
public sealed class SqlProviderScheduleLock(SchedulingDbContext db) : IProviderScheduleLock
{
    public async Task<IAsyncDisposable> AcquireAsync(Guid providerId, CancellationToken cancellationToken)
    {
        var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken).ConfigureAwait(false);
        var result = new SqlParameter("@result", SqlDbType.Int) { Direction = ParameterDirection.Output };
        await db.Database.ExecuteSqlRawAsync(
            "EXEC @result = sp_getapplock @Resource = @resource, @LockMode = 'Exclusive', @LockOwner = 'Transaction', @LockTimeout = 5000;",
            [result, new SqlParameter("@resource", $"provider:{providerId:N}")],
            cancellationToken).ConfigureAwait(false);

        if ((int)result.Value < 0)
        {
            await transaction.RollbackAsync(cancellationToken).ConfigureAwait(false);
            throw new TimeoutException("Could not acquire the provider schedule lock; retry the request.");
        }

        return new Releaser(db);
    }

    private sealed class Releaser(SchedulingDbContext db) : IAsyncDisposable
    {
        public async ValueTask DisposeAsync()
        {
            if (db.Database.CurrentTransaction is { } open)
            {
                await open.RollbackAsync().ConfigureAwait(false); // not committed by the unit of work => abandon
            }
        }
    }
}

public sealed class ProcessedMessageStore(SchedulingDbContext db, TimeProvider clock) : IProcessedMessageStore
{
    /// <summary>Adds the inbox row to the unit of work; its primary key rejects a concurrent duplicate at commit.</summary>
    public async Task<bool> TryMarkProcessedAsync(Guid messageId, CancellationToken cancellationToken)
    {
        if (await db.InboxMessages.AnyAsync(m => m.Id == messageId, cancellationToken).ConfigureAwait(false))
        {
            return false;
        }

        db.InboxMessages.Add(new InboxMessage { Id = messageId, ProcessedAt = clock.GetUtcNow() });
        return true;
    }
}
