namespace Meridian.Ledger;

/// <summary>
/// Valuation and audit evidence for one daily-priced portfolio position.
/// </summary>
public sealed record DailyPortfolioPricingLine(
    string Symbol,
    decimal Quantity,
    decimal CostPrice,
    decimal MarkPrice,
    decimal CostBasis,
    decimal MarketValue,
    decimal UnrealizedGainOrLoss,
    decimal PriorCarryingValue,
    bool HasPriorCarryingValue,
    decimal MarkAdjustment,
    string PriceSource,
    string EvidenceReference,
    string PolicyId,
    string ValuationMethod,
    string? FinancialAccountId,
    string? InstrumentType,
    FairValueLevel FairValueLevel = FairValueLevel.Unclassified,
    bool IsStalePriced = false,
    DateOnly? PriceObservedOn = null,
    DailyPortfolioPriceConfidence Confidence = DailyPortfolioPriceConfidence.High,
    Guid? SecurityId = null,
    string? CarryingValueSource = null,
    DateTimeOffset? CarryingValueCapturedAtUtc = null,
    string? CarryingValueEvidenceReference = null);
