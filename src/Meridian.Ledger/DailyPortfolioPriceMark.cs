namespace Meridian.Ledger;

/// <summary>
/// Daily mark and evidence for one valued portfolio position.
/// </summary>
public sealed record DailyPortfolioPriceMark
{
    public DailyPortfolioPriceMark(
        string Symbol,
        decimal Quantity,
        decimal CostPrice,
        decimal MarkPrice,
        string PriceSource,
        string EvidenceReference,
        string? FinancialAccountId = null,
        string? InstrumentType = null,
        FairValueLevel FairValueLevel = FairValueLevel.Unclassified,
        bool IsStalePriced = false)
    {
        if (string.IsNullOrWhiteSpace(Symbol))
            throw new ArgumentException("Symbol must not be null or whitespace.", nameof(Symbol));
        if (Quantity <= 0m)
            throw new ArgumentOutOfRangeException(nameof(Quantity), Quantity, "Position quantity must be positive.");
        if (CostPrice < 0m)
            throw new ArgumentOutOfRangeException(nameof(CostPrice), CostPrice, "Cost price cannot be negative.");
        if (MarkPrice < 0m)
            throw new ArgumentOutOfRangeException(nameof(MarkPrice), MarkPrice, "Mark price cannot be negative.");
        if (string.IsNullOrWhiteSpace(PriceSource))
            throw new ArgumentException("Price source must not be null or whitespace.", nameof(PriceSource));
        if (string.IsNullOrWhiteSpace(EvidenceReference))
            throw new ArgumentException("Price evidence reference must not be null or whitespace.", nameof(EvidenceReference));

        this.Symbol = Symbol.Trim().ToUpperInvariant();
        this.Quantity = Quantity;
        this.CostPrice = CostPrice;
        this.MarkPrice = MarkPrice;
        this.PriceSource = PriceSource.Trim();
        this.EvidenceReference = EvidenceReference.Trim();
        this.FinancialAccountId = string.IsNullOrWhiteSpace(FinancialAccountId) ? null : FinancialAccountId.Trim();
        this.InstrumentType = string.IsNullOrWhiteSpace(InstrumentType) ? null : InstrumentType.Trim();
        this.FairValueLevel = FairValueLevel;
        this.IsStalePriced = IsStalePriced;
    }

    public string Symbol { get; }

    public decimal Quantity { get; }

    public decimal CostPrice { get; }

    public decimal MarkPrice { get; }

    public string PriceSource { get; }

    public string EvidenceReference { get; }

    public string? FinancialAccountId { get; }

    public string? InstrumentType { get; }

    /// <summary>ASC 820 fair-value classification of the mark price.</summary>
    public FairValueLevel FairValueLevel { get; }

    /// <summary>True when the mark price was older than the valuation policy permitted but retained per policy.</summary>
    public bool IsStalePriced { get; }
}
