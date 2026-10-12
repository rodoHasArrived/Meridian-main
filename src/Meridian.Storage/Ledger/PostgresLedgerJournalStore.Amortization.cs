using System.Text.Json;
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
            || command.ReliefMethod is not null || command.PolicyRevision is not null || command.CorrectsMutationBatchId is not null)
            throw new LedgerValidationException("Amortization cannot carry acquisition, relief or correction inputs; corrections require reversal and rebook.");
        var scope = ResolveAtomicAssetScope(command.Journal);
        var lot = instruction.ExpectedLot;
        var posting = command.Journal.PostingCommand;
        if (!AssetAccountingEventTypeNames.TryParse(posting?.SourceEventType, out var eventKind)
            || eventKind != AssetAccountingEventKindDto.DepreciationAmortization
            || !JsonElement.DeepEquals(JsonSerializer.SerializeToElement(posting?.LotAmortization),
                JsonSerializer.SerializeToElement(instruction)))
            throw new LedgerValidationException("The governed journal must retain the exact reviewed canonical amortization inputs.");
        var reviewedEvidence = lot.Acquisition.Evidence.Append(instruction.SecurityEvidence);
        if (lot.LedgerBookId != command.LedgerBookId || lot.SecurityId != scope.SecurityId || lot.BookPositionId != scope.BookPositionId
            || instruction.AsOfDate != command.Journal.Entry.Metadata.EffectiveDate
            || lot.Acquisition.FunctionalCurrency != ResolveAtomicFunctionalCurrency(command.Journal)
            || reviewedEvidence.Any(evidence => !command.RetainedEvidence.Contains(evidence)
                || !posting!.Evidence.Any(item => MatchesAmortizationEvidence(item, evidence))))
            throw new LedgerValidationException("Amortization must bind the exact journal scope, effective date, acquisition and reference evidence.");
    }

    private static bool MatchesAmortizationEvidence(
        AccountingPostingEvidenceReferenceDto actual, RetainedEvidenceIdentityDto expected)
        => actual.Kind == AccountingPostingEvidenceKindDto.Source
           && actual.EvidenceId == expected.EvidenceId && actual.Uri == expected.EvidenceUri
           && actual.ContentHash == expected.ContentHashSha256 && actual.SourceSystem == expected.SourceSystem
           && actual.SourceReference == expected.SourceReference && actual.ReviewStatus == expected.ReviewStatus
           && actual.Reviewer == expected.ReviewedBy && actual.ReviewedAtUtc == expected.ReviewedAtUtc
           && actual.EffectiveDate == expected.EffectiveDate && actual.EvidenceVersion == expected.EvidenceVersion
           && actual.RetainedAtUtc == expected.RetainedAtUtc && actual.RetainedBy == expected.RetainedBy
           && actual.SubjectType == expected.SubjectType && actual.SubjectId == expected.SubjectId;

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
        if (JsonSerializer.Serialize(before.ToOpenLot()) != JsonSerializer.Serialize(instruction.ExpectedLot))
            throw new LedgerValidationException("Amortization lot version or carrying basis changed; rebuild and review the projection.");
        if (before.BasisAdjustment is { } prior
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
        var adjustment = new OpenLotBasisAdjustmentDto(command.MutationBatchId, OpenLotBasisAdjustmentReasons.Amortization,
            before.OpenQuantity, projection.TransactionCostBasis, projection.FunctionalCostBasis, command.Amortization);
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
}
