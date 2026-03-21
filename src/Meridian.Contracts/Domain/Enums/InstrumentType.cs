namespace Meridian.Contracts.Domain.Enums;

/// <summary>
/// Classification of financial instruments supported by the system.
/// </summary>
public enum InstrumentType : byte
{
    /// <summary>
    /// Equity security (common stock, ETF, ADR).
    /// </summary>
    Equity = 0,

    /// <summary>
    /// Exchange-traded option on an individual equity or ETF.
    /// </summary>
    EquityOption = 1,

    /// <summary>
    /// Exchange-traded option on a market index (e.g., SPX, NDX, RUT, VIX).
    /// Typically European-style, cash-settled.
    /// </summary>
    IndexOption = 2,

    /// <summary>
    /// Exchange-traded futures contract.
    /// </summary>
    Future = 3,

    /// <summary>
    /// Single stock future — a futures contract on an individual equity.
    /// </summary>
    SingleStockFuture = 4
}
