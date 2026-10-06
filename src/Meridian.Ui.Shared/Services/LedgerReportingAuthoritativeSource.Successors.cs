using System.Collections.Immutable;
using System.Text.Json;
using Meridian.Contracts.Accounting.Lots;
using Meridian.Contracts.AssetOperations;
using Meridian.Contracts.Integrity;
using Meridian.Ledger;
using Meridian.Storage.Ledger;

namespace Meridian.Ui.Shared.Services;

public sealed partial class LedgerReportingAuthoritativeSource
{
    private async Task<ImmutableArray<IReadOnlyDictionary<string, string>>> RetainOpenLotSuccessorEvidenceAsync(
        ImmutableArray<IReadOnlyDictionary<string, string>> rows,
        IReadOnlyList<LedgerJournalEntryRecord> completeJournals,
        IReadOnlyList<LedgerJournalEntryRecord> selectedJournals,
        LedgerBookRecord book,
        LedgerAccountingPeriod period,
        string tenantId,
        string companyId,
        string fundId,
        CancellationToken cancellationToken)
    {
        var selectedIds = selectedJournals.Select(static journal => journal.Entry.JournalEntryId).ToHashSet();
        var journals = completeJournals.Where(journal => selectedIds.Contains(journal.Entry.JournalEntryId))
            .ToDictionary(static journal => journal.Entry.JournalEntryId);
        var required = journals.Values.Where(static journal =>
                journal.Entry.Metadata.Tags?.ContainsKey(CanonicalOpenLotSuccessorEvidence.InstructionFingerprintTag) == true)
            .Select(static journal => journal.Entry.JournalEntryId).ToHashSet();
        if (_openLotSuccessorHistory is null)
        {
            if (required.Count != 0)
                throw Unavailable("Canonical successor mutation history is required for retained corporate-action journals.");
            return rows;
        }

        IReadOnlyList<AtomicTaxLotJournalResult> batches;
        try
        {
            batches = await _openLotSuccessorHistory.GetOpenLotSuccessorHistoryAsync(
                book.LedgerBookId, selectedIds.ToArray(), cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is NotSupportedException or ArgumentException or InvalidOperationException or JsonException or LedgerValidationException)
        {
            throw Unavailable($"Canonical successor mutation history is unavailable: {exception.Message}");
        }

        var evidence = new Dictionary<Guid, (string Json, string Hash, string InstructionHash, OpenLotSuccessorInstructionDto Instruction)>();
        foreach (var batch in batches)
        {
            var journalId = batch.Journal.Entry.JournalEntryId;
            if (!journals.TryGetValue(journalId, out var journal) || evidence.ContainsKey(journalId))
                throw Unavailable("Retained successor batches duplicate a journal or fall outside the certified journal scope.");
            try
            {
                CanonicalOpenLotSuccessorEvidence.Validate(batch, journal, book.LedgerBookId, book.BaseCurrency);
                var scope = batch.CorporateAction!.Projection.AccountingScope!;
                if (scope.TenantId != tenantId || scope.CompanyId != companyId || scope.FundProfileId != fundId
                    || scope.PeriodId != period.PeriodId || batch.CorporateAction.Projection.Treatment.AccountingBasis != book.AccountingBasis)
                    throw new ArgumentException("Retained successor authority differs from the certified owner, period or accounting basis.");
                var json = CanonicalOpenLotSuccessorEvidence.Serialize(batch);
                evidence.Add(journalId, (json, Sha256Digest.ComputeUtf8(json), OpenLotSuccessors.Fingerprint(batch.CorporateAction), batch.CorporateAction));
            }
            catch (Exception exception) when (exception is ArgumentException or InvalidOperationException or JsonException or LedgerValidationException)
            {
                throw Unavailable($"Successor batch '{batch.MutationBatchId:D}' blocks canonical reporting: {exception.Message}");
            }
        }

        if (!required.SetEquals(evidence.Keys))
            throw Unavailable("A corporate-action journal is missing its complete retained successor mutation evidence.");
        if (evidence.Count == 0)
            return rows;

        return rows.Select(row =>
        {
            if (!evidence.TryGetValue(Guid.Parse(row["journalEntryId"]), out var retained))
                return row;
            var enriched = new SortedDictionary<string, string>(row.ToDictionary(static pair => pair.Key, static pair => pair.Value), StringComparer.Ordinal)
            {
                ["openLotSuccessorInstructionFingerprint"] = retained.InstructionHash,
                ["openLotSuccessorEvidenceHash"] = retained.Hash,
                ["openLotSuccessorEvidence"] = retained.Json
            };
            var successor = retained.Instruction.Successors.SingleOrDefault(target =>
                row.GetValueOrDefault("instrumentId") == target.Lot.SecurityId.ToString("D")
                && row.GetValueOrDefault("positionId") == target.Lot.BookPositionId.ToString("D"));
            var operation = successor is null ? null : retained.Instruction.Projection.Recipe.Single(item =>
                item.Kind == CorporateActionEconomicOperationKindDto.ExchangeIn && item.SecurityId == successor.Lot.SecurityId);
            enriched["openLotSuccessorRole"] = operation?.SuccessorRole?.ToString() ?? "Predecessor";
            enriched["openLotScheduleD"] = operation?.SuccessorRole == CorporateActionSuccessorRoleDto.Refunded ? "true" : "false";
            return (IReadOnlyDictionary<string, string>)enriched;
        }).ToImmutableArray();
    }
}

/// <summary>
/// Reconciles immutable posted successor snapshots with the approved plan before Reporting retains
/// them. Current open lots are deliberately never consulted: subsequent disposal cannot rewrite
/// the basis, acquisition FX, holding period or Schedule D evidence of an earlier report.
/// </summary>
public static class CanonicalOpenLotSuccessorEvidence
{
    public const string InstructionFingerprintTag = OpenLotSuccessors.JournalFingerprintTag;

    public static void Validate(AtomicTaxLotJournalResult batch, LedgerJournalEntryRecord journal,
        Guid ledgerBookId, string functionalCurrency)
    {
        ArgumentNullException.ThrowIfNull(batch);
        ArgumentNullException.ThrowIfNull(journal);
        var instruction = batch.CorporateAction
            ?? throw new ArgumentException("The retained successor instruction is missing.");
        OpenLotSuccessors.Validate(instruction);
        var predecessor = instruction.ExpectedLot;
        Require(batch.MutationKind == AtomicTaxLotMutationKind.CorporateAction && batch.MutationBatchId != Guid.Empty
            && batch.CanonicalFingerprint.StartsWith("sha256:", StringComparison.Ordinal)
            && Sha256Digest.IsCanonical(batch.CanonicalFingerprint[7..]), "Successor batch identity or kind is invalid.");
        Require(predecessor.LedgerBookId == ledgerBookId && predecessor.Acquisition.FunctionalCurrency == functionalCurrency
            && instruction.Projection.AccountingScope?.LedgerBookId == ledgerBookId,
            "Successor basis belongs to another ledger book or functional currency.");
        Require(Equal(batch.Journal, journal), "Retained successor evidence does not match the complete certified journal.");
        Require(journal.Entry.Metadata.Tags is { } tags && tags.TryGetValue(InstructionFingerprintTag, out var hash)
            && hash == OpenLotSuccessors.Fingerprint(instruction), "The journal does not bind the retained successor instruction.");
        Require(journal.Entry.Metadata.SecurityId == predecessor.SecurityId
            && journal.Entry.Metadata.EffectiveDate == instruction.Projection.EconomicEvent!.EffectiveDate,
            "The journal does not identify the retained predecessor and corporate-action effective date.");
        Require(journal.Entry.Lines.Sum(static line => line.Debit) == predecessor.OpenFunctionalCostBasis
            && journal.Entry.Lines.Sum(static line => line.Credit) == predecessor.OpenFunctionalCostBasis,
            "The posted carrying-value transfer does not reconcile to predecessor functional basis.");
        Require(journal.Entry.Lines.Count == instruction.Successors.Count + 1,
            "Cashless successor evidence requires one predecessor credit and one debit for each successor.");
        ValidateLine(journal, predecessor, debit: false);
        foreach (var target in instruction.Successors)
            ValidateLine(journal, target.Lot, debit: true);
        Require(instruction.Successors.SelectMany(static target => target.Lot.Acquisition.Evidence)
                .Concat(predecessor.Acquisition.Evidence).All(batch.RetainedEvidence.Contains),
            "The successor batch is missing retained acquisition evidence.");
        foreach (var dependency in instruction.Projection.EvidenceManifest)
            Require(batch.RetainedEvidence.Any(evidence => evidence.EvidenceId == dependency.EvidenceId
                && evidence.EvidenceVersion == dependency.EvidenceVersion && evidence.EvidenceUri == dependency.EvidenceUri
                && evidence.ContentHashSha256 == dependency.ContentHashSha256 && evidence.SubjectType == dependency.SubjectType
                && evidence.SubjectId == dependency.SubjectId), "The successor batch is missing approved projection evidence.");
        Require(batch.Mutations.Count == instruction.Successors.Count + 1
            && batch.Mutations.Select(static mutation => mutation.TaxLotRecordId).Distinct().Count() == batch.Mutations.Count
            && batch.MutatedLots.Count == batch.Mutations.Count
            && batch.MutatedLots.Select(static lot => lot.TaxLotRecordId).Distinct().Count() == batch.Mutations.Count,
            "The complete predecessor and successor mutation set is required.");
        var expectedTargets = instruction.Successors.ToDictionary(static target => target.Lot.TaxLotRecordId);
        foreach (var mutation in batch.Mutations)
        {
            Require(mutation.MutationBatchId == batch.MutationBatchId && mutation.JournalEntryId == journal.Entry.JournalEntryId
                && mutation.MutationKind == AtomicTaxLotMutationKind.CorporateAction
                && mutation.SourceEventId == instruction.Projection.EconomicEvent!.EventId
                && mutation.LotAfter.LastMutationBatchId == batch.MutationBatchId,
                "Mutation identity or journal lineage differs from its retained batch.");
            Require(Equal(batch.MutatedLots.SingleOrDefault(lot => lot.TaxLotRecordId == mutation.TaxLotRecordId), mutation.LotAfter),
                "Retained after snapshots disagree within the successor batch.");
            Require(mutation.RetainedEvidence.Count == batch.RetainedEvidence.Count
                && mutation.RetainedEvidence.All(batch.RetainedEvidence.Contains)
                && mutation.LotAfter.Account == journal.Entry.Lines.Single(line =>
                    line.Dimensions?.InstrumentId == mutation.SecurityId && line.Dimensions?.PositionId == mutation.BookPositionId).Account,
                "Mutation account or retained evidence differs from its journal and approved batch.");
            Require(mutation.QuantityBefore + mutation.QuantityDelta == mutation.QuantityAfter
                && mutation.QuantityAfter == mutation.LotAfter.OpenQuantity
                && mutation.ResultVersion == mutation.LotAfter.Version
                && mutation.LotId == mutation.LotAfter.LotId
                && mutation.UnitCost == mutation.LotAfter.UnitCost
                && mutation.SecurityId == mutation.LotAfter.SecurityId && mutation.BookPositionId == mutation.LotAfter.BookPositionId,
                "Mutation quantities, versions or stable identities do not match its after snapshot.");
            if (mutation.TaxLotRecordId == predecessor.TaxLotRecordId)
            {
                Require(mutation.LotBefore is not null && Equal(mutation.LotBefore.ToOpenLot(), predecessor),
                    "The retained predecessor snapshot differs from approved authority.");
                Require(mutation.ExpectedVersion == predecessor.Version && mutation.ResultVersion == predecessor.Version + 1
                    && mutation.CostBasis == predecessor.OpenFunctionalCostBasis
                    && mutation.QuantityBefore == mutation.LotBefore!.OpenQuantity && mutation.QuantityAfter == 0m
                    && Equal(mutation.LotAfter, mutation.LotBefore! with
                    {
                        OpenQuantity = 0m,
                        Version = predecessor.Version + 1,
                        UpdatedAt = mutation.RecordedAt,
                        LastMutationBatchId = batch.MutationBatchId
                    })
                    && Equal(mutation.LotAfter.ToOpenLot(), predecessor with
                    {
                        OpenQuantity = 0m,
                        OpenTransactionCostBasis = 0m,
                        OpenFunctionalCostBasis = 0m,
                        Version = predecessor.Version + 1
                    }), "The predecessor did not close atomically with its acquisition facts intact.");
            }
            else
            {
                Require(expectedTargets.TryGetValue(mutation.TaxLotRecordId, out var target)
                    && mutation.LotBefore is null && mutation.ExpectedVersion == 0 && mutation.ResultVersion == 1
                    && mutation.CostBasis == target!.Lot.OpenFunctionalCostBasis
                    && mutation.LotAfter.SourceJournalEntryId == journal.Entry.JournalEntryId
                    && mutation.LotAfter.OriginatingMutationBatchId == batch.MutationBatchId
                    && mutation.LotAfter.BasisAdjustment is { Reason: OpenLotBasisAdjustmentReasons.CorporateActionSuccessor } adjustment
                    && adjustment.MutationBatchId == batch.MutationBatchId && Equal(adjustment.CorporateAction, instruction)
                    && mutation.QuantityBefore == 0m && Equal(mutation.LotAfter.ToOpenLot(), target!.Lot),
                    "A persisted successor differs from its approved identity, allocated basis or acquisition facts.");
            }
        }
        Require(batch.Mutations.Any(mutation => mutation.TaxLotRecordId == predecessor.TaxLotRecordId),
            "The predecessor closeout is missing.");
    }

    public static string Serialize(AtomicTaxLotJournalResult batch) => JsonSerializer.Serialize(new
    {
        Schema = "canonical-open-lot-successor-evidence/v1",
        batch.MutationBatchId,
        JournalEntryId = batch.Journal.Entry.JournalEntryId,
        batch.CanonicalFingerprint,
        Instruction = batch.CorporateAction,
        Mutations = batch.Mutations.OrderBy(static mutation => mutation.SelectionOrdinal).ThenBy(static mutation => mutation.TaxLotRecordId),
        batch.RetainedEvidence
    });

    private static void ValidateLine(LedgerJournalEntryRecord journal, OpenLotDto lot, bool debit)
    {
        var lines = journal.Entry.Lines.Where(line => line.Dimensions?.InstrumentId == lot.SecurityId
            && line.Dimensions?.PositionId == lot.BookPositionId).ToArray();
        Require(lines.Length == 1, "Each predecessor and successor requires an exact journal security and position line.");
        var line = lines[0];
        var acquisition = lot.Acquisition;
        var currency = line.Currency;
        var retainedAcquisitionCurrency = currency is not null
            && currency.TransactionCurrency == acquisition.AcquisitionCurrency
            && currency.FxRateToFunctional == acquisition.AcquisitionFxRateToFunctional
            && currency.TransactionDebit == (debit ? lot.OpenTransactionCostBasis : 0m)
            && currency.TransactionCredit == (debit ? 0m : lot.OpenTransactionCostBasis);
        var functionalOnly = currency is not null
            && currency.TransactionCurrency == acquisition.FunctionalCurrency && currency.FxRateToFunctional == 1m
            && currency.TransactionDebit == line.Debit && currency.TransactionCredit == line.Credit;
        Require(line.Account.AccountType == LedgerAccountType.Asset
            && line.Debit == (debit ? lot.OpenFunctionalCostBasis : 0m)
            && line.Credit == (debit ? 0m : lot.OpenFunctionalCostBasis)
            && currency?.FunctionalCurrency == acquisition.FunctionalCurrency
            && (retainedAcquisitionCurrency || functionalOnly),
            "Journal transfer lines must reconcile allocated functional basis and any acquisition-currency detail for each lot.");
    }

    private static bool Equal<T>(T left, T right) =>
        JsonElement.DeepEquals(JsonSerializer.SerializeToElement(left), JsonSerializer.SerializeToElement(right));

    private static void Require(bool condition, string message)
    {
        if (!condition)
            throw new ArgumentException(message);
    }
}
