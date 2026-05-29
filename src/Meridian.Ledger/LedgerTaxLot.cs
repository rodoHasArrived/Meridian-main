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
        decimal unitCost)
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
    }

    public string LotId { get; }

    public DateOnly AcquiredDate { get; }

    public decimal Quantity { get; }

    public decimal UnitCost { get; }
}
