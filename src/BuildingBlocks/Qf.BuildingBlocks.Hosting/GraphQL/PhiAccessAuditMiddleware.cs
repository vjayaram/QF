using System.Security.Claims;
using HotChocolate.Resolvers;
using Microsoft.Extensions.Logging;
using Qf.BuildingBlocks.Domain;

namespace Qf.BuildingBlocks.Hosting.GraphQL;

/// <summary>
/// HIPAA access audit (45 CFR 164.312(b)): records who read which PHI field of which record.
/// Attached by generated code to every field flagged "phi" in the model, so coverage cannot drift.
/// Writes to the "Qf.Audit.Phi" log category, which Azure Monitor routes to an immutable, long-retention table.
/// The audit entry contains identifiers only, never the PHI value.
/// </summary>
public sealed partial class PhiAccessAuditMiddleware(FieldDelegate next, ILoggerFactory loggerFactory)
{
    private readonly ILogger _audit = loggerFactory.CreateLogger("Qf.Audit.Phi");

    public async ValueTask InvokeAsync(IMiddlewareContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        await next(context).ConfigureAwait(false);

        if (context.Result is null || context.HasErrors)
        {
            return;
        }

        var user = context.GetGlobalStateOrDefault<ClaimsPrincipal>(nameof(ClaimsPrincipal));
        var userId = user?.FindFirst("oid")?.Value ?? user?.Identity?.Name ?? "unknown";
        var tenantId = user?.FindFirst(TenantClaims.TenantId)?.Value ?? "unknown";
        var resourceId = context.Parent<object>() is Entity<Guid> entity ? entity.Id.ToString() : context.Path.ToString();

        LogPhiAccess(_audit, userId, tenantId, context.Selection.Field.Coordinate.ToString(), resourceId);
    }

    [LoggerMessage(EventId = 4100, Level = LogLevel.Information,
        Message = "PHI read: user {UserId} tenant {TenantId} field {Field} resource {ResourceId}")]
    private static partial void LogPhiAccess(ILogger logger, string userId, string tenantId, string field, string resourceId);
}
