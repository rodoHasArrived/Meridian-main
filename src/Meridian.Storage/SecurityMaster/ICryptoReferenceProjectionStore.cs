using Meridian.Contracts.SecurityMaster;

namespace Meridian.Storage.SecurityMaster;

public interface ICryptoReferenceProjectionStore
{
    Task<CryptoProjectionRow?> GetCryptoAsync(Guid securityId, CancellationToken ct = default);
    Task<IReadOnlyList<CryptoProjectionRow>> GetByNetworkAsync(string network, CancellationToken ct = default);
    Task<IReadOnlyList<CryptoProjectionRow>> GetByBaseCurrencyAsync(string baseCurrency, CancellationToken ct = default);
}

public sealed record CryptoProjectionRow(
    Guid SecurityId,
    string DisplayName,
    string BaseCurrency,
    string QuoteCurrency,
    string? Network,
    string PrimaryIdentifierValue,
    long Version);
