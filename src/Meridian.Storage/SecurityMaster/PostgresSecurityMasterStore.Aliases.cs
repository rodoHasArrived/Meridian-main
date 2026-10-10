using Meridian.Contracts.SecurityMaster;
using Npgsql;
using NpgsqlTypes;

namespace Meridian.Storage.SecurityMaster;

/// <summary>
/// Alias write and recorded-history paths. <c>security_aliases</c> holds the current alias row used by
/// identifier resolution and posting authority. Every recorded change to an alias first appends a row
/// to the append-only <c>security_alias_revisions</c> table, so a correction updates the current row
/// without rewriting what an older recorded-as-of view reports.
/// </summary>
public sealed partial class PostgresSecurityMasterStore
{
    /// <summary>Actor recorded on revisions appended by projection replacement, which carries no caller.</summary>
    internal const string ProjectionReplacementAliasActor = "security-master.projection-replacement";

    // Material alias columns compared between the current row and its latest revision. Creation facts
    // (created_by, created_at) are deliberately excluded: they never change after the first revision.
    private const string AliasMaterialColumns =
        "alias_kind, alias_value, normalized_alias_value, provider, normalized_provider, scope, reason, valid_from, valid_to, is_enabled";

    public async Task<SecurityAliasDto?> UpsertAliasAsync(SecurityAliasDto alias, CancellationToken ct = default)
    {
        await using var connection = await OpenConnectionAsync(ct).ConfigureAwait(false);
        await using var transaction = await connection.BeginTransactionAsync(ct).ConfigureAwait(false);
        await using (var authority = connection.CreateCommand())
        {
            authority.Transaction = transaction;
            // Match projection replacement's parent-before-child write order. Touching the tuple
            // (without changing its event-stream version) also makes a Serializable posting whose
            // snapshot predates this alias commit fail on its parent lock instead of missing a
            // newly inserted or corrected alias. A row lock alone does not invalidate that older
            // snapshot. The same row lock serializes concurrent alias writers for this security, so
            // revision numbers below are assigned without a race.
            authority.CommandText = $"update {Qualified("securities")} set security_id = security_id where security_id = @security_id;";
            authority.Parameters.AddWithValue("security_id", alias.SecurityId);
            await authority.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        }

        var existing = await ReadAliasForUpdateAsync(connection, transaction, alias, ct).ConfigureAwait(false);
        SecurityAliasDto persisted;
        if (existing is null)
        {
            await EnsureAliasHistoryBelongsToSecurityAsync(connection, transaction, alias.SecurityId, [alias.AliasId], ct).ConfigureAwait(false);
            persisted = await InsertCurrentAliasAsync(connection, transaction, alias, ct).ConfigureAwait(false);
            await AppendAliasRevisionAsync(connection, transaction, alias, alias.CreatedBy, ct).ConfigureAwait(false);
        }
        else
        {
            // An alias ID stays bound to the security it was first recorded against. Re-pointing it
            // would move recorded history between securities without locking the previous parent.
            if (existing.SecurityId != alias.SecurityId)
            {
                throw new SecurityAliasHistoryConflictException(alias.AliasId);
            }

            // Creation facts are immutable: echo the originals whether this is a replay or a correction.
            persisted = alias with { CreatedBy = existing.CreatedBy, CreatedAt = existing.CreatedAt };
            if (!existing.Unchanged)
            {
                // A material correction appends the next revision (recorded by the correcting actor)
                // before replacing the current row, so older recorded-as-of views keep the prior values.
                // An identical replay appends nothing.
                await AppendAliasRevisionAsync(connection, transaction, alias, alias.CreatedBy, ct).ConfigureAwait(false);
                await UpdateCurrentAliasAsync(connection, transaction, alias, ct).ConfigureAwait(false);
            }
        }

        await transaction.CommitAsync(ct).ConfigureAwait(false);
        return persisted;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<SecurityAliasDto>> GetAliasesRecordedAsOfAsync(
        Guid securityId,
        DateTimeOffset recordedAsOfUtc,
        CancellationToken ct = default)
    {
        await using var connection = await OpenConnectionAsync(ct).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText =
            $"""
            select alias_id, alias_kind, alias_value, provider, scope, reason,
                   created_by, created_at, valid_from, valid_to, is_enabled
            from (
                select distinct on (r.alias_id)
                       r.alias_id, r.alias_kind, r.alias_value, r.provider, r.scope, r.reason,
                       f.recorded_by as created_by, f.recorded_at as created_at,
                       r.valid_from, r.valid_to, r.is_enabled, r.is_retired
                from {Qualified("security_alias_revisions")} r
                join {Qualified("security_alias_revisions")} f
                  on f.alias_id = r.alias_id and f.revision = 1
                where r.security_id = @security_id
                  and r.recorded_at <= @recorded_as_of
                order by r.alias_id, r.revision desc
            ) latest
            where not latest.is_retired
            order by alias_kind, alias_value, alias_id;
            """;
        command.Parameters.AddWithValue("security_id", securityId);
        command.Parameters.AddWithValue("recorded_as_of", recordedAsOfUtc.UtcDateTime);

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
                SecurityMasterEnumReads.ParseOrFallback(reader.GetString(4), SecurityAliasScope.Unknown),
                reader.IsDBNull(5) ? null : reader.GetString(5),
                reader.GetString(6),
                new DateTimeOffset(reader.GetDateTime(7), TimeSpan.Zero),
                new DateTimeOffset(reader.GetDateTime(8), TimeSpan.Zero),
                reader.IsDBNull(9) ? null : new DateTimeOffset(reader.GetDateTime(9), TimeSpan.Zero),
                reader.GetBoolean(10)));
        }

        return results;
    }

    private sealed record ExistingAlias(Guid SecurityId, string CreatedBy, DateTimeOffset CreatedAt, bool Unchanged);

    private async Task<ExistingAlias?> ReadAliasForUpdateAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        SecurityAliasDto alias,
        CancellationToken ct)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            $"""
            select security_id, created_by, created_at,
                   ({AliasMaterialColumns}) is not distinct from (
                       @alias_kind, @alias_value, @normalized_alias_value, @provider, @normalized_provider,
                       @scope, @reason, @valid_from, @valid_to, @is_enabled) as unchanged
            from {Qualified("security_aliases")}
            where alias_id = @alias_id
            for update;
            """;
        AddAliasParameters(command, alias);

        await using var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false);
        if (!await reader.ReadAsync(ct).ConfigureAwait(false))
        {
            return null;
        }

        return new ExistingAlias(
            reader.GetGuid(0),
            reader.GetString(1),
            new DateTimeOffset(reader.GetDateTime(2), TimeSpan.Zero),
            reader.GetBoolean(3));
    }

    private async Task<SecurityAliasDto> InsertCurrentAliasAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        SecurityAliasDto alias,
        CancellationToken ct)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            $"""
            insert into {Qualified("security_aliases")} (
                alias_id, security_id, {AliasMaterialColumns}, created_by, created_at)
            values (
                @alias_id, @security_id, @alias_kind, @alias_value, @normalized_alias_value, @provider,
                @normalized_provider, @scope, @reason, @valid_from, @valid_to, @is_enabled,
                @created_by, @created_at)
            on conflict (alias_id) do nothing
            returning created_by, created_at;
            """;
        AddAliasParameters(command, alias);
        AddCreationParameters(command, alias);

        await using var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false);
        if (!await reader.ReadAsync(ct).ConfigureAwait(false))
        {
            // Writers for the same security are serialized by the parent lock, so a row that appeared
            // after the read above was recorded concurrently against a different security.
            throw new SecurityAliasHistoryConflictException(alias.AliasId);
        }

        return alias with
        {
            CreatedBy = reader.GetString(0),
            CreatedAt = new DateTimeOffset(reader.GetDateTime(1), TimeSpan.Zero)
        };
    }

    private async Task UpdateCurrentAliasAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        SecurityAliasDto alias,
        CancellationToken ct)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            $"""
            update {Qualified("security_aliases")}
            set ({AliasMaterialColumns}) = (
                @alias_kind, @alias_value, @normalized_alias_value, @provider, @normalized_provider,
                @scope, @reason, @valid_from, @valid_to, @is_enabled)
            where alias_id = @alias_id;
            """;
        AddAliasParameters(command, alias);
        await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Appends the next revision for <paramref name="alias"/>. The first revision is recorded at the
    /// alias creation time, matching the backfill in migration 036; later revisions are recorded at the
    /// database transaction time, kept strictly after the previous revision so revision order and
    /// recorded order cannot disagree.
    /// </summary>
    private async Task AppendAliasRevisionAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        SecurityAliasDto alias,
        string recordedBy,
        CancellationToken ct)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            $"""
            insert into {Qualified("security_alias_revisions")} (
                alias_id, revision, security_id, {AliasMaterialColumns}, is_retired, recorded_by, recorded_at)
            select @alias_id,
                   coalesce(max(revision), 0) + 1,
                   @security_id, @alias_kind, @alias_value, @normalized_alias_value, @provider,
                   @normalized_provider, @scope, @reason, @valid_from, @valid_to, @is_enabled,
                   false,
                   @recorded_by,
                   case when max(revision) is null then @created_at
                        else greatest(now(), max(recorded_at) + interval '1 microsecond') end
            from {Qualified("security_alias_revisions")}
            where alias_id = @alias_id;
            """;
        AddAliasParameters(command, alias);
        AddCreationParameters(command, alias);
        command.Parameters.Add(new NpgsqlParameter("recorded_by", NpgsqlDbType.Text) { Value = recordedBy });
        await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    private static void AddAliasParameters(NpgsqlCommand command, SecurityAliasDto alias)
    {
        command.Parameters.Add(new NpgsqlParameter("alias_id", NpgsqlDbType.Uuid) { Value = alias.AliasId });
        command.Parameters.Add(new NpgsqlParameter("security_id", NpgsqlDbType.Uuid) { Value = alias.SecurityId });
        command.Parameters.Add(new NpgsqlParameter("alias_kind", NpgsqlDbType.Text) { Value = alias.AliasKind });
        command.Parameters.Add(new NpgsqlParameter("alias_value", NpgsqlDbType.Text) { Value = alias.AliasValue });
        command.Parameters.Add(new NpgsqlParameter("normalized_alias_value", NpgsqlDbType.Text)
        {
            Value = SecurityIdentifierNormalizer.NormalizeAliasValue(alias.AliasKind, alias.AliasValue)
        });
        command.Parameters.Add(new NpgsqlParameter("provider", NpgsqlDbType.Text) { Value = (object?)alias.Provider ?? DBNull.Value });
        command.Parameters.Add(new NpgsqlParameter("normalized_provider", NpgsqlDbType.Text)
        {
            Value = ToDbNullable(SecurityIdentifierNormalizer.NormalizeProvider(alias.Provider))
        });
        command.Parameters.Add(new NpgsqlParameter("scope", NpgsqlDbType.Text) { Value = alias.Scope.ToString() });
        command.Parameters.Add(new NpgsqlParameter("reason", NpgsqlDbType.Text) { Value = (object?)alias.Reason ?? DBNull.Value });
        command.Parameters.Add(new NpgsqlParameter("valid_from", NpgsqlDbType.TimestampTz) { Value = alias.ValidFrom.UtcDateTime });
        command.Parameters.Add(new NpgsqlParameter("valid_to", NpgsqlDbType.TimestampTz)
        {
            Value = (object?)alias.ValidTo?.UtcDateTime ?? DBNull.Value
        });
        command.Parameters.Add(new NpgsqlParameter("is_enabled", NpgsqlDbType.Boolean) { Value = alias.IsEnabled });
    }

    private static void AddCreationParameters(NpgsqlCommand command, SecurityAliasDto alias)
    {
        command.Parameters.Add(new NpgsqlParameter("created_by", NpgsqlDbType.Text) { Value = alias.CreatedBy });
        command.Parameters.Add(new NpgsqlParameter("created_at", NpgsqlDbType.TimestampTz) { Value = alias.CreatedAt.UtcDateTime });
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
                    alias_id, security_id, {AliasMaterialColumns}, created_by, created_at)
                values (
                    @alias_id, @security_id, @alias_kind, @alias_value, @normalized_alias_value, @provider,
                    @normalized_provider, @scope, @reason, @valid_from, @valid_to, @is_enabled,
                    @created_by, @created_at);
                """;
            AddAliasParameters(insert, alias with { SecurityId = securityId });
            AddCreationParameters(insert, alias);
            await insert.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        }

        await EnsureAliasHistoryBelongsToSecurityAsync(connection, transaction, securityId, aliasIds: null, ct).ConfigureAwait(false);
        await SyncAliasRevisionsAsync(connection, transaction, securityId, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// An alias ID stays bound to the security it was first recorded against, including after it is
    /// retired: its current row may be gone, but its revision history still belongs to that
    /// security. Refuses a write that would continue another security's history under this one.
    /// <paramref name="aliasIds"/> names the IDs to check; <see langword="null"/> checks every
    /// current alias row of <paramref name="securityId"/> (the projection-replacement path).
    /// </summary>
    private async Task EnsureAliasHistoryBelongsToSecurityAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid securityId,
        IReadOnlyList<Guid>? aliasIds,
        CancellationToken ct)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = aliasIds is null
            ? $"""
              select r.alias_id
              from {Qualified("security_alias_revisions")} r
              join {Qualified("security_aliases")} a on a.alias_id = r.alias_id
              where a.security_id = @security_id and r.security_id <> @security_id
              limit 1;
              """
            : $"""
              select r.alias_id
              from {Qualified("security_alias_revisions")} r
              where r.alias_id = any(@alias_ids) and r.security_id <> @security_id
              limit 1;
              """;
        command.Parameters.AddWithValue("security_id", securityId);
        if (aliasIds is not null)
        {
            command.Parameters.AddWithValue("alias_ids", aliasIds.ToArray());
        }

        if (await command.ExecuteScalarAsync(ct).ConfigureAwait(false) is Guid conflicting)
        {
            throw new SecurityAliasHistoryConflictException(conflicting);
        }
    }

    /// <summary>
    /// Keeps the revision history in step with a projection replacement: aliases the replacement
    /// dropped are retired, and new or materially changed aliases receive a revision. Aliases whose
    /// current row still matches their latest revision append nothing, so replaying an unchanged
    /// projection leaves the history untouched.
    /// </summary>
    private async Task SyncAliasRevisionsAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid securityId,
        CancellationToken ct)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            $"""
            insert into {Qualified("security_alias_revisions")} (
                alias_id, revision, security_id, {AliasMaterialColumns}, is_retired, recorded_by, recorded_at)
            select l.alias_id, l.revision + 1, l.security_id,
                   l.alias_kind, l.alias_value, l.normalized_alias_value, l.provider, l.normalized_provider,
                   l.scope, l.reason, l.valid_from, l.valid_to, l.is_enabled,
                   true, @actor, greatest(now(), l.recorded_at + interval '1 microsecond')
            from (
                select distinct on (alias_id) *
                from {Qualified("security_alias_revisions")}
                where security_id = @security_id
                order by alias_id, revision desc
            ) l
            where not l.is_retired
              and not exists (
                  select 1 from {Qualified("security_aliases")} a
                  where a.alias_id = l.alias_id and a.security_id = @security_id);

            insert into {Qualified("security_alias_revisions")} (
                alias_id, revision, security_id, {AliasMaterialColumns}, is_retired, recorded_by, recorded_at)
            select a.alias_id, coalesce(l.revision, 0) + 1, a.security_id,
                   a.alias_kind, a.alias_value, a.normalized_alias_value, a.provider, a.normalized_provider,
                   a.scope, a.reason, a.valid_from, a.valid_to, a.is_enabled,
                   false,
                   case when l.alias_id is null then a.created_by else @actor end,
                   case when l.alias_id is null then a.created_at
                        else greatest(now(), l.recorded_at + interval '1 microsecond') end
            from {Qualified("security_aliases")} a
            left join lateral (
                select r.*
                from {Qualified("security_alias_revisions")} r
                where r.alias_id = a.alias_id
                order by r.revision desc
                limit 1
            ) l on true
            where a.security_id = @security_id
              and (l.alias_id is null
                   or l.is_retired
                   or (a.alias_kind, a.alias_value, a.normalized_alias_value, a.provider, a.normalized_provider,
                       a.scope, a.reason, a.valid_from, a.valid_to, a.is_enabled)
                      is distinct from
                      (l.alias_kind, l.alias_value, l.normalized_alias_value, l.provider, l.normalized_provider,
                       l.scope, l.reason, l.valid_from, l.valid_to, l.is_enabled));
            """;
        command.Parameters.AddWithValue("security_id", securityId);
        command.Parameters.Add(new NpgsqlParameter("actor", NpgsqlDbType.Text) { Value = ProjectionReplacementAliasActor });
        await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }
}
