using System.Text.Json.Serialization;
using Meridian.Contracts.AssetOperations;

namespace Meridian.Contracts.FixedIncome;

/// <summary>Identifiers and cutoffs only; marks, cash, rates and accounting amounts are resolved from retained authorities.</summary>
public sealed record BondPerformanceRequest(AccountCashForecastScopeDto Scope, DateOnly PeriodStart,
    DateOnly PeriodEnd, DateTimeOffset KnownAt)
{
    public IReadOnlyList<Guid> TaxLotRecordIds { get; init; } = [];
}

/// <summary>Order-dependent model explanation. Accounting amortization is never a market-return component.</summary>
public sealed record BondPerformanceAttributionDto(
    decimal? CarryAndRoll,
    decimal? RateEffect,
    decimal? SpreadEffect,
    decimal? InflationEffect,
    decimal? CollectionAndCosts,
    decimal? FxEffect,
    decimal? Residual,
    string Currency);

public sealed record BondPerformanceAccountingBridgeDto(Guid SecurityId, IReadOnlyList<Guid> TaxLotRecordIds,
    decimal OpeningCarryingBasis, decimal ClosingCarryingBasis, decimal CouponIncome,
    decimal PremiumDiscountIncome, decimal IndexationIncome, decimal ExpensedFees,
    decimal CashGrossCoupons, decimal AccrualVersusCashDifference, string Currency,
    IReadOnlyList<string> InstructionFingerprints)
{
    public decimal AccountingIncome => CouponIncome + PremiumDiscountIncome + IndexationIncome;
}

public sealed record BondPerformanceSecurityDto(Guid SecurityId, string Currency,
    decimal? OpeningDirtyValue, decimal? ClosingDirtyValue, decimal ActualNetCash,
    decimal CashGrossCoupons, decimal PrincipalReturned, decimal WithholdingTax, decimal Fees,
    decimal? ObservedMarkAndCashChange, decimal? EconomicReturn,
    BondPerformanceAttributionDto Attribution, BondPerformanceAttributionDto? BaseCurrencyAttribution,
    decimal? BaseObservedMarkAndCashChange, IReadOnlyList<BondCalculationInput> Inputs,
    IReadOnlyList<string> CoverageGaps)
{
    /// <summary>Observed buy/sell settlement cash, separate from coupon/principal collections.</summary>
    public decimal TradeSettlementCash { get; init; }
}

public sealed record BondPerformanceResult(AccountCashForecastScopeDto Scope, DateOnly PeriodStart,
    DateOnly PeriodEnd, DateTimeOffset KnownAt, string ModelVersion, Guid? OpeningHoldingsBatchId,
    Guid? ClosingHoldingsBatchId, IReadOnlyList<BondPerformanceSecurityDto> Securities,
    IReadOnlyList<BondPerformanceAccountingBridgeDto> AccountingIncome,
    decimal? BaseObservedMarkAndCashChange, bool CompleteCoverage, IReadOnlyList<string> CoverageGaps);

/// <summary>Retained, complete transaction populations, not caller attestations or inferred zero trades.</summary>
public sealed record BondPerformanceTradeCashDto(Guid SecurityId, DateOnly CashDate, decimal Amount,
    string Currency, BondCalculationInput Evidence)
{
    public decimal? OriginalFaceDelta { get; init; }
}

public sealed record BondPerformanceTransactionEvidenceDto(bool Complete, IReadOnlyList<BondPerformanceTradeCashDto> TradeCash,
    IReadOnlyList<BondCalculationInput> Inputs, IReadOnlyList<string> CoverageGaps)
{
    public bool CashPopulationComplete { get; init; }
    public IReadOnlyList<string> PaymentVersionIds { get; init; } = [];
}

public interface IBondPerformanceTransactionSource
{
    Task<BondPerformanceTransactionEvidenceDto> GetAsync(AccountCashForecastScopeDto scope, DateOnly from,
        DateOnly through, DateTimeOffset knownAt, CancellationToken ct = default);
}

/// <summary>Rechecks current retained source bytes and accepted review before using cached cash or FX economics.</summary>
public interface IBondPerformanceSourceVerifier
{
    Task VerifyMonitoringAsync(BondMonitoringCaptureRequest request, CancellationToken ct = default);
    Task VerifyFxAsync(AccountCashForecastScopeDto scope, IReadOnlyList<AccountCashFxConversionDto> conversions, CancellationToken ct = default);
}

public interface IBondPerformanceService
{
    Task<BondPerformanceResult> ExplainAsync(BondPerformanceRequest request, CancellationToken ct = default);
}

[JsonSourceGenerationOptions(GenerationMode = JsonSourceGenerationMode.Metadata)]
[JsonSerializable(typeof(BondPerformanceRequest))]
[JsonSerializable(typeof(BondPerformanceResult))]
public partial class BondPerformanceJsonContext : JsonSerializerContext;
