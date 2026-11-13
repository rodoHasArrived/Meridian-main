using System.Text.Json.Serialization;

namespace Meridian.Contracts.FixedIncome;

[JsonConverter(typeof(JsonStringEnumConverter<BondWorkflowPriceBasis>))]
public enum BondWorkflowPriceBasis { NominalClean, RealClean }

/// <summary>Analytical only. Face is original contractual face; quotes are per 100 current face.</summary>
public sealed record BondWorkflowRequest(Guid SecurityId, BondCalculationContext Context,
    decimal OriginalFace = 100m, decimal? CleanPricePer100 = null, DateOnly? QuoteSettlementDate = null,
    decimal Fees = 0m, BondWorkflowPriceBasis PriceBasis = BondWorkflowPriceBasis.NominalClean,
    decimal RateShiftBps = 0m, decimal SpreadShiftBps = 0m)
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ModelVersion { get; init; }
    public Guid? QuoteObservationId { get; init; }
    public Guid? PriceSelectionReceiptId { get; init; }
    public string? AccountId { get; init; }
    public IReadOnlyList<BondWorkflowRatePillar> RatePillars { get; init; } = [];
    public decimal? ObservedDirtyCash { get; init; }
    public string? ConfirmationEvidenceReference { get; init; }
}
public sealed record BondWorkflowRatePillar(DateOnly Date, decimal ShiftBps);
public sealed record BondWorkflowExerciseResult(string Kind, DateOnly ExerciseDate, decimal RedemptionPrice,
    decimal? Yield, string? Issue, DateOnly? LatestNoticeDate);

public sealed record BondWorkflowIssue(string Operation, string Code, string Reason, bool BlocksCalculation = false);
public sealed record BondWorkflowMetric(string Name, bool Available, string? Reason = null);

/// <summary>Frozen daily/simple fixing evidence; annual rates are fractions and cash is signed.</summary>
public sealed record BondWorkflowObservation(DateOnly AccrualDate, DateOnly ObservationDate,
    int Days, decimal Rate, bool IsObserved, string Source);

/// <summary>All cash is absolute transaction currency on the requested original face.</summary>
public sealed record BondWorkflowCashFlow(DateOnly PaymentDate, DateOnly AccrualStart, DateOnly AccrualEnd,
    decimal Interest, decimal Principal, decimal OutstandingPrincipal, decimal IndexRatio,
    bool IsForecast, IReadOnlyList<BondWorkflowObservation> Observations)
{
    public decimal Total => Interest + Principal;
    public decimal? AccrualFraction { get; init; }
    public decimal? PeriodUnits { get; init; }
}

public sealed record BondWorkflowResult(Guid SecurityId, string Currency, string ModelVersion,
    BondCalculationContext Context, decimal OriginalFace, decimal? CurrentFace, decimal? AccruedInterest,
    decimal? CleanConsideration, decimal? DirtyConsideration, decimal Fees, decimal? TotalCash,
    decimal? PresentValue, decimal? Yield, decimal? CalibratedSpread, decimal? Dv01, decimal? Cs01,
    decimal? Duration, IReadOnlyList<BondWorkflowCashFlow> CashFlows,
    IReadOnlyList<BondCalculationInput> Inputs, IReadOnlyList<BondWorkflowIssue> Issues,
    IReadOnlyList<BondWorkflowMetric> Metrics)
{
    public bool IsBlocked => Issues.Any(issue => issue.BlocksCalculation);
    public Guid? QuoteObservationId { get; init; }
    public Guid? PriceSelectionReceiptId { get; init; }
    public decimal? YieldToWorst { get; init; }
    public decimal? ProviderYield { get; init; }
    public string? ProviderYieldConvention { get; init; }
    public IReadOnlyList<BondWorkflowExerciseResult> ExerciseScenarios { get; init; } = [];
    public decimal? SettlementIndexRatio { get; init; }
    public bool SettlementIndexIsObserved { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public decimal? ContractualAccruedInterest { get; init; }
    /// <summary>Fully earned coupon economics before their contractual payment dates, not bank-clearing status.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public decimal? DeferredCouponInterest { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? HasDetachedCoupon { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? HasPaidActiveCoupon { get; init; }
    public string YieldBasis { get; init; } = "AnnualEffective";
    public string? QuoteSource { get; init; }
    public BondQuoteCategory? QuoteCategory { get; init; }
    public DateTimeOffset? QuoteObservedAt { get; init; }
    public bool QuoteIsExpired { get; init; }
    public BondWorkflowRequest? Request { get; init; }
    public DateOnly? FrozenIndexAt { get; init; }
    public DateOnly? QuoteSettlementDate { get; init; }
    public decimal? OriginalCleanConsideration { get; init; }
    public decimal? OriginalDirtyConsideration { get; init; }
    public decimal? SettlementDifference { get; init; }
    public decimal? RealYield { get; init; }
    public string? RealYieldBasis { get; init; }
    public decimal? DiscountMargin { get; init; }
    public string? DiscountMarginConvention { get; init; }
    public decimal? SettlementAccrualFraction { get; init; }
    public decimal? SettlementPeriodAccrualFraction { get; init; }
    public decimal? SettlementAccrualPeriodUnits { get; init; }
    public decimal? SettlementPeriodUnits { get; init; }
}

public interface IBondWorkflowService
{
    Task<BondWorkflowResult?> CalculateAsync(BondWorkflowRequest request, CancellationToken ct = default);
    Task<BondWorkflowResult?> CalculateIncomeScheduleAsync(BondWorkflowRequest request, DateOnly freezeIndexAt,
        CancellationToken ct = default);
    Task<BondWorkflowResult?> ReplayAsync(BondWorkflowResult retained, CancellationToken ct = default)
        => Task.FromException<BondWorkflowResult?>(new NotSupportedException("This workflow source does not support retained-version replay."));
}

public static class BondWorkflowModelVersions
{
    public const string Legacy = "bond-workflow-v1";
    public const string Current = "bond-workflow-v2";
}

public sealed record BondWorkflowRateObservation(string IndexName, DateOnly Date, decimal Value,
    string Source, DateTimeOffset PublishedAt, DateTimeOffset RecordedAt, string ContentSha256);

/// <summary>Only retained, dated observations eligible at the explicit knowledge cutoff.</summary>
public interface IBondWorkflowObservationSource
{
    Task<IReadOnlyList<BondWorkflowRateObservation>> GetAsync(string indexName, DateOnly from, DateOnly to,
        DateTimeOffset knownAt, CancellationToken ct = default);
}

/// <summary>Typed retained sources bind the requested economic role independently of a series name.
/// Existing custom observation sources retain the legacy interface and its explicit source contract.</summary>
public interface IBondWorkflowTypedObservationSource : IBondWorkflowObservationSource
{
    Task<IReadOnlyList<BondWorkflowRateObservation>> GetAsync(string indexName, BondMarketObservationKind kind,
        DateOnly from, DateOnly to, DateTimeOffset knownAt, CancellationToken ct = default);
}

/// <summary>Authenticated tenant-scoped access to an exact retained quote observation.</summary>
public interface IBondWorkflowQuoteSource
{
    Task<BondMarketObservationDto?> GetAsync(Guid observationId, CancellationToken ct = default);
}
