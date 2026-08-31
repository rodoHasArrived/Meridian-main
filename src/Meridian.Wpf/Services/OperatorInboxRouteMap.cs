using Meridian.Contracts.Api;

namespace Meridian.Wpf.Services;

/// <summary>
/// Maps an operator work item's <c>TargetRoute</c> (a workstation API route) to the desktop page
/// that owns the corresponding recovery workflow. This is the single desktop-side route table —
/// the main shell's inbox resolution and the operator readiness console both consume it so the
/// same work item never routes to different pages depending on which surface opened it.
/// </summary>
public static class OperatorInboxRouteMap
{
    public static string? ResolvePageTag(string? targetRoute)
    {
        if (string.IsNullOrWhiteSpace(targetRoute))
        {
            return null;
        }

        var normalizedRoute = targetRoute.Split('?', 2)[0].TrimEnd('/');

        // Settings provider links (e.g. /settings#alpaca-provider-setup or the
        // /settings#provider-connection-center fallback for unlinked providers) name the
        // credential/integration workflow; the desktop's provider configuration page owns it.
        if (normalizedRoute.StartsWith("/settings", StringComparison.OrdinalIgnoreCase)
            && (normalizedRoute.Contains("provider-setup", StringComparison.OrdinalIgnoreCase)
                || normalizedRoute.Contains("provider-connection-center", StringComparison.OrdinalIgnoreCase)))
        {
            return "Provider";
        }

        if (RouteEqualsOrStartsWith(normalizedRoute, UiApiRoutes.ReconciliationBreakQueue))
        {
            return "FundReconciliation";
        }

        if (RouteEqualsOrStartsWith(normalizedRoute, UiApiRoutes.OperationsContinuity))
        {
            return "OperationsContinuity";
        }

        if (RouteEqualsOrStartsWith(normalizedRoute, UiApiRoutes.WorkstationSecurityMasterSearch))
        {
            return "SecurityMaster";
        }

        if (RouteEqualsOrStartsWith(normalizedRoute, UiApiRoutes.LedgerBooks) ||
            RouteEqualsOrStartsWith(normalizedRoute, UiApiRoutes.LedgerPeriods))
        {
            return "FundTrialBalance";
        }

        if (RouteEqualsOrStartsWith(normalizedRoute, UiApiRoutes.FundAccountBrokerageSyncAccounts) ||
            normalizedRoute.Contains("/brokerage-sync", StringComparison.OrdinalIgnoreCase))
        {
            return "AccountPortfolio";
        }

        if (RouteEqualsOrStartsWith(normalizedRoute, UiApiRoutes.WorkstationTradingReadiness) ||
            RouteEqualsOrStartsWith(normalizedRoute, UiApiRoutes.ExecutionSessions) ||
            RouteEqualsOrStartsWith(normalizedRoute, UiApiRoutes.ExecutionControls))
        {
            return "TradingShell";
        }

        return null;
    }

    private static bool RouteEqualsOrStartsWith(string route, string knownRoute)
    {
        var normalizedKnown = knownRoute.TrimEnd('/');
        return route.Equals(normalizedKnown, StringComparison.OrdinalIgnoreCase)
            || route.StartsWith(normalizedKnown + "/", StringComparison.OrdinalIgnoreCase);
    }
}
