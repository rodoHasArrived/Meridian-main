using Meridian.Contracts.Accounting.Lots;
using Meridian.Ledger;
using Npgsql;

namespace Meridian.Storage.Ledger;

public sealed partial class PostgresLedgerJournalStore
{
    // This verifies retained date-copy relationships, not historical tax calculations. Bound the
    // evidence graph so a corrupt cycle or unbounded legacy chain cannot monopolize a detail read.
    private const int MaximumHoldingPeriodCarryDepth = 32;
    private const int MaximumHoldingPeriodCarryBatches = 1024;

    private async Task CertifyCarriedHoldingPeriodsAsync(NpgsqlConnection connection, NpgsqlTransaction? transaction,
        Guid ledgerBookId,
        IReadOnlyDictionary<Guid, DisposalBatchAccumulator> batches, CancellationToken ct)
    {
        var known = batches.ToDictionary(static batch => batch.Key, static batch => batch.Value);
        var evidenceByBatch = new Dictionary<Guid, IReadOnlyList<CarriedHoldingPeriodEvidence>>();
        var pending = batches.ToDictionary(static batch => batch.Key, static batch => batch.Value);
        for (var depth = 0; pending.Count > 0; depth++)
        {
            if (depth > MaximumHoldingPeriodCarryDepth || known.Count > MaximumHoldingPeriodCarryBatches)
                throw new LedgerValidationException("Retained holding-period carry exceeds the bounded evidence verification limit.");
            var evidence = await LoadCarriedHoldingPeriodEvidenceAsync(connection, transaction, pending, ct).ConfigureAwait(false);
            foreach (var batchId in pending.Keys)
                evidenceByBatch[batchId] = evidence.Where(item => item.TargetBatchId == batchId).ToArray();
            var missing = evidence.Where(item => !known.ContainsKey(item.SourceBatchId)).ToArray();
            if (missing.Length == 0)
                break;
            var sourceIds = missing.Select(static item => item.SourceBatchId).Distinct().ToArray();
            if (known.Count + sourceIds.Length > MaximumHoldingPeriodCarryBatches)
                throw new LedgerValidationException("Retained holding-period carry exceeds the bounded evidence verification limit.");
            var sources = await LoadDisposalLotsAsync(connection, transaction, ledgerBookId,
                missing.Select(static item => item.SourceJournalId).Distinct().ToArray(), ct).ConfigureAwait(false);
            pending = new Dictionary<Guid, DisposalBatchAccumulator>();
            foreach (var sourceId in sourceIds)
            {
                if (!sources.TryGetValue(sourceId, out var source))
                    throw new LedgerValidationException("Retained holding-period carry lacks its authoritative source disposal.");
                known[sourceId] = source;
                pending[sourceId] = source;
            }
        }

        var certified = new Dictionary<Guid, IReadOnlyDictionary<Guid, DateOnly>>();
        var certifiedDepths = new Dictionary<Guid, int>();
        var visiting = new HashSet<Guid>();
        foreach (var batchId in batches.Keys)
            CertifyBatch(batchId, 0);

        IReadOnlyDictionary<Guid, DateOnly> CertifyBatch(Guid batchId, int depth)
        {
            if (depth > MaximumHoldingPeriodCarryDepth)
                throw new LedgerValidationException("Retained holding-period carry exceeds the bounded evidence verification limit.");
            if (certified.TryGetValue(batchId, out var dates))
            {
                if (depth + certifiedDepths[batchId] > MaximumHoldingPeriodCarryDepth)
                    throw new LedgerValidationException("Retained holding-period carry exceeds the bounded evidence verification limit.");
                return dates;
            }
            if (!visiting.Add(batchId))
                throw new LedgerValidationException("Retained holding-period carry has a cycle or exceeds the bounded evidence verification limit.");
            var target = known[batchId];
            var effectiveDates = new Dictionary<Guid, DateOnly>();
            var carryDepth = 0;
            foreach (var lot in target.CanonicalLots)
            {
                OpenLotValidation.Validate(lot);
                effectiveDates[lot.TaxLotRecordId] = lot.Acquisition.HoldingPeriodStartDate;
            }
            foreach (var item in evidenceByBatch[batchId])
            {
                var recipient = target.CanonicalLots.Single(lot => lot.TaxLotRecordId == item.TargetLotId);
                var source = known[item.SourceBatchId];
                if (item.LedgerBookId != ledgerBookId ||
                    item.SecurityId != recipient.SecurityId || item.SecurityId != source.SecurityId ||
                    item.SaleDate != source.SaleDate || item.DisposalAccount != source.Account ||
                    string.IsNullOrWhiteSpace(source.PolicyRevision) ||
                    !string.Equals(item.PolicyId, source.PolicyRevision, StringComparison.Ordinal) ||
                    !string.Equals(item.ReplacementLotId, recipient.LotId, StringComparison.OrdinalIgnoreCase) ||
                    item.WindowDays < 0 || !Enum.IsDefined(item.Scope) ||
                    Math.Abs(recipient.AcquiredDate.DayNumber - source.SaleDate.DayNumber) > item.WindowDays ||
                    (item.Scope == WashSaleReplacementScope.DisposingAccount && target.Account != source.Account) ||
                    source.CanonicalLots.Any(lot => lot.TaxLotRecordId == item.TargetLotId))
                    throw new LedgerValidationException("Retained holding-period carry does not match its source disposal, recipient lot, or replacement window.");

                // Retention omits source-to-recipient parcel allocations. Only a common source
                // holding date can certify this recipient without choosing an invented allocation.
                var sourceDates = CertifyBatch(item.SourceBatchId, depth + 1).Values.Distinct().ToArray();
                carryDepth = Math.Max(carryDepth, certifiedDepths[item.SourceBatchId] + 1);
                if (sourceDates.Length != 1 || sourceDates[0] != item.HoldingPeriodCarryDate)
                    throw new LedgerValidationException("Retained holding-period carry date does not match unambiguous source parcel holding-period evidence.");
                if (item.HoldingPeriodCarryDate < effectiveDates[item.TargetLotId])
                    effectiveDates[item.TargetLotId] = item.HoldingPeriodCarryDate;
            }
            visiting.Remove(batchId);
            certifiedDepths[batchId] = carryDepth;
            return certified[batchId] = effectiveDates;
        }
    }
}
