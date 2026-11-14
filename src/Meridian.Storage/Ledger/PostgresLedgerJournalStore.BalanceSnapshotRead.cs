using Meridian.Contracts.Integrity;
using Meridian.Ledger;
using Npgsql;
using System.Text.Json;

namespace Meridian.Storage.Ledger;

public sealed partial class PostgresLedgerJournalStore
{
    private async Task<LedgerBalanceCheckpointContent?> ReadBalanceCheckpointAsync(
        NpgsqlConnection connection, NpgsqlTransaction transaction, Guid bookId,
        DateTimeOffset asOfUtc, CancellationToken ct)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        // Admission starts with retained audited authority, not a cache/header lookup. Clearing
        // current reference columns or deleting a checkpoint cannot masquerade as legacy history.
        command.CommandText = $"""
            select a.close_event_snapshot,ce.balance_checkpoint_id,ce.balance_checkpoint_hash
            from {Qualified("ledger_event_audit_events")} a
            left join {Qualified("period_close_events")} ce on ce.event_id=a.close_event_id
            where a.subject_kind='period' and a.close_event_id is not null
              and a.fact_snapshot::jsonb->>'ledger_book_id'=@book_text
              and a.close_event_snapshot::jsonb->>'balance_checkpoint_id' is not null
              and ((a.fact_snapshot::jsonb->>'end_date')::date + interval '1 day' - interval '1 microsecond')
                    at time zone 'UTC' <= @asof
            """;
        command.Parameters.AddWithValue("book_text", bookId.ToString("D"));
        command.Parameters.AddWithValue("asof", asOfUtc.ToUniversalTime());
        ApplyTenantReadFilter(command, "(a.fact_snapshot::jsonb->>'tenant_id')", ResolveCallerTenant());
        command.CommandText += " order by a.fact_snapshot::jsonb->>'end_date' desc,a.chain_sequence desc limit 1;";
        Guid expectedId;
        string expectedHash;
        await using (var claim = await command.ExecuteReaderAsync(ct).ConfigureAwait(false))
        {
            if (!await claim.ReadAsync(ct).ConfigureAwait(false))
                return null;
            using var document = JsonDocument.Parse(claim.GetString(0));
            var value = document.RootElement;
            expectedId = value.GetProperty("balance_checkpoint_id").GetGuid();
            expectedHash = value.GetProperty("balance_checkpoint_hash").GetString()
                ?? throw BalanceCheckpointIntegrityFailure("The audited checkpoint reference is incomplete.");
            if (claim.IsDBNull(1) || claim.IsDBNull(2) || claim.GetGuid(1) != expectedId
                || !Sha256Digest.FixedEquals(claim.GetString(2), expectedHash))
                throw BalanceCheckpointIntegrityFailure("The retained close-event checkpoint reference has changed.");
        }
        command.Parameters.Clear();
        command.CommandText = $"""
            select checkpoint_id,close_event_id,ledger_book_id,period_id,period_version,tenant_id,
                   schema_version,as_of_utc,source_sequence,future_audit_sequence,journal_count,line_count,content_hash,created_at_utc
            from {Qualified("ledger_balance_checkpoints")}
            where checkpoint_id=@checkpoint and ledger_book_id=@book and as_of_utc<=@asof
            """;
        command.Parameters.AddWithValue("checkpoint", expectedId);
        command.Parameters.AddWithValue("book", bookId);
        command.Parameters.AddWithValue("asof", asOfUtc.ToUniversalTime());
        ApplyTenantReadFilter(command, "tenant_id", ResolveCallerTenant());
        command.CommandText += " order by as_of_utc desc,source_sequence desc,checkpoint_id desc limit 1;";
        LedgerBalanceCheckpointContent? content;
        string hash;
        await using (var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false))
        {
            if (!await reader.ReadAsync(ct).ConfigureAwait(false))
                throw BalanceCheckpointIntegrityFailure("The audited balance checkpoint is missing.");
            content = new(reader.GetInt32(6), reader.GetGuid(0), reader.GetGuid(1), reader.GetGuid(2),
                reader.GetGuid(3), reader.GetInt64(4), reader.IsDBNull(5) ? null : reader.GetString(5),
                ReadUtcDateTimeOffset(reader, 7), reader.GetInt64(8), reader.IsDBNull(9) ? null : reader.GetInt64(9),
                reader.GetInt64(10), reader.GetInt64(11), ReadUtcDateTimeOffset(reader, 13), []);
            hash = reader.GetString(12);
        }
        if (content.SchemaVersion != 1 || content.LedgerBookId != bookId
            || content.PeriodVersion < 1 || content.SourceSequence < 0 || content.JournalCount < 0 || content.LineCount < 0
            || content.FutureAuditSequence is <= 0
            || !Sha256Digest.IsCanonical(hash) || !Sha256Digest.FixedEquals(hash, expectedHash))
            throw BalanceCheckpointIntegrityFailure("The retained balance checkpoint header is invalid.");
        command.Parameters.Clear();
        command.CommandText = $"""
            select account_name,account_type,symbol,financial_account_id,dimensions::text,debit,credit,entry_count
            from {Qualified("ledger_balance_checkpoint_rows")} where checkpoint_id=@id order by row_no;
            """;
        command.Parameters.AddWithValue("id", content.CheckpointId);
        var rows = await ReadBalanceRowsAsync(command, ct).ConfigureAwait(false);
        content = content with { Rows = rows };
        if (rows.Any(row => row.Debit < 0m || row.Credit < 0m || row.EntryCount < 1)
            || rows.Sum(row => row.EntryCount) != content.LineCount
            || !Sha256Digest.FixedEquals(hash, BalanceCheckpointHash(content)))
            throw BalanceCheckpointIntegrityFailure("The retained balance checkpoint content does not match its digest.");
        return content;
    }

    private static LedgerValidationException BalanceCheckpointIntegrityFailure(string detail)
        => new("Ledger balance checkpoint integrity failure: " + detail);

    private async Task RejectPopulatedPeriodBookMoveAsync(
        NpgsqlConnection connection, NpgsqlTransaction transaction,
        LedgerAccountingPeriod current, LedgerAccountingPeriod proposed, CancellationToken ct)
    {
        if (current.LedgerBookId == proposed.LedgerBookId)
            return;
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = $"select exists(select 1 from {Qualified("journal_entries")} where period_id=@period);";
        command.Parameters.AddWithValue("period", current.PeriodId);
        if (await command.ExecuteScalarAsync(ct).ConfigureAwait(false) is true)
            throw new LedgerValidationException("A populated accounting period cannot move to a different ledger book or clear its retained book.");
    }
}
