using Meridian.Contracts.SecurityMaster;
using Npgsql;

namespace Meridian.Storage.SecurityMaster;

/// <summary>
/// Reads cryptocurrency reference projections from PostgreSQL by security, network, and base currency.
/// </summary>
public sealed class PostgresCryptoReferenceProjectionStore : ICryptoReferenceProjectionStore
{
    internal const string AssetClass = "CryptoCurrency";

    private readonly PostgresReferenceProjectionReader _reader;

    public PostgresCryptoReferenceProjectionStore(SecurityMasterOptions options)
    {
        _reader = new PostgresReferenceProjectionReader(options);
    }

    public Task<CryptoProjectionRow?> GetCryptoAsync(Guid securityId, CancellationToken ct = default)
        => _reader.QuerySingleAsync(
            _reader.SelectSql(AssetClass, "where security_id = @security_id"),
            [new NpgsqlParameter("security_id", securityId)],
            MapRow,
            ct);

    public Task<IReadOnlyList<CryptoProjectionRow>> GetByNetworkAsync(string network, CancellationToken ct = default)
        => _reader.QueryAsync(
            _reader.SelectSql(AssetClass,
                """
                where lower(network) = lower(@network)
                order by display_name
                """),
            [new NpgsqlParameter("network", network)],
            MapRow,
            ct);

    public Task<IReadOnlyList<CryptoProjectionRow>> GetByBaseCurrencyAsync(string baseCurrency, CancellationToken ct = default)
        => _reader.QueryAsync(
            _reader.SelectSql(AssetClass,
                """
                where base_currency = @base_currency
                order by display_name
                """),
            [new NpgsqlParameter("base_currency", baseCurrency)],
            MapRow,
            ct);

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
