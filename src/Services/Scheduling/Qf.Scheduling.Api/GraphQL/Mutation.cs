using HotChocolate;
using HotChocolate.Authorization;
using Microsoft.EntityFrameworkCore;
using Qf.BuildingBlocks.Application;
using Qf.BuildingBlocks.Domain;
using Qf.BuildingBlocks.Hosting.GraphQL;
using Qf.Scheduling.Application.Commands;
using Qf.Scheduling.Domain;
using Qf.Scheduling.Infrastructure.Persistence;

namespace Qf.Scheduling.Api.GraphQL;

public sealed record ScheduleAppointmentInput(
    Guid ClientId,
    Guid ProviderId,
    DateTimeOffset Start,
    DateTimeOffset End,
    ServiceType ServiceType,
    VisitModality Modality);

public sealed record AppointmentPayload(Guid? AppointmentId, IReadOnlyList<UserError> Errors);

public sealed class Mutation
{
    /// <summary>
    /// Runs inside the EF execution strategy: the provider lock opens a user transaction, and Azure SQL
    /// transient-fault retries must replay the whole unit (lock, check, insert), not a single statement.
    /// </summary>
    [Authorize(Policy = GraphQLPolicies.SchedulingWrite)]
    public async Task<AppointmentPayload> ScheduleAppointment(
        ScheduleAppointmentInput input,
        [Service] SchedulingDbContext db,
        [Service] ICommandHandler<ScheduleAppointmentCommand, Guid> handler,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(input);
        var command = new ScheduleAppointmentCommand(input.ClientId, input.ProviderId, input.Start, input.End, input.ServiceType, input.Modality);
        var result = await db.Database.CreateExecutionStrategy()
            .ExecuteAsync(() => handler.HandleAsync(command, cancellationToken))
            .ConfigureAwait(false);
        return result.IsSuccess ? new AppointmentPayload(result.Value, []) : new AppointmentPayload(null, result.ToUserErrors());
    }

    [Authorize(Policy = GraphQLPolicies.SchedulingWrite)]
    public Task<AppointmentPayload> CheckInAppointment(Guid appointmentId, [Service] ICommandHandler<ChangeAppointmentStatusCommand, Unit> handler, CancellationToken cancellationToken) =>
        Change(handler, new ChangeAppointmentStatusCommand(appointmentId, LifecycleAction.CheckIn), cancellationToken);

    [Authorize(Policy = GraphQLPolicies.SchedulingWrite)]
    public Task<AppointmentPayload> CompleteAppointment(Guid appointmentId, [Service] ICommandHandler<ChangeAppointmentStatusCommand, Unit> handler, CancellationToken cancellationToken) =>
        Change(handler, new ChangeAppointmentStatusCommand(appointmentId, LifecycleAction.Complete), cancellationToken);

    [Authorize(Policy = GraphQLPolicies.SchedulingWrite)]
    public Task<AppointmentPayload> CancelAppointment(Guid appointmentId, CancellationReason reason, [Service] ICommandHandler<ChangeAppointmentStatusCommand, Unit> handler, CancellationToken cancellationToken) =>
        Change(handler, new ChangeAppointmentStatusCommand(appointmentId, LifecycleAction.Cancel, reason), cancellationToken);

    [Authorize(Policy = GraphQLPolicies.SchedulingWrite)]
    public Task<AppointmentPayload> MarkAppointmentNoShow(Guid appointmentId, [Service] ICommandHandler<ChangeAppointmentStatusCommand, Unit> handler, CancellationToken cancellationToken) =>
        Change(handler, new ChangeAppointmentStatusCommand(appointmentId, LifecycleAction.MarkNoShow), cancellationToken);

    private static async Task<AppointmentPayload> Change(
        ICommandHandler<ChangeAppointmentStatusCommand, Unit> handler,
        ChangeAppointmentStatusCommand command,
        CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(command, cancellationToken).ConfigureAwait(false);
        return new AppointmentPayload(result.IsSuccess ? command.AppointmentId : null, result.ToUserErrors());
    }
}
