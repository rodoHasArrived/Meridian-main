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
    string PriceSource,
    string EvidenceReference,
    string PolicyId,
    string ValuationMethod,
    string? FinancialAccountId,
    string? InstrumentType,
    DateOnly? PriceObservedOn = null,
    DailyPortfolioPriceConfidence Confidence = DailyPortfolioPriceConfidence.High);
