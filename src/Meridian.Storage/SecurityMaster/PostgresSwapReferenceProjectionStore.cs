using Meridian.Contracts.SecurityMaster;
using Npgsql;

namespace Meridian.Storage.SecurityMaster;

public sealed class PostgresSwapReferenceProjectionStore : ISwapReferenceProjectionStore
{
    internal const string AssetClass = "Swap";

    private readonly PostgresReferenceProjectionReader _reader;

    public PostgresSwapReferenceProjectionStore(SecurityMasterOptions options)
    {
        _reader = new PostgresReferenceProjectionReader(options);
    }

    public Task<SwapProjectionRow?> GetSwapAsync(Guid securityId, CancellationToken ct = default)
        => _reader.QuerySingleAsync(
            _reader.SelectSql(AssetClass, "where security_id = @security_id"),
            [new NpgsqlParameter("security_id", securityId)],
            MapRow,
            ct);

    public Task<IReadOnlyList<SwapProjectionRow>> GetBySwapTypeAsync(string swapType, CancellationToken ct = default)
        => _reader.QueryAsync(
            _reader.SelectSql(AssetClass,
                """
                where lower(swap_type) = lower(@swap_type)
                order by maturity_date
                """),
            [new NpgsqlParameter("swap_type", swapType)],
            MapRow,
            ct);

    public Task<IReadOnlyList<SwapProjectionRow>> GetMaturingBeforeAsync(DateOnly beforeDate, CancellationToken ct = default)
        => _reader.QueryAsync(
            _reader.SelectSql(AssetClass,
                """
                where maturity_date < @before_date
                  and lifecycle_stat not in ('Matured', 'Terminated')
                order by maturity_date
                """),
            [new NpgsqlParameter("before_date", beforeDate.ToDateTime(TimeOnly.MinValue))],
            MapRow,
            ct);

    internal static SwapProjectionRow MapRow(ProjectionRow row)
        => new(
            row.Guid("security_id"),
            row.String("display_name"),
            row.String("currency"),
            row.NullableString("swap_type"),
            row.Date("effective_date"),
            row.Date("maturity_date"),
            row.String("lifecycle_stat"),
            row.String("primary_identifier_value"),
            row.Int64("version"));
}
