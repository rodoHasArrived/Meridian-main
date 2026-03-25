namespace Meridian.Contracts.Auth;

/// <summary>
/// Fine-grained permission flags used to control access to individual platform features.
/// Roles are mapped to a combination of these flags by <see cref="RolePermissions"/>.
/// </summary>
[Flags]
public enum UserPermission : long
{
    /// <summary>No access.</summary>
    None = 0,

    // ── Market data ──────────────────────────────────────────────────────────
    /// <summary>View real-time streaming market data.</summary>
    ViewMarketData = 1L << 0,

    /// <summary>View historical bars, quotes, and trades.</summary>
    ViewHistoricalData = 1L << 1,

    // ── Backfill / collection ────────────────────────────────────────────────
    /// <summary>Trigger backfill jobs for historical data gaps.</summary>
    TriggerBackfill = 1L << 2,

    /// <summary>Add, remove, or reconfigure data providers.</summary>
    ManageProviders = 1L << 3,

    // ── Trading / execution ──────────────────────────────────────────────────
    /// <summary>View trade records and order history.</summary>
    ViewTrades = 1L << 4,

    /// <summary>Submit orders through the execution gateway.</summary>
    ExecuteTrades = 1L << 5,

    /// <summary>Modify or cancel outstanding orders.</summary>
    ManageOrders = 1L << 6,

    // ── Analytics ────────────────────────────────────────────────────────────
    /// <summary>View analytics dashboards and reports.</summary>
    ViewAnalytics = 1L << 7,

    /// <summary>Export data to CSV, Parquet, or other formats.</summary>
    ExportData = 1L << 8,

    // ── Configuration ────────────────────────────────────────────────────────
    /// <summary>Read platform configuration settings.</summary>
    ViewConfig = 1L << 9,

    /// <summary>Change platform configuration settings.</summary>
    ModifyConfig = 1L << 10,

    /// <summary>Add or rotate provider credentials.</summary>
    ManageCredentials = 1L << 11,

    // ── System administration ────────────────────────────────────────────────
    /// <summary>View diagnostic bundles and connection health.</summary>
    ViewDiagnostics = 1L << 12,

    /// <summary>Manage storage tiers, archival, and maintenance jobs.</summary>
    ManageStorage = 1L << 13,

    /// <summary>Run admin maintenance routines (WAL repair, reindex, etc.).</summary>
    AdminMaintenance = 1L << 14,

    /// <summary>Create, modify, or delete user accounts.</summary>
    ManageUsers = 1L << 15,

    // ── Strategy / backtesting ───────────────────────────────────────────────
    /// <summary>View strategy definitions and backtest results.</summary>
    ViewStrategies = 1L << 16,

    /// <summary>Create, promote, or delete strategies and run backtests.</summary>
    ManageStrategies = 1L << 17,

    // ── Security master ──────────────────────────────────────────────────────
    /// <summary>Read security master reference data.</summary>
    ViewSecurityMaster = 1L << 18,

    /// <summary>Create or update security master entries.</summary>
    ModifySecurityMaster = 1L << 19,

    // ── Direct lending ───────────────────────────────────────────────────────
    /// <summary>View direct-lending contracts and positions.</summary>
    ViewDirectLending = 1L << 20,

    /// <summary>Create and service direct-lending contracts.</summary>
    ManageDirectLending = 1L << 21,
}
