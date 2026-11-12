using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Qf.BuildingBlocks.Application;
using Qf.BuildingBlocks.Domain;
using Qf.BuildingBlocks.Infrastructure.Messaging;
using Qf.BuildingBlocks.Infrastructure.Persistence;
using Qf.Contracts;
using Qf.Scheduling.Application.Abstractions;
using Qf.Scheduling.Application.Commands;
using Qf.Scheduling.Infrastructure.Messaging;
using Qf.Scheduling.Infrastructure.Persistence;

namespace Qf.Scheduling.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddScheduling(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddSingleton<SchedulingEventMapper>();
        services.AddQfSqlServer<SchedulingDbContext>(configuration, "Scheduling");
        services.AddScoped<IAppointmentRepository, AppointmentRepository>();
        services.AddScoped<IProviderScheduleLock, SqlProviderScheduleLock>();
        services.AddScoped<IProcessedMessageStore, ProcessedMessageStore>();

        services.AddScoped<ICommandHandler<ScheduleAppointmentCommand, Guid>, ScheduleAppointmentHandler>();
        services.AddScoped<ICommandHandler<ChangeAppointmentStatusCommand, Unit>, ChangeAppointmentStatusHandler>();
        services.AddScoped<ICommandHandler<ReassignMergedClientCommand, int>, ReassignMergedClientHandler>();

        services.AddQfServiceBus(configuration);
        services.AddSingleton(new OutboxRelayOptions { Topic = Topics.SchedulingEvents });
        services.AddHostedService<OutboxRelay<SchedulingDbContext>>();
        services.AddHostedService<ClientMergedConsumer>();
        return services;
    }
}
