using System.Data;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Meridian.Contracts.Ledger;
using Meridian.Ledger;
using Npgsql;
using NpgsqlTypes;

namespace Meridian.Storage.Ledger;

public sealed partial class PostgresLedgerJournalStore : ILedgerEventAuditVerifier
{
    private sealed record LedgerAuditHead(long NextSequence, string? LastHash, DateTimeOffset GenesisAtUtc, long GenesisFacts);

    private sealed record LedgerAuditFact(
        string Kind, Guid Id, long Version, string Action, string? Actor,
        DateTimeOffset RecordedAtUtc, string Snapshot, Guid? CloseEventId, string? CloseEventSnapshot);

    /// <summary>
    /// Checks every link and every covered retained fact, including uncovered writes since genesis.
    /// A coherent rollback of the head, suffix and corresponding facts needs an external checkpoint to detect.
    /// </summary>
    public async Task<LedgerEventAuditVerification> VerifyLedgerEventAuditAsync(CancellationToken ct = default)
    {
        await using var connection = await OpenConnectionAsync(ct).ConfigureAwait(false);
        await using var transaction = await connection.BeginTransactionAsync(IsolationLevel.ReadCommitted, ct).ConfigureAwait(false);
        var head = await LockAndVerifyLedgerAuditAsync(connection, transaction, ct).ConfigureAwait(false);
        await transaction.CommitAsync(ct).ConfigureAwait(false);
        return new(head.NextSequence - 1, head.GenesisFacts, head.GenesisAtUtc);
    }

    private async Task<LedgerAuditHead> LockAndVerifyLedgerAuditAsync(
        NpgsqlConnection connection, NpgsqlTransaction transaction, CancellationToken ct)
    {
        // Always lock after the period lock, matching journal, atomic-lot and period-save paths.
        // Serializable callers retain their existing serialization-retry contract.
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = $"""
            select schema_version, next_sequence, last_hash, genesis_at_utc, genesis_hash
            from {Qualified("ledger_event_audit_head")} where chain_id = 1 for update;
            """;
        long nextSequence;
        string? lastHash;
        DateTimeOffset genesisAtUtc;
        string genesisHash;
        await using (var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false))
        {
            if (!await reader.ReadAsync(ct).ConfigureAwait(false))
                throw AuditIntegrityFailure("The retained ledger audit head is missing.");
            if (reader.GetInt32(0) != 1)
            {
                // Hashing rules this build does not implement (a rolling upgrade or downgrade) are
                // unverifiable, not evidence of tampering; readers distinguish the two by this key.
                var unsupported = AuditIntegrityFailure("The retained ledger audit head has an unsupported version.");
                unsupported.Data[LedgerEventAuditFailureData.UnsupportedSchemaVersion] = true;
                throw unsupported;
            }
            nextSequence = reader.GetInt64(1);
            lastHash = reader.IsDBNull(2) ? null : reader.GetString(2);
            genesisAtUtc = reader.GetFieldValue<DateTimeOffset>(3);
            genesisHash = reader.GetString(4);
        }

        command.CommandText = $"""
            select encode(sha256(convert_to(coalesce(string_agg(
                subject_kind || ':' || subject_id::text || ':' || subject_version::text || E'\n',
                '' order by subject_kind collate "C", subject_id), ''), 'UTF8')), 'hex'), count(*)
            from {Qualified("ledger_event_audit_genesis")};
            """;
        string? retainedGenesisHash;
        long genesisFacts;
        await using (var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false))
        {
            await reader.ReadAsync(ct).ConfigureAwait(false);
            retainedGenesisHash = reader.IsDBNull(0) ? null : reader.GetString(0);
            genesisFacts = reader.GetInt64(1);
        }
        if (!string.Equals(genesisHash, retainedGenesisHash, StringComparison.Ordinal))
            throw AuditIntegrityFailure("The retained ledger audit genesis inventory has changed.", chainedEventsVerified: 0);
        var head = new LedgerAuditHead(nextSequence, lastHash, genesisAtUtc, genesisFacts);

        command.CommandText = $"""
            select chain_sequence, subject_kind, subject_id, subject_version, action, actor,
                   recorded_at_utc, fact_snapshot, close_event_id, close_event_snapshot,
                   payload_hash, previous_hash, entry_hash
            from {Qualified("ledger_event_audit_events")} order by chain_sequence;
            """;
        long sequence = 1;
        string? previous = null;
        await using (var reader = await command.ExecuteReaderAsync(CommandBehavior.SequentialAccess, ct).ConfigureAwait(false))
        {
            while (await reader.ReadAsync(ct).ConfigureAwait(false))
            {
                var retainedSequence = reader.GetInt64(0);
                var fact = new LedgerAuditFact(reader.GetString(1), reader.GetGuid(2), reader.GetInt64(3),
                    reader.GetString(4), reader.IsDBNull(5) ? null : reader.GetString(5),
                    reader.GetFieldValue<DateTimeOffset>(6), reader.GetString(7),
                    reader.IsDBNull(8) ? null : reader.GetGuid(8), reader.IsDBNull(9) ? null : reader.GetString(9));
                var payloadHash = reader.GetString(10);
                var retainedPrevious = reader.IsDBNull(11) ? null : reader.GetString(11);
                var entryHash = reader.GetString(12);
                if (retainedSequence != sequence || retainedPrevious != previous
                    || ComputeLedgerAuditPayloadHash(fact) != payloadHash
                    || AccountingAuditChain.ComputeEntryHash(sequence, previous, payloadHash) != entryHash)
                    throw AuditIntegrityFailure($"Ledger audit event {sequence} has been mutated, removed, or reordered.", sequence, sequence - 1, genesisFacts);
                sequence++;
                previous = entryHash;
            }
        }
        if (head.NextSequence != sequence || head.LastHash != previous)
        {
            // Every retained link verified, so `sequence` is one past the final one. Name the link
            // the head disagrees with: the first missing one when the suffix is short, the first
            // one beyond a rewound head, or the final one when only the head hash differs.
            var failedAt = head.NextSequence > sequence ? sequence
                : head.NextSequence < sequence ? head.NextSequence
                : sequence - 1;
            throw AuditIntegrityFailure("The ledger audit suffix and retained head disagree.",
                failedAt >= 1 ? failedAt : null, Math.Max(0, failedAt - 1), genesisFacts);
        }
        try
        {
            await VerifyLedgerAuditCoverageAsync(connection, transaction, ct).ConfigureAwait(false);
        }
        catch (LedgerValidationException ex) when (!ex.Data.Contains(LedgerEventAuditFailureData.ChainedEventsVerified))
        {
            // Every link verified; the coverage check failed after the scan completed.
            ex.Data[LedgerEventAuditFailureData.ChainedEventsVerified] = sequence - 1;
            ex.Data[LedgerEventAuditFailureData.UnprotectedGenesisFacts] = genesisFacts;
            throw;
        }

        return head;
    }

    private async Task AppendLedgerAuditAsync(
        NpgsqlConnection connection, NpgsqlTransaction transaction, LedgerAuditHead head,
        string kind, Guid id, long version, string action, string? actor,
        PeriodCloseEventRecord? closeEvent, CancellationToken ct)
    {
        // Snapshot the actual persisted rows, including journal legs and normalized metadata.
        // PostgreSQL produces this canonical JSON in a fixed UTC/ISO session (see snapshot query).
        var snapshot = await ReadLedgerAuditSnapshotAsync(connection, transaction, kind, id, ct).ConfigureAwait(false);
        string? closeSnapshot = closeEvent is null ? null
            : await ReadLedgerAuditSnapshotAsync(connection, transaction, "period-close", closeEvent.EventId, ct).ConfigureAwait(false);
        var now = DateTimeOffset.UtcNow;
        now = new DateTimeOffset(now.Ticks - now.Ticks % 10, TimeSpan.Zero); // PostgreSQL microseconds.
        var fact = new LedgerAuditFact(kind, id, version, action,
            string.IsNullOrWhiteSpace(actor) ? null : actor.Trim(), now, snapshot, closeEvent?.EventId, closeSnapshot);
        var payloadHash = ComputeLedgerAuditPayloadHash(fact);
        var entryHash = AccountingAuditChain.ComputeEntryHash(head.NextSequence, head.LastHash, payloadHash);
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = $"""
            insert into {Qualified("ledger_event_audit_events")}
                (chain_sequence, subject_kind, subject_id, subject_version, action, actor,
                 recorded_at_utc, fact_snapshot, close_event_id, close_event_snapshot,
                 payload_hash, previous_hash, entry_hash)
            values (@sequence, @kind, @id, @version, @action, @actor, @recorded_at,
                    @snapshot, @close_id, @close_snapshot, @payload_hash, @previous_hash, @entry_hash);
            update {Qualified("ledger_event_audit_head")}
                set next_sequence = @sequence + 1, last_hash = @entry_hash where chain_id = 1;
            """;
        command.Parameters.AddWithValue("sequence", head.NextSequence);
        command.Parameters.AddWithValue("kind", kind);
        command.Parameters.AddWithValue("id", id);
        command.Parameters.AddWithValue("version", version);
        command.Parameters.AddWithValue("action", action);
        command.Parameters.AddWithValue("actor", NpgsqlDbType.Text, (object?)fact.Actor ?? DBNull.Value);
        command.Parameters.AddWithValue("recorded_at", now);
        command.Parameters.AddWithValue("snapshot", snapshot);
        command.Parameters.AddWithValue("close_id", NpgsqlDbType.Uuid, (object?)closeEvent?.EventId ?? DBNull.Value);
        command.Parameters.AddWithValue("close_snapshot", NpgsqlDbType.Text, (object?)closeSnapshot ?? DBNull.Value);
        command.Parameters.AddWithValue("payload_hash", payloadHash);
        command.Parameters.AddWithValue("previous_hash", NpgsqlDbType.Text, (object?)head.LastHash ?? DBNull.Value);
        command.Parameters.AddWithValue("entry_hash", entryHash);
        await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    private string JournalAuditSnapshotSql(string alias) => $"""
        jsonb_build_object('journal', to_jsonb({alias}), 'legs', coalesce(
            (select jsonb_agg(to_jsonb(l) order by l.line_no)
             from {Qualified("journal_legs")} l where l.journal_entry_id = {alias}.journal_entry_id), '[]'::jsonb))::text
        """;

    private static string RetainedAuditColumnsSql(string row, string snapshot) => $"""
        (select coalesce(jsonb_object_agg(column_value.key, column_value.value), jsonb_build_object())
         from jsonb_each(to_jsonb({row})) column_value where ({snapshot}) ? column_value.key)
        """;

    private string RetainedJournalAuditSnapshotSql() => $"""
        jsonb_build_object('journal', {RetainedAuditColumnsSql("j", "a.fact_snapshot::jsonb -> 'journal'")},
            'legs', coalesce((select jsonb_agg({RetainedAuditColumnsSql("l", "retained_leg.value")} order by l.line_no)
                from {Qualified("journal_legs")} l
                join jsonb_array_elements(a.fact_snapshot::jsonb -> 'legs') retained_leg
                  on retained_leg.value ->> 'entry_id' = l.entry_id::text
                where l.journal_entry_id = j.journal_entry_id), '[]'::jsonb))
        """;

    private static async Task SetLedgerAuditFormatAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, CancellationToken ct)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "set local timezone = 'UTC'; set local datestyle = 'ISO, YMD';";
        await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    private async Task<string> ReadLedgerAuditSnapshotAsync(
        NpgsqlConnection connection, NpgsqlTransaction transaction, string kind, Guid id, CancellationToken ct)
    {
        await SetLedgerAuditFormatAsync(connection, transaction, ct).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = kind switch
        {
            "journal" => $"select {JournalAuditSnapshotSql("j")} from {Qualified("journal_entries")} j where journal_entry_id = @id;",
            "period" => $"select to_jsonb(p)::text from {Qualified("accounting_periods")} p where period_id = @id;",
            "period-close" => $"select to_jsonb(p)::text from {Qualified("period_close_events")} p where event_id = @id;",
            _ => throw new ArgumentOutOfRangeException(nameof(kind))
        };
        command.Parameters.AddWithValue("id", id);
        return await command.ExecuteScalarAsync(ct).ConfigureAwait(false) as string
            ?? throw AuditIntegrityFailure($"Ledger {kind} '{id}' disappeared before audit capture.");
    }

    private async Task VerifyLedgerAuditCoverageAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, CancellationToken ct)
    {
        await SetLedgerAuditFormatAsync(connection, transaction, ct).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        var events = Qualified("ledger_event_audit_events");
        var genesis = Qualified("ledger_event_audit_genesis");
        // Compare identities, not just counts: a deleted covered journal cannot be replaced with
        // a different uncovered journal. Compare fields retained at capture, allowing later additive
        // SQL columns only. Retained JSON columns use deep equality, so new metadata/dimension
        // keys are mutations too; leg cardinality forbids extending an immutable aggregate.
        command.CommandText = $"""
            select exists (
                select 1 from {Qualified("journal_entries")} j
                left join {events} a on a.subject_kind = 'journal' and a.subject_id = j.journal_entry_id
                left join {genesis} g on g.subject_kind = 'journal' and g.subject_id = j.journal_entry_id
                where (a.subject_id is null and g.subject_id is null)
                   or (a.subject_id is not null and
                       ({RetainedJournalAuditSnapshotSql()} <> a.fact_snapshot::jsonb
                        or jsonb_array_length(a.fact_snapshot::jsonb -> 'legs') <>
                           (select count(*) from {Qualified("journal_legs")} l where l.journal_entry_id = j.journal_entry_id)))
                union all
                select 1 from {events} a left join {Qualified("journal_entries")} j on j.journal_entry_id = a.subject_id
                where a.subject_kind = 'journal' and j.journal_entry_id is null
                union all
                select 1 from {Qualified("accounting_periods")} p
                left join lateral (select * from {events} e where e.subject_kind = 'period' and e.subject_id = p.period_id
                                   order by subject_version desc limit 1) a on true
                left join {genesis} g on g.subject_kind = 'period' and g.subject_id = p.period_id
                where (a.subject_id is null and (g.subject_id is null or g.subject_version <> p.optimistic_version))
                   or (a.subject_id is not null and (a.subject_version <> p.optimistic_version
                       or {RetainedAuditColumnsSql("p", "a.fact_snapshot::jsonb")} <> a.fact_snapshot::jsonb))
                union all
                select 1 from {events} a left join {Qualified("accounting_periods")} p on p.period_id = a.subject_id
                where a.subject_kind = 'period' and p.period_id is null
                union all
                select 1 from {Qualified("period_close_events")} c
                left join {events} a on a.close_event_id = c.event_id
                left join {genesis} g on g.subject_kind = 'period-close' and g.subject_id = c.event_id
                where (a.close_event_id is null and g.subject_id is null)
                   or (a.close_event_id is not null and
                       {RetainedAuditColumnsSql("c", "a.close_event_snapshot::jsonb")} <> a.close_event_snapshot::jsonb)
                union all
                select 1 from {events} a left join {Qualified("period_close_events")} c on c.event_id = a.close_event_id
                where a.close_event_id is not null and c.event_id is null
            );
            """;
        if ((bool)(await command.ExecuteScalarAsync(ct).ConfigureAwait(false))!)
            throw AuditIntegrityFailure("Retained ledger facts disagree with their audit coverage or include uncovered post-genesis writes.");
    }

    private static string ComputeLedgerAuditPayloadHash(LedgerAuditFact fact)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        foreach (var field in new[] { "ledger-event-v1", fact.Kind, fact.Id.ToString("D"),
            fact.Version.ToString(CultureInfo.InvariantCulture), fact.Action, fact.Actor,
            fact.Actor is null ? "unattributed" : "command-actor", fact.RecordedAtUtc.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture),
            fact.Snapshot, fact.CloseEventId?.ToString("D"), fact.CloseEventSnapshot })
        {
            var bytes = field is null ? null : Encoding.UTF8.GetBytes(field);
            hash.AppendData(Encoding.UTF8.GetBytes((bytes?.Length ?? -1).ToString(CultureInfo.InvariantCulture) + ":"));
            if (bytes is not null)
                hash.AppendData(bytes);
        }
        return Convert.ToHexStringLower(hash.GetHashAndReset());
    }

    private static LedgerValidationException AuditIntegrityFailure(
        string message, long? failedSequence = null, long? chainedEventsVerified = null, long? unprotectedGenesisFacts = null)
    {
        var failure = new LedgerValidationException("Ledger audit integrity failure: " + message);
        if (failedSequence is { } at)
            failure.Data[LedgerEventAuditFailureData.FailedSequence] = at;
        if (chainedEventsVerified is { } verified)
            failure.Data[LedgerEventAuditFailureData.ChainedEventsVerified] = verified;
        if (unprotectedGenesisFacts is { } genesis)
            failure.Data[LedgerEventAuditFailureData.UnprotectedGenesisFacts] = genesis;
        return failure;
    }
}
