using Meridian.Contracts.SecurityMaster;
using Npgsql;

namespace Meridian.Storage.SecurityMaster;

public sealed class PostgresFxSpotReferenceProjectionStore : IFxSpotReferenceProjectionStore
{
    internal const string AssetClass = "FxSpot";

    private readonly PostgresReferenceProjectionReader _reader;

    public PostgresFxSpotReferenceProjectionStore(SecurityMasterOptions options)
    {
        _reader = new PostgresReferenceProjectionReader(options);
    }

    public Task<FxSpotProjectionRow?> GetFxSpotAsync(Guid securityId, CancellationToken ct = default)
        => _reader.QuerySingleAsync(
            _reader.SelectSql(AssetClass, "where security_id = @security_id"),
            [new NpgsqlParameter("security_id", securityId)],
            MapRow,
            ct);

    public Task<FxSpotProjectionRow?> GetByPairCodeAsync(string pairCode, CancellationToken ct = default)
        => _reader.QuerySingleAsync(
            _reader.SelectSql(AssetClass, "where pair_code = @pair_code"),
            [new NpgsqlParameter("pair_code", pairCode.Trim().ToUpperInvariant())],
            MapRow,
            ct);

    public Task<IReadOnlyList<FxSpotProjectionRow>> GetByCurrencyAsync(string currency, CancellationToken ct = default)
        => _reader.QueryAsync(
            _reader.SelectSql(AssetClass,
                """
                where base_currency = @currency or quote_currency = @currency
                order by pair_code
                """),
            [new NpgsqlParameter("currency", currency.Trim().ToUpperInvariant())],
            MapRow,
            ct);

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
