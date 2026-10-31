using System.Text.Json;

namespace Meridian.Storage.SecurityMaster;

/// <summary>
/// Queries bond reference projections, lifecycle facts, and accrual conventions by security identity.
/// </summary>
public interface IBondReferenceProjectionStore
{
    Task<BondProjectionRow?> GetBondAsync(Guid securityId, CancellationToken ct = default);
    Task<BondLifecycleProjectionRow?> GetLifecycleAsync(Guid securityId, CancellationToken ct = default);
    Task<BondAccrualConventionProjectionRow?> GetAccrualConventionAsync(Guid securityId, CancellationToken ct = default);
    Task<IReadOnlyList<BondProjectionRow>> GetIssuerLadderAsync(string issuerName, CancellationToken ct = default);
    Task<IReadOnlyList<BondProjectionRow>> GetMaturityLadderAsync(DateOnly from, DateOnly to, CancellationToken ct = default);

    /// <summary>Returns at most pageSize plus one rows, using a maturity/security-id keyset.</summary>
    Task<IReadOnlyList<BondProjectionRow>> GetIssuerLadderPageAsync(string issuerName, int pageSize,
        DateOnly? afterMaturity, Guid? afterSecurityId, CancellationToken ct = default)
        => throw new NotSupportedException("This bond projection store does not support bounded issuer paging.");

    /// <summary>Returns at most pageSize plus one rows, using a maturity/security-id keyset.</summary>
    Task<IReadOnlyList<BondProjectionRow>> GetMaturityLadderPageAsync(DateOnly from, DateOnly to, int pageSize,
        DateOnly? afterMaturity, Guid? afterSecurityId, CancellationToken ct = default)
        => throw new NotSupportedException("This bond projection store does not support bounded maturity paging.");
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
    long Version,
    // Extended lifecycle fields (migration 017).
    string? Subclass = null,
    decimal? Par = null,
    string? PaymentFrequency = null,
    DateOnly? LegalFinalMaturity = null,
    DateOnly? PreRefundDate = null,
    DateOnly? MandatoryPutDate = null,
    JsonElement? CanonicalTerms = null,
    DateOnly? EffectiveTo = null);

public sealed record BondLifecycleProjectionRow(
    Guid SecurityId,
    string LifecycleStat,
    DateOnly? IssueDate,
    DateOnly? CallDate,
    DateOnly MaturityDate,
    bool IsCallable,
    long Version,
    // Extended lifecycle fields (migration 017).
    string? Subclass = null,
    decimal? Par = null,
    string? PaymentFrequency = null,
    DateOnly? LegalFinalMaturity = null,
    DateOnly? PreRefundDate = null,
    DateOnly? MandatoryPutDate = null,
    DateOnly? EffectiveTo = null);

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
