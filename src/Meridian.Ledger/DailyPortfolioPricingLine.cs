using Meridian.Contracts.Operations;

namespace Meridian.Ledger;

/// <summary>
/// Valuation and audit evidence for one daily-priced portfolio position.
/// </summary>
/// <param name="Provenance">
/// Origin of the mark price behind this line. Anything other than <see cref="DataProvenance.Real"/>
/// means the figure was fabricated rather than observed, and stays marked on every draft, journal
/// tag, and report that cites it.
/// </param>
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
    string? CarryingValueEvidenceReference = null,
    DataProvenance Provenance = DataProvenance.Real);
