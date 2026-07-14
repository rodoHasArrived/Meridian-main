namespace Meridian.Ledger;

/// <summary>
/// Confidence assigned to a retained portfolio closing mark after provider and
/// freshness checks. Operational valuation workflows reject marks below policy.
/// </summary>
public enum DailyPortfolioPriceConfidence : byte
{
    Low = 0,
    Medium = 1,
    High = 2
}

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
        DateOnly? PriceObservedOn = null,
        DailyPortfolioPriceConfidence Confidence = DailyPortfolioPriceConfidence.High)
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
        this.PriceObservedOn = PriceObservedOn;
        this.Confidence = Confidence;
    }

    public string Symbol { get; }

    public decimal Quantity { get; }

    public decimal CostPrice { get; }

    public decimal MarkPrice { get; }

    public string PriceSource { get; }

    public string EvidenceReference { get; }

    public string? FinancialAccountId { get; }

    public string? InstrumentType { get; }

    public DateOnly? PriceObservedOn { get; }

    public DailyPortfolioPriceConfidence Confidence { get; }
}
