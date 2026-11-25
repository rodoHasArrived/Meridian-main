using Meridian.Contracts.SecurityMaster;
using Npgsql;

namespace Meridian.Storage.SecurityMaster;

/// <summary>
/// Reads structured credit reference projections from PostgreSQL, including pool and tranche information.
/// </summary>
public sealed class PostgresStructuredCreditReferenceProjectionStore : IStructuredCreditReferenceProjectionStore
{
    internal const string AssetClass = "StructuredCredit";
    internal const string FactorScheduleTable = "structured_credit_factor_schedule_projection";

    internal static readonly RegistryProjectionQuery ByPool = new(nameof(ByPool),
        """
        where lower(pool_id) = lower(@pool_id)
        order by tranche, display_name
        """);

    internal static readonly RegistryProjectionQuery ByCollateralType = new(nameof(ByCollateralType),
        """
        where lower(collateral_type) = lower(@collateral_type)
        order by maturity_date nulls last, display_name
        """);

    internal static readonly RegistryProjectionQuery FactorScheduleBySecurity = new(nameof(FactorScheduleBySecurity),
        """
        where security_id = @security_id
        order by ordinal
        """);

    internal static readonly RegistryProjectionQuery FactorAsOf = new(nameof(FactorAsOf),
        """
        where security_id = @security_id and as_of_date <= @as_of_date
        order by as_of_date desc, ordinal desc
        limit 1
        """);

    private readonly RegistryReferenceProjectionStore<StructuredCreditProjectionRow> _tranches;
    private readonly RegistryReferenceProjectionStore<StructuredCreditFactorScheduleRow> _factorSchedule;

    public PostgresStructuredCreditReferenceProjectionStore(SecurityMasterOptions options)
    {
        _tranches = CreateTrancheStore(options);
        _factorSchedule = CreateFactorScheduleStore(options);
    }

    internal static RegistryReferenceProjectionStore<StructuredCreditProjectionRow> CreateTrancheStore(SecurityMasterOptions options)
        => new(options, AssetClass, MapTrancheRow, [ByPool, ByCollateralType]);

    internal static RegistryReferenceProjectionStore<StructuredCreditFactorScheduleRow> CreateFactorScheduleStore(SecurityMasterOptions options)
        => new(options, AssetClass, MapFactorScheduleRow, [FactorScheduleBySecurity, FactorAsOf], FactorScheduleTable);

    public Task<StructuredCreditProjectionRow?> GetStructuredCreditAsync(Guid securityId, CancellationToken ct = default)
        => _tranches.GetBySecurityIdAsync(securityId, ct);

    public Task<IReadOnlyList<StructuredCreditProjectionRow>> GetByPoolAsync(string poolId, CancellationToken ct = default)
        => _tranches.ListAsync(ByPool, [new NpgsqlParameter("pool_id", poolId.Trim())], ct);

    public Task<IReadOnlyList<StructuredCreditProjectionRow>> GetByCollateralTypeAsync(string collateralType, CancellationToken ct = default)
        => _tranches.ListAsync(ByCollateralType, [new NpgsqlParameter("collateral_type", collateralType.Trim())], ct);

    public Task<IReadOnlyList<StructuredCreditFactorScheduleRow>> GetFactorScheduleAsync(Guid securityId, CancellationToken ct = default)
        => _factorSchedule.ListAsync(FactorScheduleBySecurity, [new NpgsqlParameter("security_id", securityId)], ct);

    public Task<StructuredCreditFactorScheduleRow?> GetFactorAsOfAsync(Guid securityId, DateOnly asOfDate, CancellationToken ct = default)
        => _factorSchedule.SingleAsync(
            FactorAsOf,
            [
                new NpgsqlParameter("security_id", securityId),
                new NpgsqlParameter("as_of_date", asOfDate.ToDateTime(TimeOnly.MinValue))
            ],
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
