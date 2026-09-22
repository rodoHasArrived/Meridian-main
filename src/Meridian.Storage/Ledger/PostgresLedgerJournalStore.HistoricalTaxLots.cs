using System.Data;
using System.Globalization;
using Meridian.Ledger;
using Npgsql;

namespace Meridian.Storage.Ledger;

public sealed partial class PostgresLedgerJournalStore
{
    /// <inheritdoc />
    public async Task<IReadOnlyList<LedgerTaxLotRecord>> ListOpenTaxLotsByAssetScopeAsync(
        Guid ledgerBookId,
        Guid securityId,
        Guid bookPositionId,
        DateOnly effectiveDate,
        CancellationToken ct = default)
    {
        if (ledgerBookId == Guid.Empty)
        {
            throw new ArgumentException("Ledger book id is required.", nameof(ledgerBookId));
        }

        if (securityId == Guid.Empty)
        {
            throw new ArgumentException("Security Master identity is required.", nameof(securityId));
        }

        if (bookPositionId == Guid.Empty)
        {
            throw new ArgumentException("Book-position identity is required.", nameof(bookPositionId));
        }

        await using var connection = await OpenConnectionAsync(ct).ConfigureAwait(false);
        await using var transaction = await connection.BeginTransactionAsync(IsolationLevel.RepeatableRead, ct).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            $"""
            select tax_lot_record_id,
                   ledger_book_id,
                   account_name,
                   account_type,
                   symbol,
                   financial_account_id,
                   lot_id,
                   acquired_date,
                   original_quantity,
                   open_quantity,
                   unit_cost,
                   currency,
                   source_journal_entry_id,
                   evidence_ref,
                   version,
                   originating_mutation_batch_id,
                   last_mutation_batch_id,
                   created_at,
                   updated_at,
                   security_id,
                   book_position_id,
                   original_face,
                   booked_factor,
                   par_basis,
                   acquisition_terms
            from {Qualified("tax_lots")}
            where ledger_book_id = @ledger_book_id
              and security_id = @security_id
              and book_position_id = @book_position_id
              and acquired_date <= @effective_date
            order by acquired_date, lot_id;
            """;
        command.Parameters.AddWithValue("ledger_book_id", ledgerBookId);
        command.Parameters.AddWithValue("security_id", securityId);
        command.Parameters.AddWithValue("book_position_id", bookPositionId);
        command.Parameters.AddWithValue("effective_date", effectiveDate);

        var lots = new List<LedgerTaxLotRecord>();
        await using (var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false))
        {
            while (await reader.ReadAsync(ct).ConfigureAwait(false))
                lots.Add(ReadTaxLot(reader));
        }

        var mutations = await ReadDatedLotQuantitiesAsync(connection, transaction, ledgerBookId,
            lots.Select(static lot => lot.TaxLotRecordId).ToArray(), ct).ConfigureAwait(false);
        var histories = mutations.ToLookup(static mutation => mutation.TaxLotRecordId);
        var projected = lots.Select(lot => HistoricalTaxLotQuantity.Project(
                lot, histories[lot.TaxLotRecordId].ToArray(), effectiveDate))
            .Where(static lot => lot.OpenQuantity > 0m).ToArray();
        await transaction.CommitAsync(ct).ConfigureAwait(false);
        return projected;
    }

    private async Task<IReadOnlyList<DatedTaxLotQuantityMutation>> ReadDatedLotQuantitiesAsync(
        NpgsqlConnection connection, NpgsqlTransaction transaction, Guid ledgerBookId, Guid[] lotIds, CancellationToken ct)
    {
        if (lotIds.Length == 0)
            return [];
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = $"""
            select m.tax_lot_record_id, m.mutation_batch_id, m.mutation_kind,
                   m.quantity_before, m.quantity_delta, m.quantity_after,
                   m.expected_version, m.result_version, j.metadata ->> 'effectiveDate',
                   m.security_id, m.book_position_id, b.ledger_book_id, j.ledger_book_id
            from {Qualified("tax_lot_mutations")} m
            left join {Qualified("atomic_tax_lot_posting_batches")} b on b.mutation_batch_id = m.mutation_batch_id
            left join {Qualified("journal_entries")} j on j.journal_entry_id = m.journal_entry_id
            where m.tax_lot_record_id = any(@lot_ids)
            order by m.tax_lot_record_id, m.result_version;
            """;
        command.Parameters.AddWithValue("lot_ids", lotIds);
        var mutations = new List<DatedTaxLotQuantityMutation>();
        await using var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await reader.ReadAsync(ct).ConfigureAwait(false))
        {
            if (reader.IsDBNull(11) || reader.GetGuid(11) != ledgerBookId ||
                reader.IsDBNull(12) || reader.GetGuid(12) != ledgerBookId ||
                reader.IsDBNull(8) || !DateOnly.TryParseExact(reader.GetString(8), "yyyy-MM-dd",
                    CultureInfo.InvariantCulture, DateTimeStyles.None, out var effectiveDate) ||
                !Enum.TryParse<AtomicTaxLotMutationKind>(reader.GetString(2), out var kind))
                throw new LedgerValidationException("Historical lot quantity requires a scoped mutation and retained journal effective date.");
            mutations.Add(new(reader.GetGuid(0), reader.GetGuid(1), kind,
                reader.GetDecimal(3), reader.GetDecimal(4), reader.GetDecimal(5),
                reader.GetInt64(6), reader.GetInt64(7), effectiveDate, reader.GetGuid(9), reader.GetGuid(10)));
        }
        return mutations;
    }
}
