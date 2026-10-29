using Meridian.Contracts.FixedIncome;
using Meridian.Contracts.SecurityMaster;
using Meridian.Storage.SecurityMaster;

namespace Meridian.Instruments.FixedIncome;

/// <summary>
/// Reads bond reference projections and combines them with canonical Security Master data.
/// </summary>
public sealed partial class BondProjectionService
    : InstrumentProjectionServiceBase<BondProjectionRow, BondReferenceDto>, IBondReferenceService
{
    private readonly IBondReferenceProjectionStore _projectionStore;
    private readonly TimeProvider _timeProvider;

    public BondProjectionService(
        ISecurityMasterStore securityMasterStore,
        IBondReferenceProjectionStore projectionStore,
        TimeProvider? timeProvider = null)
        : base(securityMasterStore)
    {
        _projectionStore = projectionStore;
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    protected override string AssetClass => "Bond";

    protected override Task<BondProjectionRow?> FetchRowAsync(Guid securityId, CancellationToken ct)
        => _projectionStore.GetBondAsync(securityId, ct);

    public new Task<BondReferenceDto?> GetReferenceAsync(Guid securityId, CancellationToken ct = default)
        => GetReferenceAsOfAsync(securityId, Today, ct);

    /// <summary>Interprets current retained reference facts on an explicit date; this is not a historical terms lookup.</summary>
    public async Task<BondReferenceDto?> GetReferenceAsOfAsync(Guid securityId, DateOnly asOf, CancellationToken ct = default)
    {
        var security = await GetVerifiedSecurityAsync(securityId, ct).ConfigureAwait(false);
        if (security is null)
            return null;
        var row = await FetchRowAsync(securityId, ct).ConfigureAwait(false);
        return row is null ? null : MapReference(row with { CanonicalTerms = row.CanonicalTerms ?? security.AssetSpecificTerms }, asOf);
    }

    private DateOnly Today => DateOnly.FromDateTime(_timeProvider.GetUtcNow().UtcDateTime);

    public Task<BondLifecycleDto?> GetLifecycleAsync(Guid securityId, CancellationToken ct = default)
        => GetLifecycleAsOfAsync(securityId, Today, ct);

    public async Task<BondLifecycleDto?> GetLifecycleAsOfAsync(Guid securityId, DateOnly asOf, CancellationToken ct = default)
    {
        var projection = await _projectionStore.GetLifecycleAsync(securityId, ct).ConfigureAwait(false);
        if (projection is null || !TryLifecycle(projection.LifecycleStat, out var lifecycleStat))
        {
            return null;
        }

        return new BondLifecycleDto(
            projection.SecurityId,
            ResolveLifecycle(lifecycleStat, projection.IssueDate, projection.CallDate, projection.MaturityDate, projection.IsCallable, asOf, projection.EffectiveTo),
            projection.IssueDate,
            projection.CallDate,
            projection.MaturityDate,
            projection.IsCallable,
            projection.Version,
            projection.Par,
            projection.Subclass,
            projection.PaymentFrequency,
            projection.LegalFinalMaturity,
            projection.PreRefundDate,
            projection.MandatoryPutDate);
    }

    public async Task<BondAccrualConventionDto?> GetAccrualConventionAsync(Guid securityId, CancellationToken ct = default)
    {
        var projection = await _projectionStore.GetAccrualConventionAsync(securityId, ct).ConfigureAwait(false);
        return projection is null
            ? null
            : new BondAccrualConventionDto(
                projection.SecurityId,
                projection.DayCountConvention,
                projection.SettlementCycleDays,
                projection.HolidayCalendarId,
                projection.CouponKind,
                projection.FixedCouponRate,
                projection.FloatingRateIndex,
                projection.FloatingSpreadBps,
                projection.Version);
    }

    public async Task<IReadOnlyList<BondReferenceDto>> GetIssuerLadderAsync(string issuerName, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(issuerName))
        {
            return Array.Empty<BondReferenceDto>();
        }

        var projections = await _projectionStore.GetIssuerLadderAsync(issuerName, ct).ConfigureAwait(false);
        return MapReferenceList(projections, Today, ct);
    }

    public async Task<IReadOnlyList<BondReferenceDto>> GetMaturityLadderAsync(DateOnly from, DateOnly to, CancellationToken ct = default)
    {
        if (to < from)
        {
            return Array.Empty<BondReferenceDto>();
        }

        var projections = await _projectionStore.GetMaturityLadderAsync(from, to, ct).ConfigureAwait(false);
        return MapReferenceList(projections, Today, ct);
    }

    private IReadOnlyList<BondReferenceDto> MapReferenceList(
        IReadOnlyList<BondProjectionRow> projections,
        DateOnly asOf,
        CancellationToken ct)
    {
        var results = new List<BondReferenceDto>(projections.Count);
        foreach (var projection in projections)
        {
            ct.ThrowIfCancellationRequested();
            results.Add(MapReference(projection, asOf));
        }

        return results;
    }

    protected override BondReferenceDto MapRow(BondProjectionRow projection)
        => MapReference(projection, Today);

    private static BondReferenceDto MapReference(BondProjectionRow projection, DateOnly asOf)
        => BondReferenceTermMapper.Enrich(new(
            projection.SecurityId,
            projection.DisplayName,
            projection.Currency,
            projection.IssuerName,
            projection.Seniority,
            projection.PrimaryIdentifierValue,
            MapLifecycle(projection, asOf),
            MapAccrual(projection),
            projection.Version), projection.CanonicalTerms);

    private static BondLifecycleDto? MapLifecycle(BondProjectionRow projection, DateOnly asOf)
    {
        if (projection.MaturityDate is null ||
            string.IsNullOrWhiteSpace(projection.LifecycleStat) ||
            !TryLifecycle(projection.LifecycleStat, out var lifecycleStat))
        {
            return null;
        }

        return new BondLifecycleDto(
            projection.SecurityId,
            ResolveLifecycle(lifecycleStat, projection.IssueDate, projection.CallDate, projection.MaturityDate.Value, projection.IsCallable ?? false, asOf, projection.EffectiveTo),
            projection.IssueDate,
            projection.CallDate,
            projection.MaturityDate.Value,
            projection.IsCallable ?? false,
            projection.Version,
            projection.Par,
            projection.Subclass,
            projection.PaymentFrequency,
            projection.LegalFinalMaturity,
            projection.PreRefundDate,
            projection.MandatoryPutDate);
    }

    private static bool TryLifecycle(string raw, out BondLifecycleStat state)
    {
        state = default;
        return !string.IsNullOrWhiteSpace(raw)
            && Enum.GetNames<BondLifecycleStat>().Contains(raw.Trim(), StringComparer.OrdinalIgnoreCase)
            && Enum.TryParse(raw, ignoreCase: true, out state) && Enum.IsDefined(state);
    }

    private static BondLifecycleStat ResolveLifecycle(BondLifecycleStat retained, DateOnly? issueDate,
        DateOnly? callDate, DateOnly maturity, bool isCallable, DateOnly asOf, DateOnly? effectiveTo = null)
        => effectiveTo is { } retired && retired <= asOf ? BondLifecycleStat.Retired
            : effectiveTo is null && retained == BondLifecycleStat.Retired ? BondLifecycleStat.Retired
            : maturity < asOf ? BondLifecycleStat.Matured
            : isCallable && callDate is { } call && call <= asOf ? BondLifecycleStat.Callable
            : issueDate is { } issue && issue > asOf ? BondLifecycleStat.WhenIssued
            : BondLifecycleStat.Active;

    private static BondAccrualConventionDto? MapAccrual(BondProjectionRow projection)
    {
        if (projection.DayCountConvention is null &&
            projection.SettlementCycleDays is null &&
            projection.HolidayCalendarId is null &&
            projection.CouponKind is null &&
            projection.FixedCouponRate is null &&
            projection.FloatingRateIndex is null &&
            projection.FloatingSpreadBps is null)
        {
            return null;
        }

        return new BondAccrualConventionDto(
            projection.SecurityId,
            projection.DayCountConvention,
            projection.SettlementCycleDays,
            projection.HolidayCalendarId,
            projection.CouponKind,
            projection.FixedCouponRate,
            projection.FloatingRateIndex,
            projection.FloatingSpreadBps,
            projection.Version);
    }
}

/// <summary>
/// Returns absent or empty bond reference data when the reference service is unavailable.
/// </summary>
public sealed class NullBondReferenceService : IBondReferenceService
{
    public Task<BondReferenceDto?> GetReferenceAsync(Guid securityId, CancellationToken ct = default)
        => Task.FromResult<BondReferenceDto?>(null);

    public Task<BondLifecycleDto?> GetLifecycleAsync(Guid securityId, CancellationToken ct = default)
        => Task.FromResult<BondLifecycleDto?>(null);

    public Task<BondAccrualConventionDto?> GetAccrualConventionAsync(Guid securityId, CancellationToken ct = default)
        => Task.FromResult<BondAccrualConventionDto?>(null);

    public Task<IReadOnlyList<BondReferenceDto>> GetIssuerLadderAsync(string issuerName, CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<BondReferenceDto>>(Array.Empty<BondReferenceDto>());

    public Task<IReadOnlyList<BondReferenceDto>> GetMaturityLadderAsync(DateOnly from, DateOnly to, CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<BondReferenceDto>>(Array.Empty<BondReferenceDto>());

    public Task<BondReferencePageDto> GetIssuerLadderPageAsync(string issuerName, int pageSize = 100,
        string? cursor = null, DateOnly? asOf = null, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        return Task.FromResult(new BondReferencePageDto([], null));
    }

    public Task<BondReferencePageDto> GetMaturityLadderPageAsync(DateOnly from, DateOnly to, int pageSize = 100,
        string? cursor = null, DateOnly? asOf = null, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        return Task.FromResult(new BondReferencePageDto([], null));
    }

}
