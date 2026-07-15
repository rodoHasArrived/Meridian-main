namespace Meridian.Ledger;

/// <summary>
/// Open ledger tax lot available for realized-gain relief.
/// </summary>
public sealed record LedgerTaxLot
{
    public LedgerTaxLot(
        string lotId,
        DateOnly acquiredDate,
        decimal quantity,
        decimal unitCost,
        Guid? securityId = null)
    {
        if (string.IsNullOrWhiteSpace(lotId))
            throw new ArgumentException("Tax lot identifier must not be null or whitespace.", nameof(lotId));
        if (quantity <= 0m)
            throw new ArgumentOutOfRangeException(nameof(quantity), quantity, "Tax lot quantity must be positive.");
        if (unitCost < 0m)
            throw new ArgumentOutOfRangeException(nameof(unitCost), unitCost, "Tax lot unit cost cannot be negative.");

        LotId = lotId.Trim();
        AcquiredDate = acquiredDate;
        Quantity = quantity;
        UnitCost = unitCost;
        SecurityId = securityId;
    }

    public string LotId { get; }

    public DateOnly AcquiredDate { get; }

    public decimal Quantity { get; }

    public decimal UnitCost { get; }

    /// <summary>
    /// Optional Security Master identity for this lot. When present, the relief engine can be
    /// fed day-count, factor, and corporate-action basis adjustments sourced from the master
    /// (see <see cref="LedgerTaxLotBasisAdjustment"/>), linking cost-basis accounting to
    /// authoritative reference data instead of relying on quantity/unit-cost alone.
    /// </summary>
    public Guid? SecurityId { get; }
}
