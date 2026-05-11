using Meridian.Contracts.SecurityMaster;
using Npgsql;

namespace Meridian.Storage.SecurityMaster;

public sealed class PostgresCommodityReferenceProjectionStore : ICommodityReferenceProjectionStore
{
    private readonly SecurityMasterOptions _options;

    public PostgresCommodityReferenceProjectionStore(SecurityMasterOptions options)
    {
        _options = options;
    }

    public async Task<CommodityProjectionRow?> GetCommodityAsync(Guid securityId, CancellationToken ct = default)
    {
        await using var connection = await OpenConnectionAsync(ct).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText =
            $"""
            select security_id, display_name, currency, commodity_type, denomination,
                   contract_size, exchange_code, delivery_country, primary_identifier_value, version
            from {Qualified("commodity_projection")}
            where security_id = @security_id;
            """;
        command.Parameters.AddWithValue("security_id", securityId);
        return await ReadSingleAsync(command, ct).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<CommodityProjectionRow>> GetByCommodityTypeAsync(string commodityType, CancellationToken ct = default)
    {
        await using var connection = await OpenConnectionAsync(ct).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText =
            $"""
            select security_id, display_name, currency, commodity_type, denomination,
                   contract_size, exchange_code, delivery_country, primary_identifier_value, version
            from {Qualified("commodity_projection")}
            where lower(commodity_type) = lower(@commodity_type)
            order by display_name;
            """;
        command.Parameters.AddWithValue("commodity_type", commodityType);
        return await ReadManyAsync(command, ct).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<CommodityProjectionRow>> GetByExchangeAsync(string exchangeCode, CancellationToken ct = default)
    {
        await using var connection = await OpenConnectionAsync(ct).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText =
            $"""
            select security_id, display_name, currency, commodity_type, denomination,
                   contract_size, exchange_code, delivery_country, primary_identifier_value, version
            from {Qualified("commodity_projection")}
            where exchange_code = @exchange_code
            order by display_name;
            """;
        command.Parameters.AddWithValue("exchange_code", exchangeCode.Trim().ToUpperInvariant());
        return await ReadManyAsync(command, ct).ConfigureAwait(false);
    }

    private async Task<CommodityProjectionRow?> ReadSingleAsync(NpgsqlCommand command, CancellationToken ct)
    {
        await using var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false);
        if (!await reader.ReadAsync(ct).ConfigureAwait(false))
        {
            return null;
        }

        return MapRow(reader);
    }

    private async Task<IReadOnlyList<CommodityProjectionRow>> ReadManyAsync(NpgsqlCommand command, CancellationToken ct)
    {
        var results = new List<CommodityProjectionRow>();
        await using var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await reader.ReadAsync(ct).ConfigureAwait(false))
        {
            results.Add(MapRow(reader));
        }

        return results;
    }

    private static CommodityProjectionRow MapRow(NpgsqlDataReader reader)
        => new(
            reader.GetGuid(0),
            reader.GetString(1),
            reader.GetString(2),
            reader.GetString(3),
            reader.IsDBNull(4) ? null : reader.GetString(4),
            reader.IsDBNull(5) ? null : reader.GetDecimal(5),
            reader.IsDBNull(6) ? null : reader.GetString(6),
            reader.IsDBNull(7) ? null : reader.GetString(7),
            reader.GetString(8),
            reader.GetInt64(9));

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
