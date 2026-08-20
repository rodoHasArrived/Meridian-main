using Meridian.Identity.Auth;
using Meridian.Ui.Shared.Services;

namespace Meridian.Wpf.Services;

/// <summary>
/// Resolves the workflow-summary read scope for the signed-in desktop operator, so the WPF shell
/// withholds the same workspace cards the browser lane does.
///
/// <para>The desktop reaches <see cref="WorkstationWorkflowSummaryService"/> in-process rather than
/// through the HTTP route, so no endpoint filter stands between a restricted operator and the
/// composed summary. The permission sets are the same ones the route projection uses; keeping them
/// aligned is what stops the desktop from becoming the second door to another family's records.</para>
///
/// <para>Falls back to <see cref="DesktopAuthenticationSession.HasPermission"/> for every decision,
/// which already carries the shell's fail-open rule for an unconfigured local-development host and
/// fails closed for a configured one. A null session means the shell was composed without
/// authentication at all — the same unconfigured posture — so every family is readable.</para>
/// </summary>
internal static class DesktopWorkflowReadScopeResolver
{
    public static WorkstationWorkflowReadScope Resolve(DesktopAuthenticationSession? session)
    {
        if (session is null)
        {
            return WorkstationWorkflowReadScope.All;
        }

        return new WorkstationWorkflowReadScope(
            Trading: HasAny(session, UserPermission.ViewTrades, UserPermission.AdminMaintenance),
            Accounting: HasAny(
                session,
                UserPermission.ViewTrades,
                UserPermission.ViewDirectLending,
                UserPermission.ManageDirectLending,
                UserPermission.ViewSecurityMaster,
                UserPermission.ModifySecurityMaster,
                UserPermission.AdminMaintenance),
            Strategy: HasAny(
                session,
                UserPermission.ViewStrategies,
                UserPermission.ManageStrategies,
                UserPermission.AdminMaintenance),
            Data: HasAny(
                session,
                UserPermission.ViewHistoricalData,
                UserPermission.ViewDiagnostics,
                UserPermission.ManageStorage,
                UserPermission.AdminMaintenance));
    }

    // HasPermission requires every bit of its argument, so a combined flag would mean "all of these"
    // rather than "any of these" -- the families are unions, so each is asked separately.
    private static bool HasAny(DesktopAuthenticationSession session, params UserPermission[] permissions)
    {
        foreach (var permission in permissions)
        {
            if (session.HasPermission(permission))
            {
                return true;
            }
        }

        return false;
    }
}
