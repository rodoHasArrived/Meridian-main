using Meridian.Contracts.Tenancy;
using Meridian.Identity.Auth;

namespace Meridian.Ui.Shared.Endpoints;

/// <summary>
/// Request-scoped operator and tenant context resolved from authenticated workstation middleware.
/// </summary>
public sealed record WorkstationTenantContext(
    string? TenantId,
    string? CompanyId,
    string? Actor,
    string? RoleProfileName,
    UserPermission Permissions)
{
    public bool HasTenantScope => TenantReadPredicate.IsResolvedTenant(TenantId);
}

/// <summary>
/// Resolves the current authenticated workstation tenant and company scope.
/// </summary>
public interface IWorkstationTenantContextAccessor
{
    bool TryGetCurrent(out WorkstationTenantContext context);

    WorkstationTenantContext GetRequired();
}
