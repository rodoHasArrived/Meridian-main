using System.Text.Json;
using Meridian.Contracts.Accounting.Lots;
using Meridian.Ledger;
using Npgsql;

namespace Meridian.Storage.Ledger;

/// <summary>
/// Atomic average-cost relief. The disposal books the pooled basis the canonical guard certified,
/// and every surviving lot in the pool is restated to the pooled basis in the same transaction, so
/// the lots of record still tie to the asset account the journal credited. Restatements are
/// governed basis adjustments: acquisition facts never change.
/// </summary>
public sealed partial class PostgresLedgerJournalStore
{
    private const string AverageCostLotColumns =
        """
        tax_lot_record_id, ledger_book_id, account_name, account_type, symbol, financial_account_id,
        lot_id, acquired_date, original_quantity, open_quantity, unit_cost, currency, source_journal_entry_id,
        evidence_ref, version, originating_mutation_batch_id, last_mutation_batch_id, created_at, updated_at,
        security_id, book_position_id, original_face, booked_factor, par_basis, acquisition_terms,
        basis_adjustment
        """;

    private static bool IsAverageCostRelief(AtomicTaxLotJournalCommand command)
        => Enum.TryParse<LedgerTaxLotReliefMethod>(command.ReliefMethod, ignoreCase: true, out var method) &&
           method == LedgerTaxLotReliefMethod.AverageCost;

    private static void AddBasisAdjustmentParameter(NpgsqlCommand command, OpenLotBasisAdjustmentDto? adjustment)
        => command.Parameters.AddWithValue(
            "basis_adjustment",
            NpgsqlTypes.NpgsqlDbType.Jsonb,
            adjustment is null ? DBNull.Value : JsonSerializer.Serialize(adjustment));

    private async Task<IReadOnlyList<LedgerTaxLotMutationRecord>> RestateAverageCostSurvivorsAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        AtomicTaxLotJournalCommand command,
        AverageCostReliefPlan plan,
        DateTimeOffset recordedAt,
        CancellationToken ct)
    {
        var selections = command.DisposalSelections!;
        var evidenceId = selections.OrderBy(static selection => selection.SelectionOrdinal).First()
            .SelectionEvidenceId.Trim();
        var mutations = new List<LedgerTaxLotMutationRecord>(plan.UnselectedSurvivors.Count);
        for (var index = 0; index < plan.UnselectedSurvivors.Count; index++)
        {
            var before = plan.UnselectedSurvivors[index];
            var adjustment = plan.AdjustmentFor(before.TaxLotRecordId)
                ?? throw new LedgerValidationException("Average-cost survivor lacks its governed restatement.");

            await using var update = connection.CreateCommand();
            update.Transaction = transaction;
            update.CommandText =
                $"""
                update {Qualified("tax_lots")}
                set version = version + 1,
                    last_mutation_batch_id = @last_mutation_batch_id,
                    updated_at = @updated_at,
                    basis_adjustment = @basis_adjustment
                where tax_lot_record_id = @tax_lot_record_id
                  and ledger_book_id = @ledger_book_id
                  and version = @expected_version
                  and open_quantity = @expected_open_quantity
                returning {AverageCostLotColumns};
                """;
            update.Parameters.AddWithValue("last_mutation_batch_id", command.MutationBatchId);
            update.Parameters.AddWithValue("updated_at", recordedAt.UtcDateTime);
            update.Parameters.AddWithValue("tax_lot_record_id", before.TaxLotRecordId);
            update.Parameters.AddWithValue("ledger_book_id", command.LedgerBookId);
            update.Parameters.AddWithValue("expected_version", before.Version);
            update.Parameters.AddWithValue("expected_open_quantity", before.OpenQuantity);
            AddBasisAdjustmentParameter(update, adjustment);

            LedgerTaxLotRecord after;
            await using (var reader = await update.ExecuteReaderAsync(ct).ConfigureAwait(false))
            {
                if (!await reader.ReadAsync(ct).ConfigureAwait(false))
                {
                    throw new LedgerValidationException(
                        $"Average-cost pool lot '{before.TaxLotRecordId}' changed before its restatement compare-and-swap completed.");
                }

                after = ReadTaxLot(reader);
            }

            mutations.Add(BuildMutationRecord(
                command,
                after,
                before,
                selections.Count + index,
                quantityDelta: 0m,
                before.Version,
                evidenceId,
                recordedAt,
                costBasis: adjustment.FunctionalCostBasis,
                mutationKind: AtomicTaxLotMutationKind.BasisRedistribution));
        }

        return mutations;
    }
}

/// <summary>
/// The survivor restatement an average-cost disposal commits. Remaining pool basis is the pooled
/// canonical open basis less the certified relief, allocated across surviving open quantity with
/// any decimal residual on the last survivor so the pool conserves exactly.
/// </summary>
internal sealed class AverageCostReliefPlan
{
    private readonly IReadOnlyDictionary<Guid, OpenLotBasisAdjustmentDto> _adjustments;

    private AverageCostReliefPlan(
        IReadOnlyDictionary<Guid, OpenLotBasisAdjustmentDto> adjustments,
        IReadOnlyList<LedgerTaxLotRecord> unselectedSurvivors)
    {
        _adjustments = adjustments;
        UnselectedSurvivors = unselectedSurvivors;
    }

    /// <summary>Open pool lots the disposal does not touch; each is restated by its own mutation.</summary>
    public IReadOnlyList<LedgerTaxLotRecord> UnselectedSurvivors { get; }

    public OpenLotBasisAdjustmentDto? AdjustmentFor(Guid taxLotRecordId)
        => _adjustments.TryGetValue(taxLotRecordId, out var adjustment) ? adjustment : null;

    public static AverageCostReliefPlan Build(
        Guid mutationBatchId,
        IReadOnlyList<LedgerTaxLotRecord> openLots,
        IReadOnlyList<LedgerTaxLotDisposalSelection> selections,
        OpenLotReliefResultDto certifiedRelief)
    {
        var pool = openLots
            .OrderBy(static lot => lot.AcquiredDate)
            .ThenBy(static lot => lot.TaxLotRecordId)
            .ToArray();
        var canonical = pool.Select(static lot => lot.ToOpenLot()).ToArray();
        var relievedByLot = selections.ToDictionary(
            static selection => selection.TaxLotRecordId,
            static selection => selection.Quantity);
        var remainingTransaction = canonical.Sum(static lot => lot.OpenTransactionCostBasis) - certifiedRelief.TransactionCostBasis;
        var remainingFunctional = canonical.Sum(static lot => lot.OpenFunctionalCostBasis) - certifiedRelief.FunctionalCostBasis;
        var survivors = pool
            .Select(lot => (Lot: lot, OpenQuantity: lot.OpenQuantity - relievedByLot.GetValueOrDefault(lot.TaxLotRecordId)))
            .Where(static item => item.OpenQuantity > 0m)
            .ToArray();
        if (remainingTransaction < 0m || remainingFunctional < 0m ||
            (survivors.Length == 0 && (remainingTransaction != 0m || remainingFunctional != 0m)))
        {
            throw new LedgerValidationException(
                "Average-cost relief does not conserve the pooled canonical basis.");
        }

        var survivingQuantity = survivors.Sum(static item => item.OpenQuantity);
        var adjustments = new Dictionary<Guid, OpenLotBasisAdjustmentDto>(survivors.Length);
        decimal allocatedTransaction = 0m, allocatedFunctional = 0m;
        for (var index = 0; index < survivors.Length; index++)
        {
            var (lot, openQuantity) = survivors[index];
            var last = index == survivors.Length - 1;
            var transaction = last
                ? remainingTransaction - allocatedTransaction
                : remainingTransaction * openQuantity / survivingQuantity;
            var functional = last
                ? remainingFunctional - allocatedFunctional
                : remainingFunctional * openQuantity / survivingQuantity;
            allocatedTransaction += transaction;
            allocatedFunctional += functional;
            adjustments[lot.TaxLotRecordId] = new OpenLotBasisAdjustmentDto(
                mutationBatchId,
                OpenLotBasisAdjustmentReasons.AverageCostRedistribution,
                openQuantity,
                transaction,
                functional);
        }

        var unselected = survivors
            .Where(item => !relievedByLot.ContainsKey(item.Lot.TaxLotRecordId))
            .Select(static item => item.Lot)
            .ToArray();
        return new AverageCostReliefPlan(adjustments, unselected);
    }
}
