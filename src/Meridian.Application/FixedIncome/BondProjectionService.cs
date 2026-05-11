using Meridian.Contracts.FixedIncome;
using Meridian.Storage.SecurityMaster;

namespace Meridian.Application.FixedIncome;

public sealed class BondProjectionService : IBondReferenceService
{
    private readonly ISecurityMasterStore _securityMasterStore;
    private readonly IBondReferenceProjectionStore _projectionStore;

    public BondProjectionService(
        ISecurityMasterStore securityMasterStore,
        IBondReferenceProjectionStore projectionStore)
    {
        _securityMasterStore = securityMasterStore;
        _projectionStore = projectionStore;
    }

    public async Task<BondReferenceDto?> GetReferenceAsync(Guid securityId, CancellationToken ct = default)
    {
        var security = await _securityMasterStore.GetProjectionAsync(securityId, ct).ConfigureAwait(false);
        if (security is null || !string.Equals(security.AssetClass, "Bond", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var projection = await _projectionStore.GetBondAsync(securityId, ct).ConfigureAwait(false);
        if (projection is null)
        {
            return null;
        }

        var lifecycle = await GetLifecycleAsync(securityId, ct).ConfigureAwait(false);
        var accrual = await GetAccrualConventionAsync(securityId, ct).ConfigureAwait(false);

        return MapReference(projection, lifecycle, accrual);
    }

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
            projection.Version);
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
            var lifecycle = await GetLifecycleAsync(projection.SecurityId, ct).ConfigureAwait(false);
            var accrual = await GetAccrualConventionAsync(projection.SecurityId, ct).ConfigureAwait(false);
            results.Add(MapReference(projection, lifecycle, accrual));
        }

        return results;
    }

    private static BondReferenceDto MapReference(
        BondProjectionRow projection,
        BondLifecycleDto? lifecycle,
        BondAccrualConventionDto? accrual)
        => new(
            projection.SecurityId,
            projection.DisplayName,
            projection.Currency,
            projection.IssuerName,
            projection.Seniority,
            projection.PrimaryIdentifierValue,
            lifecycle,
            accrual,
            projection.Version);
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
