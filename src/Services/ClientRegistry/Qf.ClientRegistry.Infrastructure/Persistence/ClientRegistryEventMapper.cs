using Qf.BuildingBlocks.Domain;
using Qf.BuildingBlocks.Infrastructure.Persistence;
using Qf.ClientRegistry.Domain;
using Qf.Contracts;
using Qf.Contracts.ClientRegistry;

namespace Qf.ClientRegistry.Infrastructure.Persistence;

/// <summary>Published language of Client Registry: only these domain events leave the context.</summary>
public sealed class ClientRegistryEventMapper : IIntegrationEventMapper
{
    public (string Subject, IIntegrationEvent Payload)? Map(IDomainEvent domainEvent) => domainEvent switch
    {
        ClientRegistered e => (Subjects.ClientRegistered, new ClientRegisteredV1(e.EventId, e.OccurredAt, e.TenantId, e.ClientId)),
        ClientMerged e => (Subjects.ClientMerged, new ClientMergedV1(e.EventId, e.OccurredAt, e.TenantId, e.SourceClientId, e.SurvivorClientId)),
        _ => null,
    };
}
