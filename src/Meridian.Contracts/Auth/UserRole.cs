namespace Meridian.Contracts.Auth;

/// <summary>
/// Defines the built-in user roles for profile-based access control.
/// Each role maps to a fixed set of <see cref="UserPermission"/> flags via
/// <see cref="RolePermissions"/>.
/// </summary>
public enum UserRole
{
    /// <summary>
    /// Platform administrator. Full access to all features including user management,
    /// configuration, and system diagnostics.
    /// </summary>
    Admin,

    /// <summary>
    /// Software developer / platform engineer. Broad access for development and
    /// debugging purposes; cannot manage users.
    /// </summary>
    Developer,

    /// <summary>
    /// Trading desk operator. Access to market data, trade execution, strategy
    /// management, and analytics.
    /// </summary>
    TradeDesk,

    /// <summary>
    /// Quant / research analyst. Read-only access to market and historical data,
    /// analytics, and strategy results.
    /// </summary>
    Analysis,

    /// <summary>
    /// Accounting / finance team. Access to trade records, P&amp;L, direct lending,
    /// and export functionality.
    /// </summary>
    Accounting,

    /// <summary>
    /// Executive / management view. Read-only dashboard access across all
    /// business domains.
    /// </summary>
    Executive,

    /// <summary>
    /// Read-only guest access to market data and analytics dashboards only.
    /// </summary>
    ReadOnly
}
