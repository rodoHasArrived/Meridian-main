using Meridian.Contracts.SecurityMaster;
using Npgsql;

namespace Meridian.Storage.SecurityMaster;

/// <summary>
/// Reads spot foreign exchange reference projections from PostgreSQL by security, pair, and currency.
/// </summary>
public sealed class PostgresFxSpotReferenceProjectionStore : IFxSpotReferenceProjectionStore
{
    internal const string AssetClass = "FxSpot";

    internal static readonly RegistryProjectionQuery ByPairCode = new(nameof(ByPairCode), "where pair_code = @pair_code");

    internal static readonly RegistryProjectionQuery ByCurrency = new(nameof(ByCurrency),
        """
        where base_currency = @currency or quote_currency = @currency
        order by pair_code
        """);

    private readonly RegistryReferenceProjectionStore<FxSpotProjectionRow> _store;

    public PostgresFxSpotReferenceProjectionStore(SecurityMasterOptions options)
    {
        _store = CreateStore(options);
    }

    internal static RegistryReferenceProjectionStore<FxSpotProjectionRow> CreateStore(SecurityMasterOptions options)
        => new(options, AssetClass, MapRow, [ByPairCode, ByCurrency]);

    public Task<FxSpotProjectionRow?> GetFxSpotAsync(Guid securityId, CancellationToken ct = default)
        => _store.GetBySecurityIdAsync(securityId, ct);

    public Task<FxSpotProjectionRow?> GetByPairCodeAsync(string pairCode, CancellationToken ct = default)
        => _store.SingleAsync(ByPairCode, [new NpgsqlParameter("pair_code", pairCode.Trim().ToUpperInvariant())], ct);

    public Task<IReadOnlyList<FxSpotProjectionRow>> GetByCurrencyAsync(string currency, CancellationToken ct = default)
        => _store.ListAsync(ByCurrency, [new NpgsqlParameter("currency", currency.Trim().ToUpperInvariant())], ct);

    internal static FxSpotProjectionRow MapRow(ProjectionRow row)
        => new(
            row.Guid("security_id"),
            row.String("display_name"),
            row.String("base_currency"),
            row.String("quote_currency"),
            row.String("pair_code"),
            row.String("primary_identifier_value"),
            row.Int64("version"));
}
