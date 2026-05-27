using Meridian.Contracts.SecurityMaster;
using Npgsql;

namespace Meridian.Storage.SecurityMaster;

public sealed class PostgresFutureReferenceProjectionStore : IFutureReferenceProjectionStore
{
    private readonly SecurityMasterOptions _options;

    public PostgresFutureReferenceProjectionStore(SecurityMasterOptions options)
    {
        _options = options;
    }

    public async Task<FutureProjectionRow?> GetFutureAsync(Guid securityId, CancellationToken ct = default)
    {
        await using var connection = await OpenConnectionAsync(ct).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText =
            $"""
            select security_id, display_name, currency, root_symbol, contract_month, expiry_date,
                   multiplier, settlement_type, is_roll_target, roll_window_days, last_trading_dt,
                   first_notice_dt, delivery_month_dt, delivery_location_code, lifecycle_stat,
                   primary_identifier_value, version
            from {Qualified("future_projection")}
            where security_id = @security_id;
            """;
        command.Parameters.AddWithValue("security_id", securityId);
        return await ReadSingleAsync(command, ct).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<FutureProjectionRow>> GetByRootSymbolAsync(string rootSymbol, CancellationToken ct = default)
    {
        await using var connection = await OpenConnectionAsync(ct).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText =
            $"""
            select security_id, display_name, currency, root_symbol, contract_month, expiry_date,
                   multiplier, settlement_type, is_roll_target, roll_window_days, last_trading_dt,
                   first_notice_dt, delivery_month_dt, delivery_location_code, lifecycle_stat,
                   primary_identifier_value, version
            from {Qualified("future_projection")}
            where root_symbol = @root_symbol
            order by expiry_date;
            """;
        command.Parameters.AddWithValue("root_symbol", rootSymbol.Trim().ToUpperInvariant());
        return await ReadManyAsync(command, ct).ConfigureAwait(false);
    }

    private async Task<FutureProjectionRow?> ReadSingleAsync(NpgsqlCommand command, CancellationToken ct)
    {
        await using var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false);
        if (!await reader.ReadAsync(ct).ConfigureAwait(false))
        {
            return null;
        }

        return MapRow(reader);
    }

    private async Task<IReadOnlyList<FutureProjectionRow>> ReadManyAsync(NpgsqlCommand command, CancellationToken ct)
    {
        var results = new List<FutureProjectionRow>();
        await using var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await reader.ReadAsync(ct).ConfigureAwait(false))
        {
            results.Add(MapRow(reader));
        }

        return results;
    }

    private static FutureProjectionRow MapRow(NpgsqlDataReader reader)
        => new(
            reader.GetGuid(0),
            reader.GetString(1),
            reader.GetString(2),
            reader.GetString(3),
            reader.GetString(4),
            DateOnly.FromDateTime(reader.GetDateTime(5)),
            reader.GetDecimal(6),
            reader.IsDBNull(7) ? null : reader.GetString(7),
            reader.GetBoolean(8),
            reader.IsDBNull(9) ? null : reader.GetInt32(9),
            reader.IsDBNull(10) ? null : DateOnly.FromDateTime(reader.GetDateTime(10)),
            reader.IsDBNull(11) ? null : DateOnly.FromDateTime(reader.GetDateTime(11)),
            reader.IsDBNull(12) ? null : DateOnly.FromDateTime(reader.GetDateTime(12)),
            reader.IsDBNull(13) ? null : reader.GetString(13),
            reader.GetString(14),
            reader.GetString(15),
            reader.GetInt64(16));

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
