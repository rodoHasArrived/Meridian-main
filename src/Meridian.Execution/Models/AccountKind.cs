namespace Meridian.Execution.Models;

/// <summary>
/// Categorises a tracked account within the execution layer.
/// Mirrors <c>Meridian.Backtesting.Sdk.FinancialAccountKind</c> without introducing
/// a cross-pillar dependency (ADR-016 pillar isolation).
/// </summary>
public enum AccountKind
{
    /// <summary>
    /// An equity or margin brokerage account that supports order placement,
    /// short selling, and margin financing (e.g. Alpaca, Interactive Brokers).
    /// </summary>
    Brokerage,

    /// <summary>
    /// A cash / money-market / bank account that holds liquid reserves.
    /// Supports interest accrual but not direct order placement.
    /// </summary>
    Bank,
}
