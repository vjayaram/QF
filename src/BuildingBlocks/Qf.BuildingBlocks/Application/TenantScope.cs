namespace Qf.BuildingBlocks.Application;

/// <summary>
/// Scoped holder for the caller's tenant. Set once per request (from the validated token) or per
/// message (from the integration event). A background "system" scope has no tenant and therefore
/// cannot read tenant data: tenant-filtered queries throw and row-level security returns nothing.
/// </summary>
public sealed class TenantScope : ITenantContext
{
    private Guid? _tenantId;
    private string? _userId;

    public bool HasTenant => _tenantId.HasValue;

    public Guid TenantId => _tenantId ?? throw new InvalidOperationException("No tenant has been established for this scope.");

    public string UserId => _userId ?? "system";

    public void Set(Guid tenantId, string userId)
    {
        if (tenantId == Guid.Empty)
        {
            throw new ArgumentException("Tenant id cannot be empty.", nameof(tenantId));
        }

        if (_tenantId is { } existing && existing != tenantId)
        {
            throw new InvalidOperationException("The tenant for this scope is already set and cannot change.");
        }

        _tenantId = tenantId;
        _userId = userId;
    }
}
