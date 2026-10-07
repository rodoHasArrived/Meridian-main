using System.Data;
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

/// <summary>Reads the committed journal, predecessor/successor snapshots and reviewed allocation together.</summary>
public interface ILedgerOpenLotSuccessorHistory
{
    Task<IReadOnlyList<AtomicTaxLotJournalResult>> GetOpenLotSuccessorHistoryAsync(
        Guid ledgerBookId, IReadOnlyList<Guid> journalEntryIds, CancellationToken ct = default);
}

public sealed partial class PostgresLedgerJournalStore : ILedgerOpenLotSuccessorHistory
{
    private static void ValidateAtomicCorporateAction(AtomicTaxLotJournalCommand command)
    {
        var instruction = command.CorporateAction
            ?? throw new LedgerValidationException("Corporate-action posting requires the reviewed successor instruction.");
        try
        { OpenLotSuccessors.Validate(instruction); }
        catch (ArgumentException exception) { throw new LedgerValidationException(exception.Message); }
        if (command.AcquisitionLot is not null || command.DisposalSelections is { Count: > 0 }
            || command.Amortization is not null || command.ReliefMethod is not null || command.PolicyRevision is not null
            || command.CorrectsMutationBatchId is not null || command.Journal.SourceJournalEntryId is not null
            || command.Journal.PostingCommand?.SourceJournalEntryId is not null
            || command.Journal.PostingCommand?.Intent is not (AccountingPostingIntentDto.Originating or AccountingPostingIntentDto.Adjustment)
            || command.Journal.PostingKind is not (LedgerPostingKindDto.Originating or LedgerPostingKindDto.Adjustment))
            throw new LedgerValidationException("Successor posting cannot combine acquisition, relief, amortization or correction instructions.");
        if (command.Journal.PostingCommand is not { ApprovalState: AccountingPostingApprovalStateDto.Approved } posting
            || string.IsNullOrWhiteSpace(posting.ApprovalId) || string.IsNullOrWhiteSpace(posting.Actor)
            || !AssetAccountingEventTypeNames.TryParse(posting.SourceEventType, out var eventKind)
            || eventKind != AssetAccountingEventKindDto.CorporateAction)
            throw new LedgerValidationException("Successor posting requires a canonical approved corporate-action command, retained reviewer approval and named posting actor.");
        if (!JsonElement.DeepEquals(JsonSerializer.SerializeToElement(command.Journal.PostingCommand?.LotCorporateAction),
                JsonSerializer.SerializeToElement(instruction)))
            throw new LedgerValidationException("The governed journal must retain the exact reviewed successor instruction.");
        var source = instruction.ExpectedLot;
        var scope = instruction.Projection.AccountingScope!;
        if (scope.PeriodId != command.Journal.PeriodId || scope.ExpectedPeriodVersion != command.ExpectedPeriodVersion
            || instruction.Projection.EconomicEvent?.EventId != command.SourceEventId
            || instruction.Projection.Treatment.AccountingBasis != command.Journal.AccountingBasis
            || source.LedgerBookId != command.LedgerBookId
            || source.SecurityId != command.Journal.Entry.Metadata.SecurityId
            || source.Acquisition.FunctionalCurrency != ResolveAtomicFunctionalCurrency(command.Journal)
            || instruction.Projection.EconomicEvent?.EffectiveDate != command.Journal.Entry.Metadata.EffectiveDate
            || command.Journal.Entry.Metadata.Tags?.GetValueOrDefault("openLotSuccessorInstructionFingerprint") != OpenLotSuccessors.Fingerprint(instruction))
            throw new LedgerValidationException("Successor posting must bind the exact book, event date, functional currency and retained instruction fingerprint.");
        var acquisitionEvidence = instruction.Successors.SelectMany(target => target.Lot.Acquisition.Evidence)
            .Concat(source.Acquisition.Evidence);
        if (acquisitionEvidence.Any(evidence => !command.RetainedEvidence.Contains(evidence)))
            throw new LedgerValidationException("Successor posting must retain every predecessor and successor acquisition evidence identity.");
        foreach (var evidence in instruction.Projection.EvidenceManifest)
        {
            if (!command.RetainedEvidence.Any(retained => retained.EvidenceId == evidence.EvidenceId
                && retained.EvidenceVersion == evidence.EvidenceVersion && retained.EvidenceUri == evidence.EvidenceUri
                && retained.ContentHashSha256 == evidence.ContentHashSha256 && retained.SubjectType == evidence.SubjectType
                && retained.SubjectId == evidence.SubjectId))
                throw new LedgerValidationException("Successor posting must retain the complete approved projection evidence manifest.");
        }
        ValidateCorporateActionJournal(command);
    }

    private static void ValidateCorporateActionJournal(AtomicTaxLotJournalCommand command)
    {
        var instruction = command.CorporateAction!;
        var source = instruction.ExpectedLot;
        var lines = command.Journal.Entry.Lines;
        if (lines.Count != instruction.Successors.Count + 1)
            throw new LedgerValidationException("Cashless successor posting requires exactly one predecessor credit and one debit per successor.");
        ValidateCorporateActionLine(FindCorporateActionLine(lines, source, debit: false), source, debit: false);
        foreach (var target in instruction.Successors)
            ValidateCorporateActionLine(FindCorporateActionLine(lines, target.Lot, debit: true), target.Lot, debit: true);
    }

    private static LedgerEntry FindCorporateActionLine(IReadOnlyList<LedgerEntry> lines, OpenLotDto lot, bool debit)
    {
        var scopeLines = lines.Where(line => line.Dimensions?.InstrumentId == lot.SecurityId
            && line.Dimensions?.PositionId == lot.BookPositionId).ToArray();
        var matches = scopeLines.Where(line => (debit ? line.Debit > 0m && line.Credit == 0m : line.Credit > 0m && line.Debit == 0m)
            && (scopeLines.Length > 1 ? line.Dimensions?.TaxLotId == lot.LotId
                : line.Dimensions?.TaxLotId is null || line.Dimensions.TaxLotId == lot.LotId)).ToArray();
        return matches.Length == 1 ? matches[0]
            : throw new LedgerValidationException("Successor journals require one exact security and book-position line per predecessor and successor.");
    }

    private static void ValidateCorporateActionLine(LedgerEntry line, OpenLotDto lot, bool debit)
    {
        var acquisition = lot.Acquisition;
        if (line.Account.AccountType != LedgerAccountType.Asset || lot.OpenFunctionalCostBasis <= 0m
            || line.Debit != (debit ? lot.OpenFunctionalCostBasis : 0m)
            || line.Credit != (debit ? 0m : lot.OpenFunctionalCostBasis)
            || line.Currency is not { } currency || currency.FunctionalCurrency != acquisition.FunctionalCurrency)
            throw new LedgerValidationException("Successor journal lines must exactly reconcile the allocated functional carrying basis.");
        // The governed mapper may book a functional-only transfer. Its independently allocated
        // transaction basis and original FX remain authoritative in the retained lot instruction;
        // do not recreate them by translating the independently rounded functional allocation.
        var functionalOnly = currency.TransactionCurrency == acquisition.FunctionalCurrency
            && currency.FxRateToFunctional == 1m
            && currency.TransactionDebit == line.Debit && currency.TransactionCredit == line.Credit;
        var acquisitionCurrency = currency.TransactionCurrency == acquisition.AcquisitionCurrency
            && currency.FxRateToFunctional == acquisition.AcquisitionFxRateToFunctional
            && currency.TransactionDebit == (debit ? lot.OpenTransactionCostBasis : 0m)
            && currency.TransactionCredit == (debit ? 0m : lot.OpenTransactionCostBasis);
        if (!functionalOnly && !acquisitionCurrency)
            throw new LedgerValidationException("Successor journal currency must retain either exact functional amounts or allocated transaction basis at acquisition FX.");
        if (new[] { line.Debit, line.Credit, currency.TransactionDebit, currency.TransactionCredit, currency.FxRateToFunctional }
            .Any(value => decimal.Round(value, 10) != value))
            throw new LedgerValidationException("Successor journal amounts and FX must be exactly representable at durable ten-decimal precision.");
    }

    private async Task<LedgerTaxLotRecord> LockCorporateActionAuthorityAsync(
        NpgsqlConnection connection, NpgsqlTransaction transaction, AtomicTaxLotJournalCommand command, CancellationToken ct)
    {
        var instruction = command.CorporateAction!;
        if (_backfillSecurityMaster?.Invoke() is not PostgresSecurityMasterStore securities
            || _backfillPositions?.Invoke() is not PostgresAssetOperationsProjectionStore positions)
            throw new LedgerValidationException("Successor posting requires authoritative PostgreSQL Security Master and book-position stores.");
        var source = instruction.ExpectedLot;
        var effectiveDate = command.Journal.Entry.Metadata.EffectiveDate!.Value;
        if (_tenantScope.IsFailClosed && instruction.Projection.AccountingScope!.TenantId != ResolveCallerTenant())
            throw new LedgerValidationException("Successor projection tenant must match the retained posting authority.");
        var securityGuards = instruction.Successors.Select(target =>
                (target.Lot.SecurityId, Version: target.ExpectedSecurityVersion, Hash: target.ExpectedSecurityHash))
            .Append((SecurityId: source.SecurityId, Version: instruction.ExpectedSecurityVersion, Hash: instruction.ExpectedSecurityHash)).ToArray();
        foreach (var group in securityGuards.GroupBy(guard => guard.SecurityId).OrderBy(group => group.Key))
        {
            if (group.Select(guard => (guard.Version, guard.Hash)).Distinct().Count() != 1)
                throw new LedgerValidationException("Successor security authority contains inconsistent expected versions or hashes.");
            var expected = group.First();
            var actual = await securities.LockForLotPostingAsync(connection, transaction, group.Key, ct).ConfigureAwait(false);
            if (actual is null || actual.Version != expected.Version || OpenLotAmortization.SecurityHash(actual) != expected.Hash
                || actual.Currency != source.Acquisition.AcquisitionCurrency || actual.Status != SecurityStatusDto.Active
                || DateOnly.FromDateTime(actual.EffectiveFrom.UtcDateTime) > effectiveDate
                || (actual.EffectiveTo is { } until && DateOnly.FromDateTime(until.UtcDateTime) < effectiveDate))
                throw new LedgerValidationException("Successor Security Master version or hash is missing or stale; rebuild and review the projection.");
        }
        var book = await LoadLedgerBookAsync(connection, transaction, command.LedgerBookId, ct).ConfigureAwait(false)
            ?? throw new LedgerValidationException("Successor posting ledger book is missing.");
        if (book.FundProfileId != instruction.Projection.AccountingScope!.FundProfileId
            || book.AccountingBasis != instruction.Projection.Treatment.AccountingBasis
            || book.BaseCurrency != source.Acquisition.FunctionalCurrency)
            throw new LedgerValidationException("Successor projection accounting basis, fund or currency differs from the retained book.");
        var positionGuards = instruction.Successors.Select(target =>
                (Lot: target.Lot, Version: target.ExpectedBookPositionVersion))
            .Append((Lot: source, Version: instruction.Projection.LotMutations!.ExpectedPositionVersion)).ToArray();
        var retainedPositions = new Dictionary<Guid, BookPositionDto>();
        foreach (var group in positionGuards.GroupBy(guard => guard.Lot.BookPositionId).OrderBy(group => group.Key))
        {
            if (group.Select(guard => (guard.Lot.SecurityId, guard.Version)).Distinct().Count() != 1)
                throw new LedgerValidationException("Successor position authority contains inconsistent expected versions or security identities.");
            var guard = group.First();
            var position = await positions.LockForLotPostingAsync(connection, transaction, guard.Lot.BookPositionId, ct).ConfigureAwait(false);
            if (position is null || position.Version != guard.Version || position.PositionId != guard.Lot.BookPositionId
                || position.SecurityId != guard.Lot.SecurityId || position.Status != "Active"
                || position.PositionSide is not (BookPositionSides.Long or BookPositionSides.Asset)
                || position.BookContext.LedgerBookId != command.LedgerBookId
                || position.BookContext.AccountingBasis != book.AccountingBasis
                || position.BookContext.BaseCurrency != book.BaseCurrency
                || position.BookContext.FundProfileId != book.FundProfileId
                || position.BookContext.FundStructureNodeId != book.FundStructureNodeId
                || position.BookContext.FundStructureNodeKind != book.FundStructureNodeKind
                || !RequiredAuthoritativeTextEquals(position.BookContext.AccountingPolicyId, book.AccountingPolicyId)
                || !RequiredAuthoritativeTextEquals(position.BookContext.AccountingPolicyVersion, book.AccountingPolicyVersion)
                || position.EffectiveFrom > effectiveDate || (position.EffectiveTo is { } end && end < effectiveDate))
                throw new LedgerValidationException("Successor book-position version or accounting scope is missing or stale.");
            retainedPositions.Add(position.PositionId, position);
        }
        var sourcePosition = retainedPositions[source.BookPositionId];
        if (retainedPositions.Values.Any(position => !string.Equals(position.PrimaryAccountId, sourcePosition.PrimaryAccountId,
                StringComparison.OrdinalIgnoreCase)))
            throw new LedgerValidationException("Successor positions must retain the predecessor financial-account scope.");
        var sourceDimensions = CorporateActionPositionDimensions(sourcePosition);
        var sourceDimensionScope = JsonSerializer.SerializeToElement(sourceDimensions with
        { InstrumentId = null, PositionId = null, TaxLotId = null });
        foreach (var position in retainedPositions.Values)
            if (!JsonElement.DeepEquals(sourceDimensionScope, JsonSerializer.SerializeToElement(CorporateActionPositionDimensions(position) with
            { InstrumentId = null, PositionId = null, TaxLotId = null })))
                throw new LedgerValidationException("Successor positions must retain the predecessor canonical dimensions; cross-scope transfers require separate review.");
        foreach (var line in command.Journal.Entry.Lines)
        {
            if (line.Dimensions is not { } dimensions || dimensions.PositionId is not { } positionId
                || !retainedPositions.TryGetValue(positionId, out var position))
                throw new LedgerValidationException("Successor journal leg lacks a locked authoritative book-position scope.");
            var expected = CorporateActionPositionDimensions(position);
            // Reviewed lot labels may change at a split/transfer, independently of position dimensions.
            // ValidateCorporateActionJournal already binds each label to its exact source/target lot.
            if (dimensions.InstrumentId != expected.InstrumentId
                || (!string.IsNullOrWhiteSpace(dimensions.FundId) && dimensions.FundId != expected.FundId)
                || (!string.IsNullOrWhiteSpace(dimensions.BookId) && !string.Equals(dimensions.BookId, expected.BookId, StringComparison.OrdinalIgnoreCase))
                || !JsonElement.DeepEquals(JsonSerializer.SerializeToElement(expected with { TaxLotId = dimensions.TaxLotId }),
                    JsonSerializer.SerializeToElement(dimensions with { FundId = expected.FundId, BookId = expected.BookId })))
                throw new LedgerValidationException("Successor journal legs must retain the exact locked canonical position dimensions.");
        }
        await using (var targetGuard = connection.CreateCommand())
        {
            targetGuard.Transaction = transaction;
            targetGuard.CommandText = $"select exists(select 1 from {Qualified("tax_lots")} where tax_lot_record_id = any(@targets));";
            targetGuard.Parameters.AddWithValue("targets", instruction.Successors.Select(target => target.Lot.TaxLotRecordId).ToArray());
            if ((bool)(await targetGuard.ExecuteScalarAsync(ct).ConfigureAwait(false))!)
                throw new LedgerValidationException("Successor target identity is already retained; only new successor lots are supported.");
        }
        var lotIds = instruction.Successors.Select(target => target.Lot.TaxLotRecordId).Append(source.TaxLotRecordId).ToArray();
        var existing = await LoadTaxLotsForUpdateAsync(connection, transaction, command.LedgerBookId, lotIds, ct).ConfigureAwait(false);
        if (existing.Count != 1 || existing[0].TaxLotRecordId != source.TaxLotRecordId)
            throw new LedgerValidationException("Successor targets must be new lots and the exact predecessor must still exist.");
        var before = existing[0];
        if ((sourcePosition.PrimaryAccountId is not null
                && !string.Equals(sourcePosition.PrimaryAccountId, before.Account.FinancialAccountId, StringComparison.OrdinalIgnoreCase))
            || !JsonElement.DeepEquals(JsonSerializer.SerializeToElement(before.ToOpenLot()), JsonSerializer.SerializeToElement(source))
            || before.Account != FindCorporateActionLine(command.Journal.Entry.Lines, source, debit: false).Account)
            throw new LedgerValidationException("Successor predecessor version, identity, acquisition facts or current basis changed; rebuild and review.");
        if (instruction.Successors.Any(target => FindCorporateActionLine(command.Journal.Entry.Lines, target.Lot, debit: true).Account
            != before.Account))
            throw new LedgerValidationException("Successor posting cannot transfer lots between ledger accounts.");
        await CorporateActionSuccessorAncestry.ValidateAsync(before, instruction, async birthBatchId =>
        {
            var batch = await LoadAtomicTaxLotBatchAsync(connection, transaction, birthBatchId, ct).ConfigureAwait(false);
            if (batch is null)
                return null;
            if (batch.LedgerBookId != command.LedgerBookId)
                throw new LedgerValidationException("Successor ancestry must remain within the locked ledger book.");
            return await LoadAtomicTaxLotResultAsync(connection, transaction, birthBatchId, false, ct).ConfigureAwait(false);
        }, ct).ConfigureAwait(false);
        var history = await ReadDatedLotQuantitiesAsync(connection, transaction, command.LedgerBookId,
            [before.TaxLotRecordId], ct).ConfigureAwait(false);
        if (history.Any(mutation => mutation.EffectiveDate > effectiveDate)
            || HistoricalTaxLotQuantity.Project(before, history, effectiveDate).OpenQuantity != before.OpenQuantity)
            throw new LedgerValidationException("Successor posting cannot precede retained lot mutations or use unproved partial holdings.");
        // Certify durable decimal representation and canonical snapshots before the journal is inserted.
        foreach (var target in instruction.Successors)
            _ = CreateCorporateActionSuccessor(command, target.Lot, before.CreatedAt);
        return before;
    }

    private static LedgerDimensionSetDto CorporateActionPositionDimensions(BookPositionDto position)
    {
        var dimensions = position.BookContext.Dimensions ?? new LedgerDimensionSetDto();
        var fundId = position.BookContext.FundProfileId;
        var bookId = position.BookContext.LedgerBookId.ToString("D");
        if ((!string.IsNullOrWhiteSpace(dimensions.FundId) && dimensions.FundId != fundId)
            || (!string.IsNullOrWhiteSpace(dimensions.BookId) && !string.Equals(dimensions.BookId, bookId, StringComparison.OrdinalIgnoreCase))
            || (dimensions.InstrumentId.HasValue && dimensions.InstrumentId != position.SecurityId)
            || (dimensions.PositionId.HasValue && dimensions.PositionId != position.PositionId))
            throw new LedgerValidationException("Successor position dimensions contradict its authoritative fund, book, security or position identity.");
        return dimensions with { FundId = fundId, BookId = bookId, InstrumentId = position.SecurityId, PositionId = position.PositionId };
    }

    private static LedgerTaxLotRecord CreateCorporateActionSuccessor(
        AtomicTaxLotJournalCommand command, OpenLotDto target, DateTimeOffset recordedAt)
    {
        target = OpenLotSuccessors.WithLineage(command.CorporateAction!, target);
        var face = target.Acquisition.QuantityBasis == LotQuantityBasis.Face;
        var quantity = target.OriginalQuantity / (face ? LedgerTaxLotFaceValueTerms.LedgerLotParBasis : 1m);
        var unitCost = target.Acquisition.FunctionalCostBasis / quantity;
        if (decimal.Round(quantity, 12) != quantity || decimal.Round(unitCost, 12) != unitCost
            || quantity * unitCost != target.Acquisition.FunctionalCostBasis
            || decimal.Round(target.OpenTransactionCostBasis, 12) != target.OpenTransactionCostBasis
            || decimal.Round(target.OpenFunctionalCostBasis, 12) != target.OpenFunctionalCostBasis)
            throw new LedgerValidationException("Successor quantity and basis must be exactly representable at durable twelve-decimal precision.");
        var adjustment = new OpenLotBasisAdjustmentDto(command.MutationBatchId,
            OpenLotBasisAdjustmentReasons.CorporateActionSuccessor, quantity,
            target.OpenTransactionCostBasis, target.OpenFunctionalCostBasis, CorporateAction: command.CorporateAction);
        var result = new LedgerTaxLotRecord(target.TaxLotRecordId, command.LedgerBookId,
            FindCorporateActionLine(command.Journal.Entry.Lines, target, debit: true).Account, target.LotId, target.AcquiredDate,
            quantity, quantity, unitCost, target.Acquisition.FunctionalCurrency, recordedAt, recordedAt, command.Journal.Entry.JournalEntryId,
            target.Acquisition.Evidence.First(evidence => evidence.SubjectType == "OpenLotAcquisition"
                && evidence.SubjectId == target.TaxLotRecordId.ToString("D")).EvidenceId,
            Version: target.Version, SecurityId: target.SecurityId, BookPositionId: target.BookPositionId,
            OriginalFace: face ? target.OriginalQuantity : null, BookedFactor: target.Acquisition.FaceValueTerms?.BookedFactor,
            ParBasis: target.Acquisition.FaceValueTerms?.ParBasis, Acquisition: target.Acquisition, BasisAdjustment: adjustment);
        if (!JsonElement.DeepEquals(JsonSerializer.SerializeToElement(result.ToOpenLot()), JsonSerializer.SerializeToElement(target)))
            throw new LedgerValidationException("Successor canonical target snapshot cannot be preserved exactly in the lot of record.");
        return result;
    }

    private async Task<IReadOnlyList<LedgerTaxLotMutationRecord>> ApplyCorporateActionAsync(
        NpgsqlConnection connection, NpgsqlTransaction transaction, AtomicTaxLotJournalCommand command,
        LedgerTaxLotRecord before, DateTimeOffset recordedAt, CancellationToken ct)
    {
        await using var update = connection.CreateCommand();
        update.Transaction = transaction;
        update.CommandText = $"""
            update {Qualified("tax_lots")}
            set open_quantity = 0, version = version + 1, last_mutation_batch_id = @batch, updated_at = @at
            where tax_lot_record_id = @id and ledger_book_id = @book and security_id = @security
                and book_position_id = @position and version = @version and open_quantity = @quantity
            returning {AverageCostLotColumns};
            """;
        update.Parameters.AddWithValue("batch", command.MutationBatchId);
        update.Parameters.AddWithValue("at", recordedAt.UtcDateTime);
        update.Parameters.AddWithValue("id", before.TaxLotRecordId);
        update.Parameters.AddWithValue("book", command.LedgerBookId);
        update.Parameters.AddWithValue("security", before.SecurityId);
        update.Parameters.AddWithValue("position", before.BookPositionId);
        update.Parameters.AddWithValue("version", before.Version);
        update.Parameters.AddWithValue("quantity", before.OpenQuantity);
        LedgerTaxLotRecord after;
        await using (var reader = await update.ExecuteReaderAsync(ct).ConfigureAwait(false))
        {
            if (!await reader.ReadAsync(ct).ConfigureAwait(false))
                throw new LedgerValidationException("Successor predecessor compare-and-swap failed.");
            after = ReadTaxLot(reader);
        }
        var instruction = command.CorporateAction!;
        var mutations = new List<LedgerTaxLotMutationRecord>
        {
            BuildMutationRecord(command, after, before, 0, -before.OpenQuantity, before.Version,
                instruction.ExpectedLot.Acquisition.Evidence.First(evidence => evidence.SubjectType == "OpenLotAcquisition"
                    && evidence.SubjectId == before.TaxLotRecordId.ToString("D")).EvidenceId,
                recordedAt, instruction.ExpectedLot.OpenFunctionalCostBasis)
        };
        foreach (var target in instruction.Successors.OrderBy(target => target.Lot.TaxLotRecordId))
        {
            var requested = CreateCorporateActionSuccessor(command, target.Lot, recordedAt);
            var mutation = await ApplyAcquisitionAsync(connection, transaction,
                command with { AcquisitionLot = requested }, recordedAt, ct).ConfigureAwait(false);
            mutations.Add(mutation with { SelectionOrdinal = mutations.Count, CostBasis = target.Lot.OpenFunctionalCostBasis });
        }
        return mutations;
    }

    public async Task<IReadOnlyList<AtomicTaxLotJournalResult>> GetOpenLotSuccessorHistoryAsync(
        Guid ledgerBookId, IReadOnlyList<Guid> journalEntryIds, CancellationToken ct = default)
    {
        RequireWriteTenant();
        if (ledgerBookId == Guid.Empty)
            throw new ArgumentException("Ledger book id is required.", nameof(ledgerBookId));
        ArgumentNullException.ThrowIfNull(journalEntryIds);
        if (journalEntryIds.Count == 0)
            return [];
        await using var connection = await OpenConnectionAsync(ct).ConfigureAwait(false);
        await using var transaction = await connection.BeginTransactionAsync(IsolationLevel.RepeatableRead, ct).ConfigureAwait(false);
        await EnsureBookWriteAuthorityAsync(connection, transaction, ledgerBookId, ct).ConfigureAwait(false);
        await using var query = connection.CreateCommand();
        query.Transaction = transaction;
        query.CommandText = $"""
            select mutation_batch_id from {Qualified("atomic_tax_lot_posting_batches")}
            where ledger_book_id = @book and journal_entry_id = any(@journals) and mutation_kind = 'CorporateAction'
            order by mutation_batch_id;
            """;
        query.Parameters.AddWithValue("book", ledgerBookId);
        query.Parameters.AddWithValue("journals", journalEntryIds.Distinct().ToArray());
        var ids = new List<Guid>();
        await using (var reader = await query.ExecuteReaderAsync(ct).ConfigureAwait(false))
        {
            while (await reader.ReadAsync(ct).ConfigureAwait(false))
                ids.Add(reader.GetGuid(0));
        }
        var results = new List<AtomicTaxLotJournalResult>(ids.Count);
        foreach (var id in ids)
            results.Add(await LoadAtomicTaxLotResultAsync(connection, transaction, id, false, ct).ConfigureAwait(false));
        await transaction.CommitAsync(ct).ConfigureAwait(false);
        return results;
    }
}
