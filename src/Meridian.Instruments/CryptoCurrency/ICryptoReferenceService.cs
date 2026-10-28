using Meridian.Contracts.CryptoCurrency;

namespace Meridian.Instruments.CryptoCurrency;

/// <summary>
/// Queries cryptocurrency reference data by security identifier and supported asset-specific
/// filters.
/// </summary>
public interface ICryptoReferenceService
{
    Task<CryptoReferenceDto?> GetReferenceAsync(Guid securityId, CancellationToken ct = default);
    Task<IReadOnlyList<CryptoReferenceDto>> GetByNetworkAsync(string network, CancellationToken ct = default);
    Task<IReadOnlyList<CryptoReferenceDto>> GetByBaseCurrencyAsync(string baseCurrency, CancellationToken ct = default);
}
