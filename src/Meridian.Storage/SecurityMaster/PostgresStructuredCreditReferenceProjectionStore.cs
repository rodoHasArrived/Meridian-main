using Meridian.Contracts.SecurityMaster;
using Npgsql;

namespace Meridian.Storage.SecurityMaster;

public sealed class PostgresStructuredCreditReferenceProjectionStore : IStructuredCreditReferenceProjectionStore
{
    internal const string AssetClass = "StructuredCredit";
    internal const string FactorScheduleTable = "structured_credit_factor_schedule_projection";

    private readonly PostgresReferenceProjectionReader _reader;

    public PostgresStructuredCreditReferenceProjectionStore(SecurityMasterOptions options)
    {
        _reader = new PostgresReferenceProjectionReader(options);
    }

    public Task<StructuredCreditProjectionRow?> GetStructuredCreditAsync(Guid securityId, CancellationToken ct = default)
        => _reader.QuerySingleAsync(
            _reader.SelectSql(AssetClass, "where security_id = @security_id"),
            [new NpgsqlParameter("security_id", securityId)],
            MapTrancheRow,
            ct);

    public Task<IReadOnlyList<StructuredCreditProjectionRow>> GetByPoolAsync(string poolId, CancellationToken ct = default)
        => _reader.QueryAsync(
            _reader.SelectSql(AssetClass,
                """
                where lower(pool_id) = lower(@pool_id)
                order by tranche, display_name
                """),
            [new NpgsqlParameter("pool_id", poolId.Trim())],
            MapTrancheRow,
            ct);

    public Task<IReadOnlyList<StructuredCreditProjectionRow>> GetByCollateralTypeAsync(string collateralType, CancellationToken ct = default)
        => _reader.QueryAsync(
            _reader.SelectSql(AssetClass,
                """
                where lower(collateral_type) = lower(@collateral_type)
                order by maturity_date nulls last, display_name
                """),
            [new NpgsqlParameter("collateral_type", collateralType.Trim())],
            MapTrancheRow,
            ct);

    public Task<IReadOnlyList<StructuredCreditFactorScheduleRow>> GetFactorScheduleAsync(Guid securityId, CancellationToken ct = default)
        => _reader.QueryAsync(
            _reader.SelectChildSql(AssetClass, FactorScheduleTable,
                """
                where security_id = @security_id
                order by ordinal
                """),
            [new NpgsqlParameter("security_id", securityId)],
            MapFactorScheduleRow,
            ct);

    public Task<StructuredCreditFactorScheduleRow?> GetFactorAsOfAsync(Guid securityId, DateOnly asOfDate, CancellationToken ct = default)
        => _reader.QuerySingleAsync(
            _reader.SelectChildSql(AssetClass, FactorScheduleTable,
                """
                where security_id = @security_id and as_of_date <= @as_of_date
                order by as_of_date desc, ordinal desc
                limit 1
                """),
            [
                new NpgsqlParameter("security_id", securityId),
                new NpgsqlParameter("as_of_date", asOfDate.ToDateTime(TimeOnly.MinValue))
            ],
            MapFactorScheduleRow,
            ct);

    internal static StructuredCreditProjectionRow MapTrancheRow(ProjectionRow row)
        => new(
            row.Guid("security_id"),
            row.String("display_name"),
            row.String("currency"),
            row.String("tranche"),
            row.NullableString("pool_id"),
            row.String("collateral_type"),
            row.Decimal("original_face"),
            row.NullableDecimal("current_factor"),
            row.String("coupon_or_index"),
            row.NullableString("factor_schedule_reference"),
            row.NullableDate("maturity_date"),
            row.String("primary_identifier_value"),
            row.Int64("version"),
            row.NullableString("structured_credit_kind"),
            row.NullableString("seniority"),
            row.NullableDecimal("attachment_point"),
            row.NullableDecimal("detachment_point"),
            row.NullableDecimal("weighted_average_coupon"),
            row.NullableInt32("weighted_average_maturity_months"),
            row.NullableInt32("weighted_average_loan_age_months"),
            row.NullableDate("reinvestment_period_end"),
            row.NullableDate("non_call_period_end"));

    internal static StructuredCreditFactorScheduleRow MapFactorScheduleRow(ProjectionRow row)
        => new(
            row.Guid("security_id"),
            row.Int32("ordinal"),
            row.Date("as_of_date"),
            row.Decimal("factor"));
}
