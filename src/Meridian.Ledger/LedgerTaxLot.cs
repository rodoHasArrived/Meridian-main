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
        Guid? securityId = null,
        DateOnly? holdingPeriodStartDate = null)
    {
        if (string.IsNullOrWhiteSpace(lotId))
            throw new ArgumentException("Tax lot identifier must not be null or whitespace.", nameof(lotId));
        if (quantity <= 0m)
            throw new ArgumentOutOfRangeException(nameof(quantity), quantity, "Tax lot quantity must be positive.");
        if (unitCost < 0m)
            throw new ArgumentOutOfRangeException(nameof(unitCost), unitCost, "Tax lot unit cost cannot be negative.");
        if (holdingPeriodStartDate is { } holdingStart && holdingStart > acquiredDate)
        {
            throw new ArgumentOutOfRangeException(
                nameof(holdingPeriodStartDate),
                holdingStart,
                "A carried holding-period start cannot be later than the lot's acquisition date; a wash sale only ever extends a holding period backward.");
        }

        LotId = lotId.Trim();
        AcquiredDate = acquiredDate;
        Quantity = quantity;
        UnitCost = unitCost;
        SecurityId = securityId;
        HoldingPeriodStartDate = holdingPeriodStartDate;
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

    /// <summary>
    /// Holding-period start carried onto this lot by a prior wash sale, when one applied. Under
    /// IRC §1223(3) a replacement lot inherits the disallowed sale's holding period, so a lot that
    /// absorbed a wash sale starts its clock before it was actually acquired. Null for a lot whose
    /// holding period simply starts at <see cref="AcquiredDate"/>.
    /// </summary>
    public DateOnly? HoldingPeriodStartDate { get; }

    /// <summary>
    /// The date the lot's holding period actually starts: <see cref="HoldingPeriodStartDate"/> when
    /// a wash sale extended it, otherwise <see cref="AcquiredDate"/>.
    /// </summary>
    public DateOnly HoldingPeriodStart => HoldingPeriodStartDate ?? AcquiredDate;
}
