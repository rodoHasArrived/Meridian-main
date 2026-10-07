using System.Globalization;
using Meridian.Ledger;
using Npgsql;

namespace Meridian.Storage.Ledger;

public sealed partial class PostgresLedgerJournalStore
{
    private async Task ValidateCorporateActionAcquisitionChronologyAsync(
        NpgsqlConnection connection, NpgsqlTransaction transaction, LedgerTaxLotRecord lot, CancellationToken ct)
    {
        if (lot.BookPositionId == Guid.Empty)
            return;

        // Both callers are serializable. This read of retained transformations pairs with the
        // corporate-action transaction's read of the complete lot inventory: concurrent writes
        // in opposite directions cannot both commit from snapshots that omit the other write.
        // A transaction advisory lock acquired after a snapshot would not provide that guarantee.
        await using var query = connection.CreateCommand();
        query.Transaction = transaction;
        query.CommandText = $"""
            select j.metadata ->> 'effectiveDate'
            from {Qualified("atomic_tax_lot_posting_batches")} b
            left join {Qualified("journal_entries")} j on j.journal_entry_id = b.journal_entry_id
            where b.ledger_book_id = @book and b.book_position_id = @position
                and b.mutation_kind = 'CorporateAction';
            """;
        query.Parameters.AddWithValue("book", lot.LedgerBookId);
        query.Parameters.AddWithValue("position", lot.BookPositionId);
        await using var reader = await query.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await reader.ReadAsync(ct).ConfigureAwait(false))
        {
            if (reader.IsDBNull(0) || !DateOnly.TryParseExact(reader.GetString(0), "yyyy-MM-dd",
                    CultureInfo.InvariantCulture, DateTimeStyles.None, out var actionDate))
                throw new LedgerValidationException("Retained corporate-action inventory requires an authoritative effective date.");
            if (lot.AcquiredDate <= actionDate)
                throw new LedgerValidationException("A source-position lot cannot be inserted or rewritten at or before a retained corporate action; the complete affected inventory has already been transformed.");
        }
    }
}
