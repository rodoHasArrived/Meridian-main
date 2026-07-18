using Meridian.Contracts.FxSpot;
using Meridian.Storage.SecurityMaster;

namespace Meridian.Instruments.FxSpot;

public sealed class FxSpotProjectionService
    : InstrumentProjectionServiceBase<FxSpotProjectionRow, FxSpotReferenceDto>, IFxSpotReferenceService
{
    private readonly IFxSpotReferenceProjectionStore _projectionStore;

    public FxSpotProjectionService(
        ISecurityMasterStore securityMasterStore,
        IFxSpotReferenceProjectionStore projectionStore)
        : base(securityMasterStore)
    {
        _projectionStore = projectionStore;
    }

    protected override string AssetClass => "FxSpot";

    protected override Task<FxSpotProjectionRow?> FetchRowAsync(Guid securityId, CancellationToken ct)
        => _projectionStore.GetFxSpotAsync(securityId, ct);

    public async Task<FxSpotReferenceDto?> GetByPairCodeAsync(string pairCode, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(pairCode))
        {
            return null;
        }

        var row = await _projectionStore.GetByPairCodeAsync(pairCode.Trim().ToUpperInvariant(), ct).ConfigureAwait(false);
        return row is null ? null : MapRow(row);
    }

    public Task<IReadOnlyList<FxSpotReferenceDto>> GetByCurrencyAsync(string currency, CancellationToken ct = default)
        => QueryByTermAsync(currency, _projectionStore.GetByCurrencyAsync, ct, toUpperInvariant: true);

    protected override FxSpotReferenceDto MapRow(FxSpotProjectionRow row)
        => new(
            row.SecurityId,
            row.DisplayName,
            row.BaseCurrency,
            row.QuoteCurrency,
            row.PairCode,
            row.PrimaryIdentifierValue,
            row.Version);
}

public sealed class NullFxSpotReferenceService : IFxSpotReferenceService
{
    public Task<FxSpotReferenceDto?> GetReferenceAsync(Guid securityId, CancellationToken ct = default)
        => Task.FromResult<FxSpotReferenceDto?>(null);

    public Task<FxSpotReferenceDto?> GetByPairCodeAsync(string pairCode, CancellationToken ct = default)
        => Task.FromResult<FxSpotReferenceDto?>(null);

    public Task<IReadOnlyList<FxSpotReferenceDto>> GetByCurrencyAsync(string currency, CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<FxSpotReferenceDto>>(Array.Empty<FxSpotReferenceDto>());
}
