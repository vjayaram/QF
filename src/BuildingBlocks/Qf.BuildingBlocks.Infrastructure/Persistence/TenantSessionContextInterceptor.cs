using System.Data.Common;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Qf.BuildingBlocks.Application;

namespace Qf.BuildingBlocks.Infrastructure.Persistence;

/// <summary>
/// Defence in depth for multi-tenancy: stamps the tenant into SESSION_CONTEXT on every connection,
/// which the Azure SQL row-level security policy (database/*/Security/TenantIsolation.sql) enforces.
/// Even a query that forgets the EF filter cannot read another agency's rows.
/// </summary>
public sealed class TenantSessionContextInterceptor(TenantScope tenant) : DbConnectionInterceptor
{
    public override async Task ConnectionOpenedAsync(DbConnection connection, ConnectionEndEventData eventData, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(connection);
        if (!tenant.HasTenant)
        {
            return; // system scope (outbox relay): RLS exposes no tenant rows
        }

        var command = connection.CreateCommand();
        await using (command.ConfigureAwait(false))
        {
            command.CommandText = "EXEC sp_set_session_context @key = N'TenantId', @value = @tenantId, @read_only = 1;";
            var parameter = command.CreateParameter();
            parameter.ParameterName = "@tenantId";
            parameter.Value = tenant.TenantId;
            command.Parameters.Add(parameter);
            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }
    }
}
