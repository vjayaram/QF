using Qf.BuildingBlocks.Domain;
using Qf.Contracts;

namespace Qf.BuildingBlocks.Infrastructure.Persistence;

/// <summary>
/// Decides which domain events leave the bounded context and in what published shape.
/// Domain events stay internal by default; only mapped ones become integration events.
/// </summary>
public interface IIntegrationEventMapper
{
    (string Subject, IIntegrationEvent Payload)? Map(IDomainEvent domainEvent);
}
