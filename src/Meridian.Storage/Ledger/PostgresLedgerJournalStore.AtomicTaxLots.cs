using System.Data;
using System.Text.Json;
using Meridian.Contracts.AssetOperations;
using Meridian.Ledger;
using Npgsql;
using NpgsqlTypes;
using static Meridian.Contracts.Text.TextPrimitives;

namespace Meridian.Storage.Ledger;

public sealed partial class PostgresLedgerJournalStore
{
    public async Task<AtomicTaxLotJournalResult?> GetAtomicTaxLotPostingAsync(
        Guid mutationBatchId,
        CancellationToken ct = default)
    {
        if (mutationBatchId == Guid.Empty)
        {
            throw new ArgumentException("Tax-lot mutation batch id is required.", nameof(mutationBatchId));
        }

        await using var connection = await OpenConnectionAsync(ct).ConfigureAwait(false);
        await using var transaction = await connection
            .BeginTransactionAsync(IsolationLevel.ReadCommitted, ct)
            .ConfigureAwait(false);
        var retained = await LoadAtomicTaxLotBatchAsync(connection, transaction, mutationBatchId, ct)
            .ConfigureAwait(false);
        if (retained is null)
        {
            await transaction.CommitAsync(ct).ConfigureAwait(false);
            return null;
        }

        var result = await LoadAtomicTaxLotResultAsync(
                connection,
                transaction,
                mutationBatchId,
                isExactReplay: false,
                ct)
            .ConfigureAwait(false);
        await transaction.CommitAsync(ct).ConfigureAwait(false);
        return result;
    }

    public async Task<AtomicTaxLotJournalResult> AppendAssetPostingAsync(
        AtomicTaxLotJournalCommand command,
        CancellationToken ct = default)
    {
        command = NormalizeAndValidateAtomicTaxLotCommand(command);

        await using var connection = await OpenConnectionAsync(ct).ConfigureAwait(false);
        await using var transaction = await connection
            .BeginTransactionAsync(IsolationLevel.Serializable, ct)
            .ConfigureAwait(false);

        await AcquireAtomicTaxLotIdentityLocksAsync(connection, transaction, command, ct)
            .ConfigureAwait(false);

        var collisions = await LoadAtomicTaxLotBatchCollisionsAsync(connection, transaction, command, ct)
            .ConfigureAwait(false);
        if (collisions.Count > 0)
        {
            if (collisions.Count != 1 || !IsExactBatchIdentity(collisions[0], command) ||
                !string.Equals(
                    collisions[0].CanonicalFingerprint,
                    command.CanonicalFingerprint,
                    StringComparison.Ordinal))
            {
                throw new LedgerValidationException(
                    "Atomic tax-lot posting identity collision: the retained batch does not match the complete canonical fingerprint.");
            }

            var replay = await LoadAtomicTaxLotResultAsync(
                    connection,
                    transaction,
                    collisions[0].MutationBatchId,
                    isExactReplay: true,
                    ct)
                .ConfigureAwait(false);
            await transaction.CommitAsync(ct).ConfigureAwait(false);
            return replay;
        }

        var period = await LoadPeriodAsync(
                connection,
                transaction,
                command.Journal.PeriodId,
                forUpdate: true,
                ct: ct)
            .ConfigureAwait(false);
        if (period is null)
        {
            throw new LedgerValidationException(
                $"Ledger period '{command.Journal.PeriodId}' was not found for atomic tax-lot posting.");
        }

        if (period.LedgerBookId != command.LedgerBookId)
        {
            throw new LedgerValidationException(
                "Atomic tax-lot posting ledger book does not match the retained accounting period.");
        }

        if (period.Version != command.ExpectedPeriodVersion)
        {
            throw new LedgerValidationException(
                $"Accounting period version {period.Version} does not match atomic tax-lot expected version {command.ExpectedPeriodVersion}.");
        }

        await ValidateCorrectionBatchAsync(connection, transaction, command, ct).ConfigureAwait(false);
        if (command.MutationKind == AtomicTaxLotMutationKind.Disposal)
        {
            await ValidateAuthoritativeDisposalPolicyAsync(connection, transaction, command, ct)
                .ConfigureAwait(false);
        }

        // This is the existing governed journal append path, deliberately using the same connection
        // and transaction as the lot CAS and append-only mutation evidence below.
        await AppendAsync(connection, transaction, command.Journal, ct).ConfigureAwait(false);

        var recordedAt = DateTimeOffset.UtcNow;
        await InsertAtomicTaxLotBatchAsync(connection, transaction, command, recordedAt, ct)
            .ConfigureAwait(false);

        if (command.MutationKind == AtomicTaxLotMutationKind.Acquisition)
        {
            var mutation = await ApplyAcquisitionAsync(connection, transaction, command, recordedAt, ct)
                .ConfigureAwait(false);
            ValidateAtomicJournalLotEconomics(command, [mutation]);
            await InsertTaxLotMutationAsync(connection, transaction, mutation, ct).ConfigureAwait(false);
        }
        else
        {
            var mutations = new List<LedgerTaxLotMutationRecord>(command.DisposalSelections!.Count);
            foreach (var selection in command.DisposalSelections!)
            {
                var mutation = await ApplyDisposalAsync(
                        connection,
                        transaction,
                        command,
                        selection,
                        recordedAt,
                        ct)
                    .ConfigureAwait(false);
                mutations.Add(mutation);
            }

            ValidateAtomicJournalLotEconomics(command, mutations);
            foreach (var mutation in mutations)
            {
                await InsertTaxLotMutationAsync(connection, transaction, mutation, ct).ConfigureAwait(false);
            }
        }

        var result = await LoadAtomicTaxLotResultAsync(
                connection,
                transaction,
                command.MutationBatchId,
                isExactReplay: false,
                ct)
            .ConfigureAwait(false);
        await transaction.CommitAsync(ct).ConfigureAwait(false);
        return result;
    }

    private AtomicTaxLotJournalCommand NormalizeAndValidateAtomicTaxLotCommand(
        AtomicTaxLotJournalCommand command)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(command.Journal);
        ArgumentNullException.ThrowIfNull(command.Journal.Entry);
        ArgumentNullException.ThrowIfNull(command.RetainedEvidence);

        if (command.MutationBatchId == Guid.Empty)
        {
            throw new ArgumentException("Tax-lot mutation batch id is required.", nameof(command));
        }

        if (command.LedgerBookId == Guid.Empty)
        {
            throw new ArgumentException("Ledger book id is required.", nameof(command));
        }

        if (command.SourceEventId == Guid.Empty)
        {
            throw new ArgumentException("Source event id is required.", nameof(command));
        }

        if (command.ExpectedPeriodVersion < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(command),
                command.ExpectedPeriodVersion,
                "Expected accounting period version cannot be negative.");
        }

        if (command.CorrectsMutationBatchId == command.MutationBatchId)
        {
            throw new LedgerValidationException("A tax-lot mutation batch cannot correct itself.");
        }

        var idempotencyKey = RequireLineageText(command.IdempotencyKey, nameof(command.IdempotencyKey));
        var reliefMethod = NormalizeOptional(command.ReliefMethod);
        var policyRevision = NormalizeOptional(command.PolicyRevision);
        if ((reliefMethod is null) != (policyRevision is null))
        {
            throw new LedgerValidationException(
                "Atomic tax-lot relief method and policy revision must be supplied together.");
        }
        if (command.MutationKind == AtomicTaxLotMutationKind.Disposal &&
            (reliefMethod is null || policyRevision is null))
        {
            throw new LedgerValidationException(
                "Atomic disposal requires an authoritative tax-lot relief method and policy revision.");
        }

        var retainedEvidence = command.RetainedEvidence
            .OrderBy(static item => item.EvidenceId, StringComparer.Ordinal)
            .ThenBy(static item => item.EvidenceVersion)
            .ThenBy(static item => item.ContentHashSha256, StringComparer.Ordinal)
            .ToArray();
        if (retainedEvidence.Length == 0)
        {
            throw new LedgerValidationException("Atomic tax-lot posting requires retained evidence.");
        }

        var duplicateEvidenceId = retainedEvidence
            .GroupBy(static item => item.EvidenceId?.Trim(), StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault(static group => string.IsNullOrWhiteSpace(group.Key) || group.Count() > 1);
        if (duplicateEvidenceId is not null)
        {
            throw new LedgerValidationException(
                "Atomic tax-lot retained evidence ids must be non-empty and unique.");
        }

        foreach (var evidence in retainedEvidence)
        {
            var issues = RetainedEvidenceIdentityValidator.Validate(evidence);
            if (issues.Count > 0)
            {
                throw new LedgerValidationException(
                    $"Atomic tax-lot retained evidence '{evidence.EvidenceId}' is incomplete: {string.Join(" ", issues)}");
            }
        }

        var normalizedJournal = AccountingPostingCommandValidator.NormalizeAndValidate(
            command.Journal,
            _options.RequireGovernedPostingCommand,
            _options.RequireExpectedVersion);
        if (!AtomicTaxLotJournalFingerprint.IsCanonicalSha256(command.CanonicalFingerprint))
        {
            throw new LedgerValidationException(
                "Atomic tax-lot canonical fingerprint must use the lowercase sha256:<64 hex> format.");
        }

        // Fingerprint computation owns canonical journal normalization. Passing the already
        // normalized write would normalize it a second time and can change derived metadata.
        var computedFingerprint = AtomicTaxLotJournalFingerprint.Compute(command);
        if (!string.Equals(command.CanonicalFingerprint, computedFingerprint, StringComparison.Ordinal))
        {
            throw new LedgerValidationException(
                "Atomic tax-lot canonical fingerprint does not match the complete command payload after canonical journal normalization.");
        }

        if (normalizedJournal.LedgerBookId != command.LedgerBookId)
        {
            throw new LedgerValidationException(
                "Atomic tax-lot ledger book must match the governed journal write ledger book.");
        }

        if (normalizedJournal.SourceEventId != command.SourceEventId)
        {
            throw new LedgerValidationException(
                "Atomic tax-lot source event must match the governed journal write source event.");
        }

        if (!string.Equals(
                normalizedJournal.Entry.Metadata.IdempotencyKey?.Trim(),
                idempotencyKey,
                StringComparison.Ordinal))
        {
            throw new LedgerValidationException(
                "Atomic tax-lot idempotency key must match the governed journal metadata.");
        }

        if (normalizedJournal.PostingCommand?.ExpectedVersion is { } expectedVersion &&
            expectedVersion != command.ExpectedPeriodVersion)
        {
            throw new LedgerValidationException(
                "Atomic tax-lot expected period version must match the governed posting command.");
        }

        _ = ResolveAtomicAssetScope(normalizedJournal);
        var normalizedCommand = command with
        {
            Journal = normalizedJournal,
            IdempotencyKey = idempotencyKey,
            RetainedEvidence = retainedEvidence,
            ReliefMethod = reliefMethod,
            PolicyRevision = policyRevision
        };

        var evidenceIds = retainedEvidence
            .Select(static item => item.EvidenceId.Trim())
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        IReadOnlyList<LedgerTaxLotDisposalSelection>? disposalSelections = null;
        if (command.MutationKind == AtomicTaxLotMutationKind.Acquisition)
        {
            ValidateAtomicAcquisition(normalizedCommand, evidenceIds);
        }
        else if (command.MutationKind == AtomicTaxLotMutationKind.Disposal)
        {
            disposalSelections = ValidateAtomicDisposals(normalizedCommand, evidenceIds);
        }
        else
        {
            throw new LedgerValidationException(
                $"Unsupported atomic tax-lot mutation kind '{command.MutationKind}'.");
        }

        return normalizedCommand with
        {
            DisposalSelections = disposalSelections
        };
    }

    private static void ValidateAtomicAcquisition(
        AtomicTaxLotJournalCommand command,
        IReadOnlySet<string> evidenceIds)
    {
        if (command.AcquisitionLot is not { } lot)
        {
            throw new LedgerValidationException("Atomic acquisition requires a complete new tax lot.");
        }

        if (command.DisposalSelections is { Count: > 0 })
        {
            throw new LedgerValidationException("Atomic acquisition cannot include disposal selections.");
        }

        ArgumentNullException.ThrowIfNull(lot.Account);
        if (lot.TaxLotRecordId == Guid.Empty || lot.LedgerBookId != command.LedgerBookId)
        {
            throw new LedgerValidationException(
                "Atomic acquisition lot identity and ledger book are required and must match the command.");
        }


        var assetScope = ResolveAtomicAssetScope(command.Journal);
        if (lot.SecurityId != assetScope.SecurityId || lot.BookPositionId != assetScope.BookPositionId)
        {
            throw new LedgerValidationException(
                "Atomic acquisition lot Security Master and book-position identities must exactly match every journal line.");
        }
        if (!string.Equals(
                lot.Currency.Trim(),
                ResolveAtomicFunctionalCurrency(command.Journal),
                StringComparison.OrdinalIgnoreCase))
        {
            throw new LedgerValidationException(
                "Atomic acquisition lot currency must match the durable journal functional currency.");
        }

        _ = RequireLineageText(lot.LotId, nameof(lot.LotId));
        _ = RequireLineageText(lot.Currency, nameof(lot.Currency));
        var evidenceId = RequireLineageText(lot.EvidenceRef ?? string.Empty, nameof(lot.EvidenceRef));
        if (!evidenceIds.Contains(evidenceId))
        {
            throw new LedgerValidationException(
                "Atomic acquisition lot evidence must identify one of the retained evidence records.");
        }

        if (lot.OriginalQuantity <= 0m || lot.OpenQuantity != lot.OriginalQuantity)
        {
            throw new LedgerValidationException(
                "Atomic acquisition requires a positive lot whose open quantity equals its original quantity.");
        }

        if (lot.UnitCost < 0m)
        {
            throw new LedgerValidationException("Atomic acquisition lot unit cost cannot be negative.");
        }

        ValidateFaceValueTerms(lot);
        if (lot.Acquisition is not null)
            _ = lot.ToOpenLot();
        if (lot.HasFaceValueTerms &&
            lot.OriginalQuantity * LedgerTaxLotFaceValueTerms.LedgerLotParBasis != lot.OriginalFace!.Value)
        {
            // The lot states the same acquisition twice — once as a face amount, once as a quantity
            // in the engines' per-100 unit convention. A lot whose two statements disagree is the
            // silent mis-scaling the par conventions exist to prevent, and must not be retained.
            throw new LedgerValidationException(
                "Atomic acquisition lot original face must equal its quantity times 100; the lot of " +
                "record's quantity is the face expressed per 100 of par.");
        }

        var isUnstampedNewLot = lot.Version == 0 &&
                                !lot.OriginatingMutationBatchId.HasValue &&
                                !lot.LastMutationBatchId.HasValue;
        var isCommandStampedNewLot = lot.Version == 1 &&
                                     lot.OriginatingMutationBatchId == command.MutationBatchId &&
                                     lot.LastMutationBatchId == command.MutationBatchId;
        if (!isUnstampedNewLot && !isCommandStampedNewLot)
        {
            throw new LedgerValidationException(
                "Atomic acquisition requires either an unversioned new lot or initial version-one lineage stamped with this mutation batch.");
        }

        if (lot.SourceJournalEntryId != command.Journal.Entry.JournalEntryId)
        {
            throw new LedgerValidationException(
                "Atomic acquisition source journal must match the journal appended by the batch.");
        }

        if (lot.CreatedAt == default || lot.CreatedAt.Offset != TimeSpan.Zero ||
            lot.UpdatedAt == default || lot.UpdatedAt.Offset != TimeSpan.Zero ||
            lot.UpdatedAt < lot.CreatedAt)
        {
            throw new LedgerValidationException(
                "Atomic acquisition lot timestamps must be valid UTC timestamps in creation order.");
        }
    }

    private static IReadOnlyList<LedgerTaxLotDisposalSelection> ValidateAtomicDisposals(
        AtomicTaxLotJournalCommand command,
        IReadOnlySet<string> evidenceIds)
    {
        if (command.AcquisitionLot is not null)
        {
            throw new LedgerValidationException("Atomic disposal cannot include an acquisition lot.");
        }

        if (command.DisposalSelections is not { Count: > 0 } selections)
        {
            throw new LedgerValidationException("Atomic disposal requires at least one lot selection.");
        }

        var ordered = selections
            .OrderBy(static item => item.SelectionOrdinal)
            .ThenBy(static item => item.TaxLotRecordId)
            .ToArray();
        if (ordered.Select(static item => item.TaxLotRecordId).Distinct().Count() != ordered.Length ||
            ordered.Select(static item => item.SelectionOrdinal).Distinct().Count() != ordered.Length ||
            ordered.Where(static (item, index) => item.SelectionOrdinal != index).Any())
        {
            throw new LedgerValidationException(
                "Atomic disposal lot ids must be unique and selection ordinals must be consecutive from zero.");
        }

        foreach (var selection in ordered)
        {
            if (selection.TaxLotRecordId == Guid.Empty ||
                string.IsNullOrWhiteSpace(selection.LotId) ||
                selection.ExpectedVersion <= 0 ||
                selection.ExpectedOpenQuantity <= 0m ||
                selection.Quantity <= 0m ||
                selection.Quantity > selection.ExpectedOpenQuantity ||
                selection.SelectionOrdinal < 0 ||
                string.IsNullOrWhiteSpace(selection.SelectionEvidenceId) ||
                selection.ExpectedUnitCost <= 0m ||
                selection.ExpectedCostBasis <= 0m ||
                selection.ExpectedCostBasis != selection.Quantity * selection.ExpectedUnitCost)
            {
                throw new LedgerValidationException(
                    "Atomic disposal selections require lot identity, positive version/quantities, exact expected cost basis, ordinal, and evidence.");
            }

            if (!evidenceIds.Contains(selection.SelectionEvidenceId.Trim()))
            {
                throw new LedgerValidationException(
                    $"Atomic disposal selection {selection.SelectionOrdinal} does not reference retained evidence.");
            }
        }

        return ordered;
    }

    private async Task AcquireAtomicTaxLotIdentityLocksAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        AtomicTaxLotJournalCommand command,
        CancellationToken ct)
    {
        var lockKeys = new[]
        {
            $"{_options.SchemaName}:atomic-tax-lot:batch:{command.MutationBatchId:D}",
            $"{_options.SchemaName}:atomic-tax-lot:journal:{command.Journal.Entry.JournalEntryId:D}",
            $"{_options.SchemaName}:atomic-tax-lot:source:{command.LedgerBookId:D}:{command.SourceEventId:D}",
            $"{_options.SchemaName}:atomic-tax-lot:idempotency:{command.LedgerBookId:D}:{command.IdempotencyKey.ToLowerInvariant()}"
        };

        foreach (var lockKey in lockKeys.Distinct(StringComparer.Ordinal).OrderBy(static key => key, StringComparer.Ordinal))
        {
            await using var lockCommand = connection.CreateCommand();
            lockCommand.Transaction = transaction;
            lockCommand.CommandText = "select pg_advisory_xact_lock(hashtextextended(@lock_key, 0));";
            lockCommand.Parameters.AddWithValue("lock_key", lockKey);
            await lockCommand.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        }
    }

    private async Task<IReadOnlyList<StoredAtomicTaxLotBatch>> LoadAtomicTaxLotBatchCollisionsAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        AtomicTaxLotJournalCommand command,
        CancellationToken ct)
    {
        await using var query = connection.CreateCommand();
        query.Transaction = transaction;
        query.CommandText =
            $"""
            select mutation_batch_id,
                   ledger_book_id,
                   period_id,
                   journal_entry_id,
                   source_event_id,
                   idempotency_key,
                   canonical_fingerprint,
                   expected_period_version,
                   mutation_kind,
                   retained_evidence::text,
                   corrects_mutation_batch_id,
                   relief_method,
                   policy_revision,
                   created_at,
                   security_id,
                   book_position_id
            from {Qualified("atomic_tax_lot_posting_batches")}
            where mutation_batch_id = @mutation_batch_id
               or journal_entry_id = @journal_entry_id
               or (ledger_book_id = @ledger_book_id and source_event_id = @source_event_id)
               or (ledger_book_id = @ledger_book_id and lower(trim(idempotency_key)) = lower(trim(@idempotency_key)))
            order by mutation_batch_id
            for update;
            """;
        AddAtomicBatchIdentityParameters(query, command);

        var batches = new List<StoredAtomicTaxLotBatch>();
        await using var reader = await query.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await reader.ReadAsync(ct).ConfigureAwait(false))
        {
            batches.Add(ReadStoredAtomicTaxLotBatch(reader));
        }

        return batches;
    }

    private async Task ValidateCorrectionBatchAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        AtomicTaxLotJournalCommand command,
        CancellationToken ct)
    {
        if (!command.CorrectsMutationBatchId.HasValue)
        {
            return;
        }

        var corrected = await LoadAtomicTaxLotBatchAsync(
                connection,
                transaction,
                command.CorrectsMutationBatchId.Value,
                ct)
            .ConfigureAwait(false);
        if (corrected is null || corrected.LedgerBookId != command.LedgerBookId)
        {
            throw new LedgerValidationException(
                "Corrected tax-lot mutation batch was not found in the command ledger book.");
        }

        var correctionScope = ResolveAtomicAssetScope(command.Journal);
        if (corrected.SecurityId != correctionScope.SecurityId ||
            corrected.BookPositionId != correctionScope.BookPositionId)
        {
            throw new LedgerValidationException(
                "A tax-lot correction must remain within the corrected batch Security Master and book-position scope.");
        }

        if (command.Journal.SourceJournalEntryId != corrected.JournalEntryId)
        {
            throw new LedgerValidationException(
                "Correction journal lineage must identify the journal retained by the corrected tax-lot batch.");
        }
    }

    private async Task ValidateAuthoritativeDisposalPolicyAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        AtomicTaxLotJournalCommand command,
        CancellationToken ct)
    {
        var selections = command.DisposalSelections
            ?? throw new LedgerValidationException("Atomic disposal requires retained lot selections.");
        var selectedLots = await LoadTaxLotsForUpdateAsync(
                connection,
                transaction,
                command.LedgerBookId,
                selections.Select(static selection => selection.TaxLotRecordId).ToArray(),
                ct)
            .ConfigureAwait(false);
        if (selectedLots.Count != selections.Count)
        {
            throw new LedgerValidationException(
                "One or more selected tax lots were not found in the authoritative ledger book.");
        }

        var selectedById = selectedLots.ToDictionary(static lot => lot.TaxLotRecordId);
        foreach (var selection in selections)
        {
            ValidateDisposalSelectionSnapshot(command, selection, selectedById[selection.TaxLotRecordId]);
        }

        var accounts = selectedLots
            .Select(static lot => lot.Account)
            .Distinct()
            .ToArray();
        if (accounts.Length != 1)
        {
            throw new LedgerValidationException(
                "Atomic disposal selections must resolve to one exact authoritative asset account.");
        }

        var effectiveDate = command.Journal.Entry.Metadata.EffectiveDate
            ?? throw new LedgerValidationException(
                "Atomic disposal requires a governed journal effective date for tax-lot policy resolution.");
        var policy = await LoadActiveTaxLotPolicyAsync(
                connection,
                transaction,
                command.LedgerBookId,
                accounts[0],
                effectiveDate,
                ct)
            .ConfigureAwait(false)
            ?? throw new LedgerValidationException(
                $"No effective tax-lot policy was found for account '{accounts[0]}' on {effectiveDate:yyyy-MM-dd}.");

        if (!Enum.TryParse<LedgerTaxLotReliefMethod>(command.ReliefMethod, ignoreCase: true, out var requestedMethod) ||
            requestedMethod is not (LedgerTaxLotReliefMethod.Fifo or
                LedgerTaxLotReliefMethod.Lifo or
                LedgerTaxLotReliefMethod.Hifo or
                LedgerTaxLotReliefMethod.SpecificId))
        {
            throw new LedgerValidationException(
                "Atomic disposal supports authoritative FIFO, LIFO, HIFO, or SpecificId lot relief only.");
        }

        if (policy.ReliefMethod != requestedMethod ||
            !string.Equals(policy.PolicyId, command.PolicyRevision, StringComparison.Ordinal))
        {
            throw new LedgerValidationException(
                $"Atomic disposal relief '{command.ReliefMethod}' revision '{command.PolicyRevision}' does not match effective policy '{policy.ReliefMethod}' revision '{policy.PolicyId}'.");
        }

        var assetScope = ResolveAtomicAssetScope(command.Journal);
        var functionalCurrency = ResolveAtomicFunctionalCurrency(command.Journal);
        var openLots = await LoadOpenTaxLotsForUpdateAsync(
                connection,
                transaction,
                command.LedgerBookId,
                accounts[0],
                assetScope.SecurityId,
                assetScope.BookPositionId,
                functionalCurrency,
                effectiveDate,
                ct)
            .ConfigureAwait(false);
        if (openLots.Count == 0)
        {
            throw new LedgerValidationException(
                "Atomic disposal did not resolve any authoritative open tax lots in its exact book, account, security, position, currency, and effective-date scope.");
        }

        var duplicateLotId = openLots
            .GroupBy(static lot => lot.LotId.Trim(), StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault(static group => group.Count() > 1);
        if (duplicateLotId is not null)
        {
            throw new LedgerValidationException(
                $"Authoritative open tax-lot identity '{duplicateLotId.Key}' is ambiguous in the disposal scope.");
        }

        var openByLotId = openLots.ToDictionary(
            static lot => lot.LotId.Trim(),
            StringComparer.OrdinalIgnoreCase);
        if (selections.Any(selection => !openByLotId.ContainsKey(selection.LotId.Trim())))
        {
            throw new LedgerValidationException(
                "One or more selected tax lots are not open and effective in the authoritative disposal scope.");
        }

        LedgerTaxLotReliefProjection relief;
        try
        {
            relief = LedgerTaxLotReliefProjector.Project(new LedgerTaxLotReliefInput(
                accounts[0],
                effectiveDate,
                selections.Sum(static selection => selection.Quantity),
                salePrice: 0m,
                reliefMethod: requestedMethod,
                openLots: openLots.Select(static lot => new LedgerTaxLot(
                        lot.LotId,
                        lot.AcquiredDate,
                        lot.OpenQuantity,
                        lot.UnitCost,
                        lot.SecurityId))
                    .ToArray(),
                financialAccountId: accounts[0].FinancialAccountId,
                specificLotIds: requestedMethod == LedgerTaxLotReliefMethod.SpecificId
                    ? selections.Select(static selection => selection.LotId).ToArray()
                    : null));
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
        {
            throw new LedgerValidationException(
                $"Authoritative tax-lot relief could not satisfy the disposal selection: {ex.Message}");
        }

        var authoritativeSelections = relief.Selections
            .Select(selection => (
                Lot: openByLotId[selection.Lot.LotId],
                Quantity: selection.QuantityRelieved))
            .ToArray();
        if (authoritativeSelections.Length != selections.Count ||
            selections.Where((selection, index) =>
                    authoritativeSelections[index].Lot.TaxLotRecordId != selection.TaxLotRecordId ||
                    authoritativeSelections[index].Quantity != selection.Quantity)
                .Any())
        {
            throw new LedgerValidationException(
                $"Atomic disposal selections do not match the authoritative {requestedMethod} open-lot relief plan.");
        }
    }

    private async Task<IReadOnlyList<LedgerTaxLotRecord>> LoadTaxLotsForUpdateAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid ledgerBookId,
        IReadOnlyList<Guid> taxLotRecordIds,
        CancellationToken ct)
    {
        await using var query = connection.CreateCommand();
        query.Transaction = transaction;
        query.CommandText =
            $"""
            select tax_lot_record_id,
                   ledger_book_id,
                   account_name,
                   account_type,
                   symbol,
                   financial_account_id,
                   lot_id,
                   acquired_date,
                   original_quantity,
                   open_quantity,
                   unit_cost,
                   currency,
                   source_journal_entry_id,
                   evidence_ref,
                   version,
                   originating_mutation_batch_id,
                   last_mutation_batch_id,
                   created_at,
                   updated_at,
                   security_id,
                   book_position_id,
                   original_face,
                   booked_factor,
                   par_basis,
                   acquisition_terms
            from {Qualified("tax_lots")}
            where ledger_book_id = @ledger_book_id
              and tax_lot_record_id = any(@tax_lot_record_ids)
            order by tax_lot_record_id
            for update;
            """;
        query.Parameters.AddWithValue("ledger_book_id", ledgerBookId);
        query.Parameters.AddWithValue("tax_lot_record_ids", taxLotRecordIds.Distinct().ToArray());

        var lots = new List<LedgerTaxLotRecord>(taxLotRecordIds.Count);
        await using var reader = await query.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await reader.ReadAsync(ct).ConfigureAwait(false))
        {
            lots.Add(ReadTaxLot(reader));
        }

        return lots;
    }

    private async Task<LedgerAccountTaxLotPolicyRecord?> LoadActiveTaxLotPolicyAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid ledgerBookId,
        LedgerAccount account,
        DateOnly effectiveDate,
        CancellationToken ct)
    {
        await using var query = connection.CreateCommand();
        query.Transaction = transaction;
        query.CommandText =
            $"""
            select policy_record_id,
                   ledger_book_id,
                   account_name,
                   account_type,
                   symbol,
                   financial_account_id,
                   relief_method,
                   policy_id,
                   effective_date,
                   rationale,
                   created_at,
                   updated_at,
                   wash_sale_enabled,
                   wash_sale_window_days,
                   wash_sale_scope,
                   wash_sale_effective_date
            from {Qualified("tax_lot_policies")}
            where ledger_book_id = @ledger_book_id
              and account_name = @account_name
              and account_type = @account_type
              and symbol is not distinct from @symbol
              and financial_account_id is not distinct from @financial_account_id
              and effective_date <= @effective_date
            order by effective_date desc, updated_at desc, policy_id desc, policy_record_id desc
            limit 1
            for share;
            """;
        query.Parameters.AddWithValue("ledger_book_id", ledgerBookId);
        AddAccountParameters(query, account);
        query.Parameters.AddWithValue("effective_date", effectiveDate);

        await using var reader = await query.ExecuteReaderAsync(ct).ConfigureAwait(false);
        return await reader.ReadAsync(ct).ConfigureAwait(false)
            ? ReadTaxLotPolicy(reader)
            : null;
    }

    private async Task<IReadOnlyList<LedgerTaxLotRecord>> LoadOpenTaxLotsForUpdateAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid ledgerBookId,
        LedgerAccount account,
        Guid securityId,
        Guid bookPositionId,
        string currency,
        DateOnly effectiveDate,
        CancellationToken ct)
    {
        await using var query = connection.CreateCommand();
        query.Transaction = transaction;
        query.CommandText =
            $"""
            select tax_lot_record_id,
                   ledger_book_id,
                   account_name,
                   account_type,
                   symbol,
                   financial_account_id,
                   lot_id,
                   acquired_date,
                   original_quantity,
                   open_quantity,
                   unit_cost,
                   currency,
                   source_journal_entry_id,
                   evidence_ref,
                   version,
                   originating_mutation_batch_id,
                   last_mutation_batch_id,
                   created_at,
                   updated_at,
                   security_id,
                   book_position_id,
                   original_face,
                   booked_factor,
                   par_basis,
                   acquisition_terms
            from {Qualified("tax_lots")}
            where ledger_book_id = @ledger_book_id
              and account_name = @account_name
              and account_type = @account_type
              and symbol is not distinct from @symbol
              and financial_account_id is not distinct from @financial_account_id
              and security_id = @security_id
              and book_position_id = @book_position_id
              and upper(currency) = upper(@currency)
              and acquired_date <= @effective_date
              and open_quantity > 0
            order by tax_lot_record_id
            for update;
            """;
        query.Parameters.AddWithValue("ledger_book_id", ledgerBookId);
        AddAccountParameters(query, account);
        query.Parameters.AddWithValue("security_id", securityId);
        query.Parameters.AddWithValue("book_position_id", bookPositionId);
        query.Parameters.AddWithValue("currency", currency);
        query.Parameters.AddWithValue("effective_date", effectiveDate);

        var lots = new List<LedgerTaxLotRecord>();
        await using var reader = await query.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await reader.ReadAsync(ct).ConfigureAwait(false))
        {
            lots.Add(ReadTaxLot(reader));
        }

        return lots;
    }

    private async Task InsertAtomicTaxLotBatchAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        AtomicTaxLotJournalCommand command,
        DateTimeOffset recordedAt,
        CancellationToken ct)
    {
        await using var insert = connection.CreateCommand();
        insert.Transaction = transaction;
        insert.CommandText =
            $"""
            insert into {Qualified("atomic_tax_lot_posting_batches")} (
                mutation_batch_id,
                ledger_book_id,
                period_id,
                journal_entry_id,
                source_event_id,
                idempotency_key,
                canonical_fingerprint,
                expected_period_version,
                mutation_kind,
                retained_evidence,
                corrects_mutation_batch_id,
                relief_method,
                policy_revision,
                created_at,
                security_id,
                book_position_id)
            values (
                @mutation_batch_id,
                @ledger_book_id,
                @period_id,
                @journal_entry_id,
                @source_event_id,
                @idempotency_key,
                @canonical_fingerprint,
                @expected_period_version,
                @mutation_kind,
                cast(@retained_evidence as jsonb),
                @corrects_mutation_batch_id,
                @relief_method,
                @policy_revision,
                @created_at,
                @security_id,
                @book_position_id);
            """;
        AddAtomicBatchIdentityParameters(insert, command);
        insert.Parameters.AddWithValue("period_id", command.Journal.PeriodId);
        insert.Parameters.AddWithValue("canonical_fingerprint", command.CanonicalFingerprint);
        insert.Parameters.AddWithValue("expected_period_version", command.ExpectedPeriodVersion);
        insert.Parameters.AddWithValue("mutation_kind", command.MutationKind.ToString());
        insert.Parameters.AddWithValue("retained_evidence", SerializeRetainedEvidence(command.RetainedEvidence));
        insert.Parameters.AddWithValue(
            "corrects_mutation_batch_id",
            (object?)command.CorrectsMutationBatchId ?? DBNull.Value);
        insert.Parameters.AddWithValue("relief_method", (object?)command.ReliefMethod ?? DBNull.Value);
        insert.Parameters.AddWithValue("policy_revision", (object?)command.PolicyRevision ?? DBNull.Value);
        insert.Parameters.AddWithValue("created_at", recordedAt.UtcDateTime);
        var assetScope = ResolveAtomicAssetScope(command.Journal);
        insert.Parameters.AddWithValue("security_id", assetScope.SecurityId);
        insert.Parameters.AddWithValue("book_position_id", assetScope.BookPositionId);
        await insert.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    private async Task<LedgerTaxLotMutationRecord> ApplyAcquisitionAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        AtomicTaxLotJournalCommand command,
        DateTimeOffset recordedAt,
        CancellationToken ct)
    {
        var requested = command.AcquisitionLot!;
        var acquired = requested with
        {
            LotId = requested.LotId.Trim(),
            Currency = requested.Currency.Trim().ToUpperInvariant(),
            UpdatedAt = recordedAt,
            SourceJournalEntryId = command.Journal.Entry.JournalEntryId,
            EvidenceRef = requested.EvidenceRef!.Trim(),
            Version = 1,
            OriginatingMutationBatchId = command.MutationBatchId,
            LastMutationBatchId = command.MutationBatchId
        };

        await using var insert = connection.CreateCommand();
        insert.Transaction = transaction;
        insert.CommandText =
            $"""
            insert into {Qualified("tax_lots")} (
                tax_lot_record_id,
                ledger_book_id,
                account_name,
                account_type,
                symbol,
                financial_account_id,
                lot_id,
                acquired_date,
                original_quantity,
                open_quantity,
                unit_cost,
                currency,
                source_journal_entry_id,
                evidence_ref,
                version,
                originating_mutation_batch_id,
                last_mutation_batch_id,
                created_at,
                updated_at,
                security_id,
                book_position_id,
                original_face,
                booked_factor,
                par_basis,
                acquisition_terms)
            values (
                @tax_lot_record_id,
                @ledger_book_id,
                @account_name,
                @account_type,
                @symbol,
                @financial_account_id,
                @lot_id,
                @acquired_date,
                @original_quantity,
                @open_quantity,
                @unit_cost,
                @currency,
                @source_journal_entry_id,
                @evidence_ref,
                @version,
                @originating_mutation_batch_id,
                @last_mutation_batch_id,
                @created_at,
                @updated_at,
                @security_id,
                @book_position_id,
                @original_face,
                @booked_factor,
                @par_basis,
                @acquisition_terms)
            returning tax_lot_record_id,
                      ledger_book_id,
                      account_name,
                      account_type,
                      symbol,
                      financial_account_id,
                      lot_id,
                      acquired_date,
                      original_quantity,
                      open_quantity,
                      unit_cost,
                      currency,
                      source_journal_entry_id,
                      evidence_ref,
                      version,
                      originating_mutation_batch_id,
                      last_mutation_batch_id,
                      created_at,
                      updated_at,
                      security_id,
                      book_position_id,
                      original_face,
                      booked_factor,
                      par_basis,
                      acquisition_terms;
            """;
        AddAtomicTaxLotParameters(insert, acquired);

        await using var reader = await insert.ExecuteReaderAsync(ct).ConfigureAwait(false);
        if (!await reader.ReadAsync(ct).ConfigureAwait(false))
        {
            throw new InvalidOperationException("Atomic acquisition did not retain a tax-lot snapshot.");
        }

        var retained = ReadTaxLot(reader);
        return BuildMutationRecord(
            command,
            retained,
            lotBefore: null,
            selectionOrdinal: 0,
            quantityDelta: retained.OpenQuantity,
            expectedVersion: 0,
            selectionEvidenceId: retained.EvidenceRef!,
            recordedAt: recordedAt);
    }

    private async Task<LedgerTaxLotMutationRecord> ApplyDisposalAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        AtomicTaxLotJournalCommand command,
        LedgerTaxLotDisposalSelection selection,
        DateTimeOffset recordedAt,
        CancellationToken ct)
    {
        var before = await LoadTaxLotForUpdateAsync(
                connection,
                transaction,
                command.LedgerBookId,
                selection.TaxLotRecordId,
                ct)
            .ConfigureAwait(false);
        if (before is null)
        {
            throw new LedgerValidationException(
                $"Tax lot '{selection.TaxLotRecordId}' was not found for atomic disposal.");
        }

        ValidateDisposalSelectionSnapshot(command, selection, before);
        var assetScope = ResolveAtomicAssetScope(command.Journal);

        await using var update = connection.CreateCommand();
        update.Transaction = transaction;
        update.CommandText =
            $"""
            update {Qualified("tax_lots")}
            set open_quantity = open_quantity - @quantity,
                version = version + 1,
                last_mutation_batch_id = @last_mutation_batch_id,
                updated_at = @updated_at
            where tax_lot_record_id = @tax_lot_record_id
              and ledger_book_id = @ledger_book_id
              and security_id = @security_id
              and book_position_id = @book_position_id
              and lot_id = @lot_id
              and version = @expected_version
              and open_quantity = @expected_open_quantity
              and open_quantity >= @quantity
            returning tax_lot_record_id,
                      ledger_book_id,
                      account_name,
                      account_type,
                      symbol,
                      financial_account_id,
                      lot_id,
                      acquired_date,
                      original_quantity,
                      open_quantity,
                      unit_cost,
                      currency,
                      source_journal_entry_id,
                      evidence_ref,
                      version,
                      originating_mutation_batch_id,
                      last_mutation_batch_id,
                      created_at,
                      updated_at,
                      security_id,
                      book_position_id,
                      original_face,
                      booked_factor,
                      par_basis,
                      acquisition_terms;
            """;
        update.Parameters.AddWithValue("tax_lot_record_id", selection.TaxLotRecordId);
        update.Parameters.AddWithValue("ledger_book_id", command.LedgerBookId);
        update.Parameters.AddWithValue("security_id", assetScope.SecurityId);
        update.Parameters.AddWithValue("book_position_id", assetScope.BookPositionId);
        update.Parameters.AddWithValue("lot_id", selection.LotId.Trim());
        update.Parameters.AddWithValue("expected_version", selection.ExpectedVersion);
        update.Parameters.AddWithValue("expected_open_quantity", selection.ExpectedOpenQuantity);
        update.Parameters.AddWithValue("quantity", selection.Quantity);
        update.Parameters.AddWithValue("last_mutation_batch_id", command.MutationBatchId);
        update.Parameters.AddWithValue("updated_at", recordedAt.UtcDateTime);

        await using var reader = await update.ExecuteReaderAsync(ct).ConfigureAwait(false);
        if (!await reader.ReadAsync(ct).ConfigureAwait(false))
        {
            throw new LedgerValidationException(
                $"Tax lot '{selection.TaxLotRecordId}' changed before the disposal compare-and-swap completed.");
        }

        var after = ReadTaxLot(reader);
        return BuildMutationRecord(
            command,
            after,
            before,
            selection.SelectionOrdinal,
            quantityDelta: -selection.Quantity,
            selection.ExpectedVersion,
            selection.SelectionEvidenceId.Trim(),
            recordedAt);
    }

    private static void ValidateDisposalSelectionSnapshot(
        AtomicTaxLotJournalCommand command,
        LedgerTaxLotDisposalSelection selection,
        LedgerTaxLotRecord lot)
    {
        var assetScope = ResolveAtomicAssetScope(command.Journal);
        var functionalCurrency = ResolveAtomicFunctionalCurrency(command.Journal);
        if (!string.Equals(lot.LotId, selection.LotId.Trim(), StringComparison.Ordinal) ||
            lot.SecurityId != assetScope.SecurityId ||
            lot.BookPositionId != assetScope.BookPositionId ||
            !string.Equals(lot.Currency, functionalCurrency, StringComparison.OrdinalIgnoreCase) ||
            lot.UnitCost != selection.ExpectedUnitCost ||
            selection.ExpectedCostBasis != selection.Quantity * lot.UnitCost ||
            lot.Version != selection.ExpectedVersion ||
            lot.OpenQuantity != selection.ExpectedOpenQuantity ||
            lot.OpenQuantity < selection.Quantity)
        {
            throw new LedgerValidationException(
                $"Tax lot '{selection.TaxLotRecordId}' failed the disposal scope, cost basis, quantity, or version compare-and-swap guard.");
        }
    }

    private static LedgerTaxLotMutationRecord BuildMutationRecord(
        AtomicTaxLotJournalCommand command,
        LedgerTaxLotRecord lotAfter,
        LedgerTaxLotRecord? lotBefore,
        int selectionOrdinal,
        decimal quantityDelta,
        long expectedVersion,
        string selectionEvidenceId,
        DateTimeOffset recordedAt)
        => new(
            MutationRecordId: Guid.NewGuid(),
            command.MutationBatchId,
            command.MutationKind,
            lotAfter.TaxLotRecordId,
            lotAfter.LotId,
            selectionOrdinal,
            QuantityBefore: lotBefore?.OpenQuantity ?? 0m,
            quantityDelta,
            QuantityAfter: lotAfter.OpenQuantity,
            lotAfter.UnitCost,
            CostBasis: Math.Abs(quantityDelta) * lotAfter.UnitCost,
            ExpectedVersion: expectedVersion,
            ResultVersion: lotAfter.Version,
            selectionEvidenceId,
            command.Journal.Entry.JournalEntryId,
            command.SourceEventId,
            command.RetainedEvidence,
            recordedAt,
            lotBefore,
            lotAfter,
            command.CorrectsMutationBatchId,
            command.ReliefMethod,
            command.PolicyRevision,
            lotAfter.SecurityId,
            lotAfter.BookPositionId);

    private static void ValidateAtomicJournalLotEconomics(
        AtomicTaxLotJournalCommand command,
        IReadOnlyList<LedgerTaxLotMutationRecord> mutations)
    {
        if (mutations.Count == 0)
        {
            throw new LedgerValidationException(
                "Atomic tax-lot posting requires at least one retained lot mutation before commit.");
        }

        var assetAccounts = mutations
            .Select(static mutation => mutation.LotAfter.Account)
            .Distinct()
            .ToArray();
        if (assetAccounts.Length != 1)
        {
            throw new LedgerValidationException(
                "Atomic tax-lot mutations must resolve to one exact asset account.");
        }

        var expectedCostBasis = mutations.Sum(static mutation => mutation.CostBasis);
        var assetLines = command.Journal.Entry.Lines
            .Where(line => line.Account == assetAccounts[0])
            .ToArray();
        var hasExpectedMovement = assetLines.Length == 1 &&
            (command.MutationKind == AtomicTaxLotMutationKind.Acquisition
                ? assetLines[0].Debit == expectedCostBasis && assetLines[0].Credit == 0m
                : assetLines[0].Credit == expectedCostBasis && assetLines[0].Debit == 0m);
        if (expectedCostBasis <= 0m || !hasExpectedMovement)
        {
            var side = command.MutationKind == AtomicTaxLotMutationKind.Acquisition
                ? "debit"
                : "credit";
            throw new LedgerValidationException(
                $"Atomic tax-lot journal requires one exact asset-account {side} equal to authoritative lot cost basis.");
        }
    }

    private async Task InsertTaxLotMutationAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        LedgerTaxLotMutationRecord mutation,
        CancellationToken ct)
    {
        await using var insert = connection.CreateCommand();
        insert.Transaction = transaction;
        insert.CommandText =
            $"""
            insert into {Qualified("tax_lot_mutations")} (
                mutation_record_id,
                mutation_batch_id,
                mutation_kind,
                tax_lot_record_id,
                lot_id,
                selection_ordinal,
                quantity_before,
                quantity_delta,
                quantity_after,
                unit_cost,
                cost_basis,
                expected_version,
                result_version,
                selection_evidence_id,
                retained_evidence,
                journal_entry_id,
                source_event_id,
                corrects_mutation_batch_id,
                relief_method,
                policy_revision,
                lot_snapshot_before,
                lot_snapshot_after,
                recorded_at,
                security_id,
                book_position_id)
            values (
                @mutation_record_id,
                @mutation_batch_id,
                @mutation_kind,
                @tax_lot_record_id,
                @lot_id,
                @selection_ordinal,
                @quantity_before,
                @quantity_delta,
                @quantity_after,
                @unit_cost,
                @cost_basis,
                @expected_version,
                @result_version,
                @selection_evidence_id,
                cast(@retained_evidence as jsonb),
                @journal_entry_id,
                @source_event_id,
                @corrects_mutation_batch_id,
                @relief_method,
                @policy_revision,
                cast(@lot_snapshot_before as jsonb),
                cast(@lot_snapshot_after as jsonb),
                @recorded_at,
                @security_id,
                @book_position_id);
            """;
        insert.Parameters.AddWithValue("mutation_record_id", mutation.MutationRecordId);
        insert.Parameters.AddWithValue("mutation_batch_id", mutation.MutationBatchId);
        insert.Parameters.AddWithValue("mutation_kind", mutation.MutationKind.ToString());
        insert.Parameters.AddWithValue("tax_lot_record_id", mutation.TaxLotRecordId);
        insert.Parameters.AddWithValue("lot_id", mutation.LotId);
        insert.Parameters.AddWithValue("selection_ordinal", mutation.SelectionOrdinal);
        insert.Parameters.AddWithValue("quantity_before", mutation.QuantityBefore);
        insert.Parameters.AddWithValue("quantity_delta", mutation.QuantityDelta);
        insert.Parameters.AddWithValue("quantity_after", mutation.QuantityAfter);
        insert.Parameters.AddWithValue("unit_cost", mutation.UnitCost);
        insert.Parameters.AddWithValue("cost_basis", mutation.CostBasis);
        insert.Parameters.AddWithValue("expected_version", mutation.ExpectedVersion);
        insert.Parameters.AddWithValue("result_version", mutation.ResultVersion);
        insert.Parameters.AddWithValue("selection_evidence_id", mutation.SelectionEvidenceId);
        insert.Parameters.AddWithValue("retained_evidence", SerializeRetainedEvidence(mutation.RetainedEvidence));
        insert.Parameters.AddWithValue("journal_entry_id", mutation.JournalEntryId);
        insert.Parameters.AddWithValue("source_event_id", mutation.SourceEventId);
        insert.Parameters.AddWithValue(
            "corrects_mutation_batch_id",
            (object?)mutation.CorrectsMutationBatchId ?? DBNull.Value);
        insert.Parameters.AddWithValue("relief_method", (object?)mutation.ReliefMethod ?? DBNull.Value);
        insert.Parameters.AddWithValue("policy_revision", (object?)mutation.PolicyRevision ?? DBNull.Value);
        insert.Parameters.Add("lot_snapshot_before", NpgsqlDbType.Text).Value =
            mutation.LotBefore is null
                ? DBNull.Value
                : JsonSerializer.Serialize(mutation.LotBefore, JsonOptions);
        insert.Parameters.AddWithValue(
            "lot_snapshot_after",
            JsonSerializer.Serialize(mutation.LotAfter, JsonOptions));
        insert.Parameters.AddWithValue("recorded_at", mutation.RecordedAt.UtcDateTime);
        insert.Parameters.AddWithValue("security_id", mutation.SecurityId);
        insert.Parameters.AddWithValue("book_position_id", mutation.BookPositionId);
        await insert.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    private async Task<LedgerTaxLotRecord?> LoadTaxLotForUpdateAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid ledgerBookId,
        Guid taxLotRecordId,
        CancellationToken ct)
    {
        await using var query = connection.CreateCommand();
        query.Transaction = transaction;
        query.CommandText =
            $"""
            select tax_lot_record_id,
                   ledger_book_id,
                   account_name,
                   account_type,
                   symbol,
                   financial_account_id,
                   lot_id,
                   acquired_date,
                   original_quantity,
                   open_quantity,
                   unit_cost,
                   currency,
                   source_journal_entry_id,
                   evidence_ref,
                   version,
                   originating_mutation_batch_id,
                   last_mutation_batch_id,
                   created_at,
                   updated_at,
                   security_id,
                   book_position_id,
                   original_face,
                   booked_factor,
                   par_basis,
                   acquisition_terms
            from {Qualified("tax_lots")}
            where tax_lot_record_id = @tax_lot_record_id
              and ledger_book_id = @ledger_book_id
            for update;
            """;
        query.Parameters.AddWithValue("tax_lot_record_id", taxLotRecordId);
        query.Parameters.AddWithValue("ledger_book_id", ledgerBookId);

        await using var reader = await query.ExecuteReaderAsync(ct).ConfigureAwait(false);
        return await reader.ReadAsync(ct).ConfigureAwait(false) ? ReadTaxLot(reader) : null;
    }

    private async Task<AtomicTaxLotJournalResult> LoadAtomicTaxLotResultAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid mutationBatchId,
        bool isExactReplay,
        CancellationToken ct)
    {
        var batch = await LoadAtomicTaxLotBatchAsync(connection, transaction, mutationBatchId, ct)
            .ConfigureAwait(false)
            ?? throw new InvalidOperationException(
                $"Atomic tax-lot posting batch '{mutationBatchId}' was not retained.");

        await using var journalQuery = CreateJournalEntryReadCommand(connection);
        journalQuery.Transaction = transaction;
        journalQuery.CommandText +=
            " where je.journal_entry_id = @journal_entry_id order by je.occurred_at, je.global_sequence, jl.line_no;";
        journalQuery.Parameters.AddWithValue("journal_entry_id", batch.JournalEntryId);
        var journals = await ReadJournalEntriesAsync(journalQuery, ct).ConfigureAwait(false);
        if (journals.Count != 1)
        {
            throw new InvalidOperationException(
                $"Atomic tax-lot posting batch '{mutationBatchId}' does not resolve to exactly one balanced journal.");
        }

        var mutations = await LoadTaxLotMutationsAsync(connection, transaction, mutationBatchId, ct)
            .ConfigureAwait(false);
        if (mutations.Count == 0)
        {
            throw new InvalidOperationException(
                $"Atomic tax-lot posting batch '{mutationBatchId}' has no retained mutation evidence.");
        }

        return new AtomicTaxLotJournalResult(
            batch.MutationBatchId,
            batch.MutationKind,
            batch.CanonicalFingerprint,
            isExactReplay,
            journals[0],
            mutations.Select(static mutation => mutation.LotAfter).ToArray(),
            mutations,
            batch.RetainedEvidence,
            batch.CorrectsMutationBatchId,
            batch.ReliefMethod,
            batch.PolicyRevision);
    }

    private async Task<StoredAtomicTaxLotBatch?> LoadAtomicTaxLotBatchAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid mutationBatchId,
        CancellationToken ct)
    {
        await using var query = connection.CreateCommand();
        query.Transaction = transaction;
        query.CommandText =
            $"""
            select mutation_batch_id,
                   ledger_book_id,
                   period_id,
                   journal_entry_id,
                   source_event_id,
                   idempotency_key,
                   canonical_fingerprint,
                   expected_period_version,
                   mutation_kind,
                   retained_evidence::text,
                   corrects_mutation_batch_id,
                   relief_method,
                   policy_revision,
                   created_at,
                   security_id,
                   book_position_id
            from {Qualified("atomic_tax_lot_posting_batches")}
            where mutation_batch_id = @mutation_batch_id;
            """;
        query.Parameters.AddWithValue("mutation_batch_id", mutationBatchId);

        await using var reader = await query.ExecuteReaderAsync(ct).ConfigureAwait(false);
        return await reader.ReadAsync(ct).ConfigureAwait(false)
            ? ReadStoredAtomicTaxLotBatch(reader)
            : null;
    }

    private async Task<IReadOnlyList<LedgerTaxLotMutationRecord>> LoadTaxLotMutationsAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid mutationBatchId,
        CancellationToken ct)
    {
        await using var query = connection.CreateCommand();
        query.Transaction = transaction;
        query.CommandText =
            $"""
            select mutation_record_id,
                   mutation_batch_id,
                   mutation_kind,
                   tax_lot_record_id,
                   lot_id,
                   selection_ordinal,
                   quantity_before,
                   quantity_delta,
                   quantity_after,
                   unit_cost,
                   cost_basis,
                   expected_version,
                   result_version,
                   selection_evidence_id,
                   retained_evidence::text,
                   journal_entry_id,
                   source_event_id,
                   corrects_mutation_batch_id,
                   relief_method,
                   policy_revision,
                   lot_snapshot_before::text,
                   lot_snapshot_after::text,
                   recorded_at,
                   security_id,
                   book_position_id
            from {Qualified("tax_lot_mutations")}
            where mutation_batch_id = @mutation_batch_id
            order by selection_ordinal;
            """;
        query.Parameters.AddWithValue("mutation_batch_id", mutationBatchId);

        var mutations = new List<LedgerTaxLotMutationRecord>();
        await using var reader = await query.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await reader.ReadAsync(ct).ConfigureAwait(false))
        {
            var lotBefore = reader.IsDBNull(20)
                ? null
                : DeserializeTaxLotSnapshot(reader.GetString(20));
            var lotAfter = DeserializeTaxLotSnapshot(reader.GetString(21));
            mutations.Add(new LedgerTaxLotMutationRecord(
                reader.GetGuid(0),
                reader.GetGuid(1),
                Enum.Parse<AtomicTaxLotMutationKind>(reader.GetString(2), ignoreCase: false),
                reader.GetGuid(3),
                reader.GetString(4),
                reader.GetInt32(5),
                reader.GetDecimal(6),
                reader.GetDecimal(7),
                reader.GetDecimal(8),
                reader.GetDecimal(9),
                reader.GetDecimal(10),
                reader.GetInt64(11),
                reader.GetInt64(12),
                reader.GetString(13),
                reader.GetGuid(15),
                reader.GetGuid(16),
                DeserializeRetainedEvidence(reader.GetString(14)),
                ReadUtcDateTimeOffset(reader, 22),
                lotBefore,
                lotAfter,
                reader.IsDBNull(17) ? null : reader.GetGuid(17),
                reader.IsDBNull(18) ? null : reader.GetString(18),
                reader.IsDBNull(19) ? null : reader.GetString(19),
                reader.GetGuid(23),
                reader.GetGuid(24)));
        }

        return mutations;
    }

    private static bool IsExactBatchIdentity(
        StoredAtomicTaxLotBatch retained,
        AtomicTaxLotJournalCommand command)
        => retained.MutationBatchId == command.MutationBatchId &&
           retained.LedgerBookId == command.LedgerBookId &&
           retained.PeriodId == command.Journal.PeriodId &&
           retained.JournalEntryId == command.Journal.Entry.JournalEntryId &&
           retained.SourceEventId == command.SourceEventId &&
           retained.SecurityId == ResolveAtomicAssetScope(command.Journal).SecurityId &&
           retained.BookPositionId == ResolveAtomicAssetScope(command.Journal).BookPositionId &&
           string.Equals(retained.IdempotencyKey, command.IdempotencyKey, StringComparison.Ordinal);

    private static StoredAtomicTaxLotBatch ReadStoredAtomicTaxLotBatch(NpgsqlDataReader reader)
        => new(
            reader.GetGuid(0),
            reader.GetGuid(1),
            reader.GetGuid(2),
            reader.GetGuid(3),
            reader.GetGuid(4),
            reader.GetString(5),
            reader.GetString(6),
            reader.GetInt64(7),
            Enum.Parse<AtomicTaxLotMutationKind>(reader.GetString(8), ignoreCase: false),
            DeserializeRetainedEvidence(reader.GetString(9)),
            reader.IsDBNull(10) ? null : reader.GetGuid(10),
            reader.IsDBNull(11) ? null : reader.GetString(11),
            reader.IsDBNull(12) ? null : reader.GetString(12),
            ReadUtcDateTimeOffset(reader, 13),
            reader.GetGuid(14),
            reader.GetGuid(15));

    private static void AddAtomicBatchIdentityParameters(
        NpgsqlCommand databaseCommand,
        AtomicTaxLotJournalCommand command)
    {
        databaseCommand.Parameters.AddWithValue("mutation_batch_id", command.MutationBatchId);
        databaseCommand.Parameters.AddWithValue("journal_entry_id", command.Journal.Entry.JournalEntryId);
        databaseCommand.Parameters.AddWithValue("ledger_book_id", command.LedgerBookId);
        databaseCommand.Parameters.AddWithValue("source_event_id", command.SourceEventId);
        databaseCommand.Parameters.AddWithValue("idempotency_key", command.IdempotencyKey);
    }

    private static void AddAtomicTaxLotParameters(NpgsqlCommand databaseCommand, LedgerTaxLotRecord lot)
    {
        databaseCommand.Parameters.AddWithValue("tax_lot_record_id", lot.TaxLotRecordId);
        databaseCommand.Parameters.AddWithValue("ledger_book_id", lot.LedgerBookId);
        AddAccountParameters(databaseCommand, lot.Account);
        databaseCommand.Parameters.AddWithValue("lot_id", lot.LotId);
        databaseCommand.Parameters.AddWithValue("acquired_date", lot.AcquiredDate);
        databaseCommand.Parameters.AddWithValue("original_quantity", lot.OriginalQuantity);
        databaseCommand.Parameters.AddWithValue("open_quantity", lot.OpenQuantity);
        databaseCommand.Parameters.AddWithValue("unit_cost", lot.UnitCost);
        databaseCommand.Parameters.AddWithValue("currency", lot.Currency);
        databaseCommand.Parameters.AddWithValue(
            "source_journal_entry_id",
            (object?)lot.SourceJournalEntryId ?? DBNull.Value);
        databaseCommand.Parameters.AddWithValue("evidence_ref", (object?)lot.EvidenceRef ?? DBNull.Value);
        databaseCommand.Parameters.AddWithValue("version", lot.Version);
        databaseCommand.Parameters.AddWithValue(
            "originating_mutation_batch_id",
            (object?)lot.OriginatingMutationBatchId ?? DBNull.Value);
        databaseCommand.Parameters.AddWithValue(
            "last_mutation_batch_id",
            (object?)lot.LastMutationBatchId ?? DBNull.Value);
        databaseCommand.Parameters.AddWithValue("created_at", lot.CreatedAt.UtcDateTime);
        databaseCommand.Parameters.AddWithValue("updated_at", lot.UpdatedAt.UtcDateTime);
        databaseCommand.Parameters.AddWithValue("security_id", lot.SecurityId);
        databaseCommand.Parameters.AddWithValue("book_position_id", lot.BookPositionId);
        databaseCommand.Parameters.AddWithValue("original_face", (object?)lot.OriginalFace ?? DBNull.Value);
        databaseCommand.Parameters.AddWithValue("booked_factor", (object?)lot.BookedFactor ?? DBNull.Value);
        databaseCommand.Parameters.AddWithValue("par_basis", (object?)lot.ParBasis ?? DBNull.Value);
        databaseCommand.Parameters.AddWithValue("acquisition_terms", NpgsqlTypes.NpgsqlDbType.Jsonb,
            lot.Acquisition is null ? DBNull.Value : System.Text.Json.JsonSerializer.Serialize(lot.Acquisition));
    }

    private static (Guid SecurityId, Guid BookPositionId) ResolveAtomicAssetScope(
        LedgerJournalEntryWrite journal)
    {
        if (journal.Entry.Metadata.SecurityId is not { } securityId || securityId == Guid.Empty)
        {
            throw new LedgerValidationException(
                "Atomic tax-lot journal metadata requires a non-empty Security Master identity.");
        }

        var lineScopes = journal.Entry.Lines
            .Select(static line => (
                SecurityId: line.Dimensions?.InstrumentId ?? Guid.Empty,
                BookPositionId: line.Dimensions?.PositionId ?? Guid.Empty))
            .Distinct()
            .ToArray();
        if (lineScopes.Length != 1 ||
            lineScopes[0].SecurityId != securityId ||
            lineScopes[0].BookPositionId == Guid.Empty)
        {
            throw new LedgerValidationException(
                "Every atomic tax-lot journal line must carry the same Security Master and book-position identities as the journal metadata.");
        }

        return lineScopes[0];
    }

    private static string ResolveAtomicFunctionalCurrency(LedgerJournalEntryWrite journal)
    {
        var currencies = journal.Entry.Lines
            .Select(static line => line.Currency?.FunctionalCurrency)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (currencies.Length != 1 || string.IsNullOrWhiteSpace(currencies[0]))
        {
            throw new LedgerValidationException(
                "Every atomic tax-lot journal line must retain one exact functional currency.");
        }

        return currencies[0]!.Trim().ToUpperInvariant();
    }

    private static string SerializeRetainedEvidence(IReadOnlyList<RetainedEvidenceIdentityDto> evidence)
        => JsonSerializer.Serialize(evidence, JsonOptions);

    private static IReadOnlyList<RetainedEvidenceIdentityDto> DeserializeRetainedEvidence(string json)
        => JsonSerializer.Deserialize<RetainedEvidenceIdentityDto[]>(json, JsonOptions)
           ?? throw new InvalidOperationException("Stored atomic tax-lot evidence is invalid.");

    private static LedgerTaxLotRecord DeserializeTaxLotSnapshot(string json)
        => JsonSerializer.Deserialize<LedgerTaxLotRecord>(json, JsonOptions)
           ?? throw new InvalidOperationException("Stored atomic tax-lot snapshot is invalid.");

    private sealed record StoredAtomicTaxLotBatch(
        Guid MutationBatchId,
        Guid LedgerBookId,
        Guid PeriodId,
        Guid JournalEntryId,
        Guid SourceEventId,
        string IdempotencyKey,
        string CanonicalFingerprint,
        long ExpectedPeriodVersion,
        AtomicTaxLotMutationKind MutationKind,
        IReadOnlyList<RetainedEvidenceIdentityDto> RetainedEvidence,
        Guid? CorrectsMutationBatchId,
        string? ReliefMethod,
        string? PolicyRevision,
        DateTimeOffset CreatedAt,
        Guid SecurityId,
        Guid BookPositionId);
}
