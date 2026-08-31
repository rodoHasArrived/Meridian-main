using Meridian.Contracts.SecurityMaster;
using Npgsql;

namespace Meridian.Storage.SecurityMaster;

/// <summary>
/// Alias write paths. Split out of the main store file so the alias rules -- above all that
/// created_at/created_by are immutable recording facts -- sit together rather than being read
/// across a two-thousand-line class.
/// </summary>
public sealed partial class PostgresSecurityMasterStore
{
    public async Task<SecurityAliasDto?> UpsertAliasAsync(SecurityAliasDto alias, CancellationToken ct = default)
    {
        await using var connection = await OpenConnectionAsync(ct).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        // created_by/created_at are deliberately absent from the conflict update: they record WHEN and
        // by WHOM the alias was first recorded, and correcting an alias must not restate that. As-of
        // rebuilds retain aliases with created_at <= the cutoff, so advancing it on every edit would
        // retroactively remove a corrected identifier from every view older than the correction.
        // valid_from/valid_to remain mutable — those carry the alias's effective (business) time,
        // which a correction legitimately restates.
        command.CommandText =
            $"""
            insert into {Qualified("security_aliases")} (
                alias_id, security_id, alias_kind, alias_value, normalized_alias_value, provider, normalized_provider, scope, reason,
                created_by, created_at, valid_from, valid_to, is_enabled)
            values (
                @alias_id, @security_id, @alias_kind, @alias_value, @normalized_alias_value, @provider, @normalized_provider, @scope, @reason,
                @created_by, @created_at, @valid_from, @valid_to, @is_enabled)
            on conflict (alias_id) do update set
                security_id = excluded.security_id,
                alias_kind = excluded.alias_kind,
                alias_value = excluded.alias_value,
                normalized_alias_value = excluded.normalized_alias_value,
                provider = excluded.provider,
                normalized_provider = excluded.normalized_provider,
                scope = excluded.scope,
                reason = excluded.reason,
                valid_from = excluded.valid_from,
                valid_to = excluded.valid_to,
                is_enabled = excluded.is_enabled
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
            return null;
        }

        // Echo the stored creation facts, which on an update are the ORIGINAL ones rather than the
        // values just supplied.
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
