namespace Meridian.Ledger;

/// <summary>
/// Shared open-lot consumption walk used by every lot-accounting engine (ledger tax-lot relief,
/// paper-trading positions, backtest portfolios, and metrics attribution). Walks lots in the
/// caller-chosen relief order, taking <c>min(remaining, lotQuantity)</c> from each lot and
/// splitting the final partial lot, until the requested quantity is consumed or lots run out.
/// Callers own ordering, rounding, realized-P&amp;L formulas, and how a shortfall is handled
/// (throw, clamp, or flip), so engine-specific accounting semantics stay at their call sites.
/// </summary>
public static class LotConsumption
{
    /// <summary>
    /// Consumes up to <paramref name="quantity"/> from <paramref name="orderedLots"/>, visiting
    /// lots in the given order. Lots reporting a non-positive open quantity are skipped.
    /// </summary>
    /// <param name="orderedLots">Lots in relief order (FIFO/LIFO/HIFO/specific-id, caller-defined).</param>
    /// <param name="quantity">Unsigned quantity to consume.</param>
    /// <param name="openQuantity">Returns a lot's available (unsigned) open quantity.</param>
    public static LotConsumptionResult<TLot> Consume<TLot>(
        IEnumerable<TLot> orderedLots,
        decimal quantity,
        Func<TLot, decimal> openQuantity)
    {
        ArgumentNullException.ThrowIfNull(orderedLots);
        ArgumentNullException.ThrowIfNull(openQuantity);

        var slices = new List<LotSlice<TLot>>();
        var remaining = quantity;

        foreach (var lot in orderedLots)
        {
            if (remaining <= 0m)
            {
                break;
            }

            var available = openQuantity(lot);
            if (available <= 0m)
            {
                continue;
            }

            var consumed = Math.Min(remaining, available);
            slices.Add(new LotSlice<TLot>(lot, consumed, consumed >= available));
            remaining -= consumed;
        }

        return new LotConsumptionResult<TLot>(slices, remaining);
    }
}

/// <summary>
/// One lot's share of a consumption walk.
/// </summary>
/// <param name="Lot">The source lot.</param>
/// <param name="Quantity">Unsigned quantity consumed from the lot.</param>
/// <param name="ClosesLot">True when the lot is fully consumed and should be removed.</param>
public readonly record struct LotSlice<TLot>(TLot Lot, decimal Quantity, bool ClosesLot);

/// <summary>
/// Result of a lot-consumption walk: the per-lot slices in relief order and any quantity that
/// could not be filled from the supplied lots.
/// </summary>
public sealed class LotConsumptionResult<TLot>
{
    public LotConsumptionResult(IReadOnlyList<LotSlice<TLot>> slices, decimal shortfall)
    {
        Slices = slices;
        Shortfall = shortfall;
    }

    /// <summary>Per-lot consumption slices in the order lots were relieved.</summary>
    public IReadOnlyList<LotSlice<TLot>> Slices { get; }

    /// <summary>Quantity left unfilled after all lots were exhausted (zero when fully filled).</summary>
    public decimal Shortfall { get; }

    /// <summary>True when the requested quantity was fully covered by the supplied lots.</summary>
    public bool FullyConsumed => Shortfall <= 0m;
}
