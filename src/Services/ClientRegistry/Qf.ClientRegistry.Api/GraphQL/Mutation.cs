using HotChocolate;
using HotChocolate.Authorization;
using Qf.BuildingBlocks.Application;
using Qf.BuildingBlocks.Domain;
using Qf.BuildingBlocks.Hosting.GraphQL;
using Qf.ClientRegistry.Application.Commands;
using Qf.ClientRegistry.Domain;

namespace Qf.ClientRegistry.Api.GraphQL;

public sealed record RegisterClientInput(string Mrn, string GivenName, string FamilyName, string? MiddleName, DateOnly DateOfBirth);

public sealed record LinkLegacyIdentityInput(Guid ClientId, LegacySystem SourceSystem, string ExternalId);

public sealed record MergeClientsInput(Guid SourceClientId, Guid SurvivorClientId);

public sealed record ClientPayload(Guid? ClientId, IReadOnlyList<UserError> Errors);

/// <summary>Thin adapter: input -> command -> handler -> payload. No business logic here.</summary>
public sealed class Mutation
{
    [Authorize(Policy = GraphQLPolicies.ClientsWrite)]
    public async Task<ClientPayload> RegisterClient(
        RegisterClientInput input,
        [Service] ICommandHandler<RegisterClientCommand, Guid> handler,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(input);
        var result = await handler.HandleAsync(
            new RegisterClientCommand(input.Mrn, input.GivenName, input.FamilyName, input.MiddleName, input.DateOfBirth),
            cancellationToken).ConfigureAwait(false);
        return result.IsSuccess ? new ClientPayload(result.Value, []) : new ClientPayload(null, result.ToUserErrors());
    }

    [Authorize(Policy = GraphQLPolicies.ClientsWrite)]
    public async Task<ClientPayload> LinkLegacyIdentity(
        LinkLegacyIdentityInput input,
        [Service] ICommandHandler<LinkLegacyIdentityCommand, Unit> handler,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(input);
        var result = await handler.HandleAsync(
            new LinkLegacyIdentityCommand(input.ClientId, input.SourceSystem, input.ExternalId),
            cancellationToken).ConfigureAwait(false);
        return new ClientPayload(result.IsSuccess ? input.ClientId : null, result.ToUserErrors());
    }

    [Authorize(Policy = GraphQLPolicies.ClientsMerge)]
    public async Task<ClientPayload> MergeClients(
        MergeClientsInput input,
        [Service] ICommandHandler<MergeClientsCommand, Unit> handler,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(input);
        var result = await handler.HandleAsync(
            new MergeClientsCommand(input.SourceClientId, input.SurvivorClientId),
            cancellationToken).ConfigureAwait(false);
        return new ClientPayload(result.IsSuccess ? input.SurvivorClientId : null, result.ToUserErrors());
    }
}
