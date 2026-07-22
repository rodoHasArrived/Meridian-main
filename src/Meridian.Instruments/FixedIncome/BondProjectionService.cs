using Meridian.Contracts.FixedIncome;
using Meridian.Storage.SecurityMaster;

namespace Meridian.Instruments.FixedIncome;

public sealed class BondProjectionService
    : InstrumentProjectionServiceBase<BondProjectionRow, BondReferenceDto>, IBondReferenceService
{
    private readonly IBondReferenceProjectionStore _projectionStore;

    public BondProjectionService(
        ISecurityMasterStore securityMasterStore,
        IBondReferenceProjectionStore projectionStore)
        : base(securityMasterStore)
    {
        _projectionStore = projectionStore;
    }

    protected override string AssetClass => "Bond";

    protected override Task<BondProjectionRow?> FetchRowAsync(Guid securityId, CancellationToken ct)
        => _projectionStore.GetBondAsync(securityId, ct);

    public async Task<BondLifecycleDto?> GetLifecycleAsync(Guid securityId, CancellationToken ct = default)
    {
        var projection = await _projectionStore.GetLifecycleAsync(securityId, ct).ConfigureAwait(false);
        if (projection is null || !Enum.TryParse<BondLifecycleStat>(projection.LifecycleStat, ignoreCase: true, out var lifecycleStat))
        {
            return null;
        }

        return new BondLifecycleDto(
            projection.SecurityId,
            lifecycleStat,
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
        return await MapReferenceListAsync(projections, ct).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<BondReferenceDto>> GetMaturityLadderAsync(DateOnly from, DateOnly to, CancellationToken ct = default)
    {
        if (to < from)
        {
            return Array.Empty<BondReferenceDto>();
        }

        var projections = await _projectionStore.GetMaturityLadderAsync(from, to, ct).ConfigureAwait(false);
        return await MapReferenceListAsync(projections, ct).ConfigureAwait(false);
    }

    private async Task<IReadOnlyList<BondReferenceDto>> MapReferenceListAsync(
        IReadOnlyList<BondProjectionRow> projections,
        CancellationToken ct)
    {
        var results = new List<BondReferenceDto>(projections.Count);
        foreach (var projection in projections)
        {
            ct.ThrowIfCancellationRequested();
            results.Add(MapRow(projection));
        }

        return results;
    }

    protected override BondReferenceDto MapRow(BondProjectionRow projection)
        => new(
            projection.SecurityId,
            projection.DisplayName,
            projection.Currency,
            projection.IssuerName,
            projection.Seniority,
            projection.PrimaryIdentifierValue,
            MapLifecycle(projection),
            MapAccrual(projection),
            projection.Version);

    private static BondLifecycleDto? MapLifecycle(BondProjectionRow projection)
    {
        if (projection.MaturityDate is null ||
            string.IsNullOrWhiteSpace(projection.LifecycleStat) ||
            !Enum.TryParse<BondLifecycleStat>(projection.LifecycleStat, ignoreCase: true, out var lifecycleStat))
        {
            return null;
        }

        return new BondLifecycleDto(
            projection.SecurityId,
            lifecycleStat,
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
}
