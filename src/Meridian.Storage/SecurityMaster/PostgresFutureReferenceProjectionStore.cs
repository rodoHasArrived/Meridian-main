using Meridian.Contracts.SecurityMaster;
using Npgsql;

namespace Meridian.Storage.SecurityMaster;

public sealed class PostgresFutureReferenceProjectionStore : IFutureReferenceProjectionStore
{
    internal const string AssetClass = "Future";

    private readonly PostgresReferenceProjectionReader _reader;

    public PostgresFutureReferenceProjectionStore(SecurityMasterOptions options)
    {
        _reader = new PostgresReferenceProjectionReader(options);
    }

    public Task<FutureProjectionRow?> GetFutureAsync(Guid securityId, CancellationToken ct = default)
        => _reader.QuerySingleAsync(
            _reader.SelectSql(AssetClass, "where security_id = @security_id"),
            [new NpgsqlParameter("security_id", securityId)],
            MapRow,
            ct);

    public Task<IReadOnlyList<FutureProjectionRow>> GetByRootSymbolAsync(string rootSymbol, CancellationToken ct = default)
        => _reader.QueryAsync(
            _reader.SelectSql(AssetClass,
                """
                where root_symbol = @root_symbol
                order by expiry_date
                """),
            [new NpgsqlParameter("root_symbol", rootSymbol.Trim().ToUpperInvariant())],
            MapRow,
            ct);

    internal static FutureProjectionRow MapRow(ProjectionRow row)
        => new(
            row.Guid("security_id"),
            row.String("display_name"),
            row.String("currency"),
            row.String("root_symbol"),
            row.String("contract_month"),
            row.Date("expiry_date"),
            row.Decimal("multiplier"),
            row.NullableString("settlement_type"),
            row.Boolean("is_roll_target"),
            row.NullableInt32("roll_window_days"),
            row.NullableDate("last_trading_dt"),
            row.NullableDate("first_notice_dt"),
            row.NullableDate("delivery_month_dt"),
            row.NullableString("delivery_location_code"),
            row.String("lifecycle_stat"),
            row.String("primary_identifier_value"),
            row.Int64("version"));
}
