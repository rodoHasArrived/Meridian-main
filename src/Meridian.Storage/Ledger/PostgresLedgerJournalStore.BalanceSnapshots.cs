using System.Data;
using System.Text.Json;
using Meridian.Contracts.Integrity;
using Meridian.Ledger;
using Npgsql;
using static Meridian.Contracts.Text.TextPrimitives;

namespace Meridian.Storage.Ledger;

public sealed partial class PostgresLedgerJournalStore : ILedgerBalanceSnapshotSource
{
    public async Task<IReadOnlyDictionary<LedgerAccount, decimal>> ReadTrialBalanceAsOfAsync(
        Guid ledgerBookId,
        DateTimeOffset asOfUtc,
        string? financialAccountId = null,
        LedgerLineDimensionSet? lineDimensions = null,
        CancellationToken ct = default)
    {
        if (ledgerBookId == Guid.Empty)
            throw new ArgumentException("Ledger book id is required.", nameof(ledgerBookId));
        RequireWriteTenant();
        await using var connection = await OpenConnectionAsync(ct).ConfigureAwait(false);
        await using var transaction = await connection.BeginTransactionAsync(IsolationLevel.RepeatableRead, ct)
            .ConfigureAwait(false);
        await using (var readOnly = connection.CreateCommand())
        {
            readOnly.Transaction = transaction;
            readOnly.CommandText = "set transaction read only;";
            await readOnly.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        }
        await EnsureReportingBookReadAuthorityAsync(connection, transaction, ledgerBookId, ct).ConfigureAwait(false);
        var currentSequence = await ReadBalanceWatermarkAsync(connection, transaction, ct).ConfigureAwait(false);
        var checkpoint = await ReadBalanceCheckpointAsync(connection, transaction, ledgerBookId, asOfUtc, ct)
            .ConfigureAwait(false);
        if (checkpoint is not null)
            await VerifyBalanceCheckpointAuditAsync(connection, transaction, checkpoint, ct).ConfigureAwait(false);
        await VerifyBalanceTailAsync(connection, transaction, ledgerBookId, asOfUtc,
            currentSequence, checkpoint, ct).ConfigureAwait(false);

        var rows = await ReadBalanceRowsAsync(connection, transaction, ledgerBookId, asOfUtc,
            currentSequence, checkpoint, ct).ConfigureAwait(false);
        var totals = new Dictionary<LedgerAccount, (decimal Debit, decimal Credit)>();
        financialAccountId = NormalizeOptional(financialAccountId);
        foreach (var row in (checkpoint?.Rows ?? []).Concat(rows))
        {
            var account = new LedgerAccount(row.AccountName,
                Enum.Parse<LedgerAccountType>(row.AccountType, ignoreCase: true), row.Symbol, row.FinancialAccountId);
            if (!string.IsNullOrWhiteSpace(financialAccountId)
                && !string.Equals(account.FinancialAccountId, financialAccountId, StringComparison.OrdinalIgnoreCase))
                continue;
            var dimensions = row.DimensionsJson is null ? null : DeserializeLineDimensions(row.DimensionsJson);
            if (!LedgerDimensionScope.Matches(dimensions, lineDimensions))
                continue;
            var prior = totals.GetValueOrDefault(account);
            totals[account] = (prior.Debit + row.Debit, prior.Credit + row.Credit);
        }
        var result = totals.ToDictionary(pair => pair.Key,
            pair => Meridian.Ledger.Ledger.CalculateNetBalance(pair.Key, pair.Value.Debit, pair.Value.Credit));
        await transaction.CommitAsync(ct).ConfigureAwait(false);
        return result;
    }

    private async Task<long> ReadBalanceWatermarkAsync(
        NpgsqlConnection connection, NpgsqlTransaction transaction, CancellationToken ct)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = $"select coalesce(max(global_sequence), 0) from {Qualified("journal_entries")};";
        return Convert.ToInt64(await command.ExecuteScalarAsync(ct).ConfigureAwait(false));
    }

    private async Task<IReadOnlyList<LedgerBalanceCheckpointRow>> ReadBalanceRowsAsync(
        NpgsqlConnection connection, NpgsqlTransaction transaction, Guid ledgerBookId,
        DateTimeOffset asOfUtc, long sourceSequence, LedgerBalanceCheckpointContent? checkpoint,
        CancellationToken ct)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = $"""
            select jl.account_name, jl.account_type, jl.symbol, jl.financial_account_id,
                   jl.dimensions::text, sum(jl.debit), sum(jl.credit), count(*)
            from {Qualified("journal_entries")} je
            join {Qualified("accounting_periods")} p on p.period_id = je.period_id
            join {Qualified("journal_legs")} jl on jl.journal_entry_id = je.journal_entry_id
            where p.ledger_book_id = @book and je.occurred_at <= @asof
              and je.global_sequence <= @sequence
            """;
        command.Parameters.AddWithValue("book", ledgerBookId);
        command.Parameters.AddWithValue("asof", asOfUtc.ToUniversalTime());
        command.Parameters.AddWithValue("sequence", sourceSequence);
        ApplyTenantReadFilter(command, "p.tenant_id", ResolveCallerTenant());
        if (checkpoint is not null)
        {
            command.CommandText += " and (je.global_sequence > @checkpoint_sequence or je.occurred_at > @checkpoint_asof)";
            command.Parameters.AddWithValue("checkpoint_sequence", checkpoint.SourceSequence);
            command.Parameters.AddWithValue("checkpoint_asof", checkpoint.AsOfUtc);
        }
        command.CommandText += """

            group by jl.account_name, jl.account_type, jl.symbol, jl.financial_account_id, jl.dimensions
            order by jl.account_name collate "C", jl.account_type collate "C",
                     jl.symbol collate "C", jl.financial_account_id collate "C", jl.dimensions::text collate "C";
            """;
        return await ReadBalanceRowsAsync(command, ct, canonicalizeAccountType: true).ConfigureAwait(false);
    }

    private static async Task<IReadOnlyList<LedgerBalanceCheckpointRow>> ReadBalanceRowsAsync(
        NpgsqlCommand command, CancellationToken ct, bool canonicalizeAccountType = false)
    {
        var rows = new List<LedgerBalanceCheckpointRow>();
        await using var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await reader.ReadAsync(ct).ConfigureAwait(false))
            rows.Add(new(reader.GetString(0), canonicalizeAccountType
                    ? Enum.Parse<LedgerAccountType>(reader.GetString(1), ignoreCase: true).ToString()
                    : reader.GetString(1),
                reader.IsDBNull(2) ? null : reader.GetString(2),
                reader.IsDBNull(3) ? null : reader.GetString(3),
                reader.IsDBNull(4) ? null : reader.GetString(4),
                reader.GetDecimal(5), reader.GetDecimal(6), reader.GetInt64(7)));
        return rows;
    }

    private async Task<LedgerBalanceCheckpointContent> CaptureBalanceCheckpointAsync(
        NpgsqlConnection connection, NpgsqlTransaction transaction, LedgerAccountingPeriod period,
        PeriodCloseEventRecord closeEvent, CancellationToken ct)
    {
        if (period.LedgerBookId is not { } bookId || bookId == Guid.Empty)
            throw new LedgerValidationException("A retained ledger book is required for a balance checkpoint.");
        var cutoff = new DateTimeOffset(period.EndDate.ToDateTime(TimeOnly.MaxValue), TimeSpan.Zero).AddTicks(-9);
        var watermark = await ReadBalanceWatermarkAsync(connection, transaction, ct).ConfigureAwait(false);
        var rows = await ReadBalanceRowsAsync(connection, transaction, bookId, cutoff, watermark, null, ct)
            .ConfigureAwait(false);
        var futureAuditSequence = await ReadFutureBalanceAuditSequenceAsync(
            connection, transaction, bookId, cutoff, watermark, ct).ConfigureAwait(false);
        await using var count = connection.CreateCommand();
        count.Transaction = transaction;
        count.CommandText = $"""
            select count(*) from {Qualified("journal_entries")} je
            join {Qualified("accounting_periods")} p on p.period_id=je.period_id
            where p.ledger_book_id=@book and je.occurred_at<=@asof and je.global_sequence<=@sequence
            """;
        count.Parameters.AddWithValue("book", bookId);
        count.Parameters.AddWithValue("asof", cutoff);
        count.Parameters.AddWithValue("sequence", watermark);
        ApplyTenantReadFilter(count, "p.tenant_id", ResolveCallerTenant());
        var journalCount = Convert.ToInt64(await count.ExecuteScalarAsync(ct).ConfigureAwait(false));
        // Retain the persisted owner, including a compatibility writer operating without an
        // ambient tenant over an already attributed book.
        count.Parameters.Clear();
        count.CommandText = $"select tenant_id from {Qualified("accounting_periods")} where period_id=@period;";
        count.Parameters.AddWithValue("period", period.PeriodId);
        var retainedTenant = await count.ExecuteScalarAsync(ct).ConfigureAwait(false) as string;
        var now = DateTimeOffset.UtcNow;
        now = new DateTimeOffset(now.Ticks - now.Ticks % 10, TimeSpan.Zero);
        return new(1, closeEvent.EventId, closeEvent.EventId, bookId, period.PeriodId,
            period.Version, retainedTenant, cutoff, watermark, futureAuditSequence, journalCount,
            rows.Sum(row => row.EntryCount), now, rows);
    }

    private async Task<long?> ReadFutureBalanceAuditSequenceAsync(
        NpgsqlConnection connection, NpgsqlTransaction transaction, Guid bookId,
        DateTimeOffset cutoff, long watermark, CancellationToken ct)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = $"""
            select min(a.chain_sequence)
            from {Qualified("journal_entries")} j
            join {Qualified("accounting_periods")} p on p.period_id=j.period_id
            join {Qualified("ledger_event_audit_events")} a
              on a.subject_kind='journal' and a.subject_id=j.journal_entry_id
            where p.ledger_book_id=@book and j.occurred_at>@cutoff and j.global_sequence<=@watermark
            """;
        command.Parameters.AddWithValue("book", bookId);
        command.Parameters.AddWithValue("cutoff", cutoff);
        command.Parameters.AddWithValue("watermark", watermark);
        ApplyTenantReadFilter(command, "p.tenant_id", ResolveCallerTenant());
        var value = await command.ExecuteScalarAsync(ct).ConfigureAwait(false);
        return value is null or DBNull ? null : Convert.ToInt64(value);
    }

    private static string BalanceCheckpointHash(LedgerBalanceCheckpointContent content)
        => Sha256Digest.Compute(JsonSerializer.SerializeToUtf8Bytes(content,
            LedgerBalanceCheckpointJsonContext.Default.LedgerBalanceCheckpointContent));

    private async Task InsertBalanceCheckpointAsync(
        NpgsqlConnection connection, NpgsqlTransaction transaction,
        LedgerBalanceCheckpointContent content, string hash, CancellationToken ct)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = $"""
            insert into {Qualified("ledger_balance_checkpoints")}
                (checkpoint_id,close_event_id,ledger_book_id,period_id,period_version,tenant_id,
                 schema_version,as_of_utc,source_sequence,future_audit_sequence,journal_count,line_count,content_hash,created_at_utc)
            values (@id,@event,@book,@period,@version,@tenant,1,@asof,@sequence,@future_audit,@journals,@lines,@hash,@created);
            """;
        command.Parameters.AddWithValue("id", content.CheckpointId);
        command.Parameters.AddWithValue("event", content.CloseEventId);
        command.Parameters.AddWithValue("book", content.LedgerBookId);
        command.Parameters.AddWithValue("period", content.PeriodId);
        command.Parameters.AddWithValue("version", content.PeriodVersion);
        command.Parameters.AddWithValue("tenant", (object?)content.TenantId ?? DBNull.Value);
        command.Parameters.AddWithValue("asof", content.AsOfUtc);
        command.Parameters.AddWithValue("sequence", content.SourceSequence);
        command.Parameters.AddWithValue("future_audit", NpgsqlTypes.NpgsqlDbType.Bigint,
            (object?)content.FutureAuditSequence ?? DBNull.Value);
        command.Parameters.AddWithValue("journals", content.JournalCount);
        command.Parameters.AddWithValue("lines", content.LineCount);
        command.Parameters.AddWithValue("hash", hash);
        command.Parameters.AddWithValue("created", content.CreatedAtUtc);
        await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        for (var index = 0; index < content.Rows.Count; index++)
        {
            var row = content.Rows[index];
            await using var insert = connection.CreateCommand();
            insert.Transaction = transaction;
            insert.CommandText = $"""
                insert into {Qualified("ledger_balance_checkpoint_rows")}
                    (checkpoint_id,row_no,account_name,account_type,symbol,financial_account_id,
                     dimensions,debit,credit,entry_count)
                values (@id,@row,@name,@type,@symbol,@account,@dimensions::jsonb,@debit,@credit,@count);
                """;
            insert.Parameters.AddWithValue("id", content.CheckpointId);
            insert.Parameters.AddWithValue("row", index);
            insert.Parameters.AddWithValue("name", row.AccountName);
            insert.Parameters.AddWithValue("type", row.AccountType);
            insert.Parameters.AddWithValue("symbol", (object?)row.Symbol ?? DBNull.Value);
            insert.Parameters.AddWithValue("account", (object?)row.FinancialAccountId ?? DBNull.Value);
            insert.Parameters.AddWithValue("dimensions", (object?)row.DimensionsJson ?? DBNull.Value);
            insert.Parameters.AddWithValue("debit", row.Debit);
            insert.Parameters.AddWithValue("credit", row.Credit);
            insert.Parameters.AddWithValue("count", row.EntryCount);
            await insert.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        }
    }
}
