using Meridian.Ledger;
using Npgsql;

namespace Meridian.Storage.Ledger;

/// <summary>
/// A retained disposal as durable history, without the two facts that live on the journal rather
/// than in tax-lot storage: the sale's effective date and the gain or loss actually booked. The
/// caller supplies those from the journal entries it already holds, which keeps the rebuilt rows
/// tied to the same journals a report pack was certified over.
/// </summary>
public sealed record LedgerTaxLotDisposalHistoryRecord(
    Guid MutationBatchId,
    Guid JournalEntryId,
    LedgerAccount Account,
    LedgerTaxLotReliefMethod ReliefMethod,
    IReadOnlyList<LedgerTaxLotDisposalHistoryLot> Lots,
    IReadOnlyList<WashSaleBasisIncrease> WashSaleBasisIncreases,
    decimal MatchedReplacementQuantity);

/// <summary>
/// Reads retained tax-lot disposal history so realized-gain reporting can be rebuilt from the
/// durable record instead of requiring the original in-memory projection to have been kept.
/// </summary>
public interface ILedgerTaxLotDisposalHistory
{
    /// <summary>
    /// Returns the disposals recorded against <paramref name="journalEntryIds"/>, one entry per
    /// atomic tax-lot batch, ordered by batch. Journal ids that produced no disposal are absent.
    /// </summary>
    Task<IReadOnlyList<LedgerTaxLotDisposalHistoryRecord>> GetTaxLotDisposalHistoryAsync(
        Guid ledgerBookId,
        IReadOnlyList<Guid> journalEntryIds,
        CancellationToken ct = default);
}

public sealed partial class PostgresLedgerJournalStore : ILedgerTaxLotDisposalHistory
{
    /// <inheritdoc />
    public async Task<IReadOnlyList<LedgerTaxLotDisposalHistoryRecord>> GetTaxLotDisposalHistoryAsync(
        Guid ledgerBookId,
        IReadOnlyList<Guid> journalEntryIds,
        CancellationToken ct = default)
    {
        if (ledgerBookId == Guid.Empty)
        {
            throw new ArgumentException("Ledger book id is required.", nameof(ledgerBookId));
        }

        ArgumentNullException.ThrowIfNull(journalEntryIds);
        var journalIds = journalEntryIds.Where(static id => id != Guid.Empty).Distinct().ToArray();
        if (journalIds.Length == 0)
        {
            return [];
        }

        await using var connection = await OpenConnectionAsync(ct).ConfigureAwait(false);
        var lotsByBatch = await LoadDisposalLotsAsync(connection, ledgerBookId, journalIds, ct).ConfigureAwait(false);
        if (lotsByBatch.Count == 0)
        {
            return [];
        }

        var deferralsByBatch = await LoadDeferralsByBatchAsync(
                connection,
                ledgerBookId,
                lotsByBatch.Keys.ToArray(),
                ct)
            .ConfigureAwait(false);

        return lotsByBatch
            .Select(batch =>
            {
                var hasDeferrals = deferralsByBatch.TryGetValue(batch.Key, out var retained);
                return new LedgerTaxLotDisposalHistoryRecord(
                    batch.Key,
                    batch.Value.JournalEntryId,
                    batch.Value.Account,
                    batch.Value.ReliefMethod,
                    batch.Value.Lots,
                    hasDeferrals ? retained.Increases : Array.Empty<WashSaleBasisIncrease>(),
                    hasDeferrals ? retained.MatchedQuantity : 0m);
            })
            .OrderBy(static record => record.MutationBatchId)
            .ToArray();
    }

    private async Task<Dictionary<Guid, DisposalBatchAccumulator>> LoadDisposalLotsAsync(
        NpgsqlConnection connection,
        Guid ledgerBookId,
        Guid[] journalIds,
        CancellationToken ct)
    {
        await using var command = connection.CreateCommand();
        command.CommandText =
            $"""
            select batch.mutation_batch_id,
                   batch.journal_entry_id,
                   batch.relief_method,
                   mutation.lot_id,
                   mutation.quantity_delta,
                   mutation.unit_cost,
                   mutation.cost_basis,
                   lot.acquired_date,
                   lot.account_name,
                   lot.account_type,
                   lot.symbol,
                   lot.financial_account_id,
                   (select min(carried.holding_period_carry_date)
                    from {Qualified("wash_sale_deferrals")} carried
                    where carried.replacement_tax_lot_record_id = mutation.tax_lot_record_id) as holding_period_start
            from {Qualified("tax_lot_mutations")} mutation
            join {Qualified("atomic_tax_lot_posting_batches")} batch
              on batch.mutation_batch_id = mutation.mutation_batch_id
            join {Qualified("tax_lots")} lot
              on lot.tax_lot_record_id = mutation.tax_lot_record_id
            where batch.ledger_book_id = @ledger_book_id
              and mutation.mutation_kind = 'Disposal'
              and batch.journal_entry_id = any(@journal_entry_ids)
            order by batch.mutation_batch_id, mutation.selection_ordinal;
            """;
        command.Parameters.AddWithValue("ledger_book_id", ledgerBookId);
        command.Parameters.AddWithValue("journal_entry_ids", journalIds);

        var batches = new Dictionary<Guid, DisposalBatchAccumulator>();
        await using var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await reader.ReadAsync(ct).ConfigureAwait(false))
        {
            var batchId = reader.GetGuid(0);
            var acquiredDate = DateOnly.FromDateTime(reader.GetDateTime(7));

            // A disposal mutation records a negative quantity delta; relief works in positive
            // relieved quantities.
            var quantity = Math.Abs(reader.GetDecimal(4));

            // A carried holding-period start earlier than acquisition means an earlier wash sale
            // capitalized into this lot; anything else leaves the period starting at acquisition.
            var carriedStart = reader.IsDBNull(12) ? (DateOnly?)null : DateOnly.FromDateTime(reader.GetDateTime(12));
            var holdingPeriodStart = carriedStart is { } carried && carried < acquiredDate ? carried : acquiredDate;

            var lot = new LedgerTaxLotDisposalHistoryLot(
                reader.GetString(3),
                acquiredDate,
                holdingPeriodStart,
                quantity,
                reader.GetDecimal(5),
                reader.GetDecimal(6));

            if (batches.TryGetValue(batchId, out var accumulator))
            {
                accumulator.Lots.Add(lot);
                continue;
            }

            // relief_method is nullable on the batch; a disposal without one predates the
            // authoritative relief-method requirement, so FIFO is assumed for presentation only.
            var reliefMethod = reader.IsDBNull(2)
                || !Enum.TryParse<LedgerTaxLotReliefMethod>(reader.GetString(2), ignoreCase: true, out var parsed)
                    ? LedgerTaxLotReliefMethod.Fifo
                    : parsed;

            batches[batchId] = new DisposalBatchAccumulator(
                reader.GetGuid(1),
                ReadLedgerAccount(reader, 8),
                reliefMethod,
                new List<LedgerTaxLotDisposalHistoryLot> { lot });
        }

        return batches;
    }

    private async Task<Dictionary<Guid, (IReadOnlyList<WashSaleBasisIncrease> Increases, decimal MatchedQuantity)>>
        LoadDeferralsByBatchAsync(
            NpgsqlConnection connection,
            Guid ledgerBookId,
            Guid[] batchIds,
            CancellationToken ct)
    {
        await using var command = connection.CreateCommand();
        command.CommandText =
            $"""
            select disposal_mutation_batch_id,
                   replacement_lot_id,
                   disallowed_amount,
                   matched_quantity,
                   holding_period_carry_date
            from {Qualified("wash_sale_deferrals")}
            where ledger_book_id = @ledger_book_id
              and disposal_mutation_batch_id = any(@batch_ids)
            order by disposal_mutation_batch_id, replacement_lot_id;
            """;
        command.Parameters.AddWithValue("ledger_book_id", ledgerBookId);
        command.Parameters.AddWithValue("batch_ids", batchIds);

        var byBatch = new Dictionary<Guid, (List<WashSaleBasisIncrease> Increases, decimal MatchedQuantity)>();
        await using var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await reader.ReadAsync(ct).ConfigureAwait(false))
        {
            var batchId = reader.GetGuid(0);
            var increase = new WashSaleBasisIncrease(
                reader.GetString(1),
                reader.GetDecimal(2),
                DateOnly.FromDateTime(reader.GetDateTime(4)));

            if (byBatch.TryGetValue(batchId, out var existing))
            {
                existing.Increases.Add(increase);
                continue;
            }

            // Every deferral row from one disposal records the same matched quantity, so the first
            // row establishes it rather than the rows summing to a multiple of it.
            byBatch[batchId] = (new List<WashSaleBasisIncrease> { increase }, reader.GetDecimal(3));
        }

        return byBatch.ToDictionary(
            static entry => entry.Key,
            static entry => ((IReadOnlyList<WashSaleBasisIncrease>)entry.Value.Increases, entry.Value.MatchedQuantity));
    }

    private sealed record DisposalBatchAccumulator(
        Guid JournalEntryId,
        LedgerAccount Account,
        LedgerTaxLotReliefMethod ReliefMethod,
        List<LedgerTaxLotDisposalHistoryLot> Lots);
}
