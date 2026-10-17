using System.Text.Json;
using Meridian.Contracts.Accounting.Lots;
using Meridian.Contracts.AssetOperations;
using Meridian.Contracts.Ledger;
using Meridian.Contracts.SecurityMaster;
using Meridian.Ledger;
using Meridian.Storage.AssetOperations;
using Meridian.Storage.SecurityMaster;
using Npgsql;

namespace Meridian.Storage.Ledger;

public sealed partial class PostgresLedgerJournalStore
{
    private static void ValidateAtomicCapitalReturn(AtomicTaxLotJournalCommand command)
    {
        var instruction = command.CapitalReturn
            ?? throw new LedgerValidationException("Capital-return posting requires the reviewed reduction instruction.");
        var issues = AssetLotMutationInstructionValidator.Validate(AssetAccountingEventKindDto.CorporateAction,
            new AssetLotMutationInstructionDto(AssetLotMutationIntentDto.CapitalReturn, CapitalReturn: instruction),
            instruction.Projection.EventAmount, command.Journal.Entry.Metadata.EffectiveDate ?? default, command.RetainedEvidence);
        if (issues.Count > 0)
            throw new LedgerValidationException(string.Join(" ", issues));
        if (command.AcquisitionLot is not null || command.DisposalSelections is { Count: > 0 }
            || command.Amortization is not null || command.CorporateAction is not null
            || command.ReliefMethod is not null || command.PolicyRevision is not null
            || command.CorrectsMutationBatchId is not null || command.Journal.SourceJournalEntryId is not null
            || command.Journal.PostingCommand?.SourceJournalEntryId is not null
            || command.Journal.PostingCommand?.Intent is not (AccountingPostingIntentDto.Originating or AccountingPostingIntentDto.Adjustment)
            || command.Journal.PostingKind is not (LedgerPostingKindDto.Originating or LedgerPostingKindDto.Adjustment))
            throw new LedgerValidationException("Capital return cannot combine other lot mutations or correction instructions.");
        if (command.Journal.PostingCommand is not { ApprovalState: AccountingPostingApprovalStateDto.Approved } posting
            || string.IsNullOrWhiteSpace(posting.ApprovalId) || string.IsNullOrWhiteSpace(posting.Actor)
            || !AssetAccountingEventTypeNames.TryParse(posting.SourceEventType, out var kind)
            || kind != AssetAccountingEventKindDto.CorporateAction
            || !JsonElement.DeepEquals(JsonSerializer.SerializeToElement(posting.LotCapitalReturn),
                JsonSerializer.SerializeToElement(instruction)))
            throw new LedgerValidationException("Capital return requires an approved corporate-action command retaining the exact reviewed instruction.");
        var lot = instruction.ExpectedLot;
        var scope = instruction.Projection.AccountingScope!;
        if (scope.PeriodId != command.Journal.PeriodId || scope.ExpectedPeriodVersion != command.ExpectedPeriodVersion
            || instruction.Projection.EconomicEvent!.EventId != command.SourceEventId
            || instruction.Projection.Treatment.AccountingBasis != command.Journal.AccountingBasis
            || lot.LedgerBookId != command.LedgerBookId || lot.SecurityId != command.Journal.Entry.Metadata.SecurityId
            || lot.Acquisition.FunctionalCurrency != ResolveAtomicFunctionalCurrency(command.Journal)
            || command.Journal.Entry.Metadata.Tags?.GetValueOrDefault(OpenLotCapitalReturn.JournalFingerprintTag)
                != OpenLotCapitalReturn.Fingerprint(instruction))
            throw new LedgerValidationException("Capital return must bind the exact book, period, event, currency and instruction fingerprint.");
        var amount = instruction.Projection.EventAmount;
        var lines = command.Journal.Entry.Lines;
        if (lines.Count != 2 || lines.Count(line => line.Debit == amount && line.Credit == 0m) != 1
            || lines.Count(line => line.Credit == amount && line.Debit == 0m) != 1
            || lines[0].Account == lines[1].Account
            || lines.Any(line => line.Account.AccountType != LedgerAccountType.Asset
                || line.Dimensions?.InstrumentId != lot.SecurityId || line.Dimensions?.PositionId != lot.BookPositionId
                || line.Dimensions?.TaxLotId is { } label && label != lot.LotId
                || line.Currency is not { } currency || currency.FunctionalCurrency != lot.Acquisition.FunctionalCurrency
                || currency.TransactionCurrency != lot.Acquisition.FunctionalCurrency || currency.FxRateToFunctional != 1m
                || currency.TransactionDebit != line.Debit || currency.TransactionCredit != line.Credit)
            || decimal.Round(amount, 10) != amount)
            throw new LedgerValidationException("Capital return requires the reviewed functional cash debit and carrying-account credit at durable precision.");
    }

    private async Task<LedgerTaxLotRecord> LockCapitalReturnAuthorityAsync(NpgsqlConnection connection,
        NpgsqlTransaction transaction, AtomicTaxLotJournalCommand command, CancellationToken ct)
    {
        var instruction = command.CapitalReturn!;
        var lot = instruction.ExpectedLot;
        var scope = instruction.Projection.AccountingScope!;
        var effectiveDate = instruction.Projection.EconomicEvent!.EffectiveDate;
        if (_backfillSecurityMaster?.Invoke() is not PostgresSecurityMasterStore securities
            || _backfillPositions?.Invoke() is not PostgresAssetOperationsProjectionStore positions)
            throw new LedgerValidationException("Capital return requires authoritative PostgreSQL Security Master and book-position stores.");
        if (_tenantScope.IsFailClosed && scope.TenantId != ResolveCallerTenant())
            throw new LedgerValidationException("Capital-return tenant must match the retained posting authority.");
        var security = await securities.LockForLotPostingAsync(connection, transaction, lot.SecurityId, ct).ConfigureAwait(false);
        if (security is null || security.Version != instruction.ExpectedSecurityVersion
            || OpenLotAmortization.SecurityHash(security) != instruction.ExpectedSecurityHash
            || security.Currency != lot.Acquisition.AcquisitionCurrency || security.Status != SecurityStatusDto.Active
            || DateOnly.FromDateTime(security.EffectiveFrom.UtcDateTime) > effectiveDate
            || security.EffectiveTo is { } securityEnd && DateOnly.FromDateTime(securityEnd.UtcDateTime) < effectiveDate)
            throw new LedgerValidationException("Capital-return Security Master identity, terms or effective date is stale.");
        var position = await positions.LockForLotPostingAsync(connection, transaction, lot.BookPositionId, ct).ConfigureAwait(false);
        var book = await LoadLedgerBookAsync(connection, transaction, command.LedgerBookId, ct).ConfigureAwait(false);
        if (position is null || book is null || position.Version != instruction.Projection.LotMutations!.ExpectedPositionVersion
            || position.PositionSide is not (BookPositionSides.Long or BookPositionSides.Asset) || position.Status != "Active"
            || position.SecurityId != lot.SecurityId || position.PositionId != lot.BookPositionId
            || position.BookContext.LedgerBookId != book.LedgerBookId
            || position.BookContext.BaseCurrency != book.BaseCurrency || book.BaseCurrency != lot.Acquisition.FunctionalCurrency
            || position.BookContext.FundProfileId != book.FundProfileId || scope.FundProfileId != book.FundProfileId
            || position.BookContext.FundStructureNodeId != book.FundStructureNodeId
            || position.BookContext.FundStructureNodeKind != book.FundStructureNodeKind
            || position.EffectiveFrom > effectiveDate || position.EffectiveTo is { } end && end < effectiveDate)
            throw new LedgerValidationException("Capital-return book-position version or accounting scope is stale.");
        var before = (await LoadTaxLotsForUpdateAsync(connection, transaction, command.LedgerBookId,
            [lot.TaxLotRecordId], ct).ConfigureAwait(false)).SingleOrDefault()
            ?? throw new LedgerValidationException("Capital-return lot no longer exists.");
        if (!OpenLotAmortization.SameLot(before.ToOpenLot(), lot)
            || command.Journal.Entry.Lines.Single(line => line.Credit > 0m).Account != before.Account)
            throw new LedgerValidationException("Capital-return lot version, basis or exact carrying account changed.");
        var history = await ReadDatedLotQuantitiesAsync(connection, transaction, command.LedgerBookId,
            [before.TaxLotRecordId], ct).ConfigureAwait(false);
        if (history.Any(mutation => mutation.EffectiveDate > effectiveDate)
            || HistoricalTaxLotQuantity.Project(before, history, effectiveDate).OpenQuantity != before.OpenQuantity)
            throw new LedgerValidationException("Capital return cannot precede retained lot mutations or use unproved holdings.");
        await CorporateActionSuccessorAncestry.ValidateCapitalReturnAsync(before, instruction, async batchId =>
        {
            var batch = await LoadAtomicTaxLotBatchAsync(connection, transaction, batchId, ct).ConfigureAwait(false);
            if (batch is null)
                return null;
            if (batch.LedgerBookId != command.LedgerBookId)
                throw new LedgerValidationException("Capital-return ancestry must remain within the locked ledger book.");
            return await LoadAtomicTaxLotResultAsync(connection, transaction, batchId, false, ct).ConfigureAwait(false);
        }, ValidatePriorReturnsAsync, ct).ConfigureAwait(false);
        return before;

        async Task ValidatePriorReturnsAsync(LedgerTaxLotRecord ancestor)
        {
            await using var query = connection.CreateCommand();
            query.Transaction = transaction;
            query.CommandText = $"""
                select distinct batches.capital_return_instruction::text
                from {Qualified("atomic_tax_lot_posting_batches")} batches
                join {Qualified("tax_lot_mutations")} mutations on mutations.mutation_batch_id = batches.mutation_batch_id
                where mutations.tax_lot_record_id = @lot and batches.ledger_book_id = @book
                    and batches.mutation_kind = 'CapitalReturn';
                """;
            query.Parameters.AddWithValue("lot", ancestor.TaxLotRecordId);
            query.Parameters.AddWithValue("book", ancestor.LedgerBookId);
            await using var reader = await query.ExecuteReaderAsync(ct).ConfigureAwait(false);
            var sourceId = OpenLotSuccessors.GetSourceCorporateActionId(instruction.Projection);
            while (await reader.ReadAsync(ct).ConfigureAwait(false))
            {
                var retained = JsonSerializer.Deserialize<OpenLotCapitalReturnInstructionDto>(reader.GetString(0), JsonOptions)
                    ?? throw new LedgerValidationException("Retained capital-return evidence is missing.");
                if (OpenLotSuccessors.GetSourceCorporateActionId(retained.Projection) == sourceId)
                    throw new LedgerValidationException("This source capital return has already reduced the lot or its predecessor; use the retained receipt.");
            }
        }
    }

    private async Task<LedgerTaxLotMutationRecord> ApplyCapitalReturnAsync(NpgsqlConnection connection,
        NpgsqlTransaction transaction, AtomicTaxLotJournalCommand command, LedgerTaxLotRecord before,
        DateTimeOffset recordedAt, CancellationToken ct)
    {
        var after = OpenLotCapitalReturn.Project(command.CapitalReturn!);
        var adjustment = new OpenLotBasisAdjustmentDto(command.MutationBatchId, OpenLotBasisAdjustmentReasons.CapitalReturn,
            before.OpenQuantity, after.OpenTransactionCostBasis, after.OpenFunctionalCostBasis, CapitalReturn: command.CapitalReturn);
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
            throw new LedgerValidationException("Capital-return lot compare-and-swap failed.");
        return BuildMutationRecord(command, ReadTaxLot(reader), before, 0, 0m, before.Version,
            command.CapitalReturn!.Projection.EvidenceManifest[0].EvidenceId, recordedAt, command.CapitalReturn.Projection.EventAmount);
    }
}
