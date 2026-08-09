namespace Meridian.Ledger;

/// <summary>
/// Quantity, proceeds, and cost relieved from one tax lot. <see cref="UnitCost"/> is the effective
/// per-share cost used to relieve the shares: each lot's own recorded unit cost for lot-discrete
/// methods (FIFO/LIFO/HIFO/SpecificId), or the unit cost implied by the rounded pooled basis
/// (<see cref="CostBasis"/> / <see cref="QuantityRelieved"/>) for
/// <see cref="LedgerTaxLotReliefMethod.AverageCost"/>. Reporting the implied unit cost keeps a
/// realized-gain row consistent with its cost basis instead of showing a raw lot cost against a
/// pooled basis. For a repeating pooled average the implied unit cost is the nearest per-share value
/// to the rounded basis; <see cref="CostBasis"/> is always the authoritative amount.
/// </summary>
/// <param name="Lot">The lot the shares were relieved from.</param>
/// <param name="QuantityRelieved">Shares relieved from <paramref name="Lot"/>.</param>
/// <param name="CostBasis">Authoritative rounded cost basis for the relieved shares.</param>
/// <param name="UnitCost">Effective per-share cost described above.</param>
/// <param name="Proceeds">
/// Sale proceeds attributed to this parcel. Parcel proceeds are allocated so they sum exactly to
/// the projection's rounded total, with the rounding residual carried onto the final parcel — a
/// per-row export therefore ties to the journal rather than drifting a cent across lots.
/// </param>
/// <param name="RealizedGainOrLoss">
/// <paramref name="Proceeds"/> less <paramref name="CostBasis"/> for this parcel. This is the full
/// economic result; any wash-sale deferral is reported separately on the projection.
/// </param>
/// <param name="TaxCharacter">
/// Short- or long-term character of this parcel, classified from the lot's effective
/// holding-period start (which a prior wash sale may have moved earlier than acquisition).
/// </param>
/// <param name="HoldingPeriodDays">Whole days from the effective holding-period start to the sale date.</param>
/// <param name="HoldingPeriodExtendedByWashSale">
/// True when a prior wash sale carried an earlier holding-period start onto this lot, so the
/// character reflects the disallowed sale's holding period rather than the lot's own acquisition.
/// </param>
public sealed record LedgerTaxLotReliefSelection(
    LedgerTaxLot Lot,
    decimal QuantityRelieved,
    decimal CostBasis,
    decimal UnitCost,
    decimal Proceeds,
    decimal RealizedGainOrLoss,
    TaxCharacter TaxCharacter,
    int HoldingPeriodDays,
    bool HoldingPeriodExtendedByWashSale);
