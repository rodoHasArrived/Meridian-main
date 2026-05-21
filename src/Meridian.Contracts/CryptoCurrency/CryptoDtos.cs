namespace Meridian.Contracts.CryptoCurrency;

public sealed record CryptoReferenceDto(
    Guid SecurityId,
    string DisplayName,
    string BaseCurrency,
    string QuoteCurrency,
    string? Network,
    string PrimaryIdentifier,
    long Version);
