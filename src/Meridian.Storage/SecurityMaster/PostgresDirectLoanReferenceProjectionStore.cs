using Meridian.Contracts.SecurityMaster;
using Npgsql;

namespace Meridian.Storage.SecurityMaster;

public sealed class PostgresDirectLoanReferenceProjectionStore : IDirectLoanReferenceProjectionStore
{
    private const string LoanColumns =
        """
        security_id, display_name, currency, borrower, maturity_date, reference_index,
        spread_bps, current_coupon_rate, reset_frequency, pricing_source,
        primary_identifier_value, version
        """;

    private readonly SecurityMasterOptions _options;

    public PostgresDirectLoanReferenceProjectionStore(SecurityMasterOptions options)
    {
        _options = options;
    }

    public async Task<DirectLoanProjectionRow?> GetDirectLoanAsync(Guid securityId, CancellationToken ct = default)
    {
        var rows = await QueryLoansAsync(
            "where security_id = @security_id",
            [new NpgsqlParameter("security_id", securityId)],
            ct).ConfigureAwait(false);
        return rows.Count == 0 ? null : rows[0];
    }

    public Task<IReadOnlyList<DirectLoanProjectionRow>> GetByBorrowerAsync(string borrower, CancellationToken ct = default)
        => QueryLoansAsync(
            """
            where lower(borrower) = lower(@borrower)
            order by maturity_date nulls last, display_name
            """,
            [new NpgsqlParameter("borrower", borrower.Trim())],
            ct);

    public Task<IReadOnlyList<DirectLoanProjectionRow>> GetByReferenceIndexAsync(string referenceIndex, CancellationToken ct = default)
        => QueryLoansAsync(
            """
            where lower(reference_index) = lower(@reference_index)
            order by maturity_date nulls last, display_name
            """,
            [new NpgsqlParameter("reference_index", referenceIndex.Trim())],
            ct);

    public Task<IReadOnlyList<DirectLoanProjectionRow>> GetMaturityLadderAsync(DateOnly from, DateOnly to, CancellationToken ct = default)
        => QueryLoansAsync(
            """
            where maturity_date between @from_date and @to_date
            order by maturity_date, display_name
            """,
            [
                new NpgsqlParameter("from_date", from.ToDateTime(TimeOnly.MinValue)),
                new NpgsqlParameter("to_date", to.ToDateTime(TimeOnly.MinValue))
            ],
            ct);

    public async Task<IReadOnlyList<DirectLoanCovenantRow>> GetCovenantsAsync(Guid securityId, CancellationToken ct = default)
    {
        await using var connection = await OpenConnectionAsync(ct).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText =
            $"""
            select security_id, ordinal, covenant_type, threshold, notes
            from {Qualified("direct_loan_covenant_projection")}
            where security_id = @security_id
            order by ordinal;
            """;
        command.Parameters.AddWithValue("security_id", securityId);

        var results = new List<DirectLoanCovenantRow>();
        await using var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await reader.ReadAsync(ct).ConfigureAwait(false))
        {
            results.Add(new(
                reader.GetGuid(0),
                reader.GetInt32(1),
                reader.GetString(2),
                reader.GetString(3),
                reader.IsDBNull(4) ? null : reader.GetString(4)));
        }

        return results;
    }

    public Task<IReadOnlyList<DirectLoanPrincipalPaymentRow>> GetPrincipalScheduleAsync(Guid securityId, CancellationToken ct = default)
        => QueryPrincipalPaymentsAsync(
            """
            where security_id = @security_id
            order by ordinal
            """,
            [new NpgsqlParameter("security_id", securityId)],
            ct);

    public Task<IReadOnlyList<DirectLoanPrincipalPaymentRow>> GetPrincipalPaymentsDueAsync(DateOnly from, DateOnly to, CancellationToken ct = default)
        => QueryPrincipalPaymentsAsync(
            """
            where payment_date between @from_date and @to_date
            order by payment_date, security_id, ordinal
            """,
            [
                new NpgsqlParameter("from_date", from.ToDateTime(TimeOnly.MinValue)),
                new NpgsqlParameter("to_date", to.ToDateTime(TimeOnly.MinValue))
            ],
            ct);

    private async Task<IReadOnlyList<DirectLoanProjectionRow>> QueryLoansAsync(
        string predicateSql,
        IReadOnlyList<NpgsqlParameter> parameters,
        CancellationToken ct)
    {
        await using var connection = await OpenConnectionAsync(ct).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText =
            $"""
            select {LoanColumns}
            from {Qualified("direct_loan_projection")}
            {predicateSql};
            """;
        foreach (var parameter in parameters)
        {
            command.Parameters.Add(parameter);
        }

        var results = new List<DirectLoanProjectionRow>();
        await using var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await reader.ReadAsync(ct).ConfigureAwait(false))
        {
            results.Add(MapLoanRow(reader));
        }

        return results;
    }

    private async Task<IReadOnlyList<DirectLoanPrincipalPaymentRow>> QueryPrincipalPaymentsAsync(
        string predicateSql,
        IReadOnlyList<NpgsqlParameter> parameters,
        CancellationToken ct)
    {
        await using var connection = await OpenConnectionAsync(ct).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText =
            $"""
            select security_id, ordinal, payment_date, amount
            from {Qualified("direct_loan_principal_schedule_projection")}
            {predicateSql};
            """;
        foreach (var parameter in parameters)
        {
            command.Parameters.Add(parameter);
        }

        var results = new List<DirectLoanPrincipalPaymentRow>();
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

    private static DirectLoanProjectionRow MapLoanRow(NpgsqlDataReader reader)
        => new(
            reader.GetGuid(0),
            reader.GetString(1),
            reader.GetString(2),
            reader.GetString(3),
            reader.IsDBNull(4) ? null : DateOnly.FromDateTime(reader.GetDateTime(4)),
            reader.IsDBNull(5) ? null : reader.GetString(5),
            reader.IsDBNull(6) ? null : reader.GetDecimal(6),
            reader.IsDBNull(7) ? null : reader.GetDecimal(7),
            reader.IsDBNull(8) ? null : reader.GetString(8),
            reader.IsDBNull(9) ? null : reader.GetString(9),
            reader.GetString(10),
            reader.GetInt64(11));

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
