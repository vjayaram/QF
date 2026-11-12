using HotChocolate;
using HotChocolate.Authorization;
using Microsoft.EntityFrameworkCore;
using Qf.BuildingBlocks.Hosting.GraphQL;
using Qf.Scheduling.Domain;
using Qf.Scheduling.Infrastructure.Persistence;

namespace Qf.Scheduling.Api.GraphQL;

public sealed class Query
{
    [Authorize(Policy = GraphQLPolicies.SchedulingRead)]
    public Task<Appointment?> GetAppointmentById(Guid id, [Service] SchedulingDbContext db, CancellationToken cancellationToken) =>
        db.Appointments.AsNoTracking().FirstOrDefaultAsync(a => a.Id == id, cancellationToken);

    /// <summary>A provider's calendar for a window (max 31 days to bound query cost).</summary>
    [Authorize(Policy = GraphQLPolicies.SchedulingRead)]
    public async Task<IReadOnlyList<Appointment>> GetProviderSchedule(
        Guid providerId,
        DateTimeOffset from,
        DateTimeOffset to,
        [Service] SchedulingDbContext db,
        CancellationToken cancellationToken)
    {
        if (to <= from || to - from > TimeSpan.FromDays(31))
        {
            throw new GraphQLException(ErrorBuilder.New()
                .SetMessage("The window must be positive and at most 31 days.")
                .SetCode("Schedule.Window.Invalid")
                .Build());
        }

        return await db.Appointments.AsNoTracking()
            .Where(a => a.ProviderId == providerId && a.Slot.Start < to && a.Slot.End > from)
            .OrderBy(a => a.Slot.Start)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Entity resolver for the Fusion gateway: lets the gateway enter this subgraph at Client
    /// (owned by Client Registry) to resolve Client.appointments.
    /// </summary>
    [Authorize(Policy = GraphQLPolicies.SchedulingRead)]
    public ClientRef GetClientById(Guid id) => new(id);
}
