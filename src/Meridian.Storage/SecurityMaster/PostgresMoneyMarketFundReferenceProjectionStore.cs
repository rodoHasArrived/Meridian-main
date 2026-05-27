using Meridian.Contracts.SecurityMaster;
using Npgsql;

namespace Meridian.Storage.SecurityMaster;

public sealed class PostgresMoneyMarketFundReferenceProjectionStore : IMoneyMarketFundReferenceProjectionStore
{
    private readonly SecurityMasterOptions _options;

    public PostgresMoneyMarketFundReferenceProjectionStore(SecurityMasterOptions options)
    {
        _options = options;
    }

    public async Task<MoneyMarketFundProjectionRow?> GetMoneyMarketFundAsync(Guid securityId, CancellationToken ct = default)
    {
        await using var connection = await OpenConnectionAsync(ct).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText =
            $"""
            select security_id, display_name, currency, fund_family, sweep_eligible,
                   weighted_average_maturity_days, liquidity_fee_eligible, primary_identifier_value, version
            from {Qualified("money_market_fund_projection")}
            where security_id = @security_id;
            """;
        command.Parameters.AddWithValue("security_id", securityId);
        return await ReadSingleAsync(command, ct).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<MoneyMarketFundProjectionRow>> GetByFundFamilyAsync(string fundFamily, CancellationToken ct = default)
    {
        await using var connection = await OpenConnectionAsync(ct).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText =
            $"""
            select security_id, display_name, currency, fund_family, sweep_eligible,
                   weighted_average_maturity_days, liquidity_fee_eligible, primary_identifier_value, version
            from {Qualified("money_market_fund_projection")}
            where lower(fund_family) = lower(@fund_family)
            order by display_name;
            """;
        command.Parameters.AddWithValue("fund_family", fundFamily);
        return await ReadManyAsync(command, ct).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<MoneyMarketFundProjectionRow>> GetBySweepEligibilityAsync(bool sweepEligible, CancellationToken ct = default)
    {
        await using var connection = await OpenConnectionAsync(ct).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText =
            $"""
            select security_id, display_name, currency, fund_family, sweep_eligible,
                   weighted_average_maturity_days, liquidity_fee_eligible, primary_identifier_value, version
            from {Qualified("money_market_fund_projection")}
            where sweep_eligible = @sweep_eligible
            order by display_name;
            """;
        command.Parameters.AddWithValue("sweep_eligible", sweepEligible);
        return await ReadManyAsync(command, ct).ConfigureAwait(false);
    }

    private async Task<MoneyMarketFundProjectionRow?> ReadSingleAsync(NpgsqlCommand command, CancellationToken ct)
    {
        await using var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false);
        if (!await reader.ReadAsync(ct).ConfigureAwait(false))
        {
            return null;
        }

        return MapRow(reader);
    }

    private async Task<IReadOnlyList<MoneyMarketFundProjectionRow>> ReadManyAsync(NpgsqlCommand command, CancellationToken ct)
    {
        var results = new List<MoneyMarketFundProjectionRow>();
        await using var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await reader.ReadAsync(ct).ConfigureAwait(false))
        {
            results.Add(MapRow(reader));
        }

        return results;
    }

    private static MoneyMarketFundProjectionRow MapRow(NpgsqlDataReader reader)
        => new(
            reader.GetGuid(0),
            reader.GetString(1),
            reader.GetString(2),
            reader.IsDBNull(3) ? null : reader.GetString(3),
            reader.GetBoolean(4),
            reader.IsDBNull(5) ? null : reader.GetInt32(5),
            reader.GetBoolean(6),
            reader.GetString(7),
            reader.GetInt64(8));

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
