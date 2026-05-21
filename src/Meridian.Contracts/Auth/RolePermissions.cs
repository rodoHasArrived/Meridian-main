namespace Meridian.Contracts.Auth;

/// <summary>
/// Canonical mapping from <see cref="UserRole"/> to its granted <see cref="UserPermission"/> set.
/// </summary>
public static class RolePermissions
{
    // ── Per-role permission sets ─────────────────────────────────────────────

    private const UserPermission AdminPermissions =
        UserPermission.ViewMarketData |
        UserPermission.ViewHistoricalData |
        UserPermission.TriggerBackfill |
        UserPermission.ManageProviders |
        UserPermission.ViewTrades |
        UserPermission.ExecuteTrades |
        UserPermission.ManageOrders |
        UserPermission.ViewAnalytics |
        UserPermission.ExportData |
        UserPermission.ViewConfig |
        UserPermission.ModifyConfig |
        UserPermission.ManageCredentials |
        UserPermission.ViewDiagnostics |
        UserPermission.ManageStorage |
        UserPermission.AdminMaintenance |
        UserPermission.ManageUsers |
        UserPermission.ViewStrategies |
        UserPermission.ManageStrategies |
        UserPermission.ViewSecurityMaster |
        UserPermission.ModifySecurityMaster |
        UserPermission.ViewDirectLending |
        UserPermission.ManageDirectLending;

    private const UserPermission DeveloperPermissions =
        AdminPermissions & ~UserPermission.ManageUsers;

    private const UserPermission TradeDeskPermissions =
        UserPermission.ViewMarketData |
        UserPermission.ViewHistoricalData |
        UserPermission.ViewTrades |
        UserPermission.ExecuteTrades |
        UserPermission.ManageOrders |
        UserPermission.ViewAnalytics |
        UserPermission.ExportData |
        UserPermission.ViewStrategies |
        UserPermission.ManageStrategies |
        UserPermission.ViewSecurityMaster;

    private const UserPermission AnalysisPermissions =
        UserPermission.ViewMarketData |
        UserPermission.ViewHistoricalData |
        UserPermission.ViewAnalytics |
        UserPermission.ExportData |
        UserPermission.ViewStrategies |
        UserPermission.ViewSecurityMaster |
        UserPermission.ViewDirectLending;

    private const UserPermission AccountingPermissions =
        UserPermission.ViewTrades |
        UserPermission.ViewAnalytics |
        UserPermission.ExportData |
        UserPermission.ViewStrategies |
        UserPermission.ViewDirectLending |
        UserPermission.ManageDirectLending;

    private const UserPermission ExecutivePermissions =
        UserPermission.ViewMarketData |
        UserPermission.ViewHistoricalData |
        UserPermission.ViewTrades |
        UserPermission.ViewAnalytics |
        UserPermission.ExportData |
        UserPermission.ViewStrategies |
        UserPermission.ViewSecurityMaster |
        UserPermission.ViewDirectLending;

    private const UserPermission ReadOnlyPermissions =
        UserPermission.ViewMarketData |
        UserPermission.ViewHistoricalData |
        UserPermission.ViewAnalytics |
        UserPermission.ViewStrategies;

    // ── Public API ───────────────────────────────────────────────────────────

    /// <summary>
    /// Returns the <see cref="UserPermission"/> flags granted to <paramref name="role"/>.
    /// </summary>
    public static UserPermission For(UserRole role) => role switch
    {
        UserRole.Admin => AdminPermissions,
        UserRole.Developer => DeveloperPermissions,
        UserRole.TradeDesk => TradeDeskPermissions,
        UserRole.Analysis => AnalysisPermissions,
        UserRole.Accounting => AccountingPermissions,
        UserRole.Executive => ExecutivePermissions,
        UserRole.ReadOnly => ReadOnlyPermissions,
        _ => UserPermission.None
    };

    /// <summary>
    /// Returns <see langword="true"/> when <paramref name="role"/> has been granted all of the
    /// specified <paramref name="required"/> permissions.
    /// </summary>
    public static bool HasPermission(UserRole role, UserPermission required) =>
        (For(role) & required) == required;
}
