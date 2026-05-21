using Meridian.Contracts.FxSpot;
using Meridian.Storage.SecurityMaster;

namespace Meridian.Application.FxSpot;

public sealed class FxSpotProjectionService : IFxSpotReferenceService
{
    private readonly ISecurityMasterStore _securityMasterStore;
    private readonly IFxSpotReferenceProjectionStore _projectionStore;

    public FxSpotProjectionService(
        ISecurityMasterStore securityMasterStore,
        IFxSpotReferenceProjectionStore projectionStore)
    {
        _securityMasterStore = securityMasterStore;
        _projectionStore = projectionStore;
    }

    public async Task<FxSpotReferenceDto?> GetReferenceAsync(Guid securityId, CancellationToken ct = default)
    {
        var security = await _securityMasterStore.GetProjectionAsync(securityId, ct).ConfigureAwait(false);
        if (security is null || !string.Equals(security.AssetClass, "FxSpot", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var row = await _projectionStore.GetFxSpotAsync(securityId, ct).ConfigureAwait(false);
        return row is null ? null : MapRow(row);
    }

    public async Task<FxSpotReferenceDto?> GetByPairCodeAsync(string pairCode, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(pairCode))
        {
            return null;
        }

        var row = await _projectionStore.GetByPairCodeAsync(pairCode.Trim().ToUpperInvariant(), ct).ConfigureAwait(false);
        return row is null ? null : MapRow(row);
    }

    public async Task<IReadOnlyList<FxSpotReferenceDto>> GetByCurrencyAsync(string currency, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(currency))
        {
            return Array.Empty<FxSpotReferenceDto>();
        }

        var rows = await _projectionStore.GetByCurrencyAsync(currency.Trim().ToUpperInvariant(), ct).ConfigureAwait(false);
        return rows.Select(MapRow).ToArray();
    }

    private static FxSpotReferenceDto MapRow(FxSpotProjectionRow row)
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
