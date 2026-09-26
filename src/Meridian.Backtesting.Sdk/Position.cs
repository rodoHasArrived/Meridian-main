namespace Meridian.Backtesting.Sdk;

/// <summary>Per-symbol position held in the simulated portfolio.</summary>
/// <param name="Symbol">Ticker symbol.</param>
/// <param name="Quantity">Shares held; negative means short.</param>
/// <param name="AverageCostBasis">Lot-weighted average entry price.</param>
/// <param name="UnrealizedPnl">Mark-to-market unrealised P&amp;L.</param>
/// <param name="RealizedPnl">Cumulative realised P&amp;L.</param>
/// <param name="OpenLots">Individual open lots contributing to this position (empty when not tracked).</param>
public sealed record Position(
    string Symbol,
    long Quantity,
    decimal AverageCostBasis,
    decimal UnrealizedPnl,
    decimal RealizedPnl,
    IReadOnlyList<OpenLot>? OpenLots = null)
{
    private readonly decimal? _exactQuantity;

    /// <summary>
    /// Signed quantity before the whole-share rounding <see cref="Quantity"/> applies.
    /// Falls back to <see cref="Quantity"/> when a producer did not carry a fractional
    /// size, so positions that only ever hold whole shares are unaffected.
    /// </summary>
    /// <remarks>
    /// Mirrors <c>Meridian.Execution.Sdk.IPosition.ExactQuantity</c> so a strategy reading
    /// either portfolio shape sees the same value under the same name. A strategy deciding
    /// whether a holding is its own has to see the exact size: a 0.9-share position held by
    /// someone else rounds to zero in <see cref="Quantity"/> and would otherwise look like
    /// no position at all.
    /// </remarks>
    public decimal ExactQuantity
    {
        get => _exactQuantity ?? Quantity;
        init => _exactQuantity = value;
    }

    /// <summary>True when this is a short position.</summary>
    public bool IsShort => Quantity < 0;

    /// <summary>Absolute number of shares.</summary>
    public long AbsoluteQuantity => Math.Abs(Quantity);

    /// <summary>Notional market value (signed).</summary>
    public decimal NotionalValue(decimal lastPrice) => Quantity * lastPrice;
}
