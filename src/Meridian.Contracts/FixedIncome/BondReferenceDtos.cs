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

public sealed record BondLifecycleDto(
    Guid SecurityId,
    BondLifecycleStat LifecycleStat,
    DateOnly? IssueDate,
    DateOnly? CallDate,
    DateOnly MaturityDate,
    bool IsCallable,
    long Version);

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

public sealed record BondReferenceDto(
    Guid SecurityId,
    string DisplayName,
    string Currency,
    string? IssuerName,
    string? Seniority,
    string PrimaryIdentifier,
    BondLifecycleDto? Lifecycle,
    BondAccrualConventionDto? AccrualConvention,
    long Version);
