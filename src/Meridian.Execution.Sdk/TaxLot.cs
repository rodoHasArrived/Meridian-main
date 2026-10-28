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
/// <param name="CostBasis">Raw acquisition quote per unit; percent of par for face-value lots and before multiplier for contracts.</param>
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
    private SecurityLotUnitKind _unitKind;
    private decimal _multiplier;

    /// <summary>What one unit of <see cref="Quantity"/> measures. Must be supplied explicitly.</summary>
    public required SecurityLotUnitKind UnitKind
    {
        get => _unitKind;
        init => _unitKind = Enum.IsDefined(value)
            ? value
            : throw new ArgumentOutOfRangeException(nameof(UnitKind), value, "A defined lot unit is required.");
    }

    /// <summary>Positive contract multiplier scaling one unit to economic exposure. Supply 1 for unscaled units.</summary>
    public required decimal Multiplier
    {
        get => _multiplier;
        init => _multiplier = value > 0m
            ? value
            : throw new ArgumentOutOfRangeException(nameof(Multiplier), value, "A positive lot multiplier is required.");
    }

    /// <summary>Total cost of the entire lot, applying its denomination and multiplier.</summary>
    public decimal TotalCost => new LotQuantity(Math.Abs(Quantity), UnitKind, Multiplier).ValueAtPrice(CostBasis);

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
