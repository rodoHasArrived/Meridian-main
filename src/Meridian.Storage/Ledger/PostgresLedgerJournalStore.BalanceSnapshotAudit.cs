using System.Data;
using Meridian.Contracts.Integrity;
using Meridian.Contracts.Ledger;
using Npgsql;

namespace Meridian.Storage.Ledger;

public sealed partial class PostgresLedgerJournalStore
{
    private async Task VerifyBalanceCheckpointAuditAsync(
        NpgsqlConnection connection, NpgsqlTransaction transaction,
        LedgerBalanceCheckpointContent checkpoint, CancellationToken ct)
    {
        await SetLedgerAuditFormatAsync(connection, transaction, ct).ConfigureAwait(false);
        var closeSequence = await ReadBalanceCheckpointAuditSequenceAsync(
            connection, transaction, checkpoint, ct).ConfigureAwait(false);
        // Authenticate the immutable close reference before trusting the checkpoint's captured
        // lower bound. A future-dated journal can have been audited before this close event.
        await VerifyBalanceAuditSuffixAsync(connection, transaction, closeSequence, ct).ConfigureAwait(false);
    }

    private async Task<long> ReadBalanceCheckpointAuditSequenceAsync(
        NpgsqlConnection connection, NpgsqlTransaction transaction,
        LedgerBalanceCheckpointContent checkpoint, CancellationToken ct)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = $"""
            select a.chain_sequence,
                   a.subject_kind='period' and a.subject_id=@period and a.subject_version=@version
                   and a.fact_snapshot::jsonb->>'period_id'=@period_text
                   and a.fact_snapshot::jsonb->>'ledger_book_id'=@book_text
                   and (a.fact_snapshot::jsonb->>'optimistic_version')::bigint=@version
                   and a.fact_snapshot::jsonb->>'status'='HardClosed'
                   and ((a.fact_snapshot::jsonb->>'end_date')::date + interval '1 day' - interval '1 microsecond')
                         at time zone 'UTC' = @cutoff
                   and a.close_event_snapshot::jsonb->>'event_id'=@event_text
                   and a.close_event_snapshot::jsonb->>'period_id'=@period_text
                   and (a.close_event_snapshot::jsonb->>'period_version')::bigint=@version
                   and a.close_event_snapshot::jsonb->>'new_status'='HardClosed'
                   and a.close_event_snapshot::jsonb->>'balance_checkpoint_id'=@checkpoint_text
                   and a.close_event_snapshot::jsonb->>'balance_checkpoint_hash'=@hash
                   and c.balance_checkpoint_id=@checkpoint and c.balance_checkpoint_hash=@hash
                   and {RetainedAuditColumnsSql("c", "a.close_event_snapshot::jsonb")}=a.close_event_snapshot::jsonb
            from {Qualified("ledger_event_audit_events")} a
            left join {Qualified("period_close_events")} c on c.event_id=a.close_event_id
            where a.close_event_id=@event
            """;
        command.Parameters.AddWithValue("period", checkpoint.PeriodId);
        command.Parameters.AddWithValue("period_text", checkpoint.PeriodId.ToString("D"));
        command.Parameters.AddWithValue("book_text", checkpoint.LedgerBookId.ToString("D"));
        command.Parameters.AddWithValue("version", checkpoint.PeriodVersion);
        command.Parameters.AddWithValue("cutoff", checkpoint.AsOfUtc);
        command.Parameters.AddWithValue("event", checkpoint.CloseEventId);
        command.Parameters.AddWithValue("event_text", checkpoint.CloseEventId.ToString("D"));
        command.Parameters.AddWithValue("checkpoint", checkpoint.CheckpointId);
        command.Parameters.AddWithValue("checkpoint_text", checkpoint.CheckpointId.ToString("D"));
        command.Parameters.AddWithValue("hash", BalanceCheckpointHash(checkpoint));
        ApplyTenantReadFilter(command, "(a.fact_snapshot::jsonb->>'tenant_id')", ResolveCallerTenant());
        try
        {
            await using var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false);
            if (!await reader.ReadAsync(ct).ConfigureAwait(false) || reader.IsDBNull(1) || !reader.GetBoolean(1))
                throw BalanceCheckpointIntegrityFailure("The checkpoint has no matching retained hard-close audit fact.");
            return reader.GetInt64(0);
        }
        catch (PostgresException ex) when (ex.SqlState.StartsWith("22", StringComparison.Ordinal))
        {
            throw BalanceCheckpointIntegrityFailure("The checkpoint hard-close audit fact has an invalid retained representation.");
        }
    }

    private async Task VerifyBalanceTailAsync(
        NpgsqlConnection connection, NpgsqlTransaction transaction, Guid bookId,
        DateTimeOffset asOfUtc, long currentSequence,
        LedgerBalanceCheckpointContent? checkpoint, CancellationToken ct)
    {
        await SetLedgerAuditFormatAsync(connection, transaction, ct).ConfigureAwait(false);
        long auditStart;
        if (checkpoint is null)
        {
            auditStart = 1;
            await VerifyBalanceAuditSuffixAsync(connection, transaction, auditStart, ct).ConfigureAwait(false);
            // Historical genesis inventories deliberately retain identities, not invented hashes
            // of pre-audit economics. Keep the existing explicit compatibility boundary.
            try
            {
                await VerifyLedgerAuditCoverageAsync(connection, transaction, ct).ConfigureAwait(false);
            }
            catch (Meridian.Ledger.LedgerValidationException ex)
            {
                throw BalanceCheckpointIntegrityFailure("Checkpoint-free retained journal coverage is invalid: " + ex.Message);
            }
            return;
        }

        var closeSequence = await ReadBalanceCheckpointAuditSequenceAsync(
            connection, transaction, checkpoint, ct).ConfigureAwait(false);
        auditStart = Math.Min(closeSequence, checkpoint.FutureAuditSequence ?? closeSequence);
        // Verify the pinned range BEFORE using dates or identities from its JSON to select the
        // tail. Otherwise changing a preposted future fact's date could hide its own mutation.
        await VerifyBalanceAuditSuffixAsync(connection, transaction, auditStart, ct).ConfigureAwait(false);

        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = $"""
            with native_tail as (
                select j.journal_entry_id
                from {Qualified("journal_entries")} j
                join {Qualified("accounting_periods")} p on p.period_id=j.period_id
                where p.ledger_book_id=@book and j.occurred_at<=@asof and j.global_sequence<=@sequence
                  and (j.global_sequence>@checkpoint_sequence or j.occurred_at>@checkpoint_asof)
                __TENANT_FILTER__
            ), audited_tail as (
                select a.subject_id as journal_entry_id
                from {Qualified("ledger_event_audit_events")} a
                join {Qualified("accounting_periods")} p
                  on p.period_id=(a.fact_snapshot::jsonb->'journal'->>'period_id')::uuid
                where a.chain_sequence>=@audit_start and a.subject_kind='journal'
                  and p.ledger_book_id=@book
                  and (a.fact_snapshot::jsonb->'journal'->>'occurred_at')::timestamptz<=@asof
                  and ((a.fact_snapshot::jsonb->'journal'->>'global_sequence')::bigint>@checkpoint_sequence
                       or (a.fact_snapshot::jsonb->'journal'->>'occurred_at')::timestamptz>@checkpoint_asof)
                __TENANT_FILTER__
            ), selected as (
                select journal_entry_id from native_tail
                union select journal_entry_id from audited_tail
            )
            select exists (
                select 1 from selected s
                left join {Qualified("journal_entries")} j on j.journal_entry_id=s.journal_entry_id
                left join {Qualified("ledger_event_audit_events")} a
                  on a.subject_kind='journal' and a.subject_id=s.journal_entry_id
                left join {Qualified("ledger_event_audit_genesis")} g
                  on g.subject_kind='journal' and g.subject_id=s.journal_entry_id
                where j.journal_entry_id is null or j.global_sequence>@sequence
                   or (a.subject_id is null and g.subject_id is null)
                   or (a.subject_id is not null and a.chain_sequence<@audit_start)
                   or (a.subject_id is not null and
                       ({RetainedJournalAuditSnapshotSql()} is distinct from a.fact_snapshot::jsonb
                        or jsonb_array_length(a.fact_snapshot::jsonb->'legs') <>
                           (select count(*) from {Qualified("journal_legs")} l where l.journal_entry_id=j.journal_entry_id)))
            );
            """;
        command.Parameters.AddWithValue("book", bookId);
        command.Parameters.AddWithValue("asof", asOfUtc.ToUniversalTime());
        command.Parameters.AddWithValue("sequence", currentSequence);
        command.Parameters.AddWithValue("checkpoint_sequence", checkpoint.SourceSequence);
        command.Parameters.AddWithValue("checkpoint_asof", checkpoint.AsOfUtc);
        command.Parameters.AddWithValue("audit_start", auditStart);
        // Generate and bind the existing tenant predicate once, then use it in both branches.
        var sql = command.CommandText;
        command.CommandText = string.Empty;
        ApplyTenantReadFilter(command, "p.tenant_id", ResolveCallerTenant());
        command.CommandText = sql.Replace("__TENANT_FILTER__", command.CommandText, StringComparison.Ordinal);
        try
        {
            if (await command.ExecuteScalarAsync(ct).ConfigureAwait(false) is true)
                throw BalanceCheckpointIntegrityFailure("The checkpoint journal tail has missing, uncovered, or changed retained facts.");
        }
        catch (PostgresException ex) when (ex.SqlState.StartsWith("22", StringComparison.Ordinal))
        {
            throw BalanceCheckpointIntegrityFailure("The checkpoint journal tail has an invalid retained representation.");
        }
    }

    private async Task VerifyBalanceAuditSuffixAsync(
        NpgsqlConnection connection, NpgsqlTransaction transaction, long firstSequence, CancellationToken ct)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        // This verifier shares the caller's read-only repeatable-read snapshot. It must never
        // acquire the audit writer lock or silently change the reporting transaction's mode.
        command.CommandText = $"""
            select schema_version,next_sequence,last_hash,genesis_hash
            from {Qualified("ledger_event_audit_head")} where chain_id=1;
            """;
        long nextSequence;
        string? lastHash;
        string genesisHash;
        await using (var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false))
        {
            if (!await reader.ReadAsync(ct).ConfigureAwait(false))
                throw BalanceCheckpointIntegrityFailure("The retained checkpoint audit head is missing.");
            if (reader.GetInt32(0) != 1)
            {
                var failure = BalanceCheckpointIntegrityFailure("The retained checkpoint audit head uses an unsupported schema version.");
                failure.Data[LedgerEventAuditFailureData.UnsupportedSchemaVersion] = true;
                throw failure;
            }
            nextSequence = reader.GetInt64(1);
            lastHash = reader.IsDBNull(2) ? null : reader.GetString(2);
            genesisHash = reader.GetString(3);
        }
        if (firstSequence < 1 || firstSequence > nextSequence
            || (nextSequence == 1 ? lastHash is not null : !Sha256Digest.IsCanonical(lastHash)))
            throw BalanceCheckpointIntegrityFailure("The checkpoint audit range and retained head disagree.");
        command.CommandText = $"""
            select encode(sha256(convert_to(coalesce(string_agg(
                subject_kind||':'||subject_id::text||':'||subject_version::text||E'\n',
                '' order by subject_kind collate "C",subject_id),''),'UTF8')),'hex')
            from {Qualified("ledger_event_audit_genesis")};
            """;
        if (!string.Equals(genesisHash, await command.ExecuteScalarAsync(ct).ConfigureAwait(false) as string,
                StringComparison.Ordinal))
            throw BalanceCheckpointIntegrityFailure("The checkpoint audit genesis inventory has changed.");

        string? previous = null;
        if (firstSequence > 1)
        {
            command.CommandText = $"select entry_hash from {Qualified("ledger_event_audit_events")} where chain_sequence=@previous;";
            command.Parameters.AddWithValue("previous", firstSequence - 1);
            previous = await command.ExecuteScalarAsync(ct).ConfigureAwait(false) as string;
            if (!Sha256Digest.IsCanonical(previous))
                throw BalanceCheckpointIntegrityFailure("The checkpoint audit suffix has lost its preceding link.");
        }
        command.Parameters.Clear();
        command.CommandText = $"""
            select chain_sequence,subject_kind,subject_id,subject_version,action,actor,
                   recorded_at_utc,fact_snapshot,close_event_id,close_event_snapshot,
                   payload_hash,previous_hash,entry_hash
            from {Qualified("ledger_event_audit_events")}
            where chain_sequence>=@first order by chain_sequence;
            """;
        command.Parameters.AddWithValue("first", firstSequence);
        var sequence = firstSequence;
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
                    throw BalanceCheckpointIntegrityFailure($"Checkpoint audit event {sequence} has been mutated, removed, or reordered.");
                previous = entryHash;
                sequence++;
            }
        }
        if (sequence != nextSequence || previous != lastHash)
            throw BalanceCheckpointIntegrityFailure("The checkpoint audit suffix does not reach the retained head.");
    }
}
