using Meridian.Execution.Sdk;

namespace Meridian.Execution.Models;

/// <summary>
/// A position held in the live portfolio as tracked by the execution layer.
/// This is the Execution-pillar equivalent of <c>Meridian.Backtesting.Sdk.Position</c>
/// and exists so that the Execution pillar does not depend on Backtesting infrastructure
/// (per ADR-016 pillar isolation rules).
/// </summary>
/// <param name="Symbol">Ticker symbol (e.g., "AAPL").</param>
/// <param name="Quantity">Shares held; negative means short.</param>
/// <param name="AverageCostBasis">FIFO-weighted average entry price.</param>
/// <param name="UnrealisedPnl">Mark-to-market unrealised P&amp;L.</param>
/// <param name="RealisedPnl">Cumulative realised P&amp;L since session start.</param>
public sealed record ExecutionPosition(
    string Symbol,
    long Quantity,
    decimal AverageCostBasis,
    decimal UnrealisedPnl,
    decimal RealisedPnl) : IPosition
{
    /// <summary>True when this is a short (negative) position.</summary>
    public bool IsShort => Quantity < 0;

    /// <summary>Absolute number of shares without sign.</summary>
    public long AbsoluteQuantity => Math.Abs(Quantity);

    /// <summary>Signed notional market value at <paramref name="lastPrice"/>.</summary>
    public decimal NotionalValue(decimal lastPrice) => Quantity * lastPrice;

    // ── IPosition explicit implementations ──────────────────────────────────
    // ExecutionPosition uses the British spelling (UnrealisedPnl / RealisedPnl) while
    // IPosition standardises on the American spelling (UnrealizedPnl / RealizedPnl).
    // Explicit implementations bridge the naming gap without renaming the record parameters,
    // which would be a breaking wire-format change for JSON serialisation.

    /// <inheritdoc cref="IPosition.UnrealizedPnl"/>
    decimal IPosition.UnrealizedPnl => UnrealisedPnl;

    /// <inheritdoc cref="IPosition.RealizedPnl"/>
    decimal IPosition.RealizedPnl => RealisedPnl;
}
