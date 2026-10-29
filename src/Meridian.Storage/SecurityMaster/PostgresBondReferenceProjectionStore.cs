using System.Text.Json;
using Meridian.Contracts.SecurityMaster;
using NpgsqlTypes;
using Npgsql;

namespace Meridian.Storage.SecurityMaster;

/// <summary>
/// Reads the bond projection: the parent row joined to the securities spine and the issuer,
/// lifecycle and accrual-convention satellite projections that the Bond descriptor in
/// <see cref="SecurityTermsProjectionRegistry"/> writes. The parent table predates the registry's
/// identity spine and carries neither display name nor primary identifier, so this store joins
/// <c>securities</c> for them; it shares the connection, command and reader plumbing through
/// <see cref="PostgresReferenceProjectionReader"/> like every other reference-projection store.
/// </summary>
public sealed class PostgresBondReferenceProjectionStore : IBondReferenceProjectionStore
{
    private readonly PostgresReferenceProjectionReader _reader;

    public PostgresBondReferenceProjectionStore(SecurityMasterOptions options)
    {
        _reader = new PostgresReferenceProjectionReader(options);
    }

    public Task<BondProjectionRow?> GetBondAsync(Guid securityId, CancellationToken ct = default)
        => _reader.QuerySingleAsync(
            BondSelectSql(
                """
                where bp.security_id = @security_id
                """),
            [new NpgsqlParameter("security_id", securityId)],
            MapBondRow,
            ct);

    public Task<BondLifecycleProjectionRow?> GetLifecycleAsync(Guid securityId, CancellationToken ct = default)
        => _reader.QuerySingleAsync(
            $"""
            select blp.security_id,
                   lifecycle_stat,
                   issue_date,
                   call_date,
                   maturity_date,
                   is_callable,
                   blp.version,
                   subclass,
                   par,
                   payment_frequency,
                   legal_final_maturity,
                   pre_refund_date,
                   mandatory_put_date,
                   (s.effective_to at time zone 'UTC')::date as effective_to_date
            from {_reader.Qualified("bond_lifecycle_projection")} blp
            join {_reader.Qualified("securities")} s on s.security_id = blp.security_id
            where blp.security_id = @security_id;
            """,
            [new NpgsqlParameter("security_id", securityId)],
            MapLifecycleRow,
            ct);

    public Task<BondAccrualConventionProjectionRow?> GetAccrualConventionAsync(Guid securityId, CancellationToken ct = default)
        => _reader.QuerySingleAsync(
            $"""
            select security_id,
                   day_count_convention,
                   settlement_cycle_days,
                   holiday_calendar_id,
                   coupon_kind,
                   fixed_coupon_rate,
                   floating_rate_index,
                   floating_spread_bps,
                   version
            from {_reader.Qualified("bond_accrual_convention_projection")}
            where security_id = @security_id;
            """,
            [new NpgsqlParameter("security_id", securityId)],
            MapAccrualConventionRow,
            ct);

    public Task<IReadOnlyList<BondProjectionRow>> GetIssuerLadderAsync(string issuerName, CancellationToken ct = default)
        => _reader.QueryAsync(
            BondSelectSql(
                """
                where bip.issuer_name = @issuer_name
                order by bip.maturity_date nulls last, s.display_name, bp.security_id
                """),
            [new NpgsqlParameter("issuer_name", issuerName.Trim())],
            MapBondRow,
            ct);

    public Task<IReadOnlyList<BondProjectionRow>> GetMaturityLadderAsync(DateOnly from, DateOnly to, CancellationToken ct = default)
        => _reader.QueryAsync(
            BondSelectSql(
                """
                where bp.maturity_date between @from_date and @to_date
                order by bp.maturity_date, s.display_name, bp.security_id
                """),
            [new NpgsqlParameter("from_date", from.ToDateTime(TimeOnly.MinValue)), new NpgsqlParameter("to_date", to.ToDateTime(TimeOnly.MinValue))],
            MapBondRow,
            ct);

    public Task<IReadOnlyList<BondProjectionRow>> GetIssuerLadderPageAsync(string issuerName, int pageSize,
        DateOnly? afterMaturity, Guid? afterSecurityId, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(issuerName);
        var parameters = PageParameters(pageSize, afterMaturity, afterSecurityId);
        parameters.Add(new NpgsqlParameter("issuer_name", issuerName.Trim()));
        return _reader.QueryAsync(BondSelectSql(
            """
            where bp.issuer_name = @issuer_name
              and (@has_cursor = false or
                   (coalesce(bp.maturity_date, 'infinity'::date), bp.security_id) >
                   (coalesce(@after_maturity, 'infinity'::date), @after_security_id))
            order by bp.maturity_date nulls last, bp.security_id
            limit @page_limit
            """), parameters, MapBondRow, ct);
    }

    public Task<IReadOnlyList<BondProjectionRow>> GetMaturityLadderPageAsync(DateOnly from, DateOnly to, int pageSize,
        DateOnly? afterMaturity, Guid? afterSecurityId, CancellationToken ct = default)
    {
        if (to < from) throw new ArgumentException("Maturity range must be ordered.", nameof(to));
        if (afterSecurityId is not null && afterMaturity is null)
            throw new ArgumentException("Maturity paging requires a dated cursor.", nameof(afterMaturity));
        var parameters = PageParameters(pageSize, afterMaturity, afterSecurityId);
        parameters.Add(new NpgsqlParameter("from_date", NpgsqlDbType.Date) { Value = from });
        parameters.Add(new NpgsqlParameter("to_date", NpgsqlDbType.Date) { Value = to });
        return _reader.QueryAsync(BondSelectSql(
            """
            where bp.maturity_date between @from_date and @to_date
              and (@has_cursor = false or
                   (bp.maturity_date, bp.security_id) > (@after_maturity, @after_security_id))
            order by bp.maturity_date, bp.security_id
            limit @page_limit
            """), parameters, MapBondRow, ct);
    }

    private static List<NpgsqlParameter> PageParameters(int pageSize, DateOnly? afterMaturity, Guid? afterSecurityId)
    {
        if (pageSize is < 1 or > 500) throw new ArgumentOutOfRangeException(nameof(pageSize));
        if (afterMaturity is not null && afterSecurityId is null)
            throw new ArgumentException("A cursor maturity requires a security id.", nameof(afterSecurityId));
        return
        [
            new("page_limit", pageSize + 1),
            new("has_cursor", afterSecurityId.HasValue),
            new("after_maturity", NpgsqlDbType.Date) { Value = afterMaturity is { } date ? date : DBNull.Value },
            new("after_security_id", NpgsqlDbType.Uuid) { Value = afterSecurityId is { } id ? id : DBNull.Value }
        ];
    }

    private string BondSelectSql(string predicateSql)
        => $"""
            select bp.security_id,
                   s.display_name,
                   s.currency,
                   s.primary_identifier_value,
                   bp.issuer_name,
                   bp.seniority,
                   bp.maturity_date,
                   blp.lifecycle_stat,
                   blp.issue_date,
                   blp.call_date,
                   blp.is_callable,
                   bacp.day_count_convention,
                   bacp.settlement_cycle_days,
                   bacp.holiday_calendar_id,
                   bacp.coupon_kind,
                   bacp.fixed_coupon_rate,
                   bacp.floating_rate_index,
                   bacp.floating_spread_bps,
                   bp.version,
                   blp.subclass,
                   blp.par,
                   blp.payment_frequency,
                   blp.legal_final_maturity,
                   blp.pre_refund_date,
                   blp.mandatory_put_date,
                   s.asset_specific_terms::text as asset_specific_terms,
                   (s.effective_to at time zone 'UTC')::date as effective_to_date
            from {_reader.Qualified("bond_projection")} bp
            join {_reader.Qualified("securities")} s on s.security_id = bp.security_id
            left join {_reader.Qualified("bond_issuer_projection")} bip on bip.security_id = bp.security_id
            left join {_reader.Qualified("bond_lifecycle_projection")} blp on blp.security_id = bp.security_id
            left join {_reader.Qualified("bond_accrual_convention_projection")} bacp on bacp.security_id = bp.security_id
            {predicateSql};
            """;

    internal static BondProjectionRow MapBondRow(ProjectionRow row)
        => new(
            row.Guid("security_id"),
            row.String("display_name"),
            row.String("currency"),
            row.String("primary_identifier_value"),
            row.NullableString("issuer_name"),
            row.NullableString("seniority"),
            row.NullableDate("maturity_date"),
            row.NullableString("lifecycle_stat"),
            row.NullableDate("issue_date"),
            row.NullableDate("call_date"),
            row.NullableBoolean("is_callable"),
            row.NullableString("day_count_convention"),
            row.NullableInt32("settlement_cycle_days"),
            row.NullableString("holiday_calendar_id"),
            row.NullableString("coupon_kind"),
            row.NullableDecimal("fixed_coupon_rate"),
            row.NullableString("floating_rate_index"),
            row.NullableDecimal("floating_spread_bps"),
            row.Int64("version"),
            row.NullableString("subclass"),
            row.NullableDecimal("par"),
            row.NullableString("payment_frequency"),
            row.NullableDate("legal_final_maturity"),
            row.NullableDate("pre_refund_date"),
            row.NullableDate("mandatory_put_date"),
            ParseCanonicalTerms(row.NullableString("asset_specific_terms")),
            row.NullableDate("effective_to_date"));

    private static JsonElement? ParseCanonicalTerms(string? json)
    {
        if (json is null) return null;
        using var document = JsonDocument.Parse(json);
        return document.RootElement.Clone();
    }

    internal static BondLifecycleProjectionRow MapLifecycleRow(ProjectionRow row)
        => new(
            row.Guid("security_id"),
            row.String("lifecycle_stat"),
            row.NullableDate("issue_date"),
            row.NullableDate("call_date"),
            row.Date("maturity_date"),
            row.Boolean("is_callable"),
            row.Int64("version"),
            row.NullableString("subclass"),
            row.NullableDecimal("par"),
            row.NullableString("payment_frequency"),
            row.NullableDate("legal_final_maturity"),
            row.NullableDate("pre_refund_date"),
            row.NullableDate("mandatory_put_date"),
            row.NullableDate("effective_to_date"));

    internal static BondAccrualConventionProjectionRow MapAccrualConventionRow(ProjectionRow row)
        => new(
            row.Guid("security_id"),
            row.NullableString("day_count_convention"),
            row.NullableInt32("settlement_cycle_days"),
            row.NullableString("holiday_calendar_id"),
            row.NullableString("coupon_kind"),
            row.NullableDecimal("fixed_coupon_rate"),
            row.NullableString("floating_rate_index"),
            row.NullableDecimal("floating_spread_bps"),
            row.Int64("version"));
}
