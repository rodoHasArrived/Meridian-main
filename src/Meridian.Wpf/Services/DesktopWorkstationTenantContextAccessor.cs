using Meridian.Identity.Auth;
using Meridian.Ui.Shared.Endpoints;

namespace Meridian.Wpf.Services;

/// <summary>Reads current validated desktop identity for in-process governed accounting services.</summary>
public sealed class DesktopWorkstationTenantContextAccessor(DesktopAuthenticationSession? session = null)
    : IWorkstationTenantContextAccessor
{
    public bool TryGetCurrent(out WorkstationTenantContext context)
    {
        context = new WorkstationTenantContext(null, null, null, null, UserPermission.None);
        if (session is null || !session.IsAuthenticated || session.CurrentUser is not { } user ||
            string.IsNullOrWhiteSpace(user.Username) || string.IsNullOrWhiteSpace(user.CompanyId))
        {
            return false;
        }

        // Desktop identity exposes company as its tenancy boundary, matching authenticated
        // workstation middleware. A workflow or entered close subject cannot supply tenancy.
        var companyId = user.CompanyId.Trim();
        context = new WorkstationTenantContext(companyId, companyId, user.Username.Trim(),
            user.RoleProfileName, user.Permissions);
        return true;
    }

    public WorkstationTenantContext GetRequired()
        => TryGetCurrent(out var context)
            ? context
            : throw new InvalidOperationException("An authenticated desktop tenant and company scope is required.");
}
