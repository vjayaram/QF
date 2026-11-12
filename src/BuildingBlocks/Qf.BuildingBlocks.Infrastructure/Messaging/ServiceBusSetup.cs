using Azure.Identity;
using Azure.Messaging.ServiceBus;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Qf.BuildingBlocks.Infrastructure.Messaging;

public static class ServiceBusSetup
{
    /// <summary>
    /// Azure: Entra ID auth with the app's user-assigned managed identity (local auth is disabled on the namespace).
    /// Local: the Service Bus emulator connection string.
    /// </summary>
    public static IServiceCollection AddQfServiceBus(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        services.AddSingleton(_ =>
        {
            var connectionString = configuration["ServiceBus:ConnectionString"];
            if (!string.IsNullOrWhiteSpace(connectionString))
            {
                return new ServiceBusClient(connectionString);
            }

            var fullyQualifiedNamespace = configuration["ServiceBus:FullyQualifiedNamespace"]
                ?? throw new InvalidOperationException("ServiceBus:FullyQualifiedNamespace is not configured.");
            var clientId = configuration["AZURE_CLIENT_ID"]
                ?? throw new InvalidOperationException("AZURE_CLIENT_ID (user-assigned managed identity) is not configured.");
            return new ServiceBusClient(fullyQualifiedNamespace, new ManagedIdentityCredential(clientId));
        });
        return services;
    }
}
