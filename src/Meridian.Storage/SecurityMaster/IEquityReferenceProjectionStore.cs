namespace Meridian.Storage.SecurityMaster;

public interface IEquityReferenceProjectionStore
{
    Task<EquityProjectionRow?> GetEquityAsync(Guid securityId, CancellationToken ct = default);
    Task<IReadOnlyList<EquityProjectionRow>> GetByExchangeAsync(string exchangeCode, CancellationToken ct = default);
    Task<IReadOnlyList<EquityProjectionRow>> GetByIssuerAsync(string issuerName, CancellationToken ct = default);
}

public sealed record EquityProjectionRow(
    Guid SecurityId,
    string DisplayName,
    string Currency,
    string? ShareClass,
    string? VotingRightsCat,
    string? Classification,
    string? ExchangeCode,
    string? CountryOfRisk,
    string? IssuerName,
    string PrimaryIdentifierValue,
    long Version);
