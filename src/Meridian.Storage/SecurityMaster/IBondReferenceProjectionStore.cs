namespace Meridian.Storage.SecurityMaster;

public interface IBondReferenceProjectionStore
{
    Task<BondProjectionRow?> GetBondAsync(Guid securityId, CancellationToken ct = default);
    Task<BondLifecycleProjectionRow?> GetLifecycleAsync(Guid securityId, CancellationToken ct = default);
    Task<BondAccrualConventionProjectionRow?> GetAccrualConventionAsync(Guid securityId, CancellationToken ct = default);
    Task<IReadOnlyList<BondProjectionRow>> GetIssuerLadderAsync(string issuerName, CancellationToken ct = default);
    Task<IReadOnlyList<BondProjectionRow>> GetMaturityLadderAsync(DateOnly from, DateOnly to, CancellationToken ct = default);
}

public sealed record BondProjectionRow(
    Guid SecurityId,
    string DisplayName,
    string Currency,
    string PrimaryIdentifierValue,
    string? IssuerName,
    string? Seniority,
    DateOnly? MaturityDate,
    string? LifecycleStat,
    DateOnly? IssueDate,
    DateOnly? CallDate,
    bool? IsCallable,
    string? DayCountConvention,
    int? SettlementCycleDays,
    string? HolidayCalendarId,
    string? CouponKind,
    decimal? FixedCouponRate,
    string? FloatingRateIndex,
    decimal? FloatingSpreadBps,
    long Version);

public sealed record BondLifecycleProjectionRow(
    Guid SecurityId,
    string LifecycleStat,
    DateOnly? IssueDate,
    DateOnly? CallDate,
    DateOnly MaturityDate,
    bool IsCallable,
    long Version);

public sealed record BondAccrualConventionProjectionRow(
    Guid SecurityId,
    string? DayCountConvention,
    int? SettlementCycleDays,
    string? HolidayCalendarId,
    string? CouponKind,
    decimal? FixedCouponRate,
    string? FloatingRateIndex,
    decimal? FloatingSpreadBps,
    long Version);
