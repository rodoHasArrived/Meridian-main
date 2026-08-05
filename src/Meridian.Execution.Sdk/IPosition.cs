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

    /// <summary>Mark-to-market unrealized P&amp;L.</summary>
    decimal UnrealizedPnl { get; }

    /// <summary>Cumulative realized P&amp;L.</summary>
    decimal RealizedPnl { get; }

    /// <summary>True when this is a short (negative-quantity) position.</summary>
    bool IsShort => Quantity < 0;

    /// <summary>Absolute number of shares without sign.</summary>
    long AbsoluteQuantity => Math.Abs(Quantity);

    /// <summary>Signed notional market value at <paramref name="lastPrice"/>.</summary>
    decimal NotionalValue(decimal lastPrice) => Quantity * lastPrice;

    /// <summary>
    /// Signed quantity attributed to each owning fund account, when fills carried one. A
    /// shared execution book holds several funds' flow under one account id, so without
    /// this the only account key available is the shared one — which says nothing about
    /// whose position it is. Empty when no fill carried an owner.
    /// </summary>
    IReadOnlyDictionary<string, decimal> OwnerQuantities => EmptyOwnerQuantities;

    /// <summary>
    /// Signed quantity without the whole-share rounding <see cref="Quantity"/> carries.
    /// Fractional and notional-sized fills are real, and <see cref="OwnerQuantities"/>
    /// records them exactly, so deriving the unattributed remainder from the rounded
    /// quantity invents a contribution that was never held: a 0.5-share holding attributed
    /// to one fund yields owner +0.5 against a rounded aggregate of 0, hence a phantom
    /// -0.5 residual, zero net and double gross. Defaults to <see cref="Quantity"/> for
    /// positions that only ever hold whole shares.
    /// </summary>
    decimal ExactQuantity => Quantity;

    /// <summary>
    /// Contract multiplier for a derivative position: the notional one unit of
    /// <see cref="Quantity"/> represents. 1 for outright instruments, 100 for standard
    /// equity option contracts. Exposure that ignores it under-measures an option position
    /// by the multiplier.
    /// </summary>
    decimal ContractMultiplier => 1m;

    private static IReadOnlyDictionary<string, decimal> EmptyOwnerQuantities { get; } =
        new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase);
}
