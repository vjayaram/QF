using Microsoft.EntityFrameworkCore;
using Qf.BuildingBlocks.Application;
using Qf.BuildingBlocks.Domain;
using Qf.BuildingBlocks.Infrastructure.Persistence;
using Qf.Contracts;
using Qf.Contracts.Scheduling;
using Qf.Scheduling.Domain;

namespace Qf.Scheduling.Infrastructure.Persistence;

public sealed class SchedulingDbContext(
    DbContextOptions<SchedulingDbContext> options,
    ITenantContext tenant,
    SchedulingEventMapper mapper) : QfDbContext(options, tenant, mapper)
{
    public DbSet<Appointment> Appointments => Set<Appointment>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        SchedulingModelConfiguration.ApplyGeneratedConfigurations(modelBuilder);
        modelBuilder.Entity<Appointment>().HasQueryFilter(a => a.TenantId == CurrentTenantId);
    }
}

public sealed class SchedulingEventMapper : IIntegrationEventMapper
{
    public (string Subject, IIntegrationEvent Payload)? Map(IDomainEvent domainEvent) => domainEvent switch
    {
        AppointmentCompleted e => (Subjects.AppointmentCompleted,
            new AppointmentCompletedV1(e.EventId, e.OccurredAt, e.TenantId, e.AppointmentId, e.ServiceType.ToString())),
        _ => null,
    };
}
