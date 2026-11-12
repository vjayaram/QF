namespace Qf.Contracts.Scheduling;

/// <summary>Consumed by Billing to start the claim workflow for a rendered service.</summary>
public sealed record AppointmentCompletedV1(Guid EventId, DateTimeOffset OccurredAt, Guid TenantId, Guid AppointmentId, string ServiceType) : IIntegrationEvent;
