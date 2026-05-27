using Meridian.Contracts.SecurityMaster;
using Npgsql;

namespace Meridian.Storage.SecurityMaster;

public sealed class PostgresCryptoReferenceProjectionStore : ICryptoReferenceProjectionStore
{
    private readonly SecurityMasterOptions _options;

    public PostgresCryptoReferenceProjectionStore(SecurityMasterOptions options)
    {
        _options = options;
    }

    public async Task<CryptoProjectionRow?> GetCryptoAsync(Guid securityId, CancellationToken ct = default)
    {
        await using var connection = await OpenConnectionAsync(ct).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText =
            $"""
            select security_id, display_name, base_currency, quote_currency, network,
                   primary_identifier_value, version
            from {Qualified("crypto_projection")}
            where security_id = @security_id;
            """;
        command.Parameters.AddWithValue("security_id", securityId);
        return await ReadSingleAsync(command, ct).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<CryptoProjectionRow>> GetByNetworkAsync(string network, CancellationToken ct = default)
    {
        await using var connection = await OpenConnectionAsync(ct).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText =
            $"""
            select security_id, display_name, base_currency, quote_currency, network,
                   primary_identifier_value, version
            from {Qualified("crypto_projection")}
            where lower(network) = lower(@network)
            order by display_name;
            """;
        command.Parameters.AddWithValue("network", network);
        return await ReadManyAsync(command, ct).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<CryptoProjectionRow>> GetByBaseCurrencyAsync(string baseCurrency, CancellationToken ct = default)
    {
        await using var connection = await OpenConnectionAsync(ct).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText =
            $"""
            select security_id, display_name, base_currency, quote_currency, network,
                   primary_identifier_value, version
            from {Qualified("crypto_projection")}
            where base_currency = @base_currency
            order by display_name;
            """;
        command.Parameters.AddWithValue("base_currency", baseCurrency);
        return await ReadManyAsync(command, ct).ConfigureAwait(false);
    }

    private async Task<CryptoProjectionRow?> ReadSingleAsync(NpgsqlCommand command, CancellationToken ct)
    {
        await using var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false);
        if (!await reader.ReadAsync(ct).ConfigureAwait(false))
        {
            return null;
        }

        return MapRow(reader);
    }

    private async Task<IReadOnlyList<CryptoProjectionRow>> ReadManyAsync(NpgsqlCommand command, CancellationToken ct)
    {
        var results = new List<CryptoProjectionRow>();
        await using var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await reader.ReadAsync(ct).ConfigureAwait(false))
        {
            results.Add(MapRow(reader));
        }

        return results;
    }

    private static CryptoProjectionRow MapRow(NpgsqlDataReader reader)
        => new(
            reader.GetGuid(0),
            reader.GetString(1),
            reader.GetString(2),
            reader.GetString(3),
            reader.IsDBNull(4) ? null : reader.GetString(4),
            reader.GetString(5),
            reader.GetInt64(6));

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
