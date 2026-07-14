namespace Meridian.Ledger;

/// <summary>
/// Quantity and cost relieved from one tax lot. <see cref="UnitCost"/> is the effective per-share
/// cost actually used to relieve the shares — each lot's own unit cost for lot-discrete methods, or
/// the pooled average for <see cref="LedgerTaxLotReliefMethod.AverageCost"/> — so it stays
/// consistent with <see cref="CostBasis"/> in realized-gain reports rather than reporting the raw
/// lot cost against a pooled basis.
/// </summary>
public sealed record LedgerTaxLotReliefSelection(
    LedgerTaxLot Lot,
    decimal QuantityRelieved,
    decimal CostBasis,
    decimal UnitCost);
