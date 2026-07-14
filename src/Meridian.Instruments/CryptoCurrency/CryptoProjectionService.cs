using Meridian.Contracts.CryptoCurrency;
using Meridian.Storage.SecurityMaster;

namespace Meridian.Instruments.CryptoCurrency;

public sealed class CryptoProjectionService
    : InstrumentProjectionServiceBase<CryptoProjectionRow, CryptoReferenceDto>, ICryptoReferenceService
{
    private readonly ICryptoReferenceProjectionStore _projectionStore;

    public CryptoProjectionService(
        ISecurityMasterStore securityMasterStore,
        ICryptoReferenceProjectionStore projectionStore)
        : base(securityMasterStore)
    {
        _projectionStore = projectionStore;
    }

    protected override string AssetClass => "CryptoCurrency";

    protected override Task<CryptoProjectionRow?> FetchRowAsync(Guid securityId, CancellationToken ct)
        => _projectionStore.GetCryptoAsync(securityId, ct);

    public Task<IReadOnlyList<CryptoReferenceDto>> GetByNetworkAsync(string network, CancellationToken ct = default)
        => QueryByTermAsync(network, _projectionStore.GetByNetworkAsync, ct);

    public Task<IReadOnlyList<CryptoReferenceDto>> GetByBaseCurrencyAsync(string baseCurrency, CancellationToken ct = default)
        => QueryByTermAsync(baseCurrency, _projectionStore.GetByBaseCurrencyAsync, ct, toUpperInvariant: true);

    protected override CryptoReferenceDto MapRow(CryptoProjectionRow row)
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
