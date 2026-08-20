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
///
/// <para>With one exception, which exists because the fail-open rule is where the two lanes disagree.
/// A credential-free host that names an anonymous role in <c>MDC_ANONYMOUS_ROLE</c> has made an
/// explicit choice about what an unauthenticated operator may see, and the browser honours it; the
/// desktop's <c>HasPermission</c> answers true to everything for the same host, so a ReadOnly or
/// Analysis anonymous role would withhold cards in one lane and hand them over in the other. When
/// that variable names a role, the scope is resolved from that role's permissions instead. When it
/// names nothing, the shell's own fail-open decision stands unchanged — the browser refuses such a
/// host outright, and silently blanking the desktop is not the way to mirror a refusal.</para>
/// </summary>
internal static class DesktopWorkflowReadScopeResolver
{
    private const string AnonymousRoleEnvironmentVariable = "MDC_ANONYMOUS_ROLE";

    public static WorkstationWorkflowReadScope Resolve(DesktopAuthenticationSession? session)
    {
        if (TryResolveConfiguredAnonymousScope(session, out var anonymousScope))
        {
            return anonymousScope;
        }

        if (session is null)
        {
            return WorkstationWorkflowReadScope.All;
        }

        return new WorkstationWorkflowReadScope(
            Trading: HasAny(session, UserPermission.ViewTrades),
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
                UserPermission.ManageStrategies),
            Data: HasAny(
                session,
                UserPermission.ViewHistoricalData,
                UserPermission.ViewDiagnostics,
                UserPermission.ManageStorage));
    }

    /// <summary>
    /// The scope a named anonymous role grants, when this host runs credential-free and has named one.
    /// Resolved from <see cref="RolePermissions"/> rather than from the session, because the session
    /// answers true to every permission on such a host and would therefore ignore the choice.
    /// </summary>
    private static bool TryResolveConfiguredAnonymousScope(
        DesktopAuthenticationSession? session,
        out WorkstationWorkflowReadScope scope)
    {
        scope = WorkstationWorkflowReadScope.All;
        if (session is not null && !session.CanContinueWithoutCredentials)
        {
            return false;
        }

        var configured = Environment.GetEnvironmentVariable(AnonymousRoleEnvironmentVariable);
        if (string.IsNullOrWhiteSpace(configured) ||
            !Enum.TryParse<UserRole>(configured.Trim(), ignoreCase: true, out var role))
        {
            return false;
        }

        var granted = RolePermissions.For(role);
        scope = new WorkstationWorkflowReadScope(
            Trading: HasAny(granted, UserPermission.ViewTrades),
            Accounting: HasAny(
                granted,
                UserPermission.ViewTrades,
                UserPermission.ViewDirectLending,
                UserPermission.ManageDirectLending,
                UserPermission.ViewSecurityMaster,
                UserPermission.ModifySecurityMaster,
                UserPermission.AdminMaintenance),
            Strategy: HasAny(granted, UserPermission.ViewStrategies, UserPermission.ManageStrategies),
            Data: HasAny(
                granted,
                UserPermission.ViewHistoricalData,
                UserPermission.ViewDiagnostics,
                UserPermission.ManageStorage));
        return true;
    }

    private static bool HasAny(UserPermission granted, params UserPermission[] permissions)
    {
        foreach (var permission in permissions)
        {
            if ((granted & permission) == permission)
            {
                return true;
            }
        }

        return false;
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
