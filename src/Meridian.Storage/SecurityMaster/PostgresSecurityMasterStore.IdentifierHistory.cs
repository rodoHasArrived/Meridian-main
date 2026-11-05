using Meridian.Contracts.SecurityMaster;
using Npgsql;
using NpgsqlTypes;

namespace Meridian.Storage.SecurityMaster;

/// <summary>
/// Identifier replacement and its recorded history. <c>security_identifiers</c> is the current-state
/// projection; every replacement appends Added/Amended/Expired/Removed revisions for the rows it
/// changes to the append-only <c>security_identifier_revisions</c> table (migration 062) in the same
/// transaction, so the projection and its history commit or roll back together.
/// </summary>
public sealed partial class PostgresSecurityMasterStore : ISecurityIdentifierHistoryStore
{
    /// <summary>
    /// Actor recorded on identifier revisions appended by projection replacement. Projection writes
    /// carry no caller; the authenticated actor of an event-sourced change is on its event.
    /// </summary>
    internal const string ProjectionReplacementIdentifierActor = "security-master.projection-replacement";

    /// <summary>Row-level metadata written for an identifier the projection did not hold before.</summary>
    private const string DefaultIdentifierSource = "SecurityMaster";

    // The identifier row's columns that a revision copies; the identity is
    // (security_id, identifier_kind, identifier_value, valid_from), the table's primary key.
    private const string IdentifierRevisionColumns =
        "normalized_identifier_value, provider, normalized_provider, is_primary, valid_to, source, confidence, manual_override";

    /// <summary>
    /// Replaces the current identifier rows of <paramref name="securityId"/> with
    /// <paramref name="identifiers"/>. A row whose identity (kind, value, valid-from) was already
    /// held keeps its stored <c>source</c>, <c>confidence</c> and <c>manual_override</c>: the projection
    /// record does not carry them, so writing defaults would erase them on every write. New rows get
    /// the defaults. The caller holds the parent <c>securities</c> lock, which serializes replacements
    /// of one security and so the revision numbers appended here.
    /// </summary>
    private async Task ReplaceIdentifiersAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid securityId,
        IReadOnlyList<SecurityIdentifierDto> identifiers,
        CancellationToken ct)
    {
        var priorKinds = new List<string>();
        var priorValues = new List<string>();
        var priorValidFrom = new List<DateTime>();
        var priorSources = new List<string>();
        var priorConfidence = new List<decimal?>();
        var priorManualOverride = new List<bool>();
        await using (var delete = connection.CreateCommand())
        {
            delete.Transaction = transaction;
            delete.CommandText =
                $"""
                delete from {Qualified("security_identifiers")} where security_id = @security_id
                returning identifier_kind, identifier_value, valid_from, source, confidence, manual_override;
                """;
            delete.Parameters.AddWithValue("security_id", securityId);
            await using var reader = await delete.ExecuteReaderAsync(ct).ConfigureAwait(false);
            while (await reader.ReadAsync(ct).ConfigureAwait(false))
            {
                priorKinds.Add(reader.GetString(0));
                priorValues.Add(reader.GetString(1));
                priorValidFrom.Add(reader.GetDateTime(2));
                priorSources.Add(reader.GetString(3));
                priorConfidence.Add(reader.IsDBNull(4) ? null : reader.GetDecimal(4));
                priorManualOverride.Add(reader.GetBoolean(5));
            }
        }

        foreach (var identifier in identifiers)
        {
            await using var insert = connection.CreateCommand();
            insert.Transaction = transaction;
            // The prior row is matched in SQL, so valid_from compares at the stored microsecond
            // precision exactly as the primary key does. Duplicate identities in one record still
            // fail on the primary key, as before.
            insert.CommandText =
                $"""
                insert into {Qualified("security_identifiers")} (
                    security_id, identifier_kind, identifier_value, normalized_identifier_value,
                    provider, normalized_provider, is_primary,
                    valid_from, valid_to, source, confidence, manual_override)
                select
                    @security_id, @identifier_kind, @identifier_value, @normalized_identifier_value,
                    @provider, @normalized_provider, @is_primary,
                    @valid_from, @valid_to,
                    coalesce(prior.source, @default_source), prior.confidence, coalesce(prior.manual_override, false)
                from (select 1) as seed
                left join unnest(@prior_kinds, @prior_values, @prior_valid_from, @prior_sources, @prior_confidence, @prior_manual_override)
                    as prior(identifier_kind, identifier_value, valid_from, source, confidence, manual_override)
                    on prior.identifier_kind = @identifier_kind
                   and prior.identifier_value = @identifier_value
                   and prior.valid_from = @valid_from;
                """;
            insert.Parameters.Add(new NpgsqlParameter("security_id", NpgsqlDbType.Uuid) { Value = securityId });
            insert.Parameters.Add(new NpgsqlParameter("identifier_kind", NpgsqlDbType.Text) { Value = identifier.Kind.ToString() });
            insert.Parameters.Add(new NpgsqlParameter("identifier_value", NpgsqlDbType.Text) { Value = identifier.Value });
            insert.Parameters.Add(new NpgsqlParameter("normalized_identifier_value", NpgsqlDbType.Text)
            {
                Value = SecurityIdentifierNormalizer.GetOrComputeNormalizedValue(identifier)
            });
            insert.Parameters.Add(new NpgsqlParameter("provider", NpgsqlDbType.Text) { Value = (object?)identifier.Provider ?? DBNull.Value });
            insert.Parameters.Add(new NpgsqlParameter("normalized_provider", NpgsqlDbType.Text)
            {
                Value = ToDbNullable(SecurityIdentifierNormalizer.GetOrComputeNormalizedProvider(identifier))
            });
            insert.Parameters.Add(new NpgsqlParameter("is_primary", NpgsqlDbType.Boolean) { Value = identifier.IsPrimary });
            insert.Parameters.Add(new NpgsqlParameter("valid_from", NpgsqlDbType.TimestampTz) { Value = identifier.ValidFrom.UtcDateTime });
            insert.Parameters.Add(new NpgsqlParameter("valid_to", NpgsqlDbType.TimestampTz)
            {
                Value = (object?)identifier.ValidTo?.UtcDateTime ?? DBNull.Value
            });
            insert.Parameters.Add(new NpgsqlParameter("default_source", NpgsqlDbType.Text) { Value = DefaultIdentifierSource });
            insert.Parameters.Add(new NpgsqlParameter("prior_kinds", NpgsqlDbType.Array | NpgsqlDbType.Text) { Value = priorKinds.ToArray() });
            insert.Parameters.Add(new NpgsqlParameter("prior_values", NpgsqlDbType.Array | NpgsqlDbType.Text) { Value = priorValues.ToArray() });
            insert.Parameters.Add(new NpgsqlParameter("prior_valid_from", NpgsqlDbType.Array | NpgsqlDbType.TimestampTz) { Value = priorValidFrom.ToArray() });
            insert.Parameters.Add(new NpgsqlParameter("prior_sources", NpgsqlDbType.Array | NpgsqlDbType.Text) { Value = priorSources.ToArray() });
            insert.Parameters.Add(new NpgsqlParameter("prior_confidence", NpgsqlDbType.Array | NpgsqlDbType.Numeric) { Value = priorConfidence.ToArray() });
            insert.Parameters.Add(new NpgsqlParameter("prior_manual_override", NpgsqlDbType.Array | NpgsqlDbType.Boolean) { Value = priorManualOverride.ToArray() });
            await insert.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        }

        await SyncIdentifierRevisionsAsync(connection, transaction, securityId, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Brings the identifier history in step with the current rows: an identity the replacement no
    /// longer holds gets a <c>Removed</c> revision repeating its last values; a new identity (or one
    /// re-added after removal) gets <c>Added</c>; a held identity whose row changed gets
    /// <c>Expired</c> when its valid-to was first set, otherwise <c>Amended</c>. An identity whose
    /// row matches its latest revision appends nothing, so an unchanged replay leaves history as is.
    /// Recorded times are the transaction-start time, kept strictly after the identity's previous
    /// revision; they establish eligibility for a recorded cutoff, not commit visibility.
    /// </summary>
    private async Task SyncIdentifierRevisionsAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid securityId,
        CancellationToken ct)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            $"""
            insert into {Qualified("security_identifier_revisions")} (
                security_id, identifier_kind, identifier_value, valid_from, revision, change_kind,
                {IdentifierRevisionColumns}, recorded_by, recorded_at)
            select l.security_id, l.identifier_kind, l.identifier_value, l.valid_from, l.revision + 1, 'Removed',
                   l.normalized_identifier_value, l.provider, l.normalized_provider, l.is_primary, l.valid_to,
                   l.source, l.confidence, l.manual_override,
                   @actor, greatest(now(), l.recorded_at + interval '1 microsecond')
            from (
                select distinct on (identifier_kind, identifier_value, valid_from) *
                from {Qualified("security_identifier_revisions")}
                where security_id = @security_id
                order by identifier_kind, identifier_value, valid_from, revision desc
            ) l
            where l.change_kind <> 'Removed'
              and not exists (
                  select 1 from {Qualified("security_identifiers")} c
                  where c.security_id = l.security_id
                    and c.identifier_kind = l.identifier_kind
                    and c.identifier_value = l.identifier_value
                    and c.valid_from = l.valid_from);

            insert into {Qualified("security_identifier_revisions")} (
                security_id, identifier_kind, identifier_value, valid_from, revision, change_kind,
                {IdentifierRevisionColumns}, recorded_by, recorded_at)
            select c.security_id, c.identifier_kind, c.identifier_value, c.valid_from,
                   coalesce(l.revision, 0) + 1,
                   case when l.revision is null or l.change_kind = 'Removed' then 'Added'
                        when l.valid_to is null and c.valid_to is not null then 'Expired'
                        else 'Amended' end,
                   c.normalized_identifier_value, c.provider, c.normalized_provider, c.is_primary, c.valid_to,
                   c.source, c.confidence, c.manual_override,
                   @actor,
                   case when l.revision is null then now()
                        else greatest(now(), l.recorded_at + interval '1 microsecond') end
            from {Qualified("security_identifiers")} c
            left join lateral (
                select r.*
                from {Qualified("security_identifier_revisions")} r
                where r.security_id = c.security_id
                  and r.identifier_kind = c.identifier_kind
                  and r.identifier_value = c.identifier_value
                  and r.valid_from = c.valid_from
                order by r.revision desc
                limit 1
            ) l on true
            where c.security_id = @security_id
              and (l.revision is null
                   or l.change_kind = 'Removed'
                   or (c.normalized_identifier_value, c.provider, c.normalized_provider, c.is_primary, c.valid_to,
                       c.source, c.confidence, c.manual_override)
                      is distinct from
                      (l.normalized_identifier_value, l.provider, l.normalized_provider, l.is_primary, l.valid_to,
                       l.source, l.confidence, l.manual_override));
            """;
        command.Parameters.Add(new NpgsqlParameter("security_id", NpgsqlDbType.Uuid) { Value = securityId });
        command.Parameters.Add(new NpgsqlParameter("actor", NpgsqlDbType.Text) { Value = ProjectionReplacementIdentifierActor });
        await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<SecurityIdentifierRevision>> GetIdentifierRevisionsAsync(
        Guid securityId,
        DateTimeOffset recordedAsOfUtc,
        CancellationToken ct = default)
    {
        await using var lease = await PostgresSecurityMasterMutation.OpenConnectionAsync(_options, ct).ConfigureAwait(false);
        await using var command = lease.Connection.CreateCommand();
        command.Transaction = lease.Transaction;
        command.CommandText =
            $"""
            select {IdentifierRevisionSelectColumns}
            from {Qualified("security_identifier_revisions")}
            where security_id = @security_id
              and recorded_at <= @recorded_as_of
            order by identifier_kind, identifier_value, valid_from, revision;
            """;
        command.Parameters.Add(new NpgsqlParameter("security_id", NpgsqlDbType.Uuid) { Value = securityId });
        command.Parameters.Add(new NpgsqlParameter("recorded_as_of", NpgsqlDbType.TimestampTz) { Value = recordedAsOfUtc.UtcDateTime });
        return await ReadIdentifierRevisionsAsync(command, securityId, ct).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<SecurityIdentifiersRecordedView> GetIdentifiersRecordedAsOfAsync(
        Guid securityId,
        DateTimeOffset recordedAsOfUtc,
        DateTimeOffset? validAsOfUtc = null,
        CancellationToken ct = default)
    {
        await using var lease = await PostgresSecurityMasterMutation.OpenConnectionAsync(_options, ct).ConfigureAwait(false);
        DateTimeOffset? historyAvailableFrom;
        await using (var boundary = lease.Connection.CreateCommand())
        {
            boundary.Transaction = lease.Transaction;
            boundary.CommandText =
                $"""
                select max(history_available_from)
                from {Qualified("security_identifier_revisions")}
                where security_id = @security_id;
                """;
            boundary.Parameters.Add(new NpgsqlParameter("security_id", NpgsqlDbType.Uuid) { Value = securityId });
            historyAvailableFrom = await boundary.ExecuteScalarAsync(ct).ConfigureAwait(false) is DateTime available
                ? new DateTimeOffset(DateTime.SpecifyKind(available, DateTimeKind.Utc))
                : null;
        }

        await using var command = lease.Connection.CreateCommand();
        command.Transaction = lease.Transaction;
        command.CommandText =
            $"""
            select {IdentifierRevisionSelectColumns}
            from (
                select distinct on (identifier_kind, identifier_value, valid_from) *
                from {Qualified("security_identifier_revisions")}
                where security_id = @security_id
                  and recorded_at <= @recorded_as_of
                  and (history_available_from is null or history_available_from <= @recorded_as_of)
                order by identifier_kind, identifier_value, valid_from, revision desc
            ) latest
            where latest.change_kind <> 'Removed'
              and (@valid_as_of::timestamptz is null
                   or (latest.valid_from <= @valid_as_of
                       and (latest.valid_to is null or latest.valid_to > @valid_as_of)))
            order by identifier_kind, identifier_value, valid_from;
            """;
        command.Parameters.Add(new NpgsqlParameter("security_id", NpgsqlDbType.Uuid) { Value = securityId });
        command.Parameters.Add(new NpgsqlParameter("recorded_as_of", NpgsqlDbType.TimestampTz) { Value = recordedAsOfUtc.UtcDateTime });
        command.Parameters.Add(new NpgsqlParameter("valid_as_of", NpgsqlDbType.TimestampTz)
        {
            Value = (object?)validAsOfUtc?.UtcDateTime ?? DBNull.Value
        });
        var identifiers = await ReadIdentifierRevisionsAsync(command, securityId, ct).ConfigureAwait(false);
        return new SecurityIdentifiersRecordedView(
            securityId,
            recordedAsOfUtc,
            validAsOfUtc,
            identifiers,
            historyAvailableFrom);
    }

    private const string IdentifierRevisionSelectColumns =
        "identifier_kind, identifier_value, valid_from, revision, change_kind, normalized_identifier_value, " +
        "provider, normalized_provider, is_primary, valid_to, source, confidence, manual_override, " +
        "recorded_by, recorded_at, history_available_from";

    private static async Task<IReadOnlyList<SecurityIdentifierRevision>> ReadIdentifierRevisionsAsync(
        NpgsqlCommand command,
        Guid securityId,
        CancellationToken ct)
    {
        static DateTimeOffset Utc(DateTime value) => new(DateTime.SpecifyKind(value, DateTimeKind.Utc));

        var revisions = new List<SecurityIdentifierRevision>();
        await using var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await reader.ReadAsync(ct).ConfigureAwait(false))
        {
            revisions.Add(new SecurityIdentifierRevision(
                securityId,
                reader.GetString(0),
                reader.GetString(1),
                Utc(reader.GetDateTime(2)),
                reader.GetInt32(3),
                reader.GetString(4),
                reader.GetString(5),
                reader.IsDBNull(6) ? null : reader.GetString(6),
                reader.IsDBNull(7) ? null : reader.GetString(7),
                reader.GetBoolean(8),
                reader.IsDBNull(9) ? null : Utc(reader.GetDateTime(9)),
                reader.GetString(10),
                reader.IsDBNull(11) ? null : reader.GetDecimal(11),
                reader.GetBoolean(12),
                reader.GetString(13),
                Utc(reader.GetDateTime(14)),
                reader.IsDBNull(15) ? null : Utc(reader.GetDateTime(15))));
        }

        return revisions;
    }
}
