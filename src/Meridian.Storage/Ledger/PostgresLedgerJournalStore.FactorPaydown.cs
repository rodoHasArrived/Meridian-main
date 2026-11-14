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
    // Pure, pre-lock: the instruction, command shape, approved command, scope binding and the
    // structural journal shape (cash debit = principal, carrying credit = basis relieved, one
    // realized line for the difference). Account identities need the locked lots and are checked
    // in LockFactorPaydownAuthorityAsync.
    private static void ValidateAtomicFactorPaydown(AtomicTaxLotJournalCommand command)
    {
        var instruction = command.FactorPaydown
            ?? throw new LedgerValidationException("Factor-paydown posting requires the reviewed paydown instruction.");
        var issues = AssetLotMutationInstructionValidator.Validate(AssetAccountingEventKindDto.CorporateAction,
            new AssetLotMutationInstructionDto(AssetLotMutationIntentDto.FactorPaydown, FactorPaydown: instruction),
            instruction.PrincipalPaydown, command.Journal.Entry.Metadata.EffectiveDate ?? default, command.RetainedEvidence);
        if (issues.Count > 0)
            throw new LedgerValidationException(string.Join(" ", issues));
        if (instruction.ScheduledPrincipal is not null && instruction.EconomicEvent.RetainedEvidence
            .Where(e => e.SubjectType == "BondPrincipalCashReceipt").Any(e => !command.RetainedEvidence.Contains(e)))
            throw new LedgerValidationException("Scheduled principal must retain every exact principal-receipt component for locked single-use allocation.");
        if (command.AcquisitionLot is not null || command.DisposalSelections is { Count: > 0 }
            || command.Amortization is not null || command.CorporateAction is not null || command.CapitalReturn is not null
            || command.ReliefMethod is not null || command.PolicyRevision is not null
            || command.CorrectsMutationBatchId is not null || command.Journal.SourceJournalEntryId is not null
            || command.Journal.PostingCommand?.SourceJournalEntryId is not null
            || command.Journal.PostingCommand?.Intent is not (AccountingPostingIntentDto.Originating or AccountingPostingIntentDto.Adjustment)
            || command.Journal.PostingKind is not (LedgerPostingKindDto.Originating or LedgerPostingKindDto.Adjustment))
            throw new LedgerValidationException("Factor paydown cannot combine other lot mutations or correction instructions.");
        if (command.Journal.PostingCommand is not { ApprovalState: AccountingPostingApprovalStateDto.Approved } posting
            || string.IsNullOrWhiteSpace(posting.ApprovalId) || string.IsNullOrWhiteSpace(posting.Actor)
            || !AssetAccountingEventTypeNames.TryParse(posting.SourceEventType, out var kind)
            || kind != AssetAccountingEventKindDto.CorporateAction
            || !JsonElement.DeepEquals(JsonSerializer.SerializeToElement(posting.LotFactorPaydown),
                JsonSerializer.SerializeToElement(instruction)))
            throw new LedgerValidationException("Factor paydown requires an approved corporate-action command retaining the exact reviewed instruction.");
        var scope = instruction.Scope;
        if (scope.PeriodId != command.Journal.PeriodId || instruction.ExpectedPeriodVersion != command.ExpectedPeriodVersion
            || instruction.EconomicEvent.EventId != command.SourceEventId
            || scope.AccountingBasis != command.Journal.AccountingBasis
            || scope.LedgerBookId != command.LedgerBookId || scope.SecurityId != command.Journal.Entry.Metadata.SecurityId
            || instruction.Currency != ResolveAtomicFunctionalCurrency(command.Journal)
            || command.Journal.Entry.Metadata.Tags?.GetValueOrDefault(OpenLotFactorPaydown.JournalFingerprintTag)
                != OpenLotFactorPaydown.Fingerprint(instruction))
            throw new LedgerValidationException("Factor paydown must bind the exact book, period, event, currency and instruction fingerprint.");
        ValidateFactorPaydownJournalShape(command, instruction);
    }

    private static void ValidateFactorPaydownJournalShape(AtomicTaxLotJournalCommand command,
        OpenLotFactorPaydownInstructionDto instruction)
    {
        var scope = instruction.Scope;
        var lines = command.Journal.Entry.Lines;
        var relieved = OpenLotFactorPaydown.BasisRelieved(instruction).Functional;
        var result = OpenLotFactorPaydown.RealizedResult(instruction);
        var singleLotId = instruction.Lots.Count == 1 ? instruction.Lots[0].ExpectedLot.LotId : null;
        if (lines.Any(line => line.Dimensions?.InstrumentId != scope.SecurityId || line.Dimensions?.PositionId != scope.BookPositionId
                || line.Dimensions?.TaxLotId is { } label && label != singleLotId
                || (line.Debit == 0m) == (line.Credit == 0m) || line.Debit < 0m || line.Credit < 0m
                || decimal.Round(line.Debit, 10) != line.Debit || decimal.Round(line.Credit, 10) != line.Credit
                || line.Currency is not { } currency || currency.FunctionalCurrency != instruction.Currency
                || currency.TransactionCurrency != instruction.Currency || currency.FxRateToFunctional != 1m
                || currency.TransactionDebit != line.Debit || currency.TransactionCredit != line.Credit))
            throw new LedgerValidationException("Factor-paydown journal lines must carry the paid-down security and position in functional currency at durable precision.");
        var assetDebits = lines.Where(static line => line.Account.AccountType == LedgerAccountType.Asset && line.Debit > 0m).ToArray();
        var assetCredits = lines.Where(static line => line.Account.AccountType == LedgerAccountType.Asset && line.Credit > 0m).ToArray();
        if (assetDebits.Length != 1 || assetDebits[0].Debit != instruction.PrincipalPaydown)
            throw new LedgerValidationException("Factor paydown requires one cash debit equal to the principal repaid.");
        if (relieved < 0m || assetCredits.Length != (relieved == 0m ? 0 : 1)
            || (relieved != 0m && (assetCredits[0].Credit != relieved || assetCredits[0].Account == assetDebits[0].Account)))
            throw new LedgerValidationException("Factor paydown requires one carrying-account credit equal to the functional basis relieved.");
        var realized = lines.Except(assetDebits).Except(assetCredits).ToArray();
        if (result == 0m
            ? realized.Length != 0
            : realized.Length != 1 || (result > 0m
                ? realized[0].Account.AccountType != LedgerAccountType.Revenue || realized[0].Credit != result
                : realized[0].Account.AccountType != LedgerAccountType.Expense || realized[0].Debit != -result))
            throw new LedgerValidationException("Factor paydown recognises the difference between principal and relieved basis as exactly one realized gain or loss line.");
    }

    // Under lock: the carrying account is the lots' exact account, cash is the carrying account's
    // cash account, and the realized line is the carrying account's exact gain or loss account.
    private static void ValidateFactorPaydownJournalAccounts(AtomicTaxLotJournalCommand command,
        OpenLotFactorPaydownInstructionDto instruction, LedgerAccount carrying)
    {
        var entry = command.Journal.Entry;
        var cash = entry.Lines.Single(static line => line.Account.AccountType == LedgerAccountType.Asset && line.Debit > 0m);
        var cashAccount = string.IsNullOrWhiteSpace(carrying.FinancialAccountId)
            ? LedgerAccounts.Cash : LedgerAccounts.CashAccount(carrying.FinancialAccountId);
        var currencyCashAccount = LedgerAccounts.CashInCurrency(instruction.Currency, carrying.FinancialAccountId);
        if (cash.Account != cashAccount && cash.Account != currencyCashAccount)
            throw new LedgerValidationException("Factor-paydown cash must be received into the carrying account's cash account.");
        if (entry.Lines.Any(line => line.Account.AccountType == LedgerAccountType.Asset && line.Credit > 0m && line.Account != carrying))
            throw new LedgerValidationException("Factor-paydown carrying credit must use the paid-down lots' exact carrying account.");
        if (DisposalRecognizedGainOrLoss(entry, carrying) != OpenLotFactorPaydown.RealizedResult(instruction))
            throw new LedgerValidationException("Factor-paydown realized result must equal principal less functional basis relieved on the carrying account's exact gain or loss account.");
    }

    private async Task<IReadOnlyList<LedgerTaxLotRecord>> LockFactorPaydownAuthorityAsync(NpgsqlConnection connection,
        NpgsqlTransaction transaction, AtomicTaxLotJournalCommand command, CancellationToken ct)
    {
        var instruction = command.FactorPaydown!;
        var scope = instruction.Scope;
        var effectiveDate = instruction.EconomicEvent.EffectiveDate;
        if (_backfillSecurityMaster?.Invoke() is not PostgresSecurityMasterStore securities
            || _backfillPositions?.Invoke() is not PostgresAssetOperationsProjectionStore positions)
            throw new LedgerValidationException("Factor paydown requires authoritative PostgreSQL Security Master and book-position stores.");
        if (_tenantScope.IsFailClosed && scope.TenantId != ResolveCallerTenant())
            throw new LedgerValidationException("Factor-paydown tenant must match the retained posting authority.");
        var security = await securities.LockForLotPostingAsync(connection, transaction, scope.SecurityId, ct).ConfigureAwait(false);
        if (security is null || security.Version != scope.ExpectedSecurityVersion
            || !OpenLotAmortization.AcceptedSecurityHashes(security).Contains(instruction.ExpectedSecurityHash)
            || security.Currency != instruction.Currency || security.Status != SecurityStatusDto.Active
            || DateOnly.FromDateTime(security.EffectiveFrom.UtcDateTime) > effectiveDate
            || security.EffectiveTo is { } securityEnd && DateOnly.FromDateTime(securityEnd.UtcDateTime) < effectiveDate)
            throw new LedgerValidationException("Factor-paydown Security Master identity, terms or effective date is stale.");
        var position = await positions.LockForLotPostingAsync(connection, transaction, scope.BookPositionId, ct).ConfigureAwait(false);
        var book = await LoadLedgerBookAsync(connection, transaction, command.LedgerBookId, ct).ConfigureAwait(false);
        if (position is null || book is null || position.Version != scope.ExpectedBookPositionVersion
            || position.PositionSide is not (BookPositionSides.Long or BookPositionSides.Asset) || position.Status != "Active"
            || position.SecurityId != scope.SecurityId || position.PositionId != scope.BookPositionId
            || position.BookContext.LedgerBookId != book.LedgerBookId
            || position.BookContext.BaseCurrency != book.BaseCurrency || book.BaseCurrency != instruction.Currency
            || position.BookContext.FundProfileId != book.FundProfileId || scope.FundProfileId != book.FundProfileId
            || position.BookContext.FundStructureNodeId != book.FundStructureNodeId
            || position.BookContext.FundStructureNodeKind != book.FundStructureNodeKind
            || position.EffectiveFrom > effectiveDate || position.EffectiveTo is { } end && end < effectiveDate)
            throw new LedgerValidationException("Factor-paydown book-position version or accounting scope is stale.");

        var lotIds = instruction.Lots.Select(static lot => lot.ExpectedLot.TaxLotRecordId).ToArray();
        var locked = (await LoadTaxLotsForUpdateAsync(connection, transaction, command.LedgerBookId, lotIds, ct).ConfigureAwait(false))
            .ToDictionary(static lot => lot.TaxLotRecordId);
        var before = new LedgerTaxLotRecord[lotIds.Length];
        for (var index = 0; index < lotIds.Length; index++)
        {
            if (!locked.TryGetValue(lotIds[index], out var lot))
                throw new LedgerValidationException("Factor-paydown lot no longer exists.");
            var expected = instruction.Lots[index].ExpectedLot;
            if (!lot.HasFaceValueTerms || !OpenLotAmortization.SameLot(lot.ToOpenLot(), expected)
                || (lot.CurrentFactor ?? lot.BookedFactor) != instruction.PriorFactor
                || lot.Account.AccountType != LedgerAccountType.Asset || lot.Account != locked[lotIds[0]].Account)
                throw new LedgerValidationException("Factor-paydown lot version, basis, factor or exact carrying account changed.");
            before[index] = lot;
        }
        if (instruction.ScheduledPrincipal is { } scheduled)
        {
            foreach (var receipt in command.RetainedEvidence.Where(e => e.SubjectType == "BondPrincipalCashReceipt")
                .OrderBy(e => e.SourceSystem, StringComparer.Ordinal).ThenBy(e => e.SourceReference, StringComparer.Ordinal))
            {
                await using var receiptLock = connection.CreateCommand();
                receiptLock.Transaction = transaction;
                receiptLock.CommandText = "select pg_advisory_xact_lock(hashtextextended(@receipt_key, 0))";
                receiptLock.Parameters.AddWithValue("receipt_key", $"bond-principal:{book.LedgerBookId:D}:{receipt.SourceSystem}:{receipt.SourceReference}");
                await receiptLock.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
                await using var consumed = connection.CreateCommand();
                consumed.Transaction = transaction;
                consumed.CommandText = $"""
                    select exists (
                        select 1 from {Qualified("atomic_tax_lot_posting_batches")} b,
                            lateral jsonb_array_elements(b.retained_evidence) e
                        where b.ledger_book_id = @book and b.mutation_kind = 'FactorPaydown'
                          and coalesce(e->>'SubjectType', e->>'subjectType') = 'BondPrincipalCashReceipt'
                          and coalesce(e->>'SourceSystem', e->>'sourceSystem') = @source
                          and coalesce(e->>'SourceReference', e->>'sourceReference') = @reference)
                    """;
                consumed.Parameters.AddWithValue("book", book.LedgerBookId);
                consumed.Parameters.AddWithValue("source", receipt.SourceSystem);
                consumed.Parameters.AddWithValue("reference", receipt.SourceReference);
                if ((bool)(await consumed.ExecuteScalarAsync(ct).ConfigureAwait(false))!)
                    throw new LedgerValidationException("The retained principal-receipt component was already allocated to a posted principal event.");
            }
            var authority = _bondIncomeAuthority?.Invoke()
                ?? throw new LedgerValidationException("Scheduled principal posting requires authoritative bond-income recomputation.");
            foreach (var inputs in scheduled.IncomeInstructions)
            {
                var income = inputs.BondIncome!;
                if (book.AccountingBasis != AccountingBasisKindDto.Gaap || income.AccountingPolicyId != book.AccountingPolicyId
                    || income.AccountingPolicyVersion != book.AccountingPolicyVersion)
                    throw new LedgerValidationException("Scheduled principal must use the locked book's adopted GAAP policy.");
                var rebuilt = await authority.PreviewAsync(new(book.LedgerBookId, inputs.ExpectedLot.TaxLotRecordId,
                    inputs.AsOfDate, income.KnownAt, income.AccountId), ct).ConfigureAwait(false);
                if (!OpenLotAmortization.SameInstruction(inputs, rebuilt.Instruction))
                    throw new LedgerValidationException("Scheduled principal income, lot or retained input evidence changed after review.");
            }
            if (instruction.NewFactor == 0m)
            {
                var terminal = new List<BondIncomeTerminalCouponReceiptDto>();
                var openings = new List<BondIncomeTerminalCouponOpeningDto>();
                foreach (var inputs in scheduled.IncomeInstructions)
                {
                    if (!BondIncomeTerminalCoupon.HasCouponEconomics(inputs))
                        continue;
                    var couponScope = BondIncomeTerminalCoupon.Scope(instruction, inputs);
                    var latest = await ReadCouponReceiptAsync(connection, transaction, couponScope,
                        inputs.ExpectedLot.TaxLotRecordId, null, ct).ConfigureAwait(false);
                    var targets = BondIncomeTerminalCoupon.EarnedTargets(inputs);
                    if (latest is null && targets.RawReceivableEarned == 0m && targets.RawPayableEarned == 0m
                        && BondIncomeTerminalCoupon.HasCashRights(inputs))
                    {
                        var lot = before.Single(l => l.TaxLotRecordId == inputs.ExpectedLot.TaxLotRecordId);
                        var opening = BondIncomeTerminalCoupon.Opening(inputs, lot.OriginatingMutationBatchId ?? Guid.Empty,
                            lot.SourceJournalEntryId ?? Guid.Empty);
                        var couponAuthority = _couponRecognitionAuthority?.Invoke()
                            ?? throw new LedgerValidationException("Terminal purchased-interest opening requires authoritative coupon reconciliation.");
                        await couponAuthority.VerifyTerminalOpeningAsync(couponScope, inputs, opening, scope.PeriodId, ct).ConfigureAwait(false);
                        openings.Add(opening);
                        continue;
                    }
                    var reconciled = BondIncomeTerminalCoupon.Reconcile(inputs, couponScope, latest);
                    if (reconciled is not null)
                        terminal.Add(reconciled);
                }
                BondIncomeTerminalCoupon.RequireEvidence(instruction, terminal, openings);
            }
        }
        ValidateFactorPaydownJournalAccounts(command, instruction, before[0].Account);

        // A paydown that skipped an open lot would leave the position's factor split across lots,
        // so the instruction must name exactly the lots open in this scope as of the paydown date.
        var openAsOf = await ListOpenTaxLotsByAssetScopeCoreAsync(connection, transaction, command.LedgerBookId,
            scope.SecurityId, scope.BookPositionId, effectiveDate, ct).ConfigureAwait(false);
        if (openAsOf.Count != lotIds.Length || openAsOf.Any(lot => !locked.ContainsKey(lot.TaxLotRecordId)))
            throw new LedgerValidationException("Factor paydown must restate every lot open in the scope as of its effective date; a partial scope leaves the position's factor split.");
        // The allocation's rounding (largest remainder, ties to the later lot) depends on lot order,
        // so the instruction must state the lots in the order the store reads them.
        if (!openAsOf.Select(static lot => lot.TaxLotRecordId).SequenceEqual(lotIds))
            throw new LedgerValidationException("Factor-paydown lots must be stated in the store's acquired-date, lot-id order so principal rounding lands on the same lots.");
        var histories = (await ReadDatedLotQuantitiesAsync(connection, transaction, command.LedgerBookId, lotIds, ct).ConfigureAwait(false))
            .ToLookup(static mutation => mutation.TaxLotRecordId);
        foreach (var lot in before)
        {
            var history = histories[lot.TaxLotRecordId].ToArray();
            if (history.Any(mutation => mutation.EffectiveDate > effectiveDate)
                || HistoricalTaxLotQuantity.Project(lot, history, effectiveDate).OpenQuantity != lot.OpenQuantity)
                throw new LedgerValidationException("Factor paydown cannot precede retained lot mutations or use unproved holdings.");
        }
        return before;
    }

    private async Task<IReadOnlyList<LedgerTaxLotMutationRecord>> ApplyFactorPaydownAsync(NpgsqlConnection connection,
        NpgsqlTransaction transaction, AtomicTaxLotJournalCommand command, IReadOnlyList<LedgerTaxLotRecord> lots,
        DateTimeOffset recordedAt, CancellationToken ct)
    {
        var instruction = command.FactorPaydown!;
        var projected = OpenLotFactorPaydown.Project(instruction);
        var mutations = new List<LedgerTaxLotMutationRecord>(lots.Count);
        for (var index = 0; index < lots.Count; index++)
        {
            var before = lots[index];
            var lot = instruction.Lots[index];
            var adjustment = new OpenLotBasisAdjustmentDto(command.MutationBatchId, OpenLotBasisAdjustmentReasons.FactorPaydown,
                before.OpenQuantity, lot.TransactionBasisAfter, lot.FunctionalBasisAfter, FactorPaydown: instruction);
            await using var update = connection.CreateCommand();
            update.Transaction = transaction;
            update.CommandText = $"""
                update {Qualified("tax_lots")}
                set version = version + 1, last_mutation_batch_id = @batch, updated_at = @at,
                    current_factor = @new_factor, basis_adjustment = @basis_adjustment,
                    open_quantity = case when @new_factor = 0 then 0 else open_quantity end
                where tax_lot_record_id = @id and ledger_book_id = @book and security_id = @security
                    and book_position_id = @position and version = @version and open_quantity = @quantity
                    and coalesce(current_factor, booked_factor) = @prior_factor
                returning {AverageCostLotColumns};
                """;
            update.Parameters.AddWithValue("batch", command.MutationBatchId);
            update.Parameters.AddWithValue("at", recordedAt.UtcDateTime);
            update.Parameters.AddWithValue("new_factor", instruction.NewFactor);
            update.Parameters.AddWithValue("id", before.TaxLotRecordId);
            update.Parameters.AddWithValue("book", command.LedgerBookId);
            update.Parameters.AddWithValue("security", before.SecurityId);
            update.Parameters.AddWithValue("position", before.BookPositionId);
            update.Parameters.AddWithValue("version", before.Version);
            update.Parameters.AddWithValue("quantity", before.OpenQuantity);
            update.Parameters.AddWithValue("prior_factor", instruction.PriorFactor);
            AddBasisAdjustmentParameter(update, adjustment);
            LedgerTaxLotRecord after;
            await using (var reader = await update.ExecuteReaderAsync(ct).ConfigureAwait(false))
            {
                if (!await reader.ReadAsync(ct).ConfigureAwait(false))
                    throw new LedgerValidationException("Factor-paydown lot compare-and-swap failed.");
                after = ReadTaxLot(reader);
            }
            if (after.CurrentFactor != instruction.NewFactor || !OpenLotAmortization.SameLot(after.ToOpenLot(), projected[index]))
                throw new LedgerValidationException("Factor-paydown lot of record does not reproduce the reviewed projection.");
            var evidenceId = lot.ExpectedLot.Acquisition.Evidence.First(evidence => evidence.SubjectType == "OpenLotAcquisition"
                && evidence.SubjectId == before.TaxLotRecordId.ToString("D")).EvidenceId;
            mutations.Add(BuildMutationRecord(command, after, before, lot.Ordinal,
                instruction.NewFactor == 0m ? -before.OpenQuantity : 0m, before.Version, evidenceId, recordedAt,
                costBasis: lot.ExpectedLot.OpenFunctionalCostBasis - lot.FunctionalBasisAfter));
        }
        return mutations;
    }
}
