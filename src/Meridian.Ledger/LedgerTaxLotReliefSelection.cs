namespace Meridian.Ledger;

/// <summary>
/// Quantity and cost relieved from one tax lot. <see cref="UnitCost"/> is the effective per-share
/// cost used to relieve the shares: each lot's own recorded unit cost for lot-discrete methods
/// (FIFO/LIFO/HIFO/SpecificId), or the unit cost implied by the rounded pooled basis
/// (<see cref="CostBasis"/> / <see cref="QuantityRelieved"/>) for
/// <see cref="LedgerTaxLotReliefMethod.AverageCost"/>. Reporting the implied unit cost keeps a
/// realized-gain row consistent with its cost basis instead of showing a raw lot cost against a
/// pooled basis. For a repeating pooled average the implied unit cost is the nearest per-share value
/// to the rounded basis; <see cref="CostBasis"/> is always the authoritative amount.
/// </summary>
public sealed record LedgerTaxLotReliefSelection(
    LedgerTaxLot Lot,
    decimal QuantityRelieved,
    decimal CostBasis,
    decimal UnitCost);
