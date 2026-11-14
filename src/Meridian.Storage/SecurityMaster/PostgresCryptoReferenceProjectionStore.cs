using Meridian.Contracts.SecurityMaster;
using Npgsql;

namespace Meridian.Storage.SecurityMaster;

/// <summary>
/// Reads cryptocurrency reference projections from PostgreSQL by security, network, and base currency.
/// </summary>
public sealed class PostgresCryptoReferenceProjectionStore : ICryptoReferenceProjectionStore
{
    internal const string AssetClass = "CryptoCurrency";

    internal static readonly RegistryProjectionQuery ByNetwork = new(nameof(ByNetwork),
        """
        where lower(network) = lower(@network)
        order by display_name
        """);

    internal static readonly RegistryProjectionQuery ByBaseCurrency = new(nameof(ByBaseCurrency),
        """
        where base_currency = @base_currency
        order by display_name
        """);

    private readonly RegistryReferenceProjectionStore<CryptoProjectionRow> _store;

    public PostgresCryptoReferenceProjectionStore(SecurityMasterOptions options)
    {
        _store = CreateStore(options);
    }

    internal static RegistryReferenceProjectionStore<CryptoProjectionRow> CreateStore(SecurityMasterOptions options)
        => new(options, AssetClass, MapRow, [ByNetwork, ByBaseCurrency]);

    public Task<CryptoProjectionRow?> GetCryptoAsync(Guid securityId, CancellationToken ct = default)
        => _store.GetBySecurityIdAsync(securityId, ct);

    public Task<IReadOnlyList<CryptoProjectionRow>> GetByNetworkAsync(string network, CancellationToken ct = default)
        => _store.ListAsync(ByNetwork, [new NpgsqlParameter("network", network)], ct);

    public Task<IReadOnlyList<CryptoProjectionRow>> GetByBaseCurrencyAsync(string baseCurrency, CancellationToken ct = default)
        => _store.ListAsync(ByBaseCurrency, [new NpgsqlParameter("base_currency", baseCurrency)], ct);

    internal static CryptoProjectionRow MapRow(ProjectionRow row)
        => new(
            row.Guid("security_id"),
            row.String("display_name"),
            row.String("base_currency"),
            row.String("quote_currency"),
            row.NullableString("network"),
            row.String("primary_identifier_value"),
            row.Int64("version"));
}
