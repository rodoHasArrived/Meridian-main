using Npgsql;

namespace Meridian.Storage.Ledger;

public sealed partial class PostgresLedgerJournalStore
{
    public async Task<LedgerPeriodCloseSnapshot?> GetCloseSnapshotAsync(
        Guid periodId,
        long? periodVersion = null,
        CancellationToken ct = default)
    {
        if (periodId == Guid.Empty)
            throw new ArgumentException("Period id is required.", nameof(periodId));
        if (periodVersion is <= 0)
            throw new ArgumentOutOfRangeException(nameof(periodVersion));
        await using var connection = await OpenConnectionAsync(ct).ConfigureAwait(false);
        return await LoadCloseSnapshotAsync(connection, null, periodId, periodVersion, ct).ConfigureAwait(false);
    }

    private async Task<LedgerPeriodCloseSnapshot?> LoadCloseSnapshotAsync(
        NpgsqlConnection connection, NpgsqlTransaction? transaction, Guid periodId,
        long? periodVersion, CancellationToken ct)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = $"""
            select s.ledger_book_id, s.period_id, s.period_version, s.close_event_id,
                   s.closed_at, s.payload_json, s.content_hash_sha256
            from {Qualified("period_close_snapshots")} s
            join {Qualified("accounting_periods")} p on p.period_id = s.period_id
            where s.period_id = @period_id
            """;
        command.Parameters.AddWithValue("period_id", periodId);
        if (periodVersion is { } version)
        {
            command.CommandText += " and s.period_version = @period_version";
            command.Parameters.AddWithValue("period_version", version);
        }
        ApplyTenantReadFilter(command, "p.tenant_id", ResolveCallerTenant());
        command.CommandText += " order by s.period_version desc limit 1;";
        await using var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false);
        if (!await reader.ReadAsync(ct).ConfigureAwait(false))
            return null;
        var snapshot = new LedgerPeriodCloseSnapshot(
            reader.IsDBNull(0) ? null : reader.GetGuid(0), reader.GetGuid(1), reader.GetInt64(2),
            reader.GetGuid(3), ReadUtcDateTimeOffset(reader, 4), reader.GetString(5), reader.GetString(6));
        _ = snapshot.VerifyIntegrity();
        return snapshot;
    }

    // Acquire history row fences before the audit head: append takes period -> audit in that
    // order. Holding the audit head while waiting for a prior period would invert that order.
    private async Task LockCloseSnapshotHistoryAsync(
        NpgsqlConnection connection, NpgsqlTransaction transaction, LedgerAccountingPeriod period,
        CancellationToken ct)
    {
        if (period.LedgerBookId is not { } bookId)
            return; // Legacy book-less periods have no shared opening-history population.
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = $"""
            select period_id from {Qualified("accounting_periods")}
            where ledger_book_id = @ledger_book_id and start_date <= @end_date
            order by start_date, period_id for share;
            """;
        command.Parameters.AddWithValue("ledger_book_id", bookId);
        command.Parameters.AddWithValue("end_date", period.EndDate);
        await using var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await reader.ReadAsync(ct).ConfigureAwait(false))
        { }
    }

    private async Task SaveCloseSnapshotAsync(
        NpgsqlConnection connection, NpgsqlTransaction transaction, LedgerAccountingPeriod period,
        PeriodCloseEventRecord closeEvent, CancellationToken ct)
    {
        var query = period.LedgerBookId is { } bookId
            ? new LedgerJournalEntryQuery(LedgerBookId: bookId, EffectiveTo: period.EndDate)
            : new LedgerJournalEntryQuery(PeriodId: period.PeriodId);
        var entries = await QueryAsync(connection, transaction, query, null, ct).ConfigureAwait(false);
        var snapshot = LedgerPeriodCloseSnapshot.Capture(period, closeEvent, entries);
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = $"""
            insert into {Qualified("period_close_snapshots")}
                (period_id, period_version, ledger_book_id, close_event_id, closed_at, payload_json, content_hash_sha256)
            values (@period_id, @version, @book_id, @event_id, @closed_at, @payload, @hash);
            """;
        command.Parameters.AddWithValue("period_id", snapshot.PeriodId);
        command.Parameters.AddWithValue("version", snapshot.PeriodVersion);
        command.Parameters.AddWithValue("book_id", (object?)snapshot.LedgerBookId ?? DBNull.Value);
        command.Parameters.AddWithValue("event_id", snapshot.CloseEventId);
        command.Parameters.AddWithValue("closed_at", snapshot.ClosedAt.UtcDateTime);
        command.Parameters.AddWithValue("payload", snapshot.PayloadJson);
        command.Parameters.AddWithValue("hash", snapshot.ContentHashSha256);
        await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }
}
