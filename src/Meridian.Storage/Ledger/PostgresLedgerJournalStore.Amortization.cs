using System.Text.Json;
using System.Text.Json.Serialization;
using Meridian.Contracts.Accounting.Lots;
using Meridian.Contracts.AssetOperations;
using Meridian.Contracts.Ledger;
using Meridian.Storage.AssetOperations;
using Meridian.Storage.SecurityMaster;
using Meridian.Ledger;
using Npgsql;

namespace Meridian.Storage.Ledger;

public sealed partial class PostgresLedgerJournalStore
{
    private static void ValidateAtomicAmortization(AtomicTaxLotJournalCommand command)
    {
        var instruction = command.Amortization
            ?? throw new LedgerValidationException("Atomic amortization requires retained reviewed lot and Security Master inputs.");
        try
        {
            if (OpenLotAmortization.Project(instruction).FunctionalMovement == 0m)
                throw new LedgerValidationException("Amortization has no carrying-value movement to post.");
        }
        catch (ArgumentException exception)
        {
            throw new LedgerValidationException(exception.Message);
        }
        if (command.AcquisitionLot is not null || command.DisposalSelections is { Count: > 0 }
            || command.ReliefMethod is not null || command.PolicyRevision is not null || command.DisposalSalePrice is not null)
            throw new LedgerValidationException("Amortization cannot carry acquisition, relief or disposal-price inputs.");
        var scope = ResolveAtomicAssetScope(command.Journal);
        var lot = instruction.ExpectedLot;
        if (!AssetAccountingEventTypeNames.TryParse(command.Journal.PostingCommand?.SourceEventType, out var eventKind)
            || eventKind != AssetAccountingEventKindDto.DepreciationAmortization
            || !OpenLotAmortization.SameInstruction(command.Journal.PostingCommand?.LotAmortization, instruction))
            throw new LedgerValidationException("The governed journal must retain the exact reviewed canonical amortization inputs.");
        if (instruction.Reversal is { } reversal
            && (command.CorrectsMutationBatchId != reversal.MutationBatchId
                || command.Journal.SourceJournalEntryId != reversal.JournalEntryId))
            throw new LedgerValidationException("Amortization reversal must identify the exact corrected mutation batch and journal.");
        if (command.CorrectsMutationBatchId.HasValue)
        {
            var approval = command.Journal.AdjustmentApproval;
            if (command.Journal.PostingKind != LedgerPostingKindDto.Adjustment
                || command.Journal.PostingCommand?.ApprovalState != AccountingPostingApprovalStateDto.Approved
                || approval is null || approval.Status != LedgerAdjustmentApprovalStatusDto.Approved
                || string.IsNullOrWhiteSpace(approval.ApprovalId) || string.IsNullOrWhiteSpace(approval.ApprovedBy)
                || string.IsNullOrWhiteSpace(approval.ReasonCode) || approval.ApprovedAt == default
                || approval.ApprovedAt.Offset != TimeSpan.Zero || approval.ApprovedAt > command.Journal.Entry.Timestamp)
                throw new LedgerValidationException("Amortization correction requires complete approved adjustment metadata before posting.");
        }
        else if (command.Journal.SourceJournalEntryId is not null || command.Journal.AdjustmentApproval is not null)
            throw new LedgerValidationException("Amortization correction lineage requires an atomic corrected mutation batch.");
        if (lot.LedgerBookId != command.LedgerBookId || lot.SecurityId != scope.SecurityId || lot.BookPositionId != scope.BookPositionId
            || instruction.AsOfDate != command.Journal.Entry.Metadata.EffectiveDate
            || lot.Acquisition.FunctionalCurrency != ResolveAtomicFunctionalCurrency(command.Journal)
            || !command.RetainedEvidence.Contains(instruction.SecurityEvidence)
            || lot.Acquisition.Evidence.Any(evidence => !command.RetainedEvidence.Contains(evidence)))
            throw new LedgerValidationException("Amortization must bind the exact journal scope, effective date, acquisition and reference evidence.");
    }

    private static void ValidateAmortizationAssetCurrency(AtomicTaxLotJournalCommand command, LedgerEntry asset)
    {
        var inputs = command.Amortization!;
        var projection = OpenLotAmortization.Project(inputs);
        var acquisition = inputs.ExpectedLot.Acquisition;
        if (asset.Currency is not { } currency
            || currency.TransactionCurrency != acquisition.AcquisitionCurrency
            || currency.FunctionalCurrency != acquisition.FunctionalCurrency
            || currency.FxRateToFunctional != acquisition.AcquisitionFxRateToFunctional
            || currency.TransactionDebit != Math.Max(projection.TransactionMovement, 0m)
            || currency.TransactionCredit != Math.Max(-projection.TransactionMovement, 0m))
            throw new LedgerValidationException("Amortization asset journal currency, acquisition FX and transaction movement must exactly match the retained canonical lot.");
        // Cumulative functional and transaction targets are rounded independently at 12 places;
        // subtracting consecutive rounded targets can differ from multiplying their delta by FX
        // by at most one storage quantum. The broad legacy 0.01 leg tolerance is insufficient here.
        const decimal storageQuantum = 0.000000000001m;
        if (Math.Abs(asset.Debit - currency.TransactionDebit * currency.FxRateToFunctional) > storageQuantum
            || Math.Abs(asset.Credit - currency.TransactionCredit * currency.FxRateToFunctional) > storageQuantum)
            throw new LedgerValidationException("Amortization asset journal functional movement must reconcile to retained acquisition FX at the 12-decimal journal boundary.");
    }

    private async Task<LedgerTaxLotRecord> LockAmortizationAuthorityAsync(
        NpgsqlConnection connection, NpgsqlTransaction transaction, AtomicTaxLotJournalCommand command, CancellationToken ct)
    {
        var instruction = command.Amortization!;
        // Fixed order: period (caller), Security Master, book position, then lot. All authority
        // locks belong to this transaction, so evidence cannot change between checking and commit.
        if (_backfillSecurityMaster?.Invoke() is not PostgresSecurityMasterStore securities
            || _backfillPositions?.Invoke() is not PostgresAssetOperationsProjectionStore positions)
            throw new LedgerValidationException("Amortization requires authoritative PostgreSQL Security Master and book-position stores.");
        var security = await securities.LockForLotPostingAsync(connection, transaction, instruction.ExpectedLot.SecurityId, ct).ConfigureAwait(false);
        if (security is null || OpenLotAmortization.SecurityHash(security) != OpenLotAmortization.SecurityHash(instruction.Security))
            throw new LedgerValidationException("Amortization Security Master evidence is missing or stale; rebuild and review the projection.");
        var position = await positions.LockForLotPostingAsync(connection, transaction, instruction.ExpectedLot.BookPositionId, ct).ConfigureAwait(false);
        var book = await LoadLedgerBookAsync(connection, transaction, command.LedgerBookId, ct).ConfigureAwait(false);
        if (position is null || book is null || position.Version != instruction.ExpectedBookPositionVersion
            || position.PositionSide is not (Meridian.Contracts.AssetOperations.BookPositionSides.Long
                or Meridian.Contracts.AssetOperations.BookPositionSides.Asset) || position.Status != "Active"
            || position.SecurityId != instruction.ExpectedLot.SecurityId || position.PositionId != instruction.ExpectedLot.BookPositionId
            || position.BookContext.LedgerBookId != command.LedgerBookId
            || position.BookContext.BaseCurrency != book.BaseCurrency
            || position.BookContext.FundProfileId != book.FundProfileId
            || position.BookContext.FundStructureNodeId != book.FundStructureNodeId
            || position.BookContext.FundStructureNodeKind != book.FundStructureNodeKind
            || position.EffectiveFrom > instruction.AsOfDate || (position.EffectiveTo is { } end && end < instruction.AsOfDate))
            throw new LedgerValidationException("Amortization book-position version or accounting scope is missing or stale.");
        var lots = await LoadTaxLotsForUpdateAsync(connection, transaction, command.LedgerBookId,
            [instruction.ExpectedLot.TaxLotRecordId], ct).ConfigureAwait(false);
        var before = lots.SingleOrDefault()
            ?? throw new LedgerValidationException("Amortization lot no longer exists in the reviewed book.");
        if (before.Account.AccountType != LedgerAccountType.Asset)
            throw new LedgerValidationException("Amortization requires an asset carrying account.");
        if (!OpenLotAmortization.SameLot(before.ToOpenLot(), instruction.ExpectedLot))
            throw new LedgerValidationException("Amortization lot version or carrying basis changed; rebuild and review the projection.");
        if (command.CorrectsMutationBatchId is { } correctedId)
        {
            var corrected = await LoadAtomicTaxLotResultAsync(connection, transaction, correctedId, false, ct).ConfigureAwait(false)
                ?? throw new LedgerValidationException("Corrected amortization is unavailable.");
            ValidateAmortizationCorrection(command, before, corrected);
        }
        else if (before.LastMutationBatchId is { } lastId && before.BasisAdjustment?.MutationBatchId != lastId)
        {
            var last = await LoadAtomicTaxLotResultAsync(connection, transaction, lastId, false, ct).ConfigureAwait(false);
            if (last is { MutationKind: AtomicTaxLotMutationKind.Amortization, CorrectsMutationBatchId: not null }
                && last.Journal.Entry.Metadata.EffectiveDate == instruction.AsOfDate)
                throw new LedgerValidationException("Same-date amortization after reversal requires approved rebook lineage to the reversal receipt.");
        }
        if (instruction.Reversal is null && before.BasisAdjustment is { } prior
            && (prior.Reason != OpenLotBasisAdjustmentReasons.Amortization || prior.Amortization is null
                || prior.Amortization.AsOfDate >= instruction.AsOfDate))
            throw new LedgerValidationException("Amortization requires a later period and cannot overwrite another basis treatment.");
        var history = await ReadDatedLotQuantitiesAsync(connection, transaction, command.LedgerBookId,
            [before.TaxLotRecordId], ct).ConfigureAwait(false);
        if (history.Any(mutation => mutation.EffectiveDate > instruction.AsOfDate)
            || HistoricalTaxLotQuantity.Project(before, history, instruction.AsOfDate).OpenQuantity != before.OpenQuantity)
            throw new LedgerValidationException("Amortization cannot post before a retained lot mutation or use unproved partial holdings.");
        return before;
    }

    private async Task<LedgerTaxLotMutationRecord> ApplyAmortizationAsync(
        NpgsqlConnection connection, NpgsqlTransaction transaction, AtomicTaxLotJournalCommand command,
        LedgerTaxLotRecord before, DateTimeOffset recordedAt, CancellationToken ct)
    {
        var projection = OpenLotAmortization.Project(command.Amortization!);
        OpenLotBasisAdjustmentDto? adjustment = new(command.MutationBatchId, OpenLotBasisAdjustmentReasons.Amortization,
            before.OpenQuantity, projection.TransactionCostBasis, projection.FunctionalCostBasis, command.Amortization);
        if (command.Amortization!.Reversal is { } reversal)
        {
            var original = await LoadTaxLotMutationsAsync(connection, transaction, reversal.MutationBatchId, ct).ConfigureAwait(false);
            adjustment = original.Single().LotBefore!.BasisAdjustment;
        }
        await using var update = connection.CreateCommand();
        update.Transaction = transaction;
        update.CommandText = $"""
            update {Qualified("tax_lots")}
            set version = version + 1, last_mutation_batch_id = @batch, updated_at = @at,
                basis_adjustment = @basis_adjustment
            where tax_lot_record_id = @id and ledger_book_id = @book
                and version = @version and open_quantity = @quantity
            returning {AverageCostLotColumns};
            """;
        update.Parameters.AddWithValue("batch", command.MutationBatchId);
        update.Parameters.AddWithValue("at", recordedAt.UtcDateTime);
        update.Parameters.AddWithValue("id", before.TaxLotRecordId);
        update.Parameters.AddWithValue("book", command.LedgerBookId);
        update.Parameters.AddWithValue("version", before.Version);
        update.Parameters.AddWithValue("quantity", before.OpenQuantity);
        AddBasisAdjustmentParameter(update, adjustment);
        await using var reader = await update.ExecuteReaderAsync(ct).ConfigureAwait(false);
        if (!await reader.ReadAsync(ct).ConfigureAwait(false))
            throw new LedgerValidationException("Amortization lot compare-and-swap failed.");
        var after = ReadTaxLot(reader);
        return BuildMutationRecord(command, after, before, 0, 0m, before.Version,
            command.Amortization!.SecurityEvidence.EvidenceId, recordedAt, Math.Abs(projection.FunctionalMovement));
    }

    internal static void ValidateAmortizationCorrection(AtomicTaxLotJournalCommand command,
        LedgerTaxLotRecord current, AtomicTaxLotJournalResult corrected)
    {
        var instruction = command.Amortization!;
        if (corrected.MutationKind != AtomicTaxLotMutationKind.Amortization || corrected.Mutations.Count != 1
            || corrected.Mutations[0].LotBefore is null
            || current.LastMutationBatchId != corrected.MutationBatchId
            || !SameAmortizationLot(current, corrected.Mutations[0].LotAfter)
            || corrected.Journal.Entry.Metadata.EffectiveDate != instruction.AsOfDate)
            throw new LedgerValidationException("Amortization correction requires the latest unchanged amortization lot and its exact effective date.");
        var original = corrected.Mutations[0];
        if (instruction.Reversal is { } reversal)
        {
            var originalInputs = original.LotAfter.BasisAdjustment?.Amortization;
            if (original.LotAfter.BasisAdjustment?.MutationBatchId != corrected.MutationBatchId
                || originalInputs is null || originalInputs.Reversal is not null
                || !OpenLotAmortization.SameLot(reversal.RestoresLot, original.LotBefore!.ToOpenLot()))
                throw new LedgerValidationException("Amortization reversal must restore the exact retained original lot basis.");
            ValidateInverseAmortizationJournal(command.Journal.Entry, corrected.Journal.Entry);
        }
        else if (corrected.CorrectsMutationBatchId is null
                 || original.LotAfter.BasisAdjustment?.MutationBatchId == corrected.MutationBatchId
                 || original.LotBefore!.BasisAdjustment?.MutationBatchId != corrected.CorrectsMutationBatchId)
            throw new LedgerValidationException("Amortization rebook requires an atomic reversal receipt; reverse the original posting first.");
    }

    private static bool SameAmortizationLot(LedgerTaxLotRecord left, LedgerTaxLotRecord right)
        => JsonElement.DeepEquals(
            JsonSerializer.SerializeToElement(left, AmortizationStorageJsonContext.Default.LedgerTaxLotRecord),
            JsonSerializer.SerializeToElement(right, AmortizationStorageJsonContext.Default.LedgerTaxLotRecord));

    private static void ValidateInverseAmortizationJournal(JournalEntry reversal, JournalEntry original)
    {
        var unmatched = original.Lines.ToList();
        foreach (var line in reversal.Lines)
        {
            var index = unmatched.FindIndex(prior => prior.Account == line.Account
                && prior.Debit == line.Credit && prior.Credit == line.Debit
                && JsonElement.DeepEquals(
                    JsonSerializer.SerializeToElement(prior.Dimensions, AmortizationStorageJsonContext.Default.LedgerLineDimensionSet),
                    JsonSerializer.SerializeToElement(line.Dimensions, AmortizationStorageJsonContext.Default.LedgerLineDimensionSet))
                && prior.Currency is { } oldCurrency && line.Currency is { } currency
                && oldCurrency.TransactionCurrency == currency.TransactionCurrency
                && oldCurrency.FunctionalCurrency == currency.FunctionalCurrency
                && oldCurrency.FxRateToFunctional == currency.FxRateToFunctional
                && oldCurrency.TransactionDebit == currency.TransactionCredit
                && oldCurrency.TransactionCredit == currency.TransactionDebit);
            if (index < 0)
                throw new LedgerValidationException("Amortization reversal journal must exactly invert every retained financial line.");
            unmatched.RemoveAt(index);
        }
        if (unmatched.Count != 0)
            throw new LedgerValidationException("Amortization reversal journal must exactly invert every retained financial line.");
    }
}

[JsonSourceGenerationOptions(GenerationMode = JsonSourceGenerationMode.Metadata)]
[JsonSerializable(typeof(LedgerTaxLotRecord))]
[JsonSerializable(typeof(LedgerLineDimensionSet))]
internal sealed partial class AmortizationStorageJsonContext : JsonSerializerContext;
