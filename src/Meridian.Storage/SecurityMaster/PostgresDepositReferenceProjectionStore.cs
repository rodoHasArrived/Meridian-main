using Meridian.Contracts.SecurityMaster;
using Npgsql;

namespace Meridian.Storage.SecurityMaster;

public sealed class PostgresDepositReferenceProjectionStore : IDepositReferenceProjectionStore
{
    private readonly SecurityMasterOptions _options;

    public PostgresDepositReferenceProjectionStore(SecurityMasterOptions options)
    {
        _options = options;
    }

    public async Task<DepositProjectionRow?> GetDepositAsync(Guid securityId, CancellationToken ct = default)
    {
        await using var connection = await OpenConnectionAsync(ct).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText =
            $"""
            select security_id, display_name, currency, deposit_type, institution_name,
                   maturity_date, interest_rate, day_count, is_callable,
                   primary_identifier_value, version
            from {Qualified("deposit_projection")}
            where security_id = @security_id;
            """;
        command.Parameters.AddWithValue("security_id", securityId);
        return await ReadSingleAsync(command, ct).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<DepositProjectionRow>> GetByInstitutionAsync(string institutionName, CancellationToken ct = default)
    {
        await using var connection = await OpenConnectionAsync(ct).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText =
            $"""
            select security_id, display_name, currency, deposit_type, institution_name,
                   maturity_date, interest_rate, day_count, is_callable,
                   primary_identifier_value, version
            from {Qualified("deposit_projection")}
            where lower(institution_name) = lower(@institution_name)
            order by maturity_date;
            """;
        command.Parameters.AddWithValue("institution_name", institutionName);
        return await ReadManyAsync(command, ct).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<DepositProjectionRow>> GetMaturingBeforeAsync(DateOnly beforeDate, CancellationToken ct = default)
    {
        await using var connection = await OpenConnectionAsync(ct).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText =
            $"""
            select security_id, display_name, currency, deposit_type, institution_name,
                   maturity_date, interest_rate, day_count, is_callable,
                   primary_identifier_value, version
            from {Qualified("deposit_projection")}
            where maturity_date is not null
              and maturity_date < @before_date
            order by maturity_date;
            """;
        command.Parameters.AddWithValue("before_date", beforeDate.ToDateTime(TimeOnly.MinValue));
        return await ReadManyAsync(command, ct).ConfigureAwait(false);
    }

    private async Task<DepositProjectionRow?> ReadSingleAsync(NpgsqlCommand command, CancellationToken ct)
    {
        await using var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false);
        if (!await reader.ReadAsync(ct).ConfigureAwait(false))
        {
            return null;
        }

        return MapRow(reader);
    }

    private async Task<IReadOnlyList<DepositProjectionRow>> ReadManyAsync(NpgsqlCommand command, CancellationToken ct)
    {
        var results = new List<DepositProjectionRow>();
        await using var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await reader.ReadAsync(ct).ConfigureAwait(false))
        {
            results.Add(MapRow(reader));
        }

        return results;
    }

    private static DepositProjectionRow MapRow(NpgsqlDataReader reader)
        => new(
            reader.GetGuid(0),
            reader.GetString(1),
            reader.GetString(2),
            reader.GetString(3),
            reader.GetString(4),
            reader.IsDBNull(5) ? null : DateOnly.FromDateTime(reader.GetDateTime(5)),
            reader.IsDBNull(6) ? null : reader.GetDecimal(6),
            reader.IsDBNull(7) ? null : reader.GetString(7),
            reader.GetBoolean(8),
            reader.GetString(9),
            reader.GetInt64(10));

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
