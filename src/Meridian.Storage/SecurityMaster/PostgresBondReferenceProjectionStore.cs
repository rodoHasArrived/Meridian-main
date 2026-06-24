using Meridian.Contracts.SecurityMaster;
using Npgsql;

namespace Meridian.Storage.SecurityMaster;

public sealed class PostgresBondReferenceProjectionStore : IBondReferenceProjectionStore
{
    private readonly SecurityMasterOptions _options;

    public PostgresBondReferenceProjectionStore(SecurityMasterOptions options)
    {
        _options = options;
    }

    public Task<BondProjectionRow?> GetBondAsync(Guid securityId, CancellationToken ct = default)
        => GetSingleBondAsync(
            """
            where bp.security_id = @security_id
            """,
            [new NpgsqlParameter("security_id", securityId)],
            ct);

    public Task<BondLifecycleProjectionRow?> GetLifecycleAsync(Guid securityId, CancellationToken ct = default)
        => GetSingleLifecycleAsync(securityId, ct);

    public Task<BondAccrualConventionProjectionRow?> GetAccrualConventionAsync(Guid securityId, CancellationToken ct = default)
        => GetSingleAccrualAsync(securityId, ct);

    public Task<IReadOnlyList<BondProjectionRow>> GetIssuerLadderAsync(string issuerName, CancellationToken ct = default)
        => GetBondListAsync(
            """
            where bip.issuer_name = @issuer_name
            order by bip.maturity_date nulls last, s.display_name
            """,
            [new NpgsqlParameter("issuer_name", issuerName.Trim())],
            ct);

    public Task<IReadOnlyList<BondProjectionRow>> GetMaturityLadderAsync(DateOnly from, DateOnly to, CancellationToken ct = default)
        => GetBondListAsync(
            """
            where bp.maturity_date between @from_date and @to_date
            order by bp.maturity_date, s.display_name
            """,
            [new NpgsqlParameter("from_date", from.ToDateTime(TimeOnly.MinValue)), new NpgsqlParameter("to_date", to.ToDateTime(TimeOnly.MinValue))],
            ct);

    private async Task<BondProjectionRow?> GetSingleBondAsync(string predicateSql, IReadOnlyList<NpgsqlParameter> parameters, CancellationToken ct)
    {
        var results = await GetBondListAsync(predicateSql, parameters, ct).ConfigureAwait(false);
        return results.Count == 0 ? null : results[0];
    }

    private async Task<IReadOnlyList<BondProjectionRow>> GetBondListAsync(string predicateSql, IReadOnlyList<NpgsqlParameter> parameters, CancellationToken ct)
    {
        await using var connection = await OpenConnectionAsync(ct).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText =
            $"""
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
                   blp.mandatory_put_date
            from {Qualified("bond_projection")} bp
            join {Qualified("securities")} s on s.security_id = bp.security_id
            left join {Qualified("bond_issuer_projection")} bip on bip.security_id = bp.security_id
            left join {Qualified("bond_lifecycle_projection")} blp on blp.security_id = bp.security_id
            left join {Qualified("bond_accrual_convention_projection")} bacp on bacp.security_id = bp.security_id
            {predicateSql};
            """;

        foreach (var parameter in parameters)
        {
            command.Parameters.Add(parameter);
        }

        var results = new List<BondProjectionRow>();
        await using var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await reader.ReadAsync(ct).ConfigureAwait(false))
        {
            results.Add(new BondProjectionRow(
                reader.GetGuid(0),
                reader.GetString(1),
                reader.GetString(2),
                reader.GetString(3),
                reader.IsDBNull(4) ? null : reader.GetString(4),
                reader.IsDBNull(5) ? null : reader.GetString(5),
                reader.IsDBNull(6) ? null : DateOnly.FromDateTime(reader.GetDateTime(6)),
                reader.IsDBNull(7) ? null : reader.GetString(7),
                reader.IsDBNull(8) ? null : DateOnly.FromDateTime(reader.GetDateTime(8)),
                reader.IsDBNull(9) ? null : DateOnly.FromDateTime(reader.GetDateTime(9)),
                reader.IsDBNull(10) ? null : reader.GetBoolean(10),
                reader.IsDBNull(11) ? null : reader.GetString(11),
                reader.IsDBNull(12) ? null : reader.GetInt32(12),
                reader.IsDBNull(13) ? null : reader.GetString(13),
                reader.IsDBNull(14) ? null : reader.GetString(14),
                reader.IsDBNull(15) ? null : reader.GetDecimal(15),
                reader.IsDBNull(16) ? null : reader.GetString(16),
                reader.IsDBNull(17) ? null : reader.GetDecimal(17),
                reader.GetInt64(18),
                reader.IsDBNull(19) ? null : reader.GetString(19),
                reader.IsDBNull(20) ? null : reader.GetDecimal(20),
                reader.IsDBNull(21) ? null : reader.GetString(21),
                reader.IsDBNull(22) ? null : DateOnly.FromDateTime(reader.GetDateTime(22)),
                reader.IsDBNull(23) ? null : DateOnly.FromDateTime(reader.GetDateTime(23)),
                reader.IsDBNull(24) ? null : DateOnly.FromDateTime(reader.GetDateTime(24))));
        }

        return results;
    }

    private async Task<BondLifecycleProjectionRow?> GetSingleLifecycleAsync(Guid securityId, CancellationToken ct)
    {
        await using var connection = await OpenConnectionAsync(ct).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText =
            $"""
            select security_id,
                   lifecycle_stat,
                   issue_date,
                   call_date,
                   maturity_date,
                   is_callable,
                   version,
                   subclass,
                   par,
                   payment_frequency,
                   legal_final_maturity,
                   pre_refund_date,
                   mandatory_put_date
            from {Qualified("bond_lifecycle_projection")}
            where security_id = @security_id;
            """;
        command.Parameters.AddWithValue("security_id", securityId);

        await using var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false);
        if (!await reader.ReadAsync(ct).ConfigureAwait(false))
        {
            return null;
        }

        return new BondLifecycleProjectionRow(
            reader.GetGuid(0),
            reader.GetString(1),
            reader.IsDBNull(2) ? null : DateOnly.FromDateTime(reader.GetDateTime(2)),
            reader.IsDBNull(3) ? null : DateOnly.FromDateTime(reader.GetDateTime(3)),
            DateOnly.FromDateTime(reader.GetDateTime(4)),
            reader.GetBoolean(5),
            reader.GetInt64(6),
            reader.IsDBNull(7) ? null : reader.GetString(7),
            reader.IsDBNull(8) ? null : reader.GetDecimal(8),
            reader.IsDBNull(9) ? null : reader.GetString(9),
            reader.IsDBNull(10) ? null : DateOnly.FromDateTime(reader.GetDateTime(10)),
            reader.IsDBNull(11) ? null : DateOnly.FromDateTime(reader.GetDateTime(11)),
            reader.IsDBNull(12) ? null : DateOnly.FromDateTime(reader.GetDateTime(12)));
    }

    private async Task<BondAccrualConventionProjectionRow?> GetSingleAccrualAsync(Guid securityId, CancellationToken ct)
    {
        await using var connection = await OpenConnectionAsync(ct).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText =
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
            from {Qualified("bond_accrual_convention_projection")}
            where security_id = @security_id;
            """;
        command.Parameters.AddWithValue("security_id", securityId);

        await using var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false);
        if (!await reader.ReadAsync(ct).ConfigureAwait(false))
        {
            return null;
        }

        return new BondAccrualConventionProjectionRow(
            reader.GetGuid(0),
            reader.IsDBNull(1) ? null : reader.GetString(1),
            reader.IsDBNull(2) ? null : reader.GetInt32(2),
            reader.IsDBNull(3) ? null : reader.GetString(3),
            reader.IsDBNull(4) ? null : reader.GetString(4),
            reader.IsDBNull(5) ? null : reader.GetDecimal(5),
            reader.IsDBNull(6) ? null : reader.GetString(6),
            reader.IsDBNull(7) ? null : reader.GetDecimal(7),
            reader.GetInt64(8));
    }

    private async Task<NpgsqlConnection> OpenConnectionAsync(CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(_options.ConnectionString))
        {
            throw new InvalidOperationException("SecurityMasterOptions.ConnectionString is not configured.");
        }

        var connection = new NpgsqlConnection(_options.ConnectionString);
        await connection.OpenAsync(ct).ConfigureAwait(false);
        return connection;
    }

    private string Qualified(string table) => $"{_options.Schema}.{table}";
}
