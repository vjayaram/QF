using Azure.Monitor.OpenTelemetry.AspNetCore;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Hosting;

namespace Qf.BuildingBlocks.Hosting;

public static class HostingSetup
{
    /// <summary>OpenTelemetry traces/metrics/logs to Application Insights when a connection string is present.</summary>
    public static WebApplicationBuilder AddQfObservability(this WebApplicationBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        if (!string.IsNullOrWhiteSpace(builder.Configuration["APPLICATIONINSIGHTS_CONNECTION_STRING"]))
        {
            builder.Services.AddOpenTelemetry().UseAzureMonitor();
        }

        builder.Services.AddSingleton(TimeProvider.System);
        return builder;
    }

    /// <summary>
    /// /health/live: process is up (Container Apps liveness probe).
    /// /health/ready: dependencies reachable (readiness probe; tagged "ready").
    /// Both are anonymous and return no detail.
    /// </summary>
    public static WebApplication MapQfHealth(this WebApplication app)
    {
        ArgumentNullException.ThrowIfNull(app);
        app.MapHealthChecks("/health/live", new HealthCheckOptions { Predicate = _ => false }).AllowAnonymous();
        app.MapHealthChecks("/health/ready", new HealthCheckOptions { Predicate = c => c.Tags.Contains("ready") }).AllowAnonymous();
        return app;
    }

    public static IHealthChecksBuilder AddQfHealthChecks(this IServiceCollection services) => services.AddHealthChecks();
}
