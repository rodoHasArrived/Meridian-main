using Meridian.Contracts.SecurityMaster;
using Npgsql;

namespace Meridian.Storage.SecurityMaster;

/// <summary>
/// Reads commodity reference projections from PostgreSQL by security, type, and exchange.
/// </summary>
public sealed class PostgresCommodityReferenceProjectionStore : ICommodityReferenceProjectionStore
{
    internal const string AssetClass = "Commodity";

    private readonly PostgresReferenceProjectionReader _reader;

    public PostgresCommodityReferenceProjectionStore(SecurityMasterOptions options)
    {
        _reader = new PostgresReferenceProjectionReader(options);
    }

    public Task<CommodityProjectionRow?> GetCommodityAsync(Guid securityId, CancellationToken ct = default)
        => _reader.QuerySingleAsync(
            _reader.SelectSql(AssetClass, "where security_id = @security_id"),
            [new NpgsqlParameter("security_id", securityId)],
            MapRow,
            ct);

    public Task<IReadOnlyList<CommodityProjectionRow>> GetByCommodityTypeAsync(string commodityType, CancellationToken ct = default)
        => _reader.QueryAsync(
            _reader.SelectSql(AssetClass,
                """
                where lower(commodity_type) = lower(@commodity_type)
                order by display_name
                """),
            [new NpgsqlParameter("commodity_type", commodityType)],
            MapRow,
            ct);

    public Task<IReadOnlyList<CommodityProjectionRow>> GetByExchangeAsync(string exchangeCode, CancellationToken ct = default)
        => _reader.QueryAsync(
            _reader.SelectSql(AssetClass,
                """
                where exchange_code = @exchange_code
                order by display_name
                """),
            [new NpgsqlParameter("exchange_code", exchangeCode.Trim().ToUpperInvariant())],
            MapRow,
            ct);

    internal static CommodityProjectionRow MapRow(ProjectionRow row)
        => new(
            row.Guid("security_id"),
            row.String("display_name"),
            row.String("currency"),
            row.String("commodity_type"),
            row.NullableString("denomination"),
            row.NullableDecimal("contract_size"),
            row.NullableString("exchange_code"),
            row.NullableString("delivery_country"),
            row.String("primary_identifier_value"),
            row.Int64("version"));
}
