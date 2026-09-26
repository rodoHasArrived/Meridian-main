using Meridian.Contracts.SecurityMaster;
using Npgsql;

namespace Meridian.Storage.SecurityMaster;

public sealed class PostgresStructuredCreditReferenceProjectionStore : IStructuredCreditReferenceProjectionStore
{
    private const string TrancheColumns =
        """
        security_id, display_name, currency, tranche, pool_id, collateral_type,
        original_face, current_factor, coupon_or_index, factor_schedule_reference,
        maturity_date, primary_identifier_value, version
        """;

    private readonly SecurityMasterOptions _options;

    public PostgresStructuredCreditReferenceProjectionStore(SecurityMasterOptions options)
    {
        _options = options;
    }

    public async Task<StructuredCreditProjectionRow?> GetStructuredCreditAsync(Guid securityId, CancellationToken ct = default)
    {
        var rows = await QueryTranchesAsync(
            "where security_id = @security_id",
            [new NpgsqlParameter("security_id", securityId)],
            ct).ConfigureAwait(false);
        return rows.Count == 0 ? null : rows[0];
    }

    public Task<IReadOnlyList<StructuredCreditProjectionRow>> GetByPoolAsync(string poolId, CancellationToken ct = default)
        => QueryTranchesAsync(
            """
            where lower(pool_id) = lower(@pool_id)
            order by tranche, display_name
            """,
            [new NpgsqlParameter("pool_id", poolId.Trim())],
            ct);

    public Task<IReadOnlyList<StructuredCreditProjectionRow>> GetByCollateralTypeAsync(string collateralType, CancellationToken ct = default)
        => QueryTranchesAsync(
            """
            where lower(collateral_type) = lower(@collateral_type)
            order by maturity_date nulls last, display_name
            """,
            [new NpgsqlParameter("collateral_type", collateralType.Trim())],
            ct);

    public Task<IReadOnlyList<StructuredCreditFactorScheduleRow>> GetFactorScheduleAsync(Guid securityId, CancellationToken ct = default)
        => QueryFactorScheduleAsync(
            """
            where security_id = @security_id
            order by ordinal
            """,
            [new NpgsqlParameter("security_id", securityId)],
            ct);

    public async Task<StructuredCreditFactorScheduleRow?> GetFactorAsOfAsync(Guid securityId, DateOnly asOfDate, CancellationToken ct = default)
    {
        var rows = await QueryFactorScheduleAsync(
            """
            where security_id = @security_id and as_of_date <= @as_of_date
            order by as_of_date desc, ordinal desc
            limit 1
            """,
            [
                new NpgsqlParameter("security_id", securityId),
                new NpgsqlParameter("as_of_date", asOfDate.ToDateTime(TimeOnly.MinValue))
            ],
            ct).ConfigureAwait(false);
        return rows.Count == 0 ? null : rows[0];
    }

    private async Task<IReadOnlyList<StructuredCreditProjectionRow>> QueryTranchesAsync(
        string predicateSql,
        IReadOnlyList<NpgsqlParameter> parameters,
        CancellationToken ct)
    {
        await using var connection = await OpenConnectionAsync(ct).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText =
            $"""
            select {TrancheColumns}
            from {Qualified("structured_credit_projection")}
            {predicateSql};
            """;
        foreach (var parameter in parameters)
        {
            command.Parameters.Add(parameter);
        }

        var results = new List<StructuredCreditProjectionRow>();
        await using var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await reader.ReadAsync(ct).ConfigureAwait(false))
        {
            results.Add(MapTrancheRow(reader));
        }

        return results;
    }

    private async Task<IReadOnlyList<StructuredCreditFactorScheduleRow>> QueryFactorScheduleAsync(
        string predicateSql,
        IReadOnlyList<NpgsqlParameter> parameters,
        CancellationToken ct)
    {
        await using var connection = await OpenConnectionAsync(ct).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText =
            $"""
            select security_id, ordinal, as_of_date, factor
            from {Qualified("structured_credit_factor_schedule_projection")}
            {predicateSql};
            """;
        foreach (var parameter in parameters)
        {
            command.Parameters.Add(parameter);
        }

        var results = new List<StructuredCreditFactorScheduleRow>();
        await using var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await reader.ReadAsync(ct).ConfigureAwait(false))
        {
            results.Add(new(
                reader.GetGuid(0),
                reader.GetInt32(1),
                DateOnly.FromDateTime(reader.GetDateTime(2)),
                reader.GetDecimal(3)));
        }

        return results;
    }

    private static StructuredCreditProjectionRow MapTrancheRow(NpgsqlDataReader reader)
        => new(
            reader.GetGuid(0),
            reader.GetString(1),
            reader.GetString(2),
            reader.GetString(3),
            reader.IsDBNull(4) ? null : reader.GetString(4),
            reader.GetString(5),
            reader.GetDecimal(6),
            reader.IsDBNull(7) ? null : reader.GetDecimal(7),
            reader.GetString(8),
            reader.IsDBNull(9) ? null : reader.GetString(9),
            reader.IsDBNull(10) ? null : DateOnly.FromDateTime(reader.GetDateTime(10)),
            reader.GetString(11),
            reader.GetInt64(12));

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
