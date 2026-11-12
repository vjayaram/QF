using HotChocolate;
using HotChocolate.Authorization;
using HotChocolate.Types;
using Microsoft.EntityFrameworkCore;
using Qf.BuildingBlocks.Hosting.GraphQL;
using Qf.Scheduling.Domain;
using Qf.Scheduling.Infrastructure.Persistence;

namespace Qf.Scheduling.Api.GraphQL;

/// <summary>
/// Scheduling's view of a client: identity only. The Fusion gateway merges this with the
/// full Client type from Client Registry, so callers query one graph.
/// </summary>
public sealed record ClientRef(Guid Id);

public sealed class ClientRefType : ObjectType<ClientRef>
{
    protected override void Configure(IObjectTypeDescriptor<ClientRef> descriptor)
    {
        ArgumentNullException.ThrowIfNull(descriptor);
        descriptor.Name("Client");
        descriptor.Field(c => c.Id);
        descriptor.Field("appointments")
            .Authorize(GraphQLPolicies.SchedulingRead)
            .Type<NonNullType<ListType<NonNullType<AppointmentType>>>>()
            .Resolve(async ctx =>
            {
                var db = ctx.Service<SchedulingDbContext>();
                var clientId = ctx.Parent<ClientRef>().Id;
                return await db.Appointments.AsNoTracking()
                    .Where(a => a.ClientId == clientId)
                    .OrderByDescending(a => a.Slot.Start)
                    .Take(100)
                    .ToListAsync(ctx.RequestAborted)
                    .ConfigureAwait(false);
            });
    }
}

[ExtendObjectType(typeof(Appointment))]
public sealed class AppointmentExtensions
{
    /// <summary>Navigates to the Client entity; the gateway fetches its fields from Client Registry.</summary>
    public ClientRef GetClient([Parent] Appointment appointment)
    {
        ArgumentNullException.ThrowIfNull(appointment);
        return new ClientRef(appointment.ClientId);
    }
}
