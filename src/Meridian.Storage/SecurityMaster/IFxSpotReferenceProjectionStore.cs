namespace Meridian.Storage.SecurityMaster;

public interface IFxSpotReferenceProjectionStore
{
    Task<FxSpotProjectionRow?> GetFxSpotAsync(Guid securityId, CancellationToken ct = default);
    Task<FxSpotProjectionRow?> GetByPairCodeAsync(string pairCode, CancellationToken ct = default);
    Task<IReadOnlyList<FxSpotProjectionRow>> GetByCurrencyAsync(string currency, CancellationToken ct = default);
}

public sealed record FxSpotProjectionRow(
    Guid SecurityId,
    string DisplayName,
    string BaseCurrency,
    string QuoteCurrency,
    string PairCode,
    string PrimaryIdentifierValue,
    long Version);
