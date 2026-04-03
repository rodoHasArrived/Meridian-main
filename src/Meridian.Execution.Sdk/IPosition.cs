namespace Meridian.Execution.Sdk;

/// <summary>
/// Cross-pillar abstraction for a position held in any portfolio — simulated (backtesting)
/// or live (execution). Allows generic portfolio rendering, attribution, and reconciliation
/// code to work with both <c>Meridian.Backtesting.Sdk.Position</c> and
/// <c>Meridian.Execution.Models.ExecutionPosition</c> without branching on the concrete type.
/// </summary>
/// <remarks>
/// Adopt this interface wherever portfolio code currently branches on
/// <c>is Position</c> or <c>is ExecutionPosition</c>.
/// Default implementations of <see cref="IsShort"/>, <see cref="AbsoluteQuantity"/>,
/// and <see cref="NotionalValue"/> are provided so concrete types that already expose
/// equivalent members are not required to redeclare them.
/// </remarks>
public interface IPosition
{
    /// <summary>Ticker symbol (upper-case, e.g. "AAPL").</summary>
    string Symbol { get; }

    /// <summary>Shares held; negative means short.</summary>
    long Quantity { get; }

    /// <summary>Lot-weighted average entry price.</summary>
    decimal AverageCostBasis { get; }

    /// <summary>Mark-to-market unrealised P&amp;L.</summary>
    decimal UnrealizedPnl { get; }

    /// <summary>Cumulative realised P&amp;L.</summary>
    decimal RealizedPnl { get; }

    /// <summary>True when this is a short (negative-quantity) position.</summary>
    bool IsShort => Quantity < 0;

    /// <summary>Absolute number of shares without sign.</summary>
    long AbsoluteQuantity => Math.Abs(Quantity);

    /// <summary>Signed notional market value at <paramref name="lastPrice"/>.</summary>
    decimal NotionalValue(decimal lastPrice) => Quantity * lastPrice;
}
