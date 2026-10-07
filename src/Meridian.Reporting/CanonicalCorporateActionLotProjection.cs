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
    /// Verifies the complete reviewed journal and one predecessor's immutable mutation snapshots.
    /// Batch callers must supply every reviewed predecessor before certifying the resulting reports.
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
        IReadOnlyList<OpenLotCorporateActionSuccessorProjectionDto> allProjected;
        try
        { allProjected = OpenLotCorporateAction.Project(instruction); }
        catch (ArgumentException ex) { throw new LedgerValidationException($"Retained corporate-action instruction is invalid: {ex.Message}"); }
        var groups = OpenLotCorporateAction.Groups(instruction);
        var group = groups.SingleOrDefault(item => item.ExpectedLot.TaxLotRecordId == predecessorBefore.TaxLotRecordId)
            ?? throw new LedgerValidationException("Retained corporate-action predecessor was not reviewed in this batch.");
        var source = group.ExpectedLot;
        var projected = allProjected.Where(item => item.PredecessorTaxLotRecordId == source.TaxLotRecordId).ToArray();
        var closed = source with
        {
            OpenQuantity = 0m,
            OpenTransactionCostBasis = 0m,
            OpenFunctionalCostBasis = 0m,
            Version = checked(source.Version + 1)
        };
        if (predecessorAccount.ToString() != group.SourceAssetAccountId
            || !Same(source, predecessorBefore) || !Same(closed, predecessorAfter)
            || successorLots.Count != projected.Length
            || successorLots.Select(lot => lot.TaxLotRecordId).Distinct().Count() != successorLots.Count)
            throw new LedgerValidationException("Retained corporate-action predecessor or successor snapshots do not reproduce the reviewed lot transition.");
        var predecessorAccounts = ValidateJournal(instruction, journal, groups, allProjected);
        if (predecessorAccounts[source.TaxLotRecordId] != predecessorAccount)
            throw new LedgerValidationException("Retained predecessor account does not match the exact typed journal credit account.");

        var ordered = new List<OpenLotDto>(projected.Length);
        foreach (var item in projected)
        {
            var successor = item.Successor;
            var retained = successorLots.SingleOrDefault(lot => lot.TaxLotRecordId == successor.TaxLotRecordId);
            if (retained is null)
                throw new LedgerValidationException("Retained corporate-action receipt is missing a reviewed successor lot.");
            try
            { OpenLotValidation.Validate(retained); }
            catch (ArgumentException ex) { throw new LedgerValidationException($"Retained successor acquisition is invalid: {ex.Message}"); }
            var acquisition = source.Acquisition with
            {
                TransactionCostBasis = item.AcquisitionTransactionCostBasis,
                FunctionalCostBasis = item.AcquisitionFunctionalCostBasis,
                Evidence = OpenLotCorporateAction.Evidence(instruction),
                CorporateActionLineage = new(instruction.CorporateActionId, instruction.ActionType, instruction.EffectiveDate,
                    source.TaxLotRecordId, source.Version, successor.BasisAllocationPercent, successor.Role, successor.ReportingTags)
            };
            var expected = new OpenLotDto(successor.TaxLotRecordId, successor.Security.SecurityId,
                successor.BookPositionId, source.LedgerBookId, successor.LotId, source.AcquiredDate,
                successor.Quantity, successor.Quantity, item.OpenTransactionCostBasis, item.OpenFunctionalCostBasis, 1, acquisition);
            if (!Same(expected, retained))
                throw new LedgerValidationException("Retained successor does not conserve the reviewed quantity, original/current bases, acquisition dates, FX and reporting lineage.");
            ordered.Add(retained);
        }

        return new(instruction.CorporateActionId, journal.JournalEntryId, predecessorBefore, predecessorAfter, ordered.AsReadOnly());
    }

    private static IReadOnlyDictionary<Guid, LedgerAccount> ValidateJournal(OpenLotCorporateActionInstructionDto instruction, JournalEntry journal,
        IReadOnlyList<OpenLotCorporateActionPredecessorDto> groups,
        IReadOnlyList<OpenLotCorporateActionSuccessorProjectionDto> projected)
    {
        if (journal.Metadata?.EffectiveDate != instruction.EffectiveDate
            || journal.Metadata.Tags is null
            || !journal.Metadata.Tags.TryGetValue("lotCorporateActionHash", out var instructionHash)
            || instructionHash != OpenLotCorporateAction.Fingerprint(instruction)
            || journal.Lines.Count != projected.Count + groups.Count
            || journal.Lines.Select(line => line.EntryId).Distinct().Count() != journal.Lines.Count
            || journal.Lines.Any(line => line.EntryId == Guid.Empty || line.JournalEntryId != journal.JournalEntryId))
            throw new LedgerValidationException("Retained corporate-action journal does not bind the reviewed instruction and exact set of accounting legs.");
        if (OpenLotCorporateAction.Fingerprint(ReadInstruction(journal)) != instructionHash)
            throw new LedgerValidationException("Retained corporate-action journal instruction differs from the reviewed input hash.");

        var firstDimensions = journal.Lines[0].Dimensions
            ?? throw new LedgerValidationException("Retained corporate-action journal lacks its canonical dimension scope.");
        var scope = firstDimensions with { InstrumentId = null, PositionId = null, TaxLotId = null };
        if (journal.Lines.Any(line => line.Dimensions is not { } dimensions
            || !Same(scope, dimensions with { InstrumentId = null, PositionId = null, TaxLotId = null })
            || (!string.IsNullOrWhiteSpace(dimensions.BookId) && !string.Equals(dimensions.BookId, instruction.ExpectedLot.LedgerBookId.ToString("D"), StringComparison.OrdinalIgnoreCase))))
            throw new LedgerValidationException("Retained corporate-action journal does not preserve its reviewed common dimension scope.");
        var allowLegacy = groups.Count == 1 && journal.Lines.All(line => line.Dimensions?.TaxLotId is null);
        var matched = new HashSet<Guid>();
        var predecessorAccounts = new Dictionary<Guid, LedgerAccount>();
        foreach (var group in groups)
        {
            var source = group.ExpectedLot;
            var credits = journal.Lines.Where(line => MatchesIdentity(line, source.TaxLotRecordId,
                group.SourceAssetAccountId, source.SecurityId, source.BookPositionId, allowLegacy)
                && MatchesLeg(line, source.Acquisition, source.OpenTransactionCostBasis, source.OpenFunctionalCostBasis, false)).ToArray();
            if (credits.Length != 1 || !matched.Add(credits[0].EntryId))
                throw new LedgerValidationException("Retained corporate-action journal does not close each exact predecessor carrying basis and acquisition FX.");
            predecessorAccounts.Add(source.TaxLotRecordId, credits[0].Account);
            foreach (var item in projected.Where(item => item.PredecessorTaxLotRecordId == source.TaxLotRecordId))
            {
                var target = item.Successor;
                var debits = journal.Lines.Where(line => MatchesIdentity(line, target.TaxLotRecordId,
                    target.AssetAccountId, target.Security.SecurityId, target.BookPositionId, allowLegacy)
                    && MatchesLeg(line, source.Acquisition, item.OpenTransactionCostBasis, item.OpenFunctionalCostBasis, true)).ToArray();
                if (debits.Length != 1 || !matched.Add(debits[0].EntryId))
                    throw new LedgerValidationException("Retained corporate-action successor journal leg does not reconcile to the reviewed lot identity and basis.");
            }
        }
        if (matched.Count != journal.Lines.Count)
            throw new LedgerValidationException("Retained corporate-action journal contains an unreviewed lot leg.");
        return predecessorAccounts;
    }

    private static bool MatchesIdentity(LedgerEntry line, Guid lotId, string account, Guid securityId, Guid positionId, bool allowLegacy)
        => line.Account.ToString() == account
           && line.Dimensions?.InstrumentId == securityId && line.Dimensions.PositionId == positionId
           && (string.Equals(line.Dimensions.TaxLotId, lotId.ToString("D"), StringComparison.OrdinalIgnoreCase)
               || (allowLegacy && line.Dimensions.TaxLotId is null));

    private static bool MatchesLeg(LedgerEntry line, OpenLotAcquisitionDto acquisition, decimal transaction, decimal functional, bool debit)
        => line.Account.AccountType == LedgerAccountType.Asset
           && line.Debit == (debit ? functional : 0m) && line.Credit == (debit ? 0m : functional)
           && line.Currency is { } currency
           && currency.TransactionCurrency == acquisition.AcquisitionCurrency
           && currency.FunctionalCurrency == acquisition.FunctionalCurrency
           && currency.FxRateToFunctional == acquisition.AcquisitionFxRateToFunctional
           && currency.TransactionDebit == (debit ? transaction : 0m)
           && currency.TransactionCredit == (debit ? 0m : transaction);

    private static bool Same<T>(T first, T second)
        => JsonElement.DeepEquals(JsonSerializer.SerializeToElement(first), JsonSerializer.SerializeToElement(second));

    /// <summary>Reads the exact reviewed input retained on the immutable journal.</summary>
    public static OpenLotCorporateActionInstructionDto ReadInstruction(JournalEntry journal)
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
