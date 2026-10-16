using Meridian.Contracts.SecurityMaster;

namespace Meridian.Execution.Sdk;

/// <summary>
/// Represents an individual execution lot (a parcel of shares or contracts acquired at a
/// specific price and time). Lot-level tracking enables accurate capital-gains calculations
/// under FIFO, LIFO, HIFO, or specific-lot identification methods.
/// </summary>
/// <param name="LotId">Unique identifier for this lot.</param>
/// <param name="Symbol">Instrument symbol.</param>
/// <param name="Quantity">
/// Lot quantity in <see cref="UnitKind"/> units (positive = long, negative = short). Decimal, so
/// fractional shares, fund units and face amounts are representable.
/// </param>
/// <param name="CostBasis">Per-share or per-contract cost basis at the time of acquisition.</param>
/// <param name="OpenedAt">Wall-clock timestamp when the lot was opened.</param>
/// <param name="FinancialAccountId">
///     Optional brokerage account ID this lot belongs to.
/// </param>
public sealed record TaxLot(
    Guid LotId,
    string Symbol,
    decimal Quantity,
    decimal CostBasis,
    DateTimeOffset OpenedAt,
    string? FinancialAccountId = null) : IUnitizedLot
{
    /// <summary>What one unit of <see cref="Quantity"/> measures; shares unless stated.</summary>
    public SecurityLotUnitKind UnitKind { get; init; } = SecurityLotUnitKind.Shares;

    /// <summary>Contract multiplier scaling one unit to economic exposure; 1 unless stated.</summary>
    public decimal Multiplier { get; init; } = 1m;

    /// <summary>Total cost of the entire lot (|Quantity| × CostBasis).</summary>
    public decimal TotalCost => Math.Abs(Quantity) * CostBasis;

    /// <summary><c>true</c> when this lot represents a short position.</summary>
    public bool IsShort => Quantity < 0;

    /// <summary>Absolute (unsigned) quantity of the lot.</summary>
    public decimal AbsoluteQuantity => Math.Abs(Quantity);

    /// <summary>
    /// Returns a new lot with the quantity reduced by <paramref name="relief"/>.
    /// Throws <see cref="InvalidOperationException"/> when relief exceeds the lot size.
    /// </summary>
    public TaxLot WithReducedQuantity(decimal relief)
    {
        if (Math.Abs(relief) > AbsoluteQuantity)
            throw new InvalidOperationException(
                $"Cannot relieve {relief} units from lot {LotId} which only has {AbsoluteQuantity}.");
        return this with { Quantity = Quantity - (IsShort ? -Math.Abs(relief) : Math.Abs(relief)) };
    }
}
