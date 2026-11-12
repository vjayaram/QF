namespace Qf.Contracts.ClientRegistry;

/// <summary>No PHI: identifiers only. Consumers that need demographics query Client Registry with authorization.</summary>
public sealed record ClientRegisteredV1(Guid EventId, DateTimeOffset OccurredAt, Guid TenantId, Guid ClientId) : IIntegrationEvent;

/// <summary>Downstream contexts re-point every reference from SourceClientId to SurvivorClientId.</summary>
public sealed record ClientMergedV1(Guid EventId, DateTimeOffset OccurredAt, Guid TenantId, Guid SourceClientId, Guid SurvivorClientId) : IIntegrationEvent;
