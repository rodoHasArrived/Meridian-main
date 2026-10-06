using System.Text.Json;
using Meridian.Contracts.Accounting.Lots;
using Meridian.Ledger;

namespace Meridian.Reporting;

/// <summary>Fail-closed reporting adapter for basis-preserving corporate-action lot receipts.</summary>
public static class CanonicalCorporateActionLotProjection
{
    /// <summary>Reconstructs the reviewed instruction from its immutable journal metadata.</summary>
    public static CanonicalCorporateActionLotReport Project(JournalEntry journal, LedgerAccount predecessorAccount,
        OpenLotDto predecessorBefore, OpenLotDto predecessorAfter, IReadOnlyList<OpenLotDto> successorLots)
        => Project(ReadInstruction(journal), journal, predecessorAccount, predecessorBefore, predecessorAfter, successorLots);

    /// <summary>
    /// Verifies the reviewed instruction, retained journal and immutable mutation snapshots together.
    /// Callers must supply the before/after snapshots from the atomic receipt, not mutable current lots.
    /// </summary>
    public static CanonicalCorporateActionLotReport Project(
        OpenLotCorporateActionInstructionDto instruction,
        JournalEntry journal,
        LedgerAccount predecessorAccount,
        OpenLotDto predecessorBefore,
        OpenLotDto predecessorAfter,
        IReadOnlyList<OpenLotDto> successorLots)
    {
        ArgumentNullException.ThrowIfNull(journal);
        ArgumentNullException.ThrowIfNull(predecessorAccount);
        ArgumentNullException.ThrowIfNull(successorLots);
        IReadOnlyList<OpenLotCorporateActionSuccessorProjectionDto> projected;
        try
        { projected = OpenLotCorporateAction.Project(instruction); }
        catch (ArgumentException ex) { throw new LedgerValidationException($"Retained corporate-action instruction is invalid: {ex.Message}"); }

        var source = instruction.ExpectedLot;
        var closed = source with
        {
            OpenQuantity = 0m,
            OpenTransactionCostBasis = 0m,
            OpenFunctionalCostBasis = 0m,
            Version = source.Version + 1
        };
        if (predecessorAccount.ToString() != instruction.SourceAssetAccountId
            || !Same(source, predecessorBefore) || !Same(closed, predecessorAfter)
            || successorLots.Count != projected.Count
            || successorLots.Select(lot => lot.TaxLotRecordId).Distinct().Count() != successorLots.Count)
            throw new LedgerValidationException("Retained corporate-action predecessor or successor snapshots do not reproduce the reviewed lot transition.");
        if (journal.Metadata?.EffectiveDate != instruction.EffectiveDate
            || journal.Metadata.Tags is null
            || !journal.Metadata.Tags.TryGetValue("lotCorporateActionHash", out var instructionHash)
            || instructionHash != OpenLotCorporateAction.Fingerprint(instruction)
            || journal.Lines.Count != projected.Count + 1
            || journal.Lines.Select(line => line.EntryId).Distinct().Count() != journal.Lines.Count
            || journal.Lines.Any(line => line.JournalEntryId != journal.JournalEntryId))
            throw new LedgerValidationException("Retained corporate-action journal does not bind the reviewed instruction and exact set of accounting legs.");
        if (OpenLotCorporateAction.Fingerprint(ReadInstruction(journal)) != instructionHash)
            throw new LedgerValidationException("Retained corporate-action journal instruction differs from the reviewed input hash.");

        var credits = journal.Lines.Where(line => line.Credit > 0m).ToArray();
        if (credits.Length != 1 || credits[0].Account != predecessorAccount
            || !MatchesLeg(credits[0], source, source.OpenTransactionCostBasis, source.OpenFunctionalCostBasis, false))
            throw new LedgerValidationException("Retained corporate-action journal does not close the exact predecessor carrying basis and acquisition FX.");

        var ordered = new List<OpenLotDto>(projected.Count);
        foreach (var item in projected)
        {
            var successor = item.Successor;
            var retained = successorLots.SingleOrDefault(lot => lot.TaxLotRecordId == successor.TaxLotRecordId);
            if (retained is null)
                throw new LedgerValidationException("Retained corporate-action receipt is missing a reviewed successor lot.");
            try
            { OpenLotValidation.Validate(retained); }
            catch (ArgumentException ex) { throw new LedgerValidationException($"Retained successor acquisition is invalid: {ex.Message}"); }
            var acquisition = retained.Acquisition;
            var lineage = acquisition.CorporateActionLineage;
            if (retained.SecurityId != successor.Security.SecurityId || retained.BookPositionId != successor.BookPositionId
                || retained.LedgerBookId != source.LedgerBookId || retained.LotId != successor.LotId
                || retained.AcquiredDate != source.AcquiredDate || retained.Version != 1
                || retained.OriginalQuantity != successor.Quantity || retained.OpenQuantity != successor.Quantity
                || retained.OpenTransactionCostBasis != item.OpenTransactionCostBasis
                || retained.OpenFunctionalCostBasis != item.OpenFunctionalCostBasis
                || acquisition.QuantityBasis != source.Acquisition.QuantityBasis
                || acquisition.AcquisitionCurrency != source.Acquisition.AcquisitionCurrency
                || acquisition.FunctionalCurrency != source.Acquisition.FunctionalCurrency
                || acquisition.AcquisitionFxRateToFunctional != source.Acquisition.AcquisitionFxRateToFunctional
                || acquisition.HoldingPeriodStartDate != source.Acquisition.HoldingPeriodStartDate
                || acquisition.FaceValueTerms != source.Acquisition.FaceValueTerms
                || acquisition.TransactionCostBasis != item.AcquisitionTransactionCostBasis
                || acquisition.FunctionalCostBasis != item.AcquisitionFunctionalCostBasis
                || OpenLotCorporateAction.Evidence(instruction).Any(evidence => !acquisition.Evidence.Contains(evidence))
                || lineage is null || lineage.CorporateActionId != instruction.CorporateActionId
                || lineage.ActionType != instruction.ActionType || lineage.EffectiveDate != instruction.EffectiveDate
                || lineage.PredecessorTaxLotRecordId != source.TaxLotRecordId || lineage.PredecessorVersion != source.Version
                || lineage.BasisAllocationPercent != successor.BasisAllocationPercent || lineage.Role != successor.Role
                || !lineage.ReportingTags.SequenceEqual(successor.ReportingTags, StringComparer.Ordinal))
                throw new LedgerValidationException("Retained successor does not conserve the reviewed quantity, original/current bases, acquisition dates, FX and reporting lineage.");

            var debit = journal.Lines.Where(line => line.Debit > 0m
                && line.Account.ToString() == successor.AssetAccountId
                && line.Dimensions?.InstrumentId == successor.Security.SecurityId
                && line.Dimensions.PositionId == successor.BookPositionId).ToArray();
            if (debit.Length != 1 || !MatchesLeg(debit[0], retained, item.OpenTransactionCostBasis, item.OpenFunctionalCostBasis, true))
                throw new LedgerValidationException("Retained corporate-action successor journal leg does not reconcile to the immutable lot snapshot.");
            ordered.Add(retained);
        }

        return new(instruction.CorporateActionId, journal.JournalEntryId, predecessorBefore, predecessorAfter, ordered.AsReadOnly());
    }

    private static bool MatchesLeg(LedgerEntry line, OpenLotDto lot, decimal transaction, decimal functional, bool debit)
        => line.Account.AccountType == LedgerAccountType.Asset
           && line.Dimensions?.InstrumentId == lot.SecurityId && line.Dimensions.PositionId == lot.BookPositionId
           && line.Debit == (debit ? functional : 0m) && line.Credit == (debit ? 0m : functional)
           && line.Currency is { } currency
           && currency.TransactionCurrency == lot.Acquisition.AcquisitionCurrency
           && currency.FunctionalCurrency == lot.Acquisition.FunctionalCurrency
           && currency.FxRateToFunctional == lot.Acquisition.AcquisitionFxRateToFunctional
           && currency.TransactionDebit == (debit ? transaction : 0m)
           && currency.TransactionCredit == (debit ? 0m : transaction);

    private static bool Same(OpenLotDto first, OpenLotDto second)
        => JsonElement.DeepEquals(JsonSerializer.SerializeToElement(first), JsonSerializer.SerializeToElement(second));

    private static OpenLotCorporateActionInstructionDto ReadInstruction(JournalEntry journal)
    {
        ArgumentNullException.ThrowIfNull(journal);
        if (journal.Metadata?.Tags is not { } tags || !tags.TryGetValue("lotCorporateActionInputs", out var retained))
            throw new LedgerValidationException("Retained corporate-action journal is missing its reviewed instruction.");
        try
        {
            return JsonSerializer.Deserialize<OpenLotCorporateActionInstructionDto>(retained)
                ?? throw new LedgerValidationException("Retained corporate-action journal instruction is empty.");
        }
        catch (JsonException exception)
        {
            throw new LedgerValidationException($"Retained corporate-action journal instruction is invalid: {exception.Message}");
        }
    }
}
