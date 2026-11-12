using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Identity.Web;
using Qf.BuildingBlocks.Application;
using Qf.BuildingBlocks.Hosting.GraphQL;

namespace Qf.BuildingBlocks.Hosting;

public static class SecuritySetup
{
    /// <summary>
    /// Azure: validate Entra ID access tokens (issuer, audience, signature, lifetime) via Microsoft.Identity.Web.
    /// Local development only: tokens minted by `dotnet user-jwts` (Authentication:Mode = Local).
    /// Default policy denies anonymous access to everything.
    /// </summary>
    public static WebApplicationBuilder AddQfSecurity(this WebApplicationBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        var localMode = builder.Environment.IsDevelopment()
            && string.Equals(builder.Configuration["Authentication:Mode"], "Local", StringComparison.OrdinalIgnoreCase);

        if (localMode)
        {
            builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer();
        }
        else
        {
            builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
                .AddMicrosoftIdentityWebApi(builder.Configuration.GetSection("AzureAd"));
        }

        builder.Services.AddAuthorizationBuilder()
            .SetFallbackPolicy(new AuthorizationPolicyBuilder().RequireAuthenticatedUser().RequireClaim(TenantClaims.TenantId).Build())
            .AddPolicies();

        return builder;
    }

    /// <summary>Establishes the tenant for the request from the validated token, never from input.</summary>
    public static IApplicationBuilder UseQfTenancy(this IApplicationBuilder app) =>
        app.Use(async (HttpContext context, RequestDelegate next) =>
        {
            var claim = context.User.FindFirst(TenantClaims.TenantId)?.Value;
            if (context.User.Identity?.IsAuthenticated == true && Guid.TryParse(claim, out var tenantId))
            {
                var scope = context.RequestServices.GetRequiredService<TenantScope>();
                var userId = context.User.FindFirst("oid")?.Value ?? context.User.Identity.Name ?? "unknown";
                scope.Set(tenantId, userId);
            }

            await next(context).ConfigureAwait(false);
        });

    private static AuthorizationBuilder AddPolicies(this AuthorizationBuilder builder)
    {
        foreach (var (policy, role) in GraphQLPolicies.AppRoles)
        {
            builder.AddPolicy(policy, p => p.RequireAuthenticatedUser().RequireRole(role));
        }

        return builder;
    }
}
