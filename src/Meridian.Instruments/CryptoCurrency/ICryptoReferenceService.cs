using Meridian.Contracts.CryptoCurrency;

namespace Meridian.Instruments.CryptoCurrency;

public interface ICryptoReferenceService
{
    Task<CryptoReferenceDto?> GetReferenceAsync(Guid securityId, CancellationToken ct = default);
    Task<IReadOnlyList<CryptoReferenceDto>> GetByNetworkAsync(string network, CancellationToken ct = default);
    Task<IReadOnlyList<CryptoReferenceDto>> GetByBaseCurrencyAsync(string baseCurrency, CancellationToken ct = default);
}
