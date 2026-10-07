using System.Text.Json;
using Meridian.Contracts.Accounting.Lots;
using Meridian.Contracts.AssetOperations;
using Meridian.Contracts.Ledger;
using Meridian.Ledger;
using Meridian.Storage.AssetOperations;
using Meridian.Storage.SecurityMaster;
using Npgsql;

namespace Meridian.Storage.Ledger;

public sealed partial class PostgresLedgerJournalStore
{
    private static void ValidateAtomicCorporateAction(AtomicTaxLotJournalCommand command)
    {
        var instruction = command.CorporateAction
            ?? throw new LedgerValidationException("Corporate-action posting requires the complete reviewed predecessor and successor instruction.");
        if (!AssetAccountingEventTypeNames.TryParse(command.Journal.PostingCommand?.SourceEventType, out var eventKind)
            || eventKind != AssetAccountingEventKindDto.CorporateAction
            || command.Journal.PostingCommand?.ApprovalState != AccountingPostingApprovalStateDto.Approved
            || string.IsNullOrWhiteSpace(command.Journal.PostingCommand?.ApprovalId))
            throw new LedgerValidationException("Corporate-action lot posting requires an approved corporate-action command with retained approval identity.");
        IReadOnlyList<OpenLotCorporateActionSuccessorProjectionDto> projections;
        try
        { projections = OpenLotCorporateAction.Project(instruction); }
        catch (ArgumentException exception) { throw new LedgerValidationException(exception.Message); }
        if (command.AcquisitionLot is not null || command.DisposalSelections is { Count: > 0 }
            || command.Amortization is not null || command.ReliefMethod is not null || command.PolicyRevision is not null
            || command.CorrectsMutationBatchId is not null || command.Journal.SourceJournalEntryId is not null
            || command.Journal.PostingCommand?.SourceJournalEntryId is not null
            || command.Journal.PostingCommand?.Intent is not (AccountingPostingIntentDto.Originating or AccountingPostingIntentDto.Adjustment)
            || command.Journal.PostingKind is not (LedgerPostingKindDto.Originating or LedgerPostingKindDto.Adjustment))
            throw new LedgerValidationException("Corporate-action posting cannot include acquisition, disposal, amortization or correction inputs.");
        if (command.Journal.PostingCommand?.LotCorporateAction is not { } retained
            || !JsonElement.DeepEquals(JsonSerializer.SerializeToElement(retained), JsonSerializer.SerializeToElement(instruction))
            || command.Journal.Entry.Metadata.Tags is not { } tags
            || !tags.TryGetValue("lotCorporateActionHash", out var hash) || hash != OpenLotCorporateAction.Fingerprint(instruction))
            throw new LedgerValidationException("The governed journal must retain the exact approved corporate-action inputs and instruction hash.");
        var scope = ResolveAtomicAssetScope(command.Journal);
        if (instruction.CorporateActionId != command.SourceEventId
            || instruction.CorporateActionId != command.Journal.PostingCommand?.EconomicEvent?.EventId
            || OpenLotCorporateAction.Groups(instruction).Any(group => group.ExpectedLot.LedgerBookId != command.LedgerBookId
                || group.ExpectedLot.SecurityId != scope.SecurityId || group.ExpectedLot.BookPositionId != scope.BookPositionId
                || group.ExpectedLot.Acquisition.FunctionalCurrency != ResolveAtomicFunctionalCurrency(command.Journal))
            || instruction.EffectiveDate != command.Journal.Entry.Metadata.EffectiveDate
            || OpenLotCorporateAction.Evidence(instruction).Any(e => !command.RetainedEvidence.Contains(e)))
            throw new LedgerValidationException("Corporate-action posting must bind the exact book, predecessor, effective date and retained source and successor evidence.");
        ValidateCorporateActionJournal(command, projections);
    }

    private static void ValidateCorporateActionJournal(AtomicTaxLotJournalCommand command,
        IReadOnlyList<OpenLotCorporateActionSuccessorProjectionDto> projections)
    {
        var instruction = command.CorporateAction!;
        var groups = OpenLotCorporateAction.Groups(instruction);
        var lines = command.Journal.Entry.Lines;
        if (lines.Count != projections.Count + groups.Count || lines.Any(line => line.Account.AccountType != LedgerAccountType.Asset)
            || lines.Select(line => line.EntryId).Distinct().Count() != lines.Count)
            throw new LedgerValidationException("Corporate-action journal must contain only one distinct asset credit per predecessor and one asset debit per successor; cash and gain are unsupported.");
        var selected = new HashSet<Guid>();
        foreach (var group in groups)
        {
            var lot = group.ExpectedLot;
            var source = CorporateActionSourceLine(command, group);
            if (!selected.Add(source.EntryId))
                throw new LedgerValidationException("Each reviewed predecessor must resolve to a distinct asset credit.");
            ValidateCorporateActionCurrency(source, lot.Acquisition, 0m, lot.OpenTransactionCostBasis);
            foreach (var projection in projections.Where(projection => projection.PredecessorTaxLotRecordId == lot.TaxLotRecordId))
            {
                var successor = projection.Successor;
                var line = CorporateActionSuccessorLine(command, projection);
                if (!selected.Add(line.EntryId)
                    || !string.Equals(line.Account.FinancialAccountId, source.Account.FinancialAccountId, StringComparison.OrdinalIgnoreCase))
                    throw new LedgerValidationException("Each successor must resolve to a distinct asset leg in the same financial-account scope as the predecessor.");
                ValidateCorporateActionCurrency(line, lot.Acquisition, projection.OpenTransactionCostBasis, 0m);
                var scale = lot.Acquisition.QuantityBasis == LotQuantityBasis.Face ? LedgerTaxLotFaceValueTerms.LedgerLotParBasis : 1m;
                var quantity = successor.Quantity / scale;
                var cost = projection.AcquisitionFunctionalCostBasis / quantity;
                if (decimal.Round(quantity, 12) != quantity || decimal.Round(cost, 12) != cost
                    || cost * quantity != projection.AcquisitionFunctionalCostBasis)
                    throw new LedgerValidationException("Successor acquisition quantity and unit cost must preserve the allocated original basis exactly at twelve-decimal storage precision.");
            }
        }
        if (selected.Count != lines.Count)
            throw new LedgerValidationException("Corporate-action journal contains a leg outside the complete reviewed predecessor and successor inventory.");
    }

    private static LedgerEntry CorporateActionSourceLine(AtomicTaxLotJournalCommand command,
        OpenLotCorporateActionPredecessorDto group)
    {
        var lot = group.ExpectedLot;
        var matches = command.Journal.Entry.Lines.Where(line => line.Account.ToString() == group.SourceAssetAccountId
            && line.Credit == lot.OpenFunctionalCostBasis && line.Debit == 0m
            && line.Dimensions?.InstrumentId == lot.SecurityId && line.Dimensions.PositionId == lot.BookPositionId
            && CorporateActionLineIdentifiesLot(command, line, lot.TaxLotRecordId)).ToArray();
        return matches.Length == 1 ? matches[0]
            : throw new LedgerValidationException("Corporate-action journal requires one exact predecessor carrying-basis credit identified by its reviewed lot.");
    }

    private static bool CorporateActionLineIdentifiesLot(AtomicTaxLotJournalCommand command, LedgerEntry line, Guid lotId)
        => string.Equals(line.Dimensions?.TaxLotId, lotId.ToString("D"), StringComparison.OrdinalIgnoreCase)
            || (OpenLotCorporateAction.Groups(command.CorporateAction!).Count == 1
                && command.Journal.Entry.Lines.All(item => item.Dimensions?.TaxLotId is null));

    private static LedgerEntry CorporateActionSuccessorLine(AtomicTaxLotJournalCommand command,
        OpenLotCorporateActionSuccessorProjectionDto projection)
    {
        var successor = projection.Successor;
        var matches = command.Journal.Entry.Lines.Where(line => line.Account.ToString() == successor.AssetAccountId
            && line.Debit == projection.OpenFunctionalCostBasis && line.Credit == 0m
            && line.Dimensions?.InstrumentId == successor.Security.SecurityId
            && line.Dimensions.PositionId == successor.BookPositionId
            && CorporateActionLineIdentifiesLot(command, line, successor.TaxLotRecordId)).ToArray();
        return matches.Length == 1 ? matches[0]
            : throw new LedgerValidationException("Each reviewed successor must resolve to exactly one matching asset account, security, position and allocated-basis debit.");
    }

    private static void ValidateCorporateActionCurrency(LedgerEntry line, OpenLotAcquisitionDto acquisition,
        decimal transactionDebit, decimal transactionCredit)
    {
        if (line.Currency is not { } currency || currency.TransactionCurrency != acquisition.AcquisitionCurrency
            || currency.FunctionalCurrency != acquisition.FunctionalCurrency
            || currency.FxRateToFunctional != acquisition.AcquisitionFxRateToFunctional
            || currency.TransactionDebit != transactionDebit || currency.TransactionCredit != transactionCredit
            || decimal.Round(currency.FxRateToFunctional, 10) != currency.FxRateToFunctional
            || decimal.Round(transactionDebit, 10) != transactionDebit || decimal.Round(transactionCredit, 10) != transactionCredit
            || decimal.Round(line.Debit, 10) != line.Debit || decimal.Round(line.Credit, 10) != line.Credit
            || Math.Abs(line.Debit - transactionDebit * currency.FxRateToFunctional) > 0.000000000001m
            || Math.Abs(line.Credit - transactionCredit * currency.FxRateToFunctional) > 0.000000000001m)
            throw new LedgerValidationException("Corporate-action journal transaction basis, currency and FX must exactly carry the predecessor acquisition convention at the ten-decimal journal storage boundary.");
    }

    private async Task<IReadOnlyList<LedgerTaxLotRecord>> LockCorporateActionAuthorityAsync(NpgsqlConnection connection,
        NpgsqlTransaction transaction, AtomicTaxLotJournalCommand command, CancellationToken ct)
    {
        var instruction = command.CorporateAction!;
        var groups = OpenLotCorporateAction.Groups(instruction);
        if (_backfillSecurityMaster?.Invoke() is not PostgresSecurityMasterStore securities
            || _backfillPositions?.Invoke() is not PostgresAssetOperationsProjectionStore positions)
            throw new LedgerValidationException("Corporate-action posting requires authoritative PostgreSQL Security Master and book-position stores in the same database.");
        // Fixed order: period (caller), all securities sorted, all positions sorted, then lots sorted.
        // Locks remain owned by the journal transaction until every successor and receipt commits.
        var reviewedSecurities = groups.SelectMany(group => group.Successors).Select(s => s.Security).Prepend(instruction.Security).ToArray();
        foreach (var group in reviewedSecurities.GroupBy(s => s.SecurityId).OrderBy(g => g.Key))
        {
            var current = await securities.LockForLotPostingAsync(connection, transaction, group.Key, ct).ConfigureAwait(false);
            if (current is null || group.Any(reviewed => OpenLotAmortization.SecurityHash(reviewed) != OpenLotAmortization.SecurityHash(current)))
                throw new LedgerValidationException("Corporate-action Security Master evidence is missing or stale; rebuild and review the projection.");
        }
        var book = await LoadLedgerBookAsync(connection, transaction, command.LedgerBookId, ct).ConfigureAwait(false)
            ?? throw new LedgerValidationException("Corporate-action ledger book was not found.");
        var reviewedPositions = groups.SelectMany(group => group.Successors).Select(s => (PositionId: s.BookPositionId,
                SecurityId: s.Security.SecurityId, Version: s.ExpectedBookPositionVersion))
            .Prepend((PositionId: instruction.ExpectedLot.BookPositionId, SecurityId: instruction.ExpectedLot.SecurityId,
                Version: instruction.ExpectedBookPositionVersion));
        BookPositionDto? sourcePosition = null;
        var retainedPositions = new List<BookPositionDto>();
        foreach (var group in reviewedPositions.GroupBy(p => p.PositionId).OrderBy(g => g.Key))
        {
            var current = await positions.LockForLotPostingAsync(connection, transaction, group.Key, ct).ConfigureAwait(false);
            if (current is null || group.Any(reviewed => reviewed.SecurityId != current.SecurityId || reviewed.Version != current.Version)
                || current.PositionId != group.Key || current.Status != "Active"
                || current.PositionSide is not (BookPositionSides.Long or BookPositionSides.Asset)
                || current.BookContext.LedgerBookId != command.LedgerBookId || current.BookContext.BaseCurrency != book.BaseCurrency
                || current.BookContext.FundProfileId != book.FundProfileId || current.BookContext.FundStructureNodeId != book.FundStructureNodeId
                || current.BookContext.FundStructureNodeKind != book.FundStructureNodeKind
                || current.BookContext.AccountingBasis != command.Journal.AccountingBasis
                || !RequiredAuthoritativeTextEquals(current.BookContext.AccountingPolicyId, book.AccountingPolicyId)
                || !RequiredAuthoritativeTextEquals(current.BookContext.AccountingPolicyVersion, book.AccountingPolicyVersion)
                || current.EffectiveFrom > instruction.EffectiveDate || (current.EffectiveTo is { } end && end < instruction.EffectiveDate))
                throw new LedgerValidationException("Corporate-action book-position version or accounting scope is missing or stale.");
            if (group.Key == instruction.ExpectedLot.BookPositionId)
                sourcePosition = current;
            retainedPositions.Add(current);
        }
        if (sourcePosition is null || retainedPositions.Any(p => p.BookContext.AccountingPolicyId != sourcePosition.BookContext.AccountingPolicyId
            || p.BookContext.AccountingPolicyVersion != sourcePosition.BookContext.AccountingPolicyVersion
            || !string.Equals(p.PrimaryAccountId, sourcePosition.PrimaryAccountId, StringComparison.OrdinalIgnoreCase)))
            throw new LedgerValidationException("Corporate-action successor positions must retain the predecessor accounting policy and financial-account scope.");
        // This bounded carryover may change instrument and position identity only. A different
        // sleeve, entity, investor or other canonical dimension requires a separately reviewed transfer.
        var sourceDimensions = CorporateActionPositionDimensions(sourcePosition);
        var retainedDimensions = retainedPositions.ToDictionary(position => position.PositionId,
            CorporateActionPositionDimensions);
        var sourceScope = JsonSerializer.SerializeToElement(sourceDimensions with { InstrumentId = null, PositionId = null });
        if (retainedDimensions.Values.Any(dimensions => !JsonElement.DeepEquals(sourceScope,
                JsonSerializer.SerializeToElement(dimensions with { InstrumentId = null, PositionId = null }))))
            throw new LedgerValidationException("Corporate-action successor positions must retain the predecessor canonical dimension scope.");
        foreach (var line in command.Journal.Entry.Lines)
        {
            if (line.Dimensions is not { } dimensions || dimensions.PositionId is not { } positionId
                || !retainedDimensions.TryGetValue(positionId, out var expected)
                || dimensions.InstrumentId != expected.InstrumentId
                || (!string.IsNullOrWhiteSpace(dimensions.FundId) && dimensions.FundId != expected.FundId)
                || (!string.IsNullOrWhiteSpace(dimensions.BookId)
                    && !string.Equals(dimensions.BookId, expected.BookId, StringComparison.OrdinalIgnoreCase))
                || !JsonElement.DeepEquals(JsonSerializer.SerializeToElement(expected),
                    JsonSerializer.SerializeToElement(dimensions with
                    { FundId = expected.FundId, BookId = expected.BookId, TaxLotId = expected.TaxLotId })))
                throw new LedgerValidationException("Corporate-action journal legs must exactly retain each locked book-position canonical dimension scope.");
        }
        var lots = await LoadCorporateActionLotsForUpdateAsync(connection, transaction, command, ct).ConfigureAwait(false);
        var predecessorIds = groups.Select(group => group.ExpectedLot.TaxLotRecordId).ToHashSet();
        var successorIds = groups.SelectMany(group => group.Successors).Select(successor => successor.TaxLotRecordId).ToHashSet();
        var history = await ReadDatedLotQuantitiesAsync(connection, transaction, command.LedgerBookId,
            lots.Select(lot => lot.TaxLotRecordId).Order().ToArray(), ct).ConfigureAwait(false);
        if (history.Any(mutation => mutation.EffectiveDate > instruction.EffectiveDate))
            throw new LedgerValidationException("Corporate-action posting cannot precede a retained source-position lot mutation.");
        var histories = history.ToLookup(mutation => mutation.TaxLotRecordId);
        var affected = lots.Where(lot => lot.BookPositionId == sourcePosition.PositionId
            && lot.AcquiredDate <= instruction.EffectiveDate
            && HistoricalTaxLotQuantity.Project(lot, histories[lot.TaxLotRecordId].ToArray(), instruction.EffectiveDate).OpenQuantity > 0m);
        if (lots.Any(lot => successorIds.Contains(lot.TaxLotRecordId))
            || !predecessorIds.SetEquals(affected.Select(lot => lot.TaxLotRecordId)))
            throw new LedgerValidationException("Corporate-action predecessor inventory changed; posting requires the complete open inventory for the affected source position and new successor identities.");
        var retained = lots.ToDictionary(lot => lot.TaxLotRecordId);
        foreach (var group in groups)
        {
            var before = retained[group.ExpectedLot.TaxLotRecordId];
            if (before.Account.AccountType != LedgerAccountType.Asset || before.Account.ToString() != group.SourceAssetAccountId
                || before.Currency != group.ExpectedLot.Acquisition.FunctionalCurrency
                || (sourcePosition.PrimaryAccountId is not null
                    && !string.Equals(sourcePosition.PrimaryAccountId, before.Account.FinancialAccountId, StringComparison.OrdinalIgnoreCase))
                || !JsonElement.DeepEquals(JsonSerializer.SerializeToElement(before.ToOpenLot()), JsonSerializer.SerializeToElement(group.ExpectedLot))
                || CorporateActionSourceLine(command, group).Account != before.Account)
                throw new LedgerValidationException("Corporate-action predecessor account, version, quantity or basis changed; rebuild and review the projection.");
            var lotHistory = histories[before.TaxLotRecordId].ToArray();
            if (lotHistory.Any(m => m.EffectiveDate > instruction.EffectiveDate)
                || HistoricalTaxLotQuantity.Project(before, lotHistory, instruction.EffectiveDate).OpenQuantity != before.OpenQuantity)
                throw new LedgerValidationException("Corporate-action posting cannot precede a retained predecessor mutation or use unproved historical holdings.");
        }
        return groups.Select(group => retained[group.ExpectedLot.TaxLotRecordId]).ToArray();
    }

    private async Task<IReadOnlyList<LedgerTaxLotRecord>> LoadCorporateActionLotsForUpdateAsync(
        NpgsqlConnection connection, NpgsqlTransaction transaction, AtomicTaxLotJournalCommand command, CancellationToken ct)
    {
        var groups = OpenLotCorporateAction.Groups(command.CorporateAction!);
        var identities = groups.SelectMany(group => group.Successors.Select(successor => successor.TaxLotRecordId)
            .Prepend(group.ExpectedLot.TaxLotRecordId)).Distinct().ToArray();
        await using var query = connection.CreateCommand();
        query.Transaction = transaction;
        // Include closed lots: an omitted lot may have been open on the action date and relieved
        // afterward. Dated history below must agree with the current reviewed inventory.
        query.CommandText = $"""
            select {AverageCostLotColumns} from {Qualified("tax_lots")}
            where ledger_book_id = @book
                and (tax_lot_record_id = any(@identities) or (book_position_id = @position and acquired_date <= @effective_date))
            order by tax_lot_record_id for update;
            """;
        query.Parameters.AddWithValue("book", command.LedgerBookId);
        query.Parameters.AddWithValue("identities", identities);
        query.Parameters.AddWithValue("position", command.CorporateAction!.ExpectedLot.BookPositionId);
        query.Parameters.AddWithValue("effective_date", command.CorporateAction.EffectiveDate);
        var lots = new List<LedgerTaxLotRecord>();
        await using var reader = await query.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await reader.ReadAsync(ct).ConfigureAwait(false))
            lots.Add(ReadTaxLot(reader));
        return lots;
    }

    private static LedgerDimensionSetDto CorporateActionPositionDimensions(BookPositionDto position)
    {
        var dimensions = position.BookContext.Dimensions ?? new LedgerDimensionSetDto();
        var fundId = position.BookContext.FundProfileId;
        var bookId = position.BookContext.LedgerBookId.ToString("D");
        if ((!string.IsNullOrWhiteSpace(dimensions.FundId) && dimensions.FundId != fundId)
            || (!string.IsNullOrWhiteSpace(dimensions.BookId)
                && !string.Equals(dimensions.BookId, bookId, StringComparison.OrdinalIgnoreCase))
            || (dimensions.InstrumentId.HasValue && dimensions.InstrumentId != position.SecurityId)
            || (dimensions.PositionId.HasValue && dimensions.PositionId != position.PositionId))
            throw new LedgerValidationException("Corporate-action book-position dimensions contradict their authoritative fund, book, security or position identity.");
        return dimensions with
        {
            FundId = fundId,
            BookId = bookId,
            InstrumentId = position.SecurityId,
            PositionId = position.PositionId
        };
    }

    private async Task<IReadOnlyList<LedgerTaxLotMutationRecord>> ApplyCorporateActionAsync(NpgsqlConnection connection,
        NpgsqlTransaction transaction, AtomicTaxLotJournalCommand command, IReadOnlyList<LedgerTaxLotRecord> beforeLots,
        DateTimeOffset recordedAt, CancellationToken ct)
    {
        var instruction = command.CorporateAction!;
        var projections = OpenLotCorporateAction.Project(instruction);
        var retained = beforeLots.ToDictionary(lot => lot.TaxLotRecordId);
        var mutations = new List<LedgerTaxLotMutationRecord>();
        foreach (var group in OpenLotCorporateAction.Groups(instruction))
        {
            var before = retained[group.ExpectedLot.TaxLotRecordId];
            LedgerTaxLotRecord closed;
            await using (var update = connection.CreateCommand())
            {
                update.Transaction = transaction;
                update.CommandText = $"""
                    update {Qualified("tax_lots")} set open_quantity = 0, version = version + 1,
                        last_mutation_batch_id = @batch, updated_at = @at
                    where tax_lot_record_id = @id and ledger_book_id = @book and version = @version and open_quantity = @quantity
                    returning {AverageCostLotColumns};
                    """;
                update.Parameters.AddWithValue("batch", command.MutationBatchId);
                update.Parameters.AddWithValue("at", recordedAt.UtcDateTime);
                update.Parameters.AddWithValue("id", before.TaxLotRecordId);
                update.Parameters.AddWithValue("book", command.LedgerBookId);
                update.Parameters.AddWithValue("version", before.Version);
                update.Parameters.AddWithValue("quantity", before.OpenQuantity);
                await using var reader = await update.ExecuteReaderAsync(ct).ConfigureAwait(false);
                if (!await reader.ReadAsync(ct).ConfigureAwait(false))
                    throw new LedgerValidationException("Corporate-action predecessor compare-and-swap failed.");
                closed = ReadTaxLot(reader);
            }
            mutations.Add(BuildMutationRecord(command, closed, before, mutations.Count,
                -before.OpenQuantity, before.Version, instruction.SecurityEvidence.EvidenceId, recordedAt,
                group.ExpectedLot.OpenFunctionalCostBasis, AtomicTaxLotMutationKind.CorporateActionClose));
            foreach (var projection in projections.Where(projection => projection.PredecessorTaxLotRecordId == before.TaxLotRecordId))
            {
                var successor = projection.Successor;
                var face = before.Acquisition!.QuantityBasis == LotQuantityBasis.Face;
                var quantity = successor.Quantity / (face ? LedgerTaxLotFaceValueTerms.LedgerLotParBasis : 1m);
                var acquisition = before.Acquisition with
                {
                    TransactionCostBasis = projection.AcquisitionTransactionCostBasis,
                    FunctionalCostBasis = projection.AcquisitionFunctionalCostBasis,
                    Evidence = OpenLotCorporateAction.Evidence(instruction),
                    CorporateActionLineage = new(instruction.CorporateActionId, instruction.ActionType, instruction.EffectiveDate,
                        before.TaxLotRecordId, before.Version, successor.BasisAllocationPercent, successor.Role, successor.ReportingTags)
                };
                var adjustment = projection.AcquisitionTransactionCostBasis != projection.OpenTransactionCostBasis
                    || projection.AcquisitionFunctionalCostBasis != projection.OpenFunctionalCostBasis
                    ? new OpenLotBasisAdjustmentDto(command.MutationBatchId, OpenLotBasisAdjustmentReasons.CorporateAction, quantity,
                        projection.OpenTransactionCostBasis, projection.OpenFunctionalCostBasis) : null;
                var lot = new LedgerTaxLotRecord(successor.TaxLotRecordId, command.LedgerBookId,
                    CorporateActionSuccessorLine(command, projection).Account, successor.LotId, before.AcquiredDate,
                    quantity, quantity, projection.AcquisitionFunctionalCostBasis / quantity, before.Currency, recordedAt, recordedAt,
                    command.Journal.Entry.JournalEntryId, successor.AcquisitionEvidence.EvidenceId, 1, command.MutationBatchId,
                    command.MutationBatchId, successor.Security.SecurityId, successor.BookPositionId,
                    face ? successor.Quantity : null, before.BookedFactor, before.ParBasis, acquisition, adjustment);
                _ = lot.ToOpenLot();
                await using var insert = connection.CreateCommand();
                insert.Transaction = transaction;
                insert.CommandText = $"""
                    insert into {Qualified("tax_lots")} ({AverageCostLotColumns}) values (
                        @tax_lot_record_id, @ledger_book_id, @account_name, @account_type, @symbol, @financial_account_id,
                        @lot_id, @acquired_date, @original_quantity, @open_quantity, @unit_cost, @currency, @source_journal_entry_id,
                        @evidence_ref, @version, @originating_mutation_batch_id, @last_mutation_batch_id, @created_at, @updated_at,
                        @security_id, @book_position_id, @original_face, @booked_factor, @par_basis, @acquisition_terms, @basis_adjustment)
                    returning {AverageCostLotColumns};
                    """;
                AddAtomicTaxLotParameters(insert, lot);
                AddBasisAdjustmentParameter(insert, adjustment);
                await using var reader = await insert.ExecuteReaderAsync(ct).ConfigureAwait(false);
                if (!await reader.ReadAsync(ct).ConfigureAwait(false))
                    throw new LedgerValidationException("Corporate-action successor was not retained.");
                var after = ReadTaxLot(reader);
                _ = after.ToOpenLot();
                mutations.Add(BuildMutationRecord(command, after, null, mutations.Count, quantity, 0,
                    successor.AcquisitionEvidence.EvidenceId, recordedAt, projection.OpenFunctionalCostBasis,
                    AtomicTaxLotMutationKind.CorporateActionSuccessor));
            }
        }
        return mutations;
    }
}
