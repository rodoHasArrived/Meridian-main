using Meridian.Contracts.AssetOperations;
using Meridian.Storage.SecurityMaster;

namespace Meridian.Instruments.AssetOperations;

public sealed class StructuredCreditProjectionService
    : InstrumentProjectionServiceBase<StructuredCreditProjectionRow, StructuredCreditReferenceDto>, IStructuredCreditReferenceService
{
    private readonly IStructuredCreditReferenceProjectionStore _projectionStore;

    public StructuredCreditProjectionService(
        ISecurityMasterStore securityMasterStore,
        IStructuredCreditReferenceProjectionStore projectionStore)
        : base(securityMasterStore)
    {
        _projectionStore = projectionStore;
    }

    protected override string AssetClass => "StructuredCredit";

    protected override Task<StructuredCreditProjectionRow?> FetchRowAsync(Guid securityId, CancellationToken ct)
        => _projectionStore.GetStructuredCreditAsync(securityId, ct);

    public Task<IReadOnlyList<StructuredCreditReferenceDto>> GetByPoolAsync(string poolId, CancellationToken ct = default)
        => QueryByTermAsync(poolId, _projectionStore.GetByPoolAsync, ct);

    public Task<IReadOnlyList<StructuredCreditReferenceDto>> GetByCollateralTypeAsync(string collateralType, CancellationToken ct = default)
        => QueryByTermAsync(collateralType, _projectionStore.GetByCollateralTypeAsync, ct);

    public async Task<IReadOnlyList<StructuredCreditFactorPointDto>> GetFactorScheduleAsync(Guid securityId, CancellationToken ct = default)
    {
        var rows = await _projectionStore.GetFactorScheduleAsync(securityId, ct).ConfigureAwait(false);
        return rows.Select(MapFactorPoint).ToArray();
    }

    public async Task<StructuredCreditFactorPointDto?> GetFactorAsOfAsync(Guid securityId, DateOnly asOfDate, CancellationToken ct = default)
    {
        var row = await _projectionStore.GetFactorAsOfAsync(securityId, asOfDate, ct).ConfigureAwait(false);
        return row is null ? null : MapFactorPoint(row);
    }

    protected override StructuredCreditReferenceDto MapRow(StructuredCreditProjectionRow row)
        => new(
            row.SecurityId,
            row.DisplayName,
            row.Currency,
            row.Tranche,
            row.PoolId,
            row.CollateralType,
            row.OriginalFace,
            row.CurrentFactor,
            row.CouponOrIndex,
            row.FactorScheduleReference,
            row.MaturityDate,
            row.PrimaryIdentifierValue,
            row.Version);

    private static StructuredCreditFactorPointDto MapFactorPoint(StructuredCreditFactorScheduleRow row)
        => new(row.SecurityId, row.Ordinal, row.AsOfDate, row.Factor);
}

public sealed class NullStructuredCreditReferenceService : IStructuredCreditReferenceService
{
    public Task<StructuredCreditReferenceDto?> GetReferenceAsync(Guid securityId, CancellationToken ct = default)
        => Task.FromResult<StructuredCreditReferenceDto?>(null);

    public Task<IReadOnlyList<StructuredCreditReferenceDto>> GetByPoolAsync(string poolId, CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<StructuredCreditReferenceDto>>(Array.Empty<StructuredCreditReferenceDto>());

    public Task<IReadOnlyList<StructuredCreditReferenceDto>> GetByCollateralTypeAsync(string collateralType, CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<StructuredCreditReferenceDto>>(Array.Empty<StructuredCreditReferenceDto>());

    public Task<IReadOnlyList<StructuredCreditFactorPointDto>> GetFactorScheduleAsync(Guid securityId, CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<StructuredCreditFactorPointDto>>(Array.Empty<StructuredCreditFactorPointDto>());

    public Task<StructuredCreditFactorPointDto?> GetFactorAsOfAsync(Guid securityId, DateOnly asOfDate, CancellationToken ct = default)
        => Task.FromResult<StructuredCreditFactorPointDto?>(null);
}
