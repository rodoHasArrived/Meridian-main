namespace Meridian.Identity.Auth;

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
        UserPermission.ManageDirectLending |
        UserPermission.ManageFundStructure |
        UserPermission.ViewReporting |
        UserPermission.ManageReporting |
        UserPermission.ApproveReporting |
        UserPermission.DeliverReporting;

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
        UserPermission.ViewDirectLending |
        UserPermission.ViewReporting;

    private const UserPermission AccountingPermissions =
        UserPermission.ViewTrades |
        UserPermission.ViewAnalytics |
        UserPermission.ExportData |
        UserPermission.ViewStrategies |
        UserPermission.ViewDirectLending |
        UserPermission.ManageDirectLending |
        UserPermission.ManageFundStructure |
        UserPermission.ViewReporting |
        UserPermission.ManageReporting |
        UserPermission.ApproveReporting |
        UserPermission.DeliverReporting;

    private const UserPermission FundAccountantPermissions =
        UserPermission.ViewTrades |
        UserPermission.ViewAnalytics |
        UserPermission.ExportData |
        UserPermission.ViewDirectLending |
        UserPermission.ManageDirectLending |
        UserPermission.ManageFundStructure |
        UserPermission.ViewReporting |
        UserPermission.ManageReporting |
        UserPermission.DeliverReporting;

    private const UserPermission ReportingAnalystPermissions =
        UserPermission.ViewAnalytics |
        UserPermission.ExportData |
        UserPermission.ViewStrategies |
        UserPermission.ViewSecurityMaster |
        UserPermission.ViewReporting |
        UserPermission.ManageReporting;

    private const UserPermission ControllerPermissions =
        UserPermission.ViewTrades |
        UserPermission.ViewAnalytics |
        UserPermission.ExportData |
        UserPermission.ViewDirectLending |
        UserPermission.ManageDirectLending |
        UserPermission.ManageFundStructure |
        UserPermission.ViewReporting |
        UserPermission.ManageReporting |
        UserPermission.ApproveReporting |
        UserPermission.DeliverReporting;

    private const UserPermission CompliancePermissions =
        UserPermission.ViewTrades |
        UserPermission.ViewAnalytics |
        UserPermission.ExportData |
        UserPermission.ViewSecurityMaster |
        UserPermission.ViewDirectLending |
        UserPermission.ViewReporting |
        UserPermission.ApproveReporting |
        UserPermission.DeliverReporting;

    private const UserPermission ExecutivePermissions =
        UserPermission.ViewMarketData |
        UserPermission.ViewHistoricalData |
        UserPermission.ViewTrades |
        UserPermission.ViewAnalytics |
        UserPermission.ExportData |
        UserPermission.ViewStrategies |
        UserPermission.ViewSecurityMaster |
        UserPermission.ViewDirectLending |
        UserPermission.ViewReporting;

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
        UserRole.FundAccountant => FundAccountantPermissions,
        UserRole.ReportingAnalyst => ReportingAnalystPermissions,
        UserRole.Controller => ControllerPermissions,
        UserRole.Compliance => CompliancePermissions,
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

    public static RolePermissionCatalogDto GetCatalog()
    {
        var permissions = Enum.GetValues<UserPermission>()
            .Where(static permission => permission != UserPermission.None && IsSingleFlag(permission))
            .Select(static permission => new PermissionCatalogItemDto(
                permission.ToString(),
                (long)permission,
                GetPermissionGroup(permission),
                GetPermissionDescription(permission)))
            .OrderBy(static permission => permission.Value)
            .ToList();

        var roles = Enum.GetValues<UserRole>()
            .Select(static role => new RolePermissionProfileDto(
                role.ToString(),
                GetRoleDisplayName(role),
                GetRoleDescription(role),
                IsBuiltIn: true,
                GetPermissionNames(For(role)),
                (long)For(role)))
            .ToList();

        return new RolePermissionCatalogDto(roles, permissions);
    }

    public static IReadOnlyList<string> GetPermissionNames(UserPermission permissions) =>
        Enum.GetValues<UserPermission>()
            .Where(static permission => permission != UserPermission.None && IsSingleFlag(permission))
            .Where(permission => (permissions & permission) == permission)
            .Select(static permission => permission.ToString())
            .ToList();

    public static bool TryParsePermissionNames(
        IEnumerable<string>? permissionNames,
        out UserPermission permissions,
        out IReadOnlyList<string> invalidPermissionNames)
    {
        permissions = UserPermission.None;
        var invalid = new List<string>();

        if (permissionNames is null)
        {
            invalidPermissionNames = [];
            return true;
        }

        foreach (var rawName in permissionNames)
        {
            if (string.IsNullOrWhiteSpace(rawName))
            {
                continue;
            }

            if (Enum.TryParse<UserPermission>(rawName.Trim(), ignoreCase: true, out var parsed)
                && parsed != UserPermission.None)
            {
                permissions |= parsed;
            }
            else
            {
                invalid.Add(rawName);
            }
        }

        invalidPermissionNames = invalid;
        return invalid.Count == 0;
    }

    private static bool IsSingleFlag(UserPermission permission)
    {
        var value = (long)permission;
        return value > 0 && (value & (value - 1)) == 0;
    }

    private static string GetRoleDisplayName(UserRole role) => role switch
    {
        UserRole.Admin => "Administrator",
        UserRole.Developer => "Developer",
        UserRole.TradeDesk => "Trade Desk",
        UserRole.Analysis => "Analysis",
        UserRole.Accounting => "Accounting",
        UserRole.FundAccountant => "Fund Accountant",
        UserRole.ReportingAnalyst => "Reporting Analyst",
        UserRole.Controller => "Controller",
        UserRole.Compliance => "Compliance",
        UserRole.Executive => "Executive",
        UserRole.ReadOnly => "Read-only",
        _ => role.ToString()
    };

    private static string GetRoleDescription(UserRole role) => role switch
    {
        UserRole.Admin => "Full platform administration including users, configuration, credentials, storage, trading, and governed operations.",
        UserRole.Developer => "Broad development and diagnostic access without user-management authority.",
        UserRole.TradeDesk => "Trading desk access for market data, orders, strategy operation, and execution review.",
        UserRole.Analysis => "Research and analytics access for market data, strategy results, and read-only reference data.",
        UserRole.Accounting => "Accounting and fund-operations access for trade records, exports, and direct-lending operations.",
        UserRole.FundAccountant => "Fund-accounting access for reporting packages, delivery readiness, and retained evidence operations.",
        UserRole.ReportingAnalyst => "Reporting operations access for templates, schedules, runs, and pre-approval evidence review.",
        UserRole.Controller => "Controller access for governed reporting approval, publication, restatement, and delivery oversight.",
        UserRole.Compliance => "Compliance access for reporting evidence, approvals, delivery posture, and retained audit context.",
        UserRole.Executive => "Read-only management visibility across dashboards, trades, analytics, and fund operations.",
        UserRole.ReadOnly => "Minimal read-only workstation access.",
        _ => "Custom role."
    };

    private static string GetPermissionGroup(UserPermission permission) => permission switch
    {
        UserPermission.ViewMarketData or UserPermission.ViewHistoricalData => "Market data",
        UserPermission.TriggerBackfill or UserPermission.ManageProviders => "Provider and ingestion",
        UserPermission.ViewTrades or UserPermission.ExecuteTrades or UserPermission.ManageOrders => "Trading",
        UserPermission.ViewAnalytics or UserPermission.ExportData => "Analytics and exports",
        UserPermission.ViewConfig or UserPermission.ModifyConfig or UserPermission.ManageCredentials => "Configuration",
        UserPermission.ViewDiagnostics or UserPermission.ManageStorage or UserPermission.AdminMaintenance or UserPermission.ManageUsers => "Administration",
        UserPermission.ViewStrategies or UserPermission.ManageStrategies => "Strategy",
        UserPermission.ViewSecurityMaster or UserPermission.ModifySecurityMaster => "Security Master",
        UserPermission.ViewDirectLending or UserPermission.ManageDirectLending => "Direct lending",
        UserPermission.ManageFundStructure => "Fund structure",
        UserPermission.ViewReporting or UserPermission.ManageReporting or UserPermission.ApproveReporting or UserPermission.DeliverReporting => "Reporting",
        _ => "Other"
    };

    private static string GetPermissionDescription(UserPermission permission) => permission switch
    {
        UserPermission.ViewMarketData => "View real-time streaming market data.",
        UserPermission.ViewHistoricalData => "View historical bars, quotes, and trades.",
        UserPermission.TriggerBackfill => "Trigger backfill jobs for historical data gaps.",
        UserPermission.ManageProviders => "Add, remove, or reconfigure data providers.",
        UserPermission.ViewTrades => "View trade records and order history.",
        UserPermission.ExecuteTrades => "Submit orders through the execution gateway.",
        UserPermission.ManageOrders => "Modify or cancel outstanding orders.",
        UserPermission.ViewAnalytics => "View analytics dashboards and reports.",
        UserPermission.ExportData => "Export data to CSV, Parquet, or other formats.",
        UserPermission.ViewConfig => "Read platform configuration settings.",
        UserPermission.ModifyConfig => "Change platform configuration settings.",
        UserPermission.ManageCredentials => "Add or rotate provider credentials.",
        UserPermission.ViewDiagnostics => "View diagnostic bundles and connection health.",
        UserPermission.ManageStorage => "Manage storage tiers, archival, and maintenance jobs.",
        UserPermission.AdminMaintenance => "Run admin maintenance routines.",
        UserPermission.ManageUsers => "Create, modify, or delete user accounts.",
        UserPermission.ViewStrategies => "View strategy definitions and backtest results.",
        UserPermission.ManageStrategies => "Create, promote, or delete strategies and run backtests.",
        UserPermission.ViewSecurityMaster => "Read Security Master reference data.",
        UserPermission.ModifySecurityMaster => "Create or update Security Master entries.",
        UserPermission.ViewDirectLending => "View direct-lending contracts and positions.",
        UserPermission.ManageDirectLending => "Create and service direct-lending contracts.",
        UserPermission.ManageFundStructure => "Create or modify fund-structure ownership and governance records.",
        UserPermission.ViewReporting => "View governed reporting runs, schedules, delivery posture, and evidence.",
        UserPermission.ManageReporting => "Create and manage reporting templates, schedules, runs, and work packages.",
        UserPermission.ApproveReporting => "Approve, reject, publish, restate, and archive governed report packs.",
        UserPermission.DeliverReporting => "Deliver report packs and record delivery failures or retry evidence.",
        _ => permission.ToString()
    };
}

public sealed record RolePermissionCatalogDto(
    IReadOnlyList<RolePermissionProfileDto> Roles,
    IReadOnlyList<PermissionCatalogItemDto> Permissions);

public sealed record RolePermissionProfileDto(
    string Role,
    string DisplayName,
    string Description,
    bool IsBuiltIn,
    IReadOnlyList<string> Permissions,
    long PermissionMask,
    string? BaseRole = null,
    string? CreatedBy = null,
    DateTimeOffset? CreatedAtUtc = null,
    string? UpdatedBy = null,
    DateTimeOffset? UpdatedAtUtc = null,
    string? LastRationale = null,
    string? LastAuditId = null);

public sealed record PermissionCatalogItemDto(
    string Name,
    long Value,
    string Group,
    string Description);

public sealed record RolePermissionProfileUpsertRequestDto(
    string ProfileName,
    string DisplayName,
    string? Description,
    string BaseRole,
    IReadOnlyList<string> PermissionNames,
    string RequestedBy,
    string Rationale,
    string? CorrelationId = null);

public sealed record RolePermissionProfileUpsertResultDto(
    RolePermissionProfileDto Profile,
    RolePermissionCatalogDto Catalog,
    RolePermissionProfileAuditEventDto AuditEvent);

public sealed record RolePermissionProfileAuditEventDto(
    string AuditId,
    string EventType,
    DateTimeOffset OccurredAtUtc,
    string Actor,
    string Rationale,
    string CorrelationId,
    string ProfileName,
    string BaseRole,
    IReadOnlyList<string> PermissionNames,
    long PermissionMask);
