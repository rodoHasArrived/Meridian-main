using System.Text.Json.Serialization;

namespace Meridian.Contracts.FixedIncome;

/// <summary>Yield rates are annual fractions: 0.05 means five percent.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<BondYieldConvention>))]
public enum BondYieldConvention
{
    /// <summary>Nominal annual yield compounded at the contractual coupon frequency.</summary>
    NominalPeriodic,
    /// <summary>Annual effective yield on Actual/365 Fixed time.</summary>
    AnnualEffective,
    /// <summary>Simple annual yield on Actual/365 Fixed time.</summary>
    Simple
}

/// <summary>Exactly one of clean price per 100 face or annual yield must be supplied.</summary>
public sealed record BondAnalyticsRequest(
    Guid SecurityId,
    BondCalculationContext Context,
    decimal? CleanPrice = null,
    decimal? Yield = null,
    BondYieldConvention? YieldConvention = null);

public sealed record BondAnalyticsBlocker(string Code, string Reason);

/// <summary>Cash per 100 face; identity depends on unadjusted contractual period dates.</summary>
public sealed record BondAnalyticsCashFlow(
    Guid FlowId,
    DateOnly PaymentDate,
    DateOnly AccrualStart,
    DateOnly AccrualEnd,
    decimal Interest,
    decimal Principal,
    decimal TimeInYears);

/// <summary>Contractual exercise scenario, not an exercise forecast or trading instruction.</summary>
public sealed record BondExerciseYield(
    string Kind,
    DateOnly ExerciseDate,
    decimal RedemptionPrice,
    decimal Yield,
    DateOnly? LatestNoticeDate,
    DateOnly? EarliestNoticeDate = null);

public sealed record BondOperationCapability(string Operation, bool Supported, string? Reason = null);

/// <summary>
/// All prices, accrued interest and DV01 are per 100 face; yields are annual fractions and
/// durations are years. Blocked results contain no calculated metrics.
/// </summary>
public sealed record BondAnalyticsResult(
    Guid SecurityId,
    string Currency,
    string ModelVersion,
    BondCalculationContext Context,
    BondYieldConvention? YieldConvention,
    decimal? CleanPrice,
    decimal? DirtyPrice,
    decimal? AccruedInterest,
    decimal? YieldToMaturity,
    decimal? CurrentYield,
    decimal? MacaulayDuration,
    decimal? ModifiedDuration,
    decimal? Convexity,
    decimal? Dv01,
    decimal? YieldToWorst,
    IReadOnlyList<BondAnalyticsCashFlow> CashFlows,
    IReadOnlyList<BondExerciseYield> ExerciseYields,
    IReadOnlyList<BondCalculationInput> Inputs,
    IReadOnlyList<BondAnalyticsBlocker> Blockers,
    IReadOnlyList<BondOperationCapability> Capabilities,
    IReadOnlyList<string> Diagnostics)
{
    public bool IsBlocked => Blockers.Count > 0;
}
