using System.Collections.Generic;
using System.Data;
using System.Text.Json;
using Meridian.Contracts.SecurityMaster;
using Npgsql;

namespace Meridian.Storage.SecurityMaster;

public sealed class PostgresSecurityMasterPricingStore : ISecurityMasterPricingStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = null,
        WriteIndented = false
    };

    private readonly SecurityMasterOptions _options;

    public PostgresSecurityMasterPricingStore(SecurityMasterOptions options)
    {
        _options = options;
    }

    public async Task<SecurityPricingHierarchyDto?> GetHierarchyAsync(
        Guid securityId, string? accountId, CancellationToken ct = default)
    {
        var accountIdKey = NormalizeAccountId(accountId);

        await using var connection = await OpenConnectionAsync(ct).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText =
            $"""
            select entries, as_of, updated_by
            from {Qualified("security_pricing_hierarchy")}
            where security_id = @security_id
              and account_id = @account_id;
            """;
        command.Parameters.AddWithValue("security_id", securityId);
        command.Parameters.AddWithValue("account_id", accountIdKey);

        await using var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false);
        if (!await reader.ReadAsync(ct).ConfigureAwait(false))
            return null;

        var entriesJson = reader.GetString(0);
        var asOf = new DateTimeOffset(DateTime.SpecifyKind(reader.GetDateTime(1), DateTimeKind.Utc));
        var updatedBy = reader.GetString(2);

        var entries = JsonSerializer.Deserialize<List<PricingHierarchyEntryDto>>(entriesJson, JsonOptions)
            ?? new List<PricingHierarchyEntryDto>();

        return new SecurityPricingHierarchyDto(securityId, accountId, entries, asOf, updatedBy);
    }

    public async Task UpsertHierarchyAsync(
        SecurityPricingHierarchyDto hierarchy, CancellationToken ct = default)
    {
        var entriesJson = JsonSerializer.Serialize(hierarchy.Entries, JsonOptions);
        var accountIdKey = NormalizeAccountId(hierarchy.AccountId);

        await using var connection = await OpenConnectionAsync(ct).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText =
            $"""
            insert into {Qualified("security_pricing_hierarchy")}
                (security_id, account_id, entries, as_of, updated_by)
            values (@security_id, @account_id, @entries::jsonb, @as_of, @updated_by)
            on conflict (security_id, account_id) do update
                set entries = excluded.entries,
                    as_of = excluded.as_of,
                    updated_by = excluded.updated_by;
            """;
        command.Parameters.AddWithValue("security_id", hierarchy.SecurityId);
        command.Parameters.AddWithValue("account_id", accountIdKey);
        command.Parameters.AddWithValue("entries", entriesJson);
        command.Parameters.AddWithValue("as_of", hierarchy.AsOf.UtcDateTime);
        command.Parameters.AddWithValue("updated_by", hierarchy.UpdatedBy);

        await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    private static string NormalizeAccountId(string? accountId)
        => string.IsNullOrWhiteSpace(accountId) ? string.Empty : accountId.Trim();

    public async Task RecordRawPriceAsync(
        Guid securityId, string sourceId, decimal price, DateTimeOffset priceAsOf,
        string recordedBy, CancellationToken ct = default)
    {
        await using var connection = await OpenConnectionAsync(ct).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText =
            $"""
            insert into {Qualified("security_raw_prices")}
                (security_id, source_id, price, price_as_of, recorded_by, recorded_at)
            values (@security_id, @source_id, @price, @price_as_of, @recorded_by, now())
            on conflict (security_id, source_id) do update
                set price = excluded.price,
                    price_as_of = excluded.price_as_of,
                    recorded_by = excluded.recorded_by,
                    recorded_at = now();
            """;
        command.Parameters.AddWithValue("security_id", securityId);
        command.Parameters.AddWithValue("source_id", sourceId);
        command.Parameters.AddWithValue("price", price);
        command.Parameters.AddWithValue("price_as_of", priceAsOf.UtcDateTime);
        command.Parameters.AddWithValue("recorded_by", recordedBy);

        await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<(string SourceId, decimal Price, DateTimeOffset PriceAsOf)>> GetRawPricesAsync(
        Guid securityId, CancellationToken ct = default)
    {
        await using var connection = await OpenConnectionAsync(ct).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText =
            $"""
            select source_id, price, price_as_of
            from {Qualified("security_raw_prices")}
            where security_id = @security_id
            order by source_id;
            """;
        command.Parameters.AddWithValue("security_id", securityId);

        await using var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false);
        var results = new List<(string, decimal, DateTimeOffset)>();
        while (await reader.ReadAsync(ct).ConfigureAwait(false))
        {
            var sourceId = reader.GetString(0);
            var price = reader.GetDecimal(1);
            var priceAsOf = new DateTimeOffset(DateTime.SpecifyKind(reader.GetDateTime(2), DateTimeKind.Utc));
            results.Add((sourceId, price, priceAsOf));
        }

        return results;
    }

    private async Task<NpgsqlConnection> OpenConnectionAsync(CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(_options.ConnectionString))
            throw new InvalidOperationException("SecurityMasterOptions.ConnectionString is not configured.");

        var connection = new NpgsqlConnection(_options.ConnectionString);
        await connection.OpenAsync(ct).ConfigureAwait(false);
        return connection;
    }

    private string Qualified(string table) => $"{_options.Schema}.{table}";
}
