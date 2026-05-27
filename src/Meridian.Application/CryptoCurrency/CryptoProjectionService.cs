using Meridian.Contracts.CryptoCurrency;
using Meridian.Storage.SecurityMaster;

namespace Meridian.Application.CryptoCurrency;

public sealed class CryptoProjectionService : ICryptoReferenceService
{
    private readonly ISecurityMasterStore _securityMasterStore;
    private readonly ICryptoReferenceProjectionStore _projectionStore;

    public CryptoProjectionService(
        ISecurityMasterStore securityMasterStore,
        ICryptoReferenceProjectionStore projectionStore)
    {
        _securityMasterStore = securityMasterStore;
        _projectionStore = projectionStore;
    }

    public async Task<CryptoReferenceDto?> GetReferenceAsync(Guid securityId, CancellationToken ct = default)
    {
        var security = await _securityMasterStore.GetProjectionAsync(securityId, ct).ConfigureAwait(false);
        if (security is null || !string.Equals(security.AssetClass, "CryptoCurrency", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var row = await _projectionStore.GetCryptoAsync(securityId, ct).ConfigureAwait(false);
        return row is null ? null : MapRow(row);
    }

    public async Task<IReadOnlyList<CryptoReferenceDto>> GetByNetworkAsync(string network, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(network))
        {
            return Array.Empty<CryptoReferenceDto>();
        }

        var rows = await _projectionStore.GetByNetworkAsync(network.Trim(), ct).ConfigureAwait(false);
        return rows.Select(MapRow).ToArray();
    }

    public async Task<IReadOnlyList<CryptoReferenceDto>> GetByBaseCurrencyAsync(string baseCurrency, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(baseCurrency))
        {
            return Array.Empty<CryptoReferenceDto>();
        }

        var rows = await _projectionStore.GetByBaseCurrencyAsync(baseCurrency.Trim().ToUpperInvariant(), ct).ConfigureAwait(false);
        return rows.Select(MapRow).ToArray();
    }

    private static CryptoReferenceDto MapRow(CryptoProjectionRow row)
        => new(
            row.SecurityId,
            row.DisplayName,
            row.BaseCurrency,
            row.QuoteCurrency,
            row.Network,
            row.PrimaryIdentifierValue,
            row.Version);
}

public sealed class NullCryptoReferenceService : ICryptoReferenceService
{
    public Task<CryptoReferenceDto?> GetReferenceAsync(Guid securityId, CancellationToken ct = default)
        => Task.FromResult<CryptoReferenceDto?>(null);

    public Task<IReadOnlyList<CryptoReferenceDto>> GetByNetworkAsync(string network, CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<CryptoReferenceDto>>(Array.Empty<CryptoReferenceDto>());

    public Task<IReadOnlyList<CryptoReferenceDto>> GetByBaseCurrencyAsync(string baseCurrency, CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<CryptoReferenceDto>>(Array.Empty<CryptoReferenceDto>());
}
