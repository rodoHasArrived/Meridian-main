using Meridian.Contracts.SecurityMaster;
using Npgsql;

namespace Meridian.Storage.SecurityMaster;

public sealed class PostgresCertificateOfDepositReferenceProjectionStore : ICertificateOfDepositReferenceProjectionStore
{
    internal const string AssetClass = "CertificateOfDeposit";

    private readonly PostgresReferenceProjectionReader _reader;

    public PostgresCertificateOfDepositReferenceProjectionStore(SecurityMasterOptions options)
    {
        _reader = new PostgresReferenceProjectionReader(options);
    }

    public Task<CertificateOfDepositProjectionRow?> GetCertificateOfDepositAsync(Guid securityId, CancellationToken ct = default)
        => _reader.QuerySingleAsync(
            _reader.SelectSql(AssetClass, "where security_id = @security_id"),
            [new NpgsqlParameter("security_id", securityId)],
            MapRow,
            ct);

    public Task<IReadOnlyList<CertificateOfDepositProjectionRow>> GetByIssuerAsync(string issuerName, CancellationToken ct = default)
        => _reader.QueryAsync(
            _reader.SelectSql(AssetClass,
                """
                where lower(issuer_name) = lower(@issuer_name)
                order by maturity_date
                """),
            [new NpgsqlParameter("issuer_name", issuerName)],
            MapRow,
            ct);

    public Task<IReadOnlyList<CertificateOfDepositProjectionRow>> GetMaturingBeforeAsync(DateOnly beforeDate, CancellationToken ct = default)
        => _reader.QueryAsync(
            _reader.SelectSql(AssetClass,
                """
                where maturity_date < @before_date
                order by maturity_date
                """),
            [new NpgsqlParameter("before_date", beforeDate.ToDateTime(TimeOnly.MinValue))],
            MapRow,
            ct);

    internal static CertificateOfDepositProjectionRow MapRow(ProjectionRow row)
        => new(
            row.Guid("security_id"),
            row.String("display_name"),
            row.String("currency"),
            row.String("issuer_name"),
            row.Date("maturity_date"),
            row.NullableDecimal("coupon_rate"),
            row.NullableDate("callable_date"),
            row.NullableString("day_count"),
            row.String("primary_identifier_value"),
            row.Int64("version"));
}
