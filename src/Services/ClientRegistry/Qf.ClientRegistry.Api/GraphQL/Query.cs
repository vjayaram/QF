using HotChocolate;
using HotChocolate.Authorization;
using HotChocolate.Data;
using HotChocolate.Types;
using Microsoft.EntityFrameworkCore;
using Qf.BuildingBlocks.Hosting.GraphQL;
using Qf.ClientRegistry.Domain;
using Qf.ClientRegistry.Infrastructure.Persistence;

namespace Qf.ClientRegistry.Api.GraphQL;

/// <summary>Reads go straight to EF (CQRS-lite); tenant filtering and RLS apply automatically.</summary>
public sealed class Query
{
    [Authorize(Policy = GraphQLPolicies.ClientsRead)]
    [UsePaging(IncludeTotalCount = true, MaxPageSize = 100, DefaultPageSize = 25)]
    [UseFiltering(typeof(ClientFilterType))]
    [UseSorting(typeof(ClientSortType))]
    public IQueryable<Client> GetClients([Service] ClientRegistryDbContext db) =>
        db.Clients.AsNoTracking().Where(c => c.Status != ClientStatus.Merged);

    /// <summary>Entity lookup used by the Fusion gateway to resolve Client from other subgraphs.</summary>
    [Authorize(Policy = GraphQLPolicies.ClientsRead)]
    public Task<Client?> GetClientById(Guid id, [Service] ClientRegistryDbContext db, CancellationToken cancellationToken) =>
        db.Clients.AsNoTracking().FirstOrDefaultAsync(c => c.Id == id, cancellationToken);
}
