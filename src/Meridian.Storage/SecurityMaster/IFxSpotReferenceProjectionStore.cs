namespace Meridian.Storage.SecurityMaster;

/// <summary>
/// Queries spot foreign exchange reference projections by security, currency pair, and currency.
/// </summary>
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
