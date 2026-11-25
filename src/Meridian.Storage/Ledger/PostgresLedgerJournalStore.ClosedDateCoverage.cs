using System.Globalization;
using Meridian.Ledger;
using Npgsql;

namespace Meridian.Storage.Ledger;

public sealed partial class PostgresLedgerJournalStore
{
    /// <summary>
    /// Rejects a posting whose date is already covered by a different hard-closed period of the
    /// same book. Periods of one book may overlap (for example an adjustment period), and the
    /// period guard only inspects the period the caller names, so without this check an open
    /// overlapping period would admit new activity into dates a hard close has finalized.
    /// Called after the audit-head write lock shared by appends and period saves: READ COMMITTED
    /// sees a preceding close, and SERIALIZABLE aborts a snapshot that predates one.
    /// </summary>
    private async Task ValidatePostingDateNotHardClosedElsewhereAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        LedgerJournalEntryWrite write,
        LedgerAccountingPeriod period,
        CancellationToken ct)
    {
        // Legacy periods without a book have no shared calendar to compare against.
        if (period.LedgerBookId is not { } bookId)
            return;

        var postingDate = DateOnly.FromDateTime(write.Entry.Timestamp.UtcDateTime);
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = $"""
            select label, start_date, end_date
            from {Qualified("accounting_periods")}
            where ledger_book_id = @book
              and period_id <> @period
              and status = 'HardClosed'
              and start_date <= @posting_date
              and end_date >= @posting_date
            order by start_date, period_id
            limit 1;
            """;
        command.Parameters.AddWithValue("book", bookId);
        command.Parameters.AddWithValue("period", period.PeriodId);
        command.Parameters.AddWithValue("posting_date", postingDate);

        await using var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false);
        if (!await reader.ReadAsync(ct).ConfigureAwait(false))
            return;

        var closedLabel = reader.GetString(0);
        var closedStart = DateOnly.FromDateTime(reader.GetDateTime(1));
        var closedEnd = DateOnly.FromDateTime(reader.GetDateTime(2));
        throw new LedgerValidationException(
            $"Journal entry '{write.Entry.JournalEntryId}' posting date '{postingDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)}' "
            + $"falls inside hard-closed accounting period '{closedLabel}' "
            + $"({closedStart.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)} to {closedEnd.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)}); "
            + $"overlapping period '{period.Label}' cannot accept postings for dates that close has finalized. "
            + "Date the entry in an open period outside the closed range, or use a governed reopen.");
    }
}
