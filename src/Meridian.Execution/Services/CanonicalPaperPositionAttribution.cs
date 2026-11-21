using Meridian.Ledger;
using Meridian.Storage.Ledger;

namespace Meridian.Execution.Services;

internal sealed record CanonicalPaperRunAttribution(string? RunId, IReadOnlyList<Guid> FillIds);

/// <summary>Replays retained quantity mutations solely to preserve run ownership across recovery.</summary>
internal static class CanonicalPaperPositionAttribution
{
    internal static CanonicalPaperRunAttribution Recover(LedgerReportingSnapshot snapshot,
        LedgerAccount account, Guid securityId, Guid positionId, IReadOnlyList<LedgerTaxLotRecord> currentLots,
        IReadOnlyDictionary<Guid, AtomicTaxLotJournalResult> trustedReceipts)
    {
        var population = snapshot.Journals.Where(record =>
            record.Entry.Metadata.SecurityId == securityId
            && string.Equals(record.Entry.Metadata.Symbol, account.Symbol, StringComparison.OrdinalIgnoreCase)
            && string.Equals(record.Entry.Metadata.FinancialAccountId, account.FinancialAccountId, StringComparison.OrdinalIgnoreCase)
            && record.Entry.Metadata.Tags?.GetValueOrDefault("sourceEventType") == "execution.trade-fill")
            .OrderBy(static record => record.GlobalSequence).ToArray();
        if (population.Length == 0 || population.Any(static record => record.GlobalSequence <= 0)
            || population.Select(static record => record.GlobalSequence).Distinct().Count() != population.Length)
            return Unknown();
        if (trustedReceipts.Values.GroupBy(static receipt => receipt.Journal.Entry.JournalEntryId).Any(static group => group.Count() != 1))
            return Unknown();
        var byJournal = trustedReceipts.Values.ToDictionary(static receipt => receipt.Journal.Entry.JournalEntryId);
        var quantities = new Dictionary<Guid, decimal>();
        var fillIds = new HashSet<Guid>();
        string? runId = null;
        foreach (var record in population)
        {
            if (!byJournal.TryGetValue(record.Entry.JournalEntryId, out var receipt)
                || receipt.Mutations.Count == 0 || record.Entry.Metadata.FillId is not { } fillId || fillId == Guid.Empty)
                return Unknown();
            var before = quantities.Values.Sum();
            foreach (var mutation in receipt.Mutations.OrderBy(static item => item.SelectionOrdinal))
            {
                if (mutation.MutationBatchId != receipt.MutationBatchId
                    || mutation.MutationKind != receipt.MutationKind
                    || mutation.JournalEntryId != record.Entry.JournalEntryId
                    || mutation.SecurityId != securityId || mutation.BookPositionId != positionId
                    || mutation.LotAfter.Account != account || mutation.TaxLotRecordId != mutation.LotAfter.TaxLotRecordId
                    || mutation.LotAfter.SecurityId != securityId || mutation.LotAfter.BookPositionId != positionId
                    || mutation.QuantityBefore < 0m || mutation.QuantityAfter < 0m
                    || receipt.MutationKind == AtomicTaxLotMutationKind.Acquisition && (mutation.QuantityBefore != 0m || mutation.QuantityDelta <= 0m)
                    || receipt.MutationKind == AtomicTaxLotMutationKind.Disposal && mutation.QuantityDelta >= 0m
                    || mutation.QuantityBefore + mutation.QuantityDelta != mutation.QuantityAfter
                    || mutation.LotAfter.OpenQuantity != mutation.QuantityAfter
                    || quantities.GetValueOrDefault(mutation.TaxLotRecordId) != mutation.QuantityBefore)
                    return Unknown();
                quantities[mutation.TaxLotRecordId] = mutation.QuantityAfter;
            }
            var after = quantities.Values.Sum();
            LedgerRunAttribution.TryGetTrustedRunId(record.Entry.Metadata, out var observedRun);
            if (after == 0m)
            {
                runId = null;
                fillIds.Clear();
            }
            else
            {
                if (before == 0m) runId = observedRun;
                else if (!string.Equals(runId, observedRun, StringComparison.Ordinal)) runId = null;
                fillIds.Add(fillId);
            }
        }
        if (quantities.Values.Sum() != currentLots.Sum(static lot => lot.OpenQuantity)
            || currentLots.Any(lot => quantities.GetValueOrDefault(lot.TaxLotRecordId) != lot.OpenQuantity)
            || quantities.Any(item => item.Value != 0m && !currentLots.Any(lot => lot.TaxLotRecordId == item.Key)))
            return Unknown();
        return new(runId, Array.AsReadOnly(fillIds.Order().ToArray()));

        static CanonicalPaperRunAttribution Unknown() => new(null, []);
    }
}
