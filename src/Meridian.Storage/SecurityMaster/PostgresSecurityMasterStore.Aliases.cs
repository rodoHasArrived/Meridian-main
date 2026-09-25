using Meridian.Contracts.SecurityMaster;
using Npgsql;

namespace Meridian.Storage.SecurityMaster;

/// <summary>
/// Alias write paths. Alias rows are immutable recording facts: changing an existing row would
/// rewrite every recorded-as-of projection that reads that row. Corrections therefore need a new,
/// append-only alias revision rather than an in-place upsert.
/// </summary>
public sealed partial class PostgresSecurityMasterStore
{
    public async Task<SecurityAliasDto?> UpsertAliasAsync(SecurityAliasDto alias, CancellationToken ct = default)
    {
        await using var connection = await OpenConnectionAsync(ct).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        // An alias is part of recorded-as-of state, but the current schema has no alias revision
        // table. Permit an idempotent replay of the same alias ID and reject every material change;
        // otherwise a correction made today would silently alter what an older as-of view reports.
        // The no-op update is used only to return the existing creation facts atomically.
        command.CommandText =
            $"""
            insert into {Qualified("security_aliases")} (
                alias_id, security_id, alias_kind, alias_value, normalized_alias_value, provider, normalized_provider, scope, reason,
                created_by, created_at, valid_from, valid_to, is_enabled)
            values (
                @alias_id, @security_id, @alias_kind, @alias_value, @normalized_alias_value, @provider, @normalized_provider, @scope, @reason,
                @created_by, @created_at, @valid_from, @valid_to, @is_enabled)
            on conflict (alias_id) do update set
                alias_id = excluded.alias_id
            where security_aliases.security_id = excluded.security_id
                and security_aliases.alias_kind = excluded.alias_kind
                and security_aliases.alias_value = excluded.alias_value
                and security_aliases.normalized_alias_value = excluded.normalized_alias_value
                and security_aliases.provider is not distinct from excluded.provider
                and security_aliases.normalized_provider is not distinct from excluded.normalized_provider
                and security_aliases.scope = excluded.scope
                and security_aliases.reason is not distinct from excluded.reason
                and security_aliases.valid_from = excluded.valid_from
                and security_aliases.valid_to is not distinct from excluded.valid_to
                and security_aliases.is_enabled = excluded.is_enabled
            returning created_by, created_at;
            """;

        command.Parameters.AddWithValue("alias_id", alias.AliasId);
        command.Parameters.AddWithValue("security_id", alias.SecurityId);
        command.Parameters.AddWithValue("alias_kind", alias.AliasKind);
        command.Parameters.AddWithValue("alias_value", alias.AliasValue);
        command.Parameters.AddWithValue("normalized_alias_value", SecurityIdentifierNormalizer.NormalizeAliasValue(alias.AliasKind, alias.AliasValue));
        command.Parameters.AddWithValue("provider", (object?)alias.Provider ?? DBNull.Value);
        command.Parameters.AddWithValue("normalized_provider", ToDbNullable(SecurityIdentifierNormalizer.NormalizeProvider(alias.Provider)));
        command.Parameters.AddWithValue("scope", alias.Scope.ToString());
        command.Parameters.AddWithValue("reason", (object?)alias.Reason ?? DBNull.Value);
        command.Parameters.AddWithValue("created_by", alias.CreatedBy);
        command.Parameters.AddWithValue("created_at", alias.CreatedAt.UtcDateTime);
        command.Parameters.AddWithValue("valid_from", alias.ValidFrom.UtcDateTime);
        command.Parameters.AddWithValue("valid_to", (object?)alias.ValidTo?.UtcDateTime ?? DBNull.Value);
        command.Parameters.AddWithValue("is_enabled", alias.IsEnabled);

        await using var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false);
        if (!await reader.ReadAsync(ct).ConfigureAwait(false))
        {
            throw new SecurityAliasHistoryConflictException(alias.AliasId);
        }

        // Echo the original creation facts when the write was an idempotent replay.
        return alias with
        {
            CreatedBy = reader.GetString(0),
            CreatedAt = new DateTimeOffset(reader.GetDateTime(1), TimeSpan.Zero)
        };
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
                    alias_id, security_id, alias_kind, alias_value, normalized_alias_value,
                    provider, normalized_provider, scope, reason,
                    created_by, created_at, valid_from, valid_to, is_enabled)
                values (
                    @alias_id, @security_id, @alias_kind, @alias_value, @normalized_alias_value,
                    @provider, @normalized_provider, @scope, @reason,
                    @created_by, @created_at, @valid_from, @valid_to, @is_enabled);
                """;
            insert.Parameters.AddWithValue("alias_id", alias.AliasId);
            insert.Parameters.AddWithValue("security_id", securityId);
            insert.Parameters.AddWithValue("alias_kind", alias.AliasKind);
            insert.Parameters.AddWithValue("alias_value", alias.AliasValue);
            insert.Parameters.AddWithValue("normalized_alias_value", SecurityIdentifierNormalizer.NormalizeAliasValue(alias.AliasKind, alias.AliasValue));
            insert.Parameters.AddWithValue("provider", (object?)alias.Provider ?? DBNull.Value);
            insert.Parameters.AddWithValue("normalized_provider", ToDbNullable(SecurityIdentifierNormalizer.NormalizeProvider(alias.Provider)));
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
}
