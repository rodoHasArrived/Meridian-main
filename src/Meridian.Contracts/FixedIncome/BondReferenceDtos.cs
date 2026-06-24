using System.Text.Json.Serialization;

namespace Meridian.Contracts.FixedIncome;

[JsonConverter(typeof(JsonStringEnumConverter<BondLifecycleStat>))]
public enum BondLifecycleStat
{
    WhenIssued,
    Active,
    Callable,
    Matured,
    Retired
}

/// Amortization method applied at the account or security level.
/// Mirrors <c>AmortizationMethod</c> in the F# domain.
[JsonConverter(typeof(JsonStringEnumConverter<BondAmortizationMethod>))]
public enum BondAmortizationMethod
{
    /// Effective-interest method — income recognised as a constant proportion of book value.
    ConstantYield,
    /// Equal daily movement from starting book price to target price.
    StraightLine,
    /// Book price held flat; no daily premium/discount recognised.
    NoAmortization,
    /// Recognises premium/discount immediately to par; used for auction-rate securities.
    AuctionRate,
    /// Expected-yield accounting over remaining life; used for PCI-designated lots.
    PurchasedCreditImpaired
}

/// FAS 91-style yield adjustment method for prepaying structured securities.
[JsonConverter(typeof(JsonStringEnumConverter<BondYieldAdjustmentMethod>))]
public enum BondYieldAdjustmentMethod
{
    Retrospective,
    Prospective,
    NoAdjustment
}

public sealed record BondLifecycleDto(
    Guid SecurityId,
    BondLifecycleStat LifecycleStat,
    DateOnly? IssueDate,
    DateOnly? CallDate,
    DateOnly MaturityDate,
    bool IsCallable,
    long Version,
    // --- Clearwater extended lifecycle fields ---
    /// <summary>Face/par value; used in market-value calculation: Par × Factor × Price / 100.</summary>
    decimal? Par = null,
    string? BondSubclass = null,
    string? PaymentFrequency = null,
    DateOnly? LegalFinalMaturity = null,
    DateOnly? PreRefundDate = null,
    DateOnly? MandatoryPutDate = null);

public sealed record BondAccrualConventionDto(
    Guid SecurityId,
    string? DayCountConvention,
    int? SettlementCycleDays,
    string? HolidayCalendarId,
    string? CouponKind,
    decimal? FixedCouponRate,
    string? FloatingRateIndex,
    decimal? FloatingSpreadBps,
    long Version);

/// A single instalment in a sinking fund or scheduled principal amortisation schedule.
public sealed record BondSinkingFundEntryDto(
    DateOnly SinkDate,
    decimal Amount);

/// Sinking fund schedule for bonds with contractual principal instalments.
public sealed record BondSinkingFundDto(
    Guid SecurityId,
    IReadOnlyList<BondSinkingFundEntryDto> Schedule,
    string? SinkFrequency,
    bool IsProRata,
    long Version);

/// Inflation index and factor data for TIPS, index-linked GILTs, and similar instruments.
public sealed record BondInflationLinkedDto(
    Guid SecurityId,
    /// <summary>Index series (e.g. "CPI-U", "RPI", "HICPxT").</summary>
    string? InflationIndex,
    decimal? BaseIndexValue,
    DateOnly? BaseDate,
    decimal? CurrentFactor,
    DateOnly? FactorDate,
    /// <summary>Minimum factor floor; 1.0 for US TIPS.</summary>
    decimal? IndexFloor,
    long Version);

/// Account or lot-level accounting elections controlling amortization, yield update, and cash-flow source.
public sealed record BondAccountingElectionsDto(
    Guid SecurityId,
    BondAmortizationMethod AmortizationMethod,
    string? YieldUpdateFrequency,
    BondYieldAdjustmentMethod? AdjustmentMethod,
    string? CashFlowSource,
    bool IgnoreCallDates,
    bool IgnorePutDates,
    bool IsLotLevelNoAmortization,
    long Version);

public sealed record BondReferenceDto(
    Guid SecurityId,
    string DisplayName,
    string Currency,
    string? IssuerName,
    string? Seniority,
    string PrimaryIdentifier,
    BondLifecycleDto? Lifecycle,
    BondAccrualConventionDto? AccrualConvention,
    long Version,
    BondSinkingFundDto? SinkingFund = null,
    BondInflationLinkedDto? InflationLinked = null,
    BondAccountingElectionsDto? AccountingElections = null);
