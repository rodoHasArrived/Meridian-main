using Meridian.Contracts.SecurityMaster;
using Npgsql;

namespace Meridian.Storage.SecurityMaster;

public sealed class PostgresSwapReferenceProjectionStore : ISwapReferenceProjectionStore
{
    private readonly SecurityMasterOptions _options;

    public PostgresSwapReferenceProjectionStore(SecurityMasterOptions options)
    {
        _options = options;
    }

    public async Task<SwapProjectionRow?> GetSwapAsync(Guid securityId, CancellationToken ct = default)
    {
        await using var connection = await OpenConnectionAsync(ct).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText =
            $"""
            select security_id, display_name, currency, swap_type, effective_date, maturity_date,
                   lifecycle_stat, primary_identifier_value, version
            from {Qualified("swap_projection")}
            where security_id = @security_id;
            """;
        command.Parameters.AddWithValue("security_id", securityId);
        return await ReadSingleAsync(command, ct).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<SwapProjectionRow>> GetBySwapTypeAsync(string swapType, CancellationToken ct = default)
    {
        await using var connection = await OpenConnectionAsync(ct).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText =
            $"""
            select security_id, display_name, currency, swap_type, effective_date, maturity_date,
                   lifecycle_stat, primary_identifier_value, version
            from {Qualified("swap_projection")}
            where lower(swap_type) = lower(@swap_type)
            order by maturity_date;
            """;
        command.Parameters.AddWithValue("swap_type", swapType);
        return await ReadManyAsync(command, ct).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<SwapProjectionRow>> GetMaturingBeforeAsync(DateOnly beforeDate, CancellationToken ct = default)
    {
        await using var connection = await OpenConnectionAsync(ct).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText =
            $"""
            select security_id, display_name, currency, swap_type, effective_date, maturity_date,
                   lifecycle_stat, primary_identifier_value, version
            from {Qualified("swap_projection")}
            where maturity_date < @before_date
              and lifecycle_stat not in ('Matured', 'Terminated')
            order by maturity_date;
            """;
        command.Parameters.AddWithValue("before_date", beforeDate.ToDateTime(TimeOnly.MinValue));
        return await ReadManyAsync(command, ct).ConfigureAwait(false);
    }

    private async Task<SwapProjectionRow?> ReadSingleAsync(NpgsqlCommand command, CancellationToken ct)
    {
        await using var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false);
        if (!await reader.ReadAsync(ct).ConfigureAwait(false))
        {
            return null;
        }

        return MapRow(reader);
    }

    private async Task<IReadOnlyList<SwapProjectionRow>> ReadManyAsync(NpgsqlCommand command, CancellationToken ct)
    {
        var results = new List<SwapProjectionRow>();
        await using var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await reader.ReadAsync(ct).ConfigureAwait(false))
        {
            results.Add(MapRow(reader));
        }

        return results;
    }

    private static SwapProjectionRow MapRow(NpgsqlDataReader reader)
        => new(
            reader.GetGuid(0),
            reader.GetString(1),
            reader.GetString(2),
            reader.IsDBNull(3) ? null : reader.GetString(3),
            DateOnly.FromDateTime(reader.GetDateTime(4)),
            DateOnly.FromDateTime(reader.GetDateTime(5)),
            reader.GetString(6),
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
