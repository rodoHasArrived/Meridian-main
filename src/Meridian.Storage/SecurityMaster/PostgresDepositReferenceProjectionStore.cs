using Meridian.Contracts.SecurityMaster;
using Npgsql;

namespace Meridian.Storage.SecurityMaster;

public sealed class PostgresDepositReferenceProjectionStore : IDepositReferenceProjectionStore
{
    internal const string AssetClass = "Deposit";

    private readonly PostgresReferenceProjectionReader _reader;

    public PostgresDepositReferenceProjectionStore(SecurityMasterOptions options)
    {
        _reader = new PostgresReferenceProjectionReader(options);
    }

    public Task<DepositProjectionRow?> GetDepositAsync(Guid securityId, CancellationToken ct = default)
        => _reader.QuerySingleAsync(
            _reader.SelectSql(AssetClass, "where security_id = @security_id"),
            [new NpgsqlParameter("security_id", securityId)],
            MapRow,
            ct);

    public Task<IReadOnlyList<DepositProjectionRow>> GetByInstitutionAsync(string institutionName, CancellationToken ct = default)
        => _reader.QueryAsync(
            _reader.SelectSql(AssetClass,
                """
                where lower(institution_name) = lower(@institution_name)
                order by maturity_date
                """),
            [new NpgsqlParameter("institution_name", institutionName)],
            MapRow,
            ct);

    public Task<IReadOnlyList<DepositProjectionRow>> GetMaturingBeforeAsync(DateOnly beforeDate, CancellationToken ct = default)
        => _reader.QueryAsync(
            _reader.SelectSql(AssetClass,
                """
                where maturity_date is not null
                  and maturity_date < @before_date
                order by maturity_date
                """),
            [new NpgsqlParameter("before_date", beforeDate.ToDateTime(TimeOnly.MinValue))],
            MapRow,
            ct);

    internal static DepositProjectionRow MapRow(ProjectionRow row)
        => new(
            row.Guid("security_id"),
            row.String("display_name"),
            row.String("currency"),
            row.String("deposit_type"),
            row.String("institution_name"),
            row.NullableDate("maturity_date"),
            row.NullableDecimal("interest_rate"),
            row.NullableString("day_count"),
            row.Boolean("is_callable"),
            row.String("primary_identifier_value"),
            row.Int64("version"));
}
