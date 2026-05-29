namespace Meridian.Ledger;

/// <summary>
/// Quantity and cost relieved from one tax lot.
/// </summary>
public sealed record LedgerTaxLotReliefSelection(
    LedgerTaxLot Lot,
    decimal QuantityRelieved,
    decimal CostBasis)
{
    public decimal UnitCost => Lot.UnitCost;
}
