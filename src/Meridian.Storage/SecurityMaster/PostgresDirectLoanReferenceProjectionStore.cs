using Meridian.Contracts.SecurityMaster;
using Npgsql;

namespace Meridian.Storage.SecurityMaster;

/// <summary>
/// Reads direct loan reference projections from PostgreSQL by security and borrower.
/// </summary>
public sealed class PostgresDirectLoanReferenceProjectionStore : IDirectLoanReferenceProjectionStore
{
    internal const string AssetClass = "DirectLoan";
    internal const string CovenantTable = "direct_loan_covenant_projection";
    internal const string PrincipalScheduleTable = "direct_loan_principal_schedule_projection";

    private readonly PostgresReferenceProjectionReader _reader;

    public PostgresDirectLoanReferenceProjectionStore(SecurityMasterOptions options)
    {
        _reader = new PostgresReferenceProjectionReader(options);
    }

    public Task<DirectLoanProjectionRow?> GetDirectLoanAsync(Guid securityId, CancellationToken ct = default)
        => _reader.QuerySingleAsync(
            _reader.SelectSql(AssetClass, "where security_id = @security_id"),
            [new NpgsqlParameter("security_id", securityId)],
            MapLoanRow,
            ct);

    public Task<IReadOnlyList<DirectLoanProjectionRow>> GetByBorrowerAsync(string borrower, CancellationToken ct = default)
        => _reader.QueryAsync(
            _reader.SelectSql(AssetClass,
                """
                where lower(borrower) = lower(@borrower)
                order by maturity_date nulls last, display_name
                """),
            [new NpgsqlParameter("borrower", borrower.Trim())],
            MapLoanRow,
            ct);

    public Task<IReadOnlyList<DirectLoanProjectionRow>> GetByReferenceIndexAsync(string referenceIndex, CancellationToken ct = default)
        => _reader.QueryAsync(
            _reader.SelectSql(AssetClass,
                """
                where lower(reference_index) = lower(@reference_index)
                order by maturity_date nulls last, display_name
                """),
            [new NpgsqlParameter("reference_index", referenceIndex.Trim())],
            MapLoanRow,
            ct);

    public Task<IReadOnlyList<DirectLoanProjectionRow>> GetMaturityLadderAsync(DateOnly from, DateOnly to, CancellationToken ct = default)
        => _reader.QueryAsync(
            _reader.SelectSql(AssetClass,
                """
                where maturity_date between @from_date and @to_date
                order by maturity_date, display_name
                """),
            [
                new NpgsqlParameter("from_date", from.ToDateTime(TimeOnly.MinValue)),
                new NpgsqlParameter("to_date", to.ToDateTime(TimeOnly.MinValue))
            ],
            MapLoanRow,
            ct);

    public Task<IReadOnlyList<DirectLoanCovenantRow>> GetCovenantsAsync(Guid securityId, CancellationToken ct = default)
        => _reader.QueryAsync(
            _reader.SelectChildSql(AssetClass, CovenantTable,
                """
                where security_id = @security_id
                order by ordinal
                """),
            [new NpgsqlParameter("security_id", securityId)],
            MapCovenantRow,
            ct);

    public Task<IReadOnlyList<DirectLoanPrincipalPaymentRow>> GetPrincipalScheduleAsync(Guid securityId, CancellationToken ct = default)
        => _reader.QueryAsync(
            _reader.SelectChildSql(AssetClass, PrincipalScheduleTable,
                """
                where security_id = @security_id
                order by ordinal
                """),
            [new NpgsqlParameter("security_id", securityId)],
            MapPrincipalPaymentRow,
            ct);

    public Task<IReadOnlyList<DirectLoanPrincipalPaymentRow>> GetPrincipalPaymentsDueAsync(DateOnly from, DateOnly to, CancellationToken ct = default)
        => _reader.QueryAsync(
            _reader.SelectChildSql(AssetClass, PrincipalScheduleTable,
                """
                where payment_date between @from_date and @to_date
                order by payment_date, security_id, ordinal
                """),
            [
                new NpgsqlParameter("from_date", from.ToDateTime(TimeOnly.MinValue)),
                new NpgsqlParameter("to_date", to.ToDateTime(TimeOnly.MinValue))
            ],
            MapPrincipalPaymentRow,
            ct);

    internal static DirectLoanProjectionRow MapLoanRow(ProjectionRow row)
        => new(
            row.Guid("security_id"),
            row.String("display_name"),
            row.String("currency"),
            row.String("borrower"),
            row.NullableDate("maturity_date"),
            row.NullableString("reference_index"),
            row.NullableDecimal("spread_bps"),
            row.NullableDecimal("current_coupon_rate"),
            row.NullableString("reset_frequency"),
            row.NullableString("pricing_source"),
            row.String("primary_identifier_value"),
            row.Int64("version"));

    internal static DirectLoanCovenantRow MapCovenantRow(ProjectionRow row)
        => new(
            row.Guid("security_id"),
            row.Int32("ordinal"),
            row.String("covenant_type"),
            row.String("threshold"),
            row.NullableString("notes"));

    internal static DirectLoanPrincipalPaymentRow MapPrincipalPaymentRow(ProjectionRow row)
        => new(
            row.Guid("security_id"),
            row.Int32("ordinal"),
            row.Date("payment_date"),
            row.Decimal("amount"));
}
