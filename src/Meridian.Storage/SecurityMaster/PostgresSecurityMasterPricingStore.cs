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

    public Task<SecurityPricingHierarchyDto?> GetHierarchyAsync(
        Guid securityId, string? accountId, CancellationToken ct = default)
        => GetHierarchyAsOfAsync(securityId, accountId, DateTimeOffset.UtcNow, ct);

    public async Task<SecurityPricingHierarchyDto?> GetHierarchyAsOfAsync(
        Guid securityId, string? accountId, DateTimeOffset asOf, CancellationToken ct = default, DateTimeOffset? knownAt = null)
    {
        var accountIdKey = NormalizeAccountId(accountId);

        await using var connection = await OpenConnectionAsync(ct).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText =
            $"""
            select entries, as_of, updated_by from (
                select entries, as_of, updated_by from {Qualified("security_pricing_hierarchy")}
                where security_id = @security_id and account_id = @account_id and as_of <= @as_of and recorded_at <= @known_at
                union all
                select entries, as_of, updated_by from {Qualified("security_pricing_hierarchy_history")}
                where security_id = @security_id and account_id = @account_id and as_of <= @as_of and recorded_at <= @known_at
            ) versions order by as_of desc limit 1;
            """;
        command.Parameters.AddWithValue("security_id", securityId);
        command.Parameters.AddWithValue("account_id", accountIdKey);
        command.Parameters.AddWithValue("as_of", asOf.UtcDateTime);
        command.Parameters.AddWithValue("known_at", (knownAt ?? DateTimeOffset.UtcNow).UtcDateTime);

        await using var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false);
        if (!await reader.ReadAsync(ct).ConfigureAwait(false))
            return null;

        var entriesJson = reader.GetString(0);
        var effectiveAt = reader.GetFieldValue<DateTimeOffset>(1);
        var updatedBy = reader.GetString(2);

        var entries = JsonSerializer.Deserialize<List<PricingHierarchyEntryDto>>(entriesJson, JsonOptions)
            ?? new List<PricingHierarchyEntryDto>();

        return new SecurityPricingHierarchyDto(securityId, accountId, entries, effectiveAt, updatedBy);
    }

    public async Task UpsertHierarchyAsync(SecurityPricingHierarchyDto hierarchy, CancellationToken ct = default)
    {
        await using var connection = await OpenConnectionAsync(ct).ConfigureAwait(false);
        await using var transaction = await connection.BeginTransactionAsync(ct).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = $"""
            select pg_advisory_xact_lock(hashtextextended(@scope, 0));
            insert into {Qualified("security_pricing_hierarchy_history")}
                (security_id, account_id, entries, as_of, updated_by, recorded_at)
            select security_id, account_id, entries, as_of, updated_by, recorded_at
            from {Qualified("security_pricing_hierarchy")}
            where security_id = @security_id and account_id = @account_id
            on conflict do nothing;
            """;
        command.Parameters.AddWithValue("scope", $"pricing:{hierarchy.SecurityId}:{NormalizeAccountId(hierarchy.AccountId)}");
        command.Parameters.AddWithValue("security_id", hierarchy.SecurityId);
        command.Parameters.AddWithValue("account_id", NormalizeAccountId(hierarchy.AccountId));
        await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        command.Parameters.AddWithValue("entries", JsonSerializer.Serialize(hierarchy.Entries, JsonOptions));
        command.Parameters.AddWithValue("as_of", hierarchy.AsOf.UtcDateTime);
        command.Parameters.AddWithValue("updated_by", hierarchy.UpdatedBy);
        command.CommandText = $"""
            insert into {Qualified("security_pricing_hierarchy_history")} as retained
                (security_id, account_id, entries, as_of, updated_by)
            values (@security_id, @account_id, @entries::jsonb, @as_of, @updated_by)
            on conflict (security_id, account_id, as_of) do update set entries = retained.entries
            where retained.entries = excluded.entries and retained.updated_by = excluded.updated_by
            returning 1;
            """;
        if (await command.ExecuteScalarAsync(ct).ConfigureAwait(false) is null)
            throw new InvalidOperationException("A retained pricing hierarchy version cannot be replaced; use a new effective timestamp.");
        command.CommandText = $"""
            insert into {Qualified("security_pricing_hierarchy")} as current
                (security_id, account_id, entries, as_of, updated_by)
            values (@security_id, @account_id, @entries::jsonb, @as_of, @updated_by)
            on conflict (security_id, account_id) do update set entries = excluded.entries,
                as_of = excluded.as_of, updated_by = excluded.updated_by, recorded_at = excluded.recorded_at
            where excluded.as_of >= current.as_of;
            """;
        await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        await transaction.CommitAsync(ct).ConfigureAwait(false);
    }

    private static string NormalizeAccountId(string? accountId)
        => string.IsNullOrWhiteSpace(accountId) ? string.Empty : accountId.Trim();

    public async Task RecordRawPriceAsync(RecordRawPriceRequest request, CancellationToken ct = default)
    {
        if (request.Unit == SecurityPriceUnit.Unspecified || !Enum.IsDefined(request.Unit))
            throw new ArgumentException("An explicit price quote unit is required.", nameof(request));
        await using var connection = await OpenConnectionAsync(ct).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = $"""
            insert into {Qualified("security_raw_prices")} as retained
                (security_id, source_id, price, price_as_of, price_unit, recorded_by, recorded_at)
            values (@security_id, @source_id, @price, @price_as_of, @price_unit, @recorded_by, now())
            on conflict (security_id, source_id, price_as_of) do update set price = retained.price
            where retained.price = excluded.price and retained.price_unit = excluded.price_unit
                and retained.recorded_by = excluded.recorded_by
            returning 1;
            """;
        command.Parameters.AddWithValue("security_id", request.SecurityId);
        command.Parameters.AddWithValue("source_id", request.SourceId.Trim().ToLowerInvariant());
        command.Parameters.AddWithValue("price", request.Price);
        command.Parameters.AddWithValue("price_as_of", request.PriceAsOf.UtcDateTime);
        command.Parameters.AddWithValue("price_unit", request.Unit.ToString());
        command.Parameters.AddWithValue("recorded_by", request.RecordedBy);
        if (await command.ExecuteScalarAsync(ct).ConfigureAwait(false) is null)
            throw new InvalidOperationException("A retained price observation cannot be replaced; record a new observation timestamp.");
    }

    public async Task<IReadOnlyList<SecurityRawPriceDto>> GetRawPricesAsync(
        Guid securityId, DateTimeOffset asOf, CancellationToken ct = default, DateTimeOffset? knownAt = null)
    {
        await using var connection = await OpenConnectionAsync(ct).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = $"""
            select distinct on (source_id) source_id, price, price_as_of, price_unit
            from {Qualified("security_raw_prices")}
            where security_id = @security_id and price_as_of <= @as_of and recorded_at <= @known_at
            order by source_id, price_as_of desc;
            """;
        command.Parameters.AddWithValue("security_id", securityId);
        command.Parameters.AddWithValue("as_of", asOf.UtcDateTime);
        command.Parameters.AddWithValue("known_at", (knownAt ?? DateTimeOffset.UtcNow).UtcDateTime);
        await using var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false);
        var results = new List<SecurityRawPriceDto>();
        while (await reader.ReadAsync(ct).ConfigureAwait(false))
            results.Add(new SecurityRawPriceDto(reader.GetString(0), reader.GetDecimal(1),
                reader.GetFieldValue<DateTimeOffset>(2), Enum.Parse<SecurityPriceUnit>(reader.GetString(3))));
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
