using Meridian.Contracts.Operations;

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
        FairValueLevel FairValueLevel = FairValueLevel.Unclassified,
        bool IsStalePriced = false,
        DateOnly? PriceObservedOn = null,
        DailyPortfolioPriceConfidence Confidence = DailyPortfolioPriceConfidence.High,
        Guid? SecurityId = null,
        decimal? PriorCarryingValue = null,
        string? CarryingValueSource = null,
        DateTimeOffset? CarryingValueCapturedAtUtc = null,
        string? CarryingValueEvidenceReference = null,
        DataProvenance Provenance = DataProvenance.Real)
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
        if (SecurityId == Guid.Empty)
            throw new ArgumentException("Security identifier cannot be empty when supplied.", nameof(SecurityId));
        if (PriorCarryingValue.HasValue && string.IsNullOrWhiteSpace(CarryingValueSource))
        {
            throw new ArgumentException(
                "Carrying-value source is required when a prior carrying value is supplied.",
                nameof(CarryingValueSource));
        }

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
        this.PriceObservedOn = PriceObservedOn;
        this.Confidence = Confidence;
        this.SecurityId = SecurityId;
        this.PriorCarryingValue = PriorCarryingValue;
        this.CarryingValueSource = string.IsNullOrWhiteSpace(CarryingValueSource) ? null : CarryingValueSource.Trim();
        this.CarryingValueCapturedAtUtc = CarryingValueCapturedAtUtc?.ToUniversalTime();
        this.CarryingValueEvidenceReference = string.IsNullOrWhiteSpace(CarryingValueEvidenceReference)
            ? null
            : CarryingValueEvidenceReference.Trim();
        this.Provenance = Provenance;
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

    public DateOnly? PriceObservedOn { get; }

    public DailyPortfolioPriceConfidence Confidence { get; }

    /// <summary>Canonical Security Master identity used by governed posting controls.</summary>
    public Guid? SecurityId { get; }

    /// <summary>
    /// Durable carrying value before this mark. A null value means the carrying-value source
    /// explicitly reported that the securities account does not yet exist; zero means the
    /// account exists with a zero balance.
    /// </summary>
    public decimal? PriorCarryingValue { get; }

    public string? CarryingValueSource { get; }

    public DateTimeOffset? CarryingValueCapturedAtUtc { get; }

    public string? CarryingValueEvidenceReference { get; }

    /// <summary>
    /// Origin of the mark price. Anything other than <see cref="DataProvenance.Real"/> means the
    /// figure was fabricated rather than observed, and must be carried outward — onto the valuation
    /// line, the journal draft, and every report that cites it — so it is never mistaken for a real
    /// market price.
    /// </summary>
    public DataProvenance Provenance { get; }
}
