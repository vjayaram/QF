using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Qf.BuildingBlocks.Application;
using Qf.BuildingBlocks.Infrastructure.Messaging;
using Qf.BuildingBlocks.Infrastructure.Persistence;
using Qf.ClientRegistry.Application.Abstractions;
using Qf.ClientRegistry.Application.Commands;
using Qf.ClientRegistry.Infrastructure.Persistence;
using Qf.Contracts;

namespace Qf.ClientRegistry.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddClientRegistry(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddSingleton<ClientRegistryEventMapper>();
        services.AddQfSqlServer<ClientRegistryDbContext>(configuration, "ClientRegistry");
        services.AddScoped<IClientRepository, ClientRepository>();
        services.AddScoped<IMatchReviewInbox, MatchReviewInbox>();

        services.AddScoped<ICommandHandler<RegisterClientCommand, Guid>, RegisterClientHandler>();
        services.AddScoped<ICommandHandler<LinkLegacyIdentityCommand, BuildingBlocks.Domain.Unit>, LinkLegacyIdentityHandler>();
        services.AddScoped<ICommandHandler<MergeClientsCommand, BuildingBlocks.Domain.Unit>, MergeClientsHandler>();
        services.AddScoped<ICommandHandler<ImportLegacyClientCommand, ImportOutcome>, ImportLegacyClientHandler>();

        services.AddQfServiceBus(configuration);
        services.AddSingleton(new OutboxRelayOptions { Topic = Topics.ClientRegistryEvents });
        services.AddHostedService<OutboxRelay<ClientRegistryDbContext>>();
        return services;
    }
}
