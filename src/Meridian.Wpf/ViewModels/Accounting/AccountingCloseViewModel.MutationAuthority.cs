using Meridian.Identity.Auth;

namespace Meridian.Wpf.ViewModels.Accounting;

public sealed partial class AccountingCloseViewModel
{
    private bool TryGetCloseControllerAuthority(out string actor, out string role)
    {
        role = string.Empty;
        if (!TryGetLedgerMutationActor(out actor))
        {
            return false;
        }

        if (_authenticationSession!.CurrentRole == UserRole.Controller)
        {
            role = "Controller";
            return true;
        }

        var profile = _authenticationSession.CurrentUser?.RoleProfileName?.Trim();
        if (string.Equals(profile, "Controller", StringComparison.OrdinalIgnoreCase)
            || string.Equals(profile, "Fund Controller", StringComparison.OrdinalIgnoreCase))
        {
            role = profile!;
            return true;
        }

        return false;
    }

    private bool TryGetCloseMutationScope(out string tenantId, out string companyId)
    {
        companyId = _authenticationSession?.CurrentUser?.CompanyId?.Trim() ?? string.Empty;

        // Desktop authentication currently exposes company as the tenancy boundary.
        // Keep both scoped arguments explicit so close mutations fail closed until
        // the identity model exposes a distinct tenant identifier.
        tenantId = companyId;
        return !string.IsNullOrWhiteSpace(companyId);
    }
}
