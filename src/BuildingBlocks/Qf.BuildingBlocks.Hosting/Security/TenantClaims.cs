namespace Qf.BuildingBlocks.Hosting;

/// <summary>
/// The agency (tenant) a user belongs to is issued as a custom claim by Entra ID
/// (directory extension attribute surfaced through a claims-mapping policy).
/// </summary>
public static class TenantClaims
{
    public const string TenantId = "qf_tenant_id";
}
