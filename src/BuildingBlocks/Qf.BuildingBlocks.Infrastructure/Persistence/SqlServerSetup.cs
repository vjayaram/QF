using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Qf.BuildingBlocks.Application;

namespace Qf.BuildingBlocks.Infrastructure.Persistence;

public static class SqlServerSetup
{
    /// <summary>
    /// Registers a service DbContext against Azure SQL. In Azure the connection string uses
    /// "Authentication=Active Directory Managed Identity" (no passwords anywhere); locally it points at SQL Server in Docker.
    /// </summary>
    public static IServiceCollection AddQfSqlServer<TContext>(this IServiceCollection services, IConfiguration configuration, string connectionName)
        where TContext : QfDbContext
    {
        ArgumentNullException.ThrowIfNull(configuration);
        var connectionString = configuration.GetConnectionString(connectionName)
            ?? throw new InvalidOperationException($"Connection string '{connectionName}' is not configured.");

        services.AddScoped<TenantScope>();
        services.AddScoped<ITenantContext>(sp => sp.GetRequiredService<TenantScope>());
        services.AddScoped<TenantSessionContextInterceptor>();
        services.AddDbContext<TContext>((sp, options) =>
        {
            options.UseSqlServer(connectionString, sql =>
            {
                sql.EnableRetryOnFailure(maxRetryCount: 5, maxRetryDelay: TimeSpan.FromSeconds(10), errorNumbersToAdd: null);
                sql.CommandTimeout(30);
            });
            options.AddInterceptors(sp.GetRequiredService<TenantSessionContextInterceptor>());
        });
        services.AddScoped<IUnitOfWork>(sp => sp.GetRequiredService<TContext>());
        return services;
    }
}
