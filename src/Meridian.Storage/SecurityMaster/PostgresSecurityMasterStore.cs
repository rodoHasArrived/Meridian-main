using System.Text.Json;
using Meridian.Contracts.SecurityMaster;
using Meridian.Core.Serialization;
using Npgsql;
using NpgsqlTypes;

namespace Meridian.Storage.SecurityMaster;

public sealed class PostgresSecurityMasterStore : ISecurityMasterStore
{
    private readonly SecurityMasterOptions _options;

    public PostgresSecurityMasterStore(SecurityMasterOptions options)
    {
        _options = options;
    }

    public async Task UpsertProjectionAsync(SecurityProjectionRecord record, CancellationToken ct = default)
    {
        await using var connection = await OpenConnectionAsync(ct).ConfigureAwait(false);
        await using var transaction = await connection.BeginTransactionAsync(ct).ConfigureAwait(false);
        await UpsertProjectionCoreAsync(connection, transaction, record, ct).ConfigureAwait(false);
        await transaction.CommitAsync(ct).ConfigureAwait(false);
    }

    public async Task PersistProjectionBatchAsync(
        string projectionName,
        long lastGlobalSequence,
        IReadOnlyList<SecurityProjectionRecord> records,
        CancellationToken ct = default)
    {
        await using var connection = await OpenConnectionAsync(ct).ConfigureAwait(false);
        await using var transaction = await connection.BeginTransactionAsync(ct).ConfigureAwait(false);

        foreach (var record in records)
        {
            await UpsertProjectionCoreAsync(connection, transaction, record, ct).ConfigureAwait(false);
        }

        await SaveCheckpointCoreAsync(connection, transaction, projectionName, lastGlobalSequence, ct).ConfigureAwait(false);
        await transaction.CommitAsync(ct).ConfigureAwait(false);
    }

    public async Task UpsertAliasAsync(SecurityAliasDto alias, CancellationToken ct = default)
    {
        await using var connection = await OpenConnectionAsync(ct).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText =
            $"""
            insert into {Qualified("security_aliases")} (
                alias_id, security_id, alias_kind, alias_value, provider, scope, reason,
                created_by, created_at, valid_from, valid_to, is_enabled)
            values (
                @alias_id, @security_id, @alias_kind, @alias_value, @provider, @scope, @reason,
                @created_by, @created_at, @valid_from, @valid_to, @is_enabled)
            on conflict (alias_id) do update set
                security_id = excluded.security_id,
                alias_kind = excluded.alias_kind,
                alias_value = excluded.alias_value,
                provider = excluded.provider,
                scope = excluded.scope,
                reason = excluded.reason,
                created_by = excluded.created_by,
                created_at = excluded.created_at,
                valid_from = excluded.valid_from,
                valid_to = excluded.valid_to,
                is_enabled = excluded.is_enabled;
            """;

        command.Parameters.AddWithValue("alias_id", alias.AliasId);
        command.Parameters.AddWithValue("security_id", alias.SecurityId);
        command.Parameters.AddWithValue("alias_kind", alias.AliasKind);
        command.Parameters.AddWithValue("alias_value", alias.AliasValue);
        command.Parameters.AddWithValue("provider", (object?)alias.Provider ?? DBNull.Value);
        command.Parameters.AddWithValue("scope", alias.Scope.ToString());
        command.Parameters.AddWithValue("reason", (object?)alias.Reason ?? DBNull.Value);
        command.Parameters.AddWithValue("created_by", alias.CreatedBy);
        command.Parameters.AddWithValue("created_at", alias.CreatedAt.UtcDateTime);
        command.Parameters.AddWithValue("valid_from", alias.ValidFrom.UtcDateTime);
        command.Parameters.AddWithValue("valid_to", (object?)alias.ValidTo?.UtcDateTime ?? DBNull.Value);
        command.Parameters.AddWithValue("is_enabled", alias.IsEnabled);

        await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    public async Task DeactivateProjectionAsync(Guid securityId, DateTimeOffset effectiveTo, long version, CancellationToken ct = default)
    {
        await using var connection = await OpenConnectionAsync(ct).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText =
            $"""
            update {Qualified("securities")}
            set status = 'Inactive',
                effective_to = @effective_to,
                version = @version
            where security_id = @security_id;
            """;
        command.Parameters.AddWithValue("security_id", securityId);
        command.Parameters.AddWithValue("effective_to", effectiveTo.UtcDateTime);
        command.Parameters.AddWithValue("version", version);
        await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    public async Task<SecurityDetailDto?> GetDetailAsync(Guid securityId, CancellationToken ct = default)
    {
        var projection = await GetProjectionAsync(securityId, ct).ConfigureAwait(false);
        return projection is null ? null : SecurityMasterDbMapper.ToDetail(projection);
    }

    public async Task<SecurityProjectionRecord?> GetProjectionAsync(Guid securityId, CancellationToken ct = default)
    {
        await using var connection = await OpenConnectionAsync(ct).ConfigureAwait(false);
        return await GetProjectionCoreAsync(connection, securityId, ct).ConfigureAwait(false);
    }

    public async Task<SecurityProjectionRecord?> GetByIdentifierAsync(SecurityIdentifierKind kind, string value, string? provider, DateTimeOffset asOfUtc, bool includeInactive, CancellationToken ct = default)
    {
        await using var connection = await OpenConnectionAsync(ct).ConfigureAwait(false);
        var securityId = await ResolveSecurityIdAsync(connection, kind, value, provider, asOfUtc, includeInactive, ct).ConfigureAwait(false);
        return securityId is null ? null : await GetProjectionCoreAsync(connection, securityId.Value, ct).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<SecuritySummaryDto>> SearchAsync(SecuritySearchRequest request, CancellationToken ct = default)
    {
        await using var connection = await OpenConnectionAsync(ct).ConfigureAwait(false);
        await using var command = connection.CreateCommand();

        var trimmedQuery = request.Query.Trim();
        var useFts = trimmedQuery.Length >= 2;

        // Use full-text search when the query is long enough for meaningful tokenisation.
        // The ILIKE fallback catches short identifiers (e.g. "GE", "T") that tsvector skips.
        command.CommandText = useFts
            ? $"""
               select security_id,
                      ts_rank(search_vector, plainto_tsquery('simple', @query)) as rank
               from {Qualified("securities")}
               where (
                   search_vector @@ plainto_tsquery('simple', @query)
                   or display_name ilike @pattern
                   or primary_identifier_value ilike @pattern)
                 and (@active_only = false or status = 'Active')
               order by rank desc, display_name
               limit @take offset @skip;
               """
            : $"""
               select security_id, 0::float4 as rank
               from {Qualified("securities")}
               where (
                   display_name ilike @pattern
                   or primary_identifier_value ilike @pattern)
                 and (@active_only = false or status = 'Active')
               order by display_name
               limit @take offset @skip;
               """;

        command.Parameters.AddWithValue("query", trimmedQuery);
        command.Parameters.AddWithValue("pattern", $"%{trimmedQuery}%");
        command.Parameters.AddWithValue("active_only", request.ActiveOnly);
        command.Parameters.AddWithValue("take", request.Take);
        command.Parameters.AddWithValue("skip", request.Skip);

        var ids = new List<Guid>();
        await using var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await reader.ReadAsync(ct).ConfigureAwait(false))
        {
            ids.Add(reader.GetGuid(0));
        }

        var results = new List<SecuritySummaryDto>(ids.Count);
        foreach (var id in ids)
        {
            var projection = await GetProjectionCoreAsync(connection, id, ct).ConfigureAwait(false);
            if (projection is not null)
            {
                results.Add(SecurityMasterDbMapper.ToSummary(projection));
            }
        }

        return results;
    }

    public async Task<IReadOnlyList<SecurityProjectionRecord>> LoadAllAsync(CancellationToken ct = default)
    {
        await using var connection = await OpenConnectionAsync(ct).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = $"select security_id from {Qualified("securities")} order by display_name;";

        var ids = new List<Guid>();
        await using var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await reader.ReadAsync(ct).ConfigureAwait(false))
        {
            ids.Add(reader.GetGuid(0));
        }

        var results = new List<SecurityProjectionRecord>(ids.Count);
        foreach (var id in ids)
        {
            var projection = await GetProjectionCoreAsync(connection, id, ct).ConfigureAwait(false);
            if (projection is not null)
            {
                results.Add(projection);
            }
        }

        return results;
    }

    public async Task<long?> GetCheckpointAsync(string projectionName, CancellationToken ct = default)
    {
        await using var connection = await OpenConnectionAsync(ct).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText =
            $"""
            select last_global_sequence
            from {Qualified("projection_checkpoint")}
            where projection_name = @projection_name;
            """;
        command.Parameters.AddWithValue("projection_name", projectionName);
        var result = await command.ExecuteScalarAsync(ct).ConfigureAwait(false);
        return result is null or DBNull ? null : (long)result;
    }

    public async Task SaveCheckpointAsync(string projectionName, long lastGlobalSequence, CancellationToken ct = default)
    {
        await using var connection = await OpenConnectionAsync(ct).ConfigureAwait(false);
        await SaveCheckpointCoreAsync(connection, transaction: null, projectionName, lastGlobalSequence, ct).ConfigureAwait(false);
    }

    private async Task UpsertProjectionCoreAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        SecurityProjectionRecord record,
        CancellationToken ct)
    {
        await using (var command = connection.CreateCommand())
        {
            command.Transaction = transaction;
            command.CommandText =
                $"""
                insert into {Qualified("securities")} (
                    security_id, asset_class, status, display_name, currency, country_of_risk, issuer_name,
                    exchange_code, lot_size, tick_size, primary_identifier_kind, primary_identifier_value,
                    common_terms, asset_specific_terms, provenance, version, effective_from, effective_to)
                values (
                    @security_id, @asset_class, @status, @display_name, @currency, @country_of_risk, @issuer_name,
                    @exchange_code, @lot_size, @tick_size, @primary_identifier_kind, @primary_identifier_value,
                    @common_terms::jsonb, @asset_specific_terms::jsonb, @provenance::jsonb, @version,
                    @effective_from, @effective_to)
                on conflict (security_id) do update set
                    asset_class = excluded.asset_class,
                    status = excluded.status,
                    display_name = excluded.display_name,
                    currency = excluded.currency,
                    country_of_risk = excluded.country_of_risk,
                    issuer_name = excluded.issuer_name,
                    exchange_code = excluded.exchange_code,
                    lot_size = excluded.lot_size,
                    tick_size = excluded.tick_size,
                    primary_identifier_kind = excluded.primary_identifier_kind,
                    primary_identifier_value = excluded.primary_identifier_value,
                    common_terms = excluded.common_terms,
                    asset_specific_terms = excluded.asset_specific_terms,
                    provenance = excluded.provenance,
                    version = excluded.version,
                    effective_from = excluded.effective_from,
                    effective_to = excluded.effective_to;
                """;

            command.Parameters.AddWithValue("security_id", record.SecurityId);
            command.Parameters.AddWithValue("asset_class", record.AssetClass);
            command.Parameters.AddWithValue("status", record.Status.ToString());
            command.Parameters.AddWithValue("display_name", record.DisplayName);
            command.Parameters.AddWithValue("currency", record.Currency);
            command.Parameters.AddWithValue("country_of_risk", (object?)GetOptionalString(record.CommonTerms, "countryOfRisk") ?? DBNull.Value);
            command.Parameters.AddWithValue("issuer_name", (object?)GetOptionalString(record.CommonTerms, "issuerName") ?? DBNull.Value);
            command.Parameters.AddWithValue("exchange_code", (object?)GetOptionalString(record.CommonTerms, "exchange") ?? DBNull.Value);
            command.Parameters.AddWithValue("lot_size", (object?)GetOptionalDecimal(record.CommonTerms, "lotSize") ?? DBNull.Value);
            command.Parameters.AddWithValue("tick_size", (object?)GetOptionalDecimal(record.CommonTerms, "tickSize") ?? DBNull.Value);
            command.Parameters.AddWithValue("primary_identifier_kind", record.PrimaryIdentifierKind);
            command.Parameters.AddWithValue("primary_identifier_value", record.PrimaryIdentifierValue);
            command.Parameters.AddWithValue("common_terms", record.CommonTerms.GetRawText());
            command.Parameters.AddWithValue("asset_specific_terms", record.AssetSpecificTerms.GetRawText());
            command.Parameters.AddWithValue("provenance", record.Provenance.GetRawText());
            command.Parameters.AddWithValue("version", record.Version);
            command.Parameters.AddWithValue("effective_from", record.EffectiveFrom.UtcDateTime);
            command.Parameters.AddWithValue("effective_to", (object?)record.EffectiveTo?.UtcDateTime ?? DBNull.Value);
            await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        }

        await ReplaceIdentifiersAsync(connection, transaction, record.SecurityId, record.Identifiers, ct).ConfigureAwait(false);
        await ReplaceAliasesAsync(connection, transaction, record.SecurityId, record.Aliases, ct).ConfigureAwait(false);
    }

    private async Task ReplaceIdentifiersAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid securityId,
        IReadOnlyList<SecurityIdentifierDto> identifiers,
        CancellationToken ct)
    {
        await using (var delete = connection.CreateCommand())
        {
            delete.Transaction = transaction;
            delete.CommandText = $"delete from {Qualified("security_identifiers")} where security_id = @security_id;";
            delete.Parameters.AddWithValue("security_id", securityId);
            await delete.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        }

        foreach (var identifier in identifiers)
        {
            await using var insert = connection.CreateCommand();
            insert.Transaction = transaction;
            insert.CommandText =
                $"""
                insert into {Qualified("security_identifiers")} (
                    security_id, identifier_kind, identifier_value, provider, is_primary,
                    valid_from, valid_to, source, confidence, manual_override)
                values (
                    @security_id, @identifier_kind, @identifier_value, @provider, @is_primary,
                    @valid_from, @valid_to, @source, @confidence, @manual_override);
                """;
            insert.Parameters.AddWithValue("security_id", securityId);
            insert.Parameters.AddWithValue("identifier_kind", identifier.Kind.ToString());
            insert.Parameters.AddWithValue("identifier_value", identifier.Value);
            insert.Parameters.AddWithValue("provider", (object?)identifier.Provider ?? DBNull.Value);
            insert.Parameters.AddWithValue("is_primary", identifier.IsPrimary);
            insert.Parameters.AddWithValue("valid_from", identifier.ValidFrom.UtcDateTime);
            insert.Parameters.AddWithValue("valid_to", (object?)identifier.ValidTo?.UtcDateTime ?? DBNull.Value);
            insert.Parameters.AddWithValue("source", "SecurityMaster");
            insert.Parameters.AddWithValue("confidence", DBNull.Value);
            insert.Parameters.AddWithValue("manual_override", false);
            await insert.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        }
    }

    private async Task ReplaceAliasesAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid securityId,
        IReadOnlyList<SecurityAliasDto> aliases,
        CancellationToken ct)
    {
        await using (var delete = connection.CreateCommand())
        {
            delete.Transaction = transaction;
            delete.CommandText = $"delete from {Qualified("security_aliases")} where security_id = @security_id;";
            delete.Parameters.AddWithValue("security_id", securityId);
            await delete.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        }

        foreach (var alias in aliases)
        {
            await using var insert = connection.CreateCommand();
            insert.Transaction = transaction;
            insert.CommandText =
                $"""
                insert into {Qualified("security_aliases")} (
                    alias_id, security_id, alias_kind, alias_value, provider, scope, reason,
                    created_by, created_at, valid_from, valid_to, is_enabled)
                values (
                    @alias_id, @security_id, @alias_kind, @alias_value, @provider, @scope, @reason,
                    @created_by, @created_at, @valid_from, @valid_to, @is_enabled);
                """;
            insert.Parameters.AddWithValue("alias_id", alias.AliasId);
            insert.Parameters.AddWithValue("security_id", securityId);
            insert.Parameters.AddWithValue("alias_kind", alias.AliasKind);
            insert.Parameters.AddWithValue("alias_value", alias.AliasValue);
            insert.Parameters.AddWithValue("provider", (object?)alias.Provider ?? DBNull.Value);
            insert.Parameters.AddWithValue("scope", alias.Scope.ToString());
            insert.Parameters.AddWithValue("reason", (object?)alias.Reason ?? DBNull.Value);
            insert.Parameters.AddWithValue("created_by", alias.CreatedBy);
            insert.Parameters.AddWithValue("created_at", alias.CreatedAt.UtcDateTime);
            insert.Parameters.AddWithValue("valid_from", alias.ValidFrom.UtcDateTime);
            insert.Parameters.AddWithValue("valid_to", (object?)alias.ValidTo?.UtcDateTime ?? DBNull.Value);
            insert.Parameters.AddWithValue("is_enabled", alias.IsEnabled);
            await insert.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        }
    }

    private async Task SaveCheckpointCoreAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction? transaction,
        string projectionName,
        long lastGlobalSequence,
        CancellationToken ct)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            $"""
            insert into {Qualified("projection_checkpoint")} (
                projection_name, last_global_sequence, updated_at)
            values (
                @projection_name, @last_global_sequence, @updated_at)
            on conflict (projection_name) do update set
                last_global_sequence = excluded.last_global_sequence,
                updated_at = excluded.updated_at;
            """;
        command.Parameters.AddWithValue("projection_name", projectionName);
        command.Parameters.AddWithValue("last_global_sequence", lastGlobalSequence);
        command.Parameters.AddWithValue("updated_at", DateTime.UtcNow);
        await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    private async Task<SecurityProjectionRecord?> GetProjectionCoreAsync(NpgsqlConnection connection, Guid securityId, CancellationToken ct)
    {
        await using var command = connection.CreateCommand();
        command.CommandText =
            $"""
            select security_id,
                   asset_class,
                   status,
                   display_name,
                   currency,
                   primary_identifier_kind,
                   primary_identifier_value,
                   common_terms::text,
                   asset_specific_terms::text,
                   provenance::text,
                   version,
                   effective_from,
                   effective_to
            from {Qualified("securities")}
            where security_id = @security_id;
            """;
        command.Parameters.AddWithValue("security_id", securityId);

        await using var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false);
        if (!await reader.ReadAsync(ct).ConfigureAwait(false))
        {
            return null;
        }

        var projection = new SecurityProjectionRecord(
            reader.GetGuid(0),
            reader.GetString(1),
            Enum.Parse<SecurityStatusDto>(reader.GetString(2), ignoreCase: true),
            reader.GetString(3),
            reader.GetString(4),
            reader.GetString(5),
            reader.GetString(6),
            JsonDocument.Parse(reader.GetString(7)).RootElement.Clone(),
            JsonDocument.Parse(reader.GetString(8)).RootElement.Clone(),
            JsonDocument.Parse(reader.GetString(9)).RootElement.Clone(),
            reader.GetInt64(10),
            new DateTimeOffset(reader.GetDateTime(11), TimeSpan.Zero),
            reader.IsDBNull(12) ? null : new DateTimeOffset(reader.GetDateTime(12), TimeSpan.Zero),
            Array.Empty<SecurityIdentifierDto>(),
            Array.Empty<SecurityAliasDto>());

        await reader.CloseAsync().ConfigureAwait(false);

        var identifiers = await LoadIdentifiersAsync(connection, securityId, ct).ConfigureAwait(false);
        var aliases = await LoadAliasesAsync(connection, securityId, ct).ConfigureAwait(false);
        return projection with { Identifiers = identifiers, Aliases = aliases };
    }

    private async Task<IReadOnlyList<SecurityIdentifierDto>> LoadIdentifiersAsync(NpgsqlConnection connection, Guid securityId, CancellationToken ct)
    {
        await using var command = connection.CreateCommand();
        command.CommandText =
            $"""
            select identifier_kind, identifier_value, is_primary, valid_from, valid_to, provider
            from {Qualified("security_identifiers")}
            where security_id = @security_id
            order by is_primary desc, identifier_kind, identifier_value;
            """;
        command.Parameters.AddWithValue("security_id", securityId);

        var results = new List<SecurityIdentifierDto>();
        await using var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await reader.ReadAsync(ct).ConfigureAwait(false))
        {
            results.Add(new SecurityIdentifierDto(
                Enum.Parse<SecurityIdentifierKind>(reader.GetString(0), ignoreCase: true),
                reader.GetString(1),
                reader.GetBoolean(2),
                new DateTimeOffset(reader.GetDateTime(3), TimeSpan.Zero),
                reader.IsDBNull(4) ? null : new DateTimeOffset(reader.GetDateTime(4), TimeSpan.Zero),
                reader.IsDBNull(5) ? null : reader.GetString(5)));
        }

        return results;
    }

    private async Task<IReadOnlyList<SecurityAliasDto>> LoadAliasesAsync(NpgsqlConnection connection, Guid securityId, CancellationToken ct)
    {
        await using var command = connection.CreateCommand();
        command.CommandText =
            $"""
            select alias_id, alias_kind, alias_value, provider, scope, reason,
                   created_by, created_at, valid_from, valid_to, is_enabled
            from {Qualified("security_aliases")}
            where security_id = @security_id
            order by alias_kind, alias_value;
            """;
        command.Parameters.AddWithValue("security_id", securityId);

        var results = new List<SecurityAliasDto>();
        await using var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await reader.ReadAsync(ct).ConfigureAwait(false))
        {
            results.Add(new SecurityAliasDto(
                reader.GetGuid(0),
                securityId,
                reader.GetString(1),
                reader.GetString(2),
                reader.IsDBNull(3) ? null : reader.GetString(3),
                Enum.Parse<SecurityAliasScope>(reader.GetString(4), ignoreCase: true),
                reader.IsDBNull(5) ? null : reader.GetString(5),
                reader.GetString(6),
                new DateTimeOffset(reader.GetDateTime(7), TimeSpan.Zero),
                new DateTimeOffset(reader.GetDateTime(8), TimeSpan.Zero),
                reader.IsDBNull(9) ? null : new DateTimeOffset(reader.GetDateTime(9), TimeSpan.Zero),
                reader.GetBoolean(10)));
        }

        return results;
    }

    private async Task<Guid?> ResolveSecurityIdAsync(
        NpgsqlConnection connection,
        SecurityIdentifierKind kind,
        string value,
        string? provider,
        DateTimeOffset asOfUtc,
        bool includeInactive,
        CancellationToken ct)
    {
        var identifierKind = kind.ToString();

        foreach (var sql in new[]
        {
            $"""
            select s.security_id
            from {Qualified("security_identifiers")} i
            join {Qualified("securities")} s on s.security_id = i.security_id
            where i.identifier_kind = @identifier_kind
              and i.identifier_value = @identifier_value
              and ((@provider is null and i.provider is null) or i.provider = @provider)
              and i.valid_from <= @as_of
              and (i.valid_to is null or i.valid_to > @as_of)
              and (@include_inactive = true or s.status = 'Active')
            order by i.is_primary desc
            limit 1;
            """,
            $"""
            select s.security_id
            from {Qualified("security_aliases")} a
            join {Qualified("securities")} s on s.security_id = a.security_id
            where a.alias_kind = @identifier_kind
              and a.alias_value = @identifier_value
              and ((@provider is null and a.provider is null) or a.provider = @provider)
              and a.valid_from <= @as_of
              and (a.valid_to is null or a.valid_to > @as_of)
              and a.is_enabled = true
              and (@include_inactive = true or s.status = 'Active')
            limit 1;
            """,
            $"""
            select security_id
            from {Qualified("securities")}
            where primary_identifier_kind = @identifier_kind
              and primary_identifier_value = @identifier_value
              and (@include_inactive = true or status = 'Active')
            limit 1;
            """
        })
        {
            await using var command = connection.CreateCommand();
            command.CommandText = sql;
            command.Parameters.AddWithValue("identifier_kind", identifierKind);
            command.Parameters.AddWithValue("identifier_value", value);
            // Use an explicitly typed parameter so PostgreSQL can resolve the type of the
            // positional placeholder ($3) when provider is null. AddWithValue with DBNull.Value
            // produces an untyped null, which causes error 42P08 in a SELECT WHERE context.
            command.Parameters.Add(new NpgsqlParameter("provider", NpgsqlDbType.Text) { Value = (object?)provider ?? DBNull.Value });
            command.Parameters.AddWithValue("as_of", asOfUtc.UtcDateTime);
            command.Parameters.AddWithValue("include_inactive", includeInactive);

            var result = await command.ExecuteScalarAsync(ct).ConfigureAwait(false);
            if (result is Guid guid)
            {
                return guid;
            }
        }

        return null;
    }

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

    private static string? GetOptionalString(JsonElement json, string propertyName)
        => json.TryGetProperty(propertyName, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static decimal? GetOptionalDecimal(JsonElement json, string propertyName)
        => json.TryGetProperty(propertyName, out var value) && value.TryGetDecimal(out var decimalValue)
            ? decimalValue
            : null;
}
