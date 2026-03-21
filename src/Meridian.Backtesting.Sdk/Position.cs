namespace Meridian.Backtesting.Sdk;

/// <summary>Per-symbol position held in the simulated portfolio.</summary>
/// <param name="Symbol">Ticker symbol.</param>
/// <param name="Quantity">Shares held; negative means short.</param>
/// <param name="AverageCostBasis">FIFO-weighted average entry price.</param>
/// <param name="UnrealizedPnl">Mark-to-market unrealised P&amp;L.</param>
/// <param name="RealizedPnl">Cumulative realised P&amp;L.</param>
public sealed record Position(
    string Symbol,
    long Quantity,
    decimal AverageCostBasis,
    decimal UnrealizedPnl,
    decimal RealizedPnl)
{
    /// <summary>True when this is a short position.</summary>
    public bool IsShort => Quantity < 0;

    /// <summary>Absolute number of shares.</summary>
    public long AbsoluteQuantity => Math.Abs(Quantity);

    /// <summary>Notional market value (signed).</summary>
    public decimal NotionalValue(decimal lastPrice) => Quantity * lastPrice;
}
