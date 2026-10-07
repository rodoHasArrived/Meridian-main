using System.Text.Json;
using Meridian.Contracts.Accounting.Lots;
using Meridian.Contracts.AssetOperations;
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
                reports.AddRange(ReconcileCorporateActionReceipt(receipt, journal, ledgerBookId, functionalCurrency));
            }
            catch (Exception exception) when (exception is NotSupportedException or LedgerValidationException
                or ArgumentException or InvalidOperationException or JsonException or OverflowException)
            {
                throw Unavailable($"Corporate-action journal '{journal.Entry.JournalEntryId:D}' blocks canonical reporting: {exception.Message}");
            }
        }
        return reports;
    }

    private static IReadOnlyList<CanonicalCorporateActionLotReport> ReconcileCorporateActionReceipt(
        AtomicTaxLotJournalResult receipt, LedgerJournalEntryRecord journal, Guid ledgerBookId, string functionalCurrency)
    {
        ArgumentNullException.ThrowIfNull(receipt.Mutations);
        ArgumentNullException.ThrowIfNull(receipt.MutatedLots);
        ArgumentNullException.ThrowIfNull(receipt.RetainedEvidence);
        if (receipt.Mutations.Any(mutation => mutation is null || mutation.LotAfter is null)
            || receipt.MutatedLots.Any(lot => lot is null))
            throw new LedgerValidationException("Corporate-action receipt contains a missing immutable mutation or snapshot.");
        var instruction = CanonicalCorporateActionLotProjection.ReadInstruction(journal.Entry);
        var projected = OpenLotCorporateAction.Project(instruction);
        var groups = OpenLotCorporateAction.Groups(instruction);
        if (receipt.MutationKind != AtomicTaxLotMutationKind.CorporateAction || receipt.MutationBatchId == Guid.Empty
            || receipt.CorrectsMutationBatchId is not null || receipt.ReliefMethod is not null || receipt.PolicyRevision is not null
            || journal.SourceEventId != instruction.CorporateActionId
            || !SameCorporateActionFact(journal, receipt.Journal)
            || receipt.Mutations.Count != groups.Count + projected.Count
            || receipt.Mutations.Any(m => m.MutationRecordId == Guid.Empty)
            || receipt.Mutations.Select(m => m.MutationRecordId).Distinct().Count() != receipt.Mutations.Count
            || receipt.Mutations.Select(m => m.TaxLotRecordId).Distinct().Count() != receipt.Mutations.Count
            || !receipt.Mutations.Select(m => m.SelectionOrdinal).Order().SequenceEqual(Enumerable.Range(0, receipt.Mutations.Count))
            || receipt.RetainedEvidence.Count == 0
            || receipt.RetainedEvidence.Any(e => !RetainedEvidenceIdentityValidator.IsComplete(e))
            || receipt.RetainedEvidence.Select(e => e.EvidenceId.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).Count() != receipt.RetainedEvidence.Count
            || OpenLotCorporateAction.Evidence(instruction).Any(e => !receipt.RetainedEvidence.Contains(e))
            || receipt.MutatedLots.Count != receipt.Mutations.Count
            || receipt.MutatedLots.Select(lot => lot.TaxLotRecordId).Distinct().Count() != receipt.MutatedLots.Count
            || receipt.Mutations.Any(m => !receipt.MutatedLots.Any(lot => SameCorporateActionFact(lot, m.LotAfter))))
            throw new LedgerValidationException("Corporate-action receipt does not bind the exact captured journal, reviewed population and complete immutable evidence.");

        var allowLegacy = groups.Count == 1 && journal.Entry.Lines.All(line => line.Dimensions?.TaxLotId is null);
        var mutations = receipt.Mutations.OrderBy(m => m.SelectionOrdinal).ToArray();
        var recordedAt = mutations[0].RecordedAt;
        if (recordedAt == default)
            throw new LedgerValidationException("Corporate-action receipt is missing its retained batch recording time.");
        var reports = new List<CanonicalCorporateActionLotReport>(groups.Count);
        var ordinal = 0;
        foreach (var group in groups)
        {
            var source = group.ExpectedLot;
            var close = mutations[ordinal++];
            var before = close.LotBefore
                ?? throw new LedgerValidationException("Corporate-action receipt is missing a predecessor snapshot.");
            if (source.LedgerBookId != ledgerBookId || source.Acquisition.FunctionalCurrency != functionalCurrency
                || before.Currency != functionalCurrency || before.Account.ToString() != group.SourceAssetAccountId
                || !SameCorporateActionFact(before.ToOpenLot(), source))
                throw new LedgerValidationException("Corporate-action receipt lacks the reviewed predecessor in the certified book and currency.");
            var expectedClose = before with
            {
                OpenQuantity = 0m,
                Version = checked(source.Version + 1),
                LastMutationBatchId = receipt.MutationBatchId,
                UpdatedAt = recordedAt
            };
            ValidateCorporateActionMutation(close, receipt, instruction, AtomicTaxLotMutationKind.CorporateActionClose,
                expectedClose, before, source.OpenFunctionalCostBasis, instruction.SecurityEvidence.EvidenceId, recordedAt);
            var successors = new List<OpenLotDto>(group.Successors.Count);
            foreach (var item in projected.Where(item => item.PredecessorTaxLotRecordId == source.TaxLotRecordId))
            {
                var mutation = mutations[ordinal++];
                var target = item.Successor;
                var face = source.Acquisition.QuantityBasis == LotQuantityBasis.Face;
                var quantity = target.Quantity / (face ? LedgerTaxLotFaceValueTerms.LedgerLotParBasis : 1m);
                var legs = journal.Entry.Lines.Where(line => line.Debit == item.OpenFunctionalCostBasis && line.Credit == 0m
                    && line.Account.ToString() == target.AssetAccountId
                    && line.Dimensions?.InstrumentId == target.Security.SecurityId && line.Dimensions.PositionId == target.BookPositionId
                    && (string.Equals(line.Dimensions.TaxLotId, target.TaxLotRecordId.ToString("D"), StringComparison.OrdinalIgnoreCase)
                        || (allowLegacy && line.Dimensions.TaxLotId is null))).ToArray();
                if (legs.Length != 1)
                    throw new LedgerValidationException("Corporate-action successor snapshot does not resolve to one reviewed journal account and lot identity.");
                var acquisition = source.Acquisition with
                {
                    TransactionCostBasis = item.AcquisitionTransactionCostBasis,
                    FunctionalCostBasis = item.AcquisitionFunctionalCostBasis,
                    Evidence = OpenLotCorporateAction.Evidence(instruction),
                    CorporateActionLineage = new(instruction.CorporateActionId, instruction.ActionType, instruction.EffectiveDate,
                        source.TaxLotRecordId, source.Version, target.BasisAllocationPercent, target.Role, target.ReportingTags)
                };
                var adjustment = item.AcquisitionTransactionCostBasis != item.OpenTransactionCostBasis
                    || item.AcquisitionFunctionalCostBasis != item.OpenFunctionalCostBasis
                    ? new OpenLotBasisAdjustmentDto(receipt.MutationBatchId, OpenLotBasisAdjustmentReasons.CorporateAction,
                        quantity, item.OpenTransactionCostBasis, item.OpenFunctionalCostBasis) : null;
                var expected = new LedgerTaxLotRecord(target.TaxLotRecordId, ledgerBookId, legs[0].Account, target.LotId,
                    source.AcquiredDate, quantity, quantity, item.AcquisitionFunctionalCostBasis / quantity, before.Currency,
                    recordedAt, recordedAt, journal.Entry.JournalEntryId, target.AcquisitionEvidence.EvidenceId, 1,
                    receipt.MutationBatchId, receipt.MutationBatchId, target.Security.SecurityId, target.BookPositionId,
                    face ? target.Quantity : null, before.BookedFactor, before.ParBasis, acquisition, adjustment);
                ValidateCorporateActionMutation(mutation, receipt, instruction, AtomicTaxLotMutationKind.CorporateActionSuccessor,
                    expected, null, item.OpenFunctionalCostBasis, target.AcquisitionEvidence.EvidenceId, recordedAt);
                successors.Add(mutation.LotAfter.ToOpenLot());
            }
            reports.Add(CanonicalCorporateActionLotProjection.Project(instruction, journal.Entry, before.Account,
                before.ToOpenLot(), close.LotAfter.ToOpenLot(), successors));
        }
        // The caller receives no reports unless every predecessor, successor and mutation reconciles.
        return reports;
    }

    private static void ValidateCorporateActionMutation(LedgerTaxLotMutationRecord mutation, AtomicTaxLotJournalResult receipt,
        OpenLotCorporateActionInstructionDto instruction, AtomicTaxLotMutationKind kind,
        LedgerTaxLotRecord expectedAfter, LedgerTaxLotRecord? expectedBefore, decimal costBasis,
        string selectionEvidenceId, DateTimeOffset recordedAt)
    {
        var quantityBefore = expectedBefore?.OpenQuantity ?? 0m;
        if (mutation.MutationKind != kind || mutation.MutationBatchId != receipt.MutationBatchId
            || mutation.JournalEntryId != receipt.Journal.Entry.JournalEntryId || mutation.SourceEventId != instruction.CorporateActionId
            || mutation.TaxLotRecordId != expectedAfter.TaxLotRecordId || mutation.LotId != expectedAfter.LotId
            || mutation.SecurityId != expectedAfter.SecurityId || mutation.BookPositionId != expectedAfter.BookPositionId
            || mutation.QuantityBefore != quantityBefore || mutation.QuantityAfter != expectedAfter.OpenQuantity
            || mutation.QuantityDelta != expectedAfter.OpenQuantity - quantityBefore
            || mutation.UnitCost != expectedAfter.UnitCost || mutation.CostBasis != costBasis
            || mutation.ExpectedVersion != (expectedBefore?.Version ?? 0) || mutation.ResultVersion != expectedAfter.Version
            || mutation.SelectionEvidenceId != selectionEvidenceId || mutation.RecordedAt != recordedAt
            || mutation.CorrectsMutationBatchId is not null || mutation.ReliefMethod is not null || mutation.PolicyRevision is not null
            || !SameCorporateActionFact(mutation.RetainedEvidence, receipt.RetainedEvidence)
            || !SameCorporateActionFact(mutation.LotBefore, expectedBefore) || !SameCorporateActionFact(mutation.LotAfter, expectedAfter))
            throw new LedgerValidationException("Corporate-action mutation economics, versions, provenance or immutable snapshots differ from the reviewed batch.");
    }

    private static bool SameCorporateActionFact<T>(T first, T second)
        => JsonElement.DeepEquals(JsonSerializer.SerializeToElement(first), JsonSerializer.SerializeToElement(second));
}
