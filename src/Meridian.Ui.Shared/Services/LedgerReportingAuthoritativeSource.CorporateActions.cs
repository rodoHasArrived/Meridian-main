using System.Text.Json;
using Meridian.Ledger;
using Meridian.Reporting;
using Meridian.Storage.Ledger;

namespace Meridian.Ui.Shared.Services;

public sealed partial class LedgerReportingAuthoritativeSource
{
    private async Task<IReadOnlyList<CanonicalCorporateActionLotReport>> BuildCorporateActionLotReportsAsync(
        Guid ledgerBookId,
        IReadOnlyList<LedgerJournalEntryRecord> journals,
        string functionalCurrency,
        LedgerLineDimensionSet selectedDimensions,
        CancellationToken cancellationToken)
    {
        var reports = new List<CanonicalCorporateActionLotReport>();
        foreach (var journal in journals)
        {
            var tags = journal.Entry.Metadata?.Tags;
            if (tags is null || (!tags.ContainsKey("lotCorporateActionHash")
                && !tags.ContainsKey("lotCorporateActionInputs")))
                continue;
            if (!journal.Entry.Lines.Any(line => line.Dimensions is { } dimensions
                && MatchesSelectedDimensions(dimensions, selectedDimensions)))
                continue;

            try
            {
                // A partial journal cannot supply the exact predecessor/successor proof without
                // exposing rows outside the requested dimensional scope.
                if (journal.Entry.Lines.Any(line => line.Dimensions is not { } dimensions
                    || !MatchesSelectedDimensions(dimensions, selectedDimensions)))
                    throw new LedgerValidationException("Corporate-action journal crosses the selected reporting dimensions.");
                var receipt = await _journalStore.GetAtomicTaxLotPostingByJournalAsync(
                    journal.Entry.JournalEntryId, cancellationToken).ConfigureAwait(false)
                    ?? throw new LedgerValidationException("Corporate-action journal has no retained atomic lot receipt.");
                if (receipt.MutationKind != AtomicTaxLotMutationKind.CorporateAction
                    || receipt.MutationBatchId == Guid.Empty
                    || !JsonElement.DeepEquals(JsonSerializer.SerializeToElement(journal),
                        JsonSerializer.SerializeToElement(receipt.Journal))
                    || receipt.Mutations.Count < 2
                    || receipt.Mutations.Count(m => m.MutationKind == AtomicTaxLotMutationKind.CorporateActionClose) != 1
                    || receipt.Mutations.Any(m => m.MutationKind is not (AtomicTaxLotMutationKind.CorporateActionClose
                        or AtomicTaxLotMutationKind.CorporateActionSuccessor)
                        || m.MutationBatchId != receipt.MutationBatchId
                        || m.JournalEntryId != journal.Entry.JournalEntryId
                        || m.SourceEventId != journal.SourceEventId
                        || m.TaxLotRecordId != m.LotAfter.TaxLotRecordId
                        || m.LotAfter.LedgerBookId != ledgerBookId
                        || m.ResultVersion != m.LotAfter.Version)
                    || receipt.Mutations.Select(m => m.TaxLotRecordId).Distinct().Count() != receipt.Mutations.Count)
                    throw new LedgerValidationException("Corporate-action receipt does not bind the exact captured journal and lot mutation identities.");
                var close = receipt.Mutations.Single(m => m.MutationKind == AtomicTaxLotMutationKind.CorporateActionClose);
                var successors = receipt.Mutations.Where(m => m.MutationKind == AtomicTaxLotMutationKind.CorporateActionSuccessor).ToArray();
                if (close.LotBefore is null || close.LotBefore.LedgerBookId != ledgerBookId
                    || close.LotBefore.TaxLotRecordId != close.TaxLotRecordId
                    || close.ExpectedVersion != close.LotBefore.Version
                    || successors.Any(m => m.LotBefore is not null || m.ExpectedVersion != 0)
                    || close.LotBefore.Acquisition?.FunctionalCurrency != functionalCurrency)
                    throw new LedgerValidationException("Corporate-action receipt lacks the canonical before/after snapshots in the certified book and currency.");

                reports.Add(CanonicalCorporateActionLotProjection.Project(journal.Entry, close.LotBefore.Account,
                    close.LotBefore.ToOpenLot(), close.LotAfter.ToOpenLot(),
                    successors.Select(m => m.LotAfter.ToOpenLot()).ToArray()));
            }
            catch (Exception exception) when (exception is NotSupportedException or LedgerValidationException
                or ArgumentException or InvalidOperationException or JsonException)
            {
                throw Unavailable($"Corporate-action journal '{journal.Entry.JournalEntryId:D}' blocks canonical reporting: {exception.Message}");
            }
        }
        return reports;
    }
}
