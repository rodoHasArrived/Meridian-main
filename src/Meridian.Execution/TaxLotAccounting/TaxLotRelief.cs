using Meridian.Execution.Sdk;

namespace Meridian.Execution.TaxLotAccounting;

/// <summary>
/// Represents one lot that was partially or fully relieved during a closing trade,
/// together with the realized gain or loss produced by that relief.
/// </summary>
/// <param name="Lot">The lot that was relieved (with its original cost basis and ID).</param>
/// <param name="RelievedQuantity">Number of shares/contracts consumed from this lot.</param>
/// <param name="ClosePrice">The execution price at which the lot was closed.</param>
public sealed record RelievedLot(
    TaxLot Lot,
    long RelievedQuantity,
    decimal ClosePrice)
{
    /// <summary>Realized gain/(loss) for this specific lot parcel.</summary>
    public decimal RealizedPnl =>
        (ClosePrice - Lot.CostBasis) * RelievedQuantity * (Lot.IsShort ? -1m : 1m);

    /// <summary>Proceeds received for the relieved quantity.</summary>
    public decimal Proceeds => RelievedQuantity * ClosePrice;

    /// <summary>Cost basis of the relieved quantity.</summary>
    public decimal CostRelieved => RelievedQuantity * Lot.CostBasis;
}

/// <summary>
/// The aggregate result of relieving one or more tax lots to fill a closing order.
/// </summary>
/// <param name="RelievedLots">Individual lot-level relief details.</param>
/// <param name="RemainingLots">
///     The updated open-lot list after the relief has been applied.
///     Partially consumed lots appear here with their reduced quantity.
/// </param>
public sealed record TaxLotReliefResult(
    IReadOnlyList<RelievedLot> RelievedLots,
    IReadOnlyList<TaxLot> RemainingLots)
{
    /// <summary>Total realized P&amp;L across all relieved lots.</summary>
    public decimal TotalRealizedPnl => RelievedLots.Sum(r => r.RealizedPnl);

    /// <summary>Total number of shares/contracts relieved.</summary>
    public long TotalRelievedQuantity => RelievedLots.Sum(r => r.RelievedQuantity);
}
