using Meridian.Contracts.SecurityMaster;
using Npgsql;

namespace Meridian.Storage.SecurityMaster;

public sealed class PostgresFxSpotReferenceProjectionStore : IFxSpotReferenceProjectionStore
{
    private readonly SecurityMasterOptions _options;

    public PostgresFxSpotReferenceProjectionStore(SecurityMasterOptions options)
    {
        _options = options;
    }

    public async Task<FxSpotProjectionRow?> GetFxSpotAsync(Guid securityId, CancellationToken ct = default)
    {
        await using var connection = await OpenConnectionAsync(ct).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText =
            $"""
            select security_id, display_name, base_currency, quote_currency, pair_code,
                   primary_identifier_value, version
            from {Qualified("fxspot_projection")}
            where security_id = @security_id;
            """;
        command.Parameters.AddWithValue("security_id", securityId);
        return await ReadSingleAsync(command, ct).ConfigureAwait(false);
    }

    public async Task<FxSpotProjectionRow?> GetByPairCodeAsync(string pairCode, CancellationToken ct = default)
    {
        await using var connection = await OpenConnectionAsync(ct).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText =
            $"""
            select security_id, display_name, base_currency, quote_currency, pair_code,
                   primary_identifier_value, version
            from {Qualified("fxspot_projection")}
            where pair_code = @pair_code;
            """;
        command.Parameters.AddWithValue("pair_code", pairCode.Trim().ToUpperInvariant());
        return await ReadSingleAsync(command, ct).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<FxSpotProjectionRow>> GetByCurrencyAsync(string currency, CancellationToken ct = default)
    {
        await using var connection = await OpenConnectionAsync(ct).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText =
            $"""
            select security_id, display_name, base_currency, quote_currency, pair_code,
                   primary_identifier_value, version
            from {Qualified("fxspot_projection")}
            where base_currency = @currency or quote_currency = @currency
            order by pair_code;
            """;
        command.Parameters.AddWithValue("currency", currency.Trim().ToUpperInvariant());
        return await ReadManyAsync(command, ct).ConfigureAwait(false);
    }

    private async Task<FxSpotProjectionRow?> ReadSingleAsync(NpgsqlCommand command, CancellationToken ct)
    {
        await using var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false);
        if (!await reader.ReadAsync(ct).ConfigureAwait(false))
        {
            return null;
        }

        return MapRow(reader);
    }

    private async Task<IReadOnlyList<FxSpotProjectionRow>> ReadManyAsync(NpgsqlCommand command, CancellationToken ct)
    {
        var results = new List<FxSpotProjectionRow>();
        await using var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await reader.ReadAsync(ct).ConfigureAwait(false))
        {
            results.Add(MapRow(reader));
        }

        return results;
    }

    private static FxSpotProjectionRow MapRow(NpgsqlDataReader reader)
        => new(
            reader.GetGuid(0),
            reader.GetString(1),
            reader.GetString(2),
            reader.GetString(3),
            reader.GetString(4),
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
