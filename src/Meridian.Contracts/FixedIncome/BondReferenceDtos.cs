using System.Text.Json;
using System.Text.Json.Serialization;
using Meridian.Contracts.SecurityMaster;

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

public sealed record BondLifecycleDto(
    Guid SecurityId,
    BondLifecycleStat LifecycleStat,
    DateOnly? IssueDate,
    DateOnly? CallDate,
    DateOnly MaturityDate,
    bool IsCallable,
    long Version,
    // --- Extended lifecycle fields (migration 017) ---
    /// <summary>Face/par value; used in market-value calculation: Par × Factor × Price / 100.</summary>
    decimal? Par = null,
    string? BondSubclass = null,
    string? PaymentFrequency = null,
    DateOnly? LegalFinalMaturity = null,
    DateOnly? PreRefundDate = null,
    DateOnly? MandatoryPutDate = null)
{
    /// <summary>
    /// <see cref="PaymentFrequency"/> as a typed cadence, or null when the stored text is absent or
    /// names no recognized frequency. The raw text stays on <see cref="PaymentFrequency"/>.
    /// </summary>
    public SecurityPaymentFrequency? PaymentCadence
        => SecurityTermConventions.TryParsePaymentFrequency(PaymentFrequency, out var frequency) ? frequency : null;
}

public sealed record BondAccrualConventionDto(
    Guid SecurityId,
    string? DayCountConvention,
    int? SettlementCycleDays,
    string? HolidayCalendarId,
    string? CouponKind,
    decimal? FixedCouponRate,
    string? FloatingRateIndex,
    decimal? FloatingSpreadBps,
    long Version)
{
    /// <summary>
    /// <see cref="DayCountConvention"/> as a typed basis, or null when the stored text is absent or
    /// names no recognized convention. The raw text stays on <see cref="DayCountConvention"/>.
    /// </summary>
    public Meridian.Contracts.SecurityMaster.DayCountConvention? DayCount
        => DayCountConventions.TryParse(DayCountConvention, out var convention) ? convention : null;
}

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
    long Version)
{
    /// <summary>
    /// <see cref="SinkFrequency"/> as a typed cadence, or null when the stored text is absent or
    /// names no recognized frequency. The raw text stays on <see cref="SinkFrequency"/>.
    /// </summary>
    public SecurityPaymentFrequency? SinkCadence
        => SecurityTermConventions.TryParsePaymentFrequency(SinkFrequency, out var frequency) ? frequency : null;
}

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
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] BondPrincipalFactorDto? PrincipalFactor = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] BondCouponStructureDto? CouponStructure = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] ScheduleConventionTerms? ScheduleConventions = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] JsonElement? CallSchedule = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] JsonElement? PutSchedule = null);

/// <summary>Outstanding-principal evidence, separate from inflation indexation.</summary>
public sealed record BondPrincipalFactorDto(decimal? CurrentFactor, DateOnly? FactorDate, JsonElement? Schedule);

/// <summary>Coupon-structure terms retained for downstream capability checks and calculations.</summary>
public sealed record BondCouponStructureDto(
    string? Kind,
    decimal? CapRate,
    decimal? FloorRate,
    JsonElement? StepSchedule,
    string? RateUnit);

/// <summary>A bounded issuer or maturity page. The cursor is opaque and bound to its query.</summary>
public sealed record BondReferencePageDto(IReadOnlyList<BondReferenceDto> Items, string? NextCursor);
