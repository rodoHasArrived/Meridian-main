using System.Text.Json;
using Meridian.Contracts.Accounting.Lots;
using Meridian.Contracts.AssetOperations;
using Meridian.Contracts.Ledger;
using Meridian.Ledger;
using Meridian.Storage.Ledger;

namespace Meridian.FinancialOperations.Ledger;

/// <summary>
/// Proves a zero opening recognized-coupon balance from the existing lot and journal authority.
/// This does not migrate legacy balances or infer that missing dimensional history means zero.
/// </summary>
public static class BondCouponRecognitionOpeningGuard
{
    private const string Reconcile = "Coupon recognition requires an explicit retained opening reconciliation: prior or ambiguous coupon accounting cannot be assumed unrecognized.";

    /// <summary>
    /// Call only when the lot has no native coupon-recognition receipt. Repeat at final canonical
    /// verification. The journal store must expose the complete owning-book history, not an as-of
    /// or knowledge-time projection: a later-dated posted entry can already consume this income.
    /// </summary>
    public static Task ValidateAsync(ILedgerJournalStore ledger, AccountCashForecastScopeDto scope,
        OpenLotDto lot, BondCouponBookAccountsDto accountPaths,
        IReadOnlyList<BondCouponRecognitionAccountDto> accounts, CancellationToken ct = default)
        => ValidateCoreAsync(ledger, scope, lot, accountPaths, accounts, null, ct);

    internal static async Task ValidateTerminalAsync(ILedgerJournalStore ledger, AccountCashForecastScopeDto scope,
        OpenLotDto lot, BondCouponBookAccountsDto accountPaths, IReadOnlyList<BondCouponRecognitionAccountDto> accounts,
        BondCouponTerminalCashAuthorityDto terminal, CancellationToken ct)
    {
        if (terminal.Opening is null || terminal.EarnedReceipt is not null || lot.OpenQuantity != 0m)
            throw new InvalidOperationException(Reconcile);
        var physical = (await ledger.GetTaxLotsByIdsAsync(lot.LedgerBookId, [lot.TaxLotRecordId], ct).ConfigureAwait(false)).SingleOrDefault();
        var principal = await ledger.GetAtomicTaxLotPostingAsync(terminal.PrincipalMutationBatchId, ct).ConfigureAwait(false);
        if (physical is null || physical.LastMutationBatchId != terminal.PrincipalMutationBatchId || physical.CurrentFactor != 0m
            || principal is null || principal.MutationKind != AtomicTaxLotMutationKind.FactorPaydown
            || principal.FactorPaydown is not { NewFactor: 0m, ScheduledPrincipal: not null }
            || principal.Journal.Entry.JournalEntryId != terminal.PrincipalJournalEntryId
            || principal.Journal.AggregateId != lot.LedgerBookId
            || principal.Mutations.SingleOrDefault(row => row.TaxLotRecordId == lot.TaxLotRecordId) is not { } mutation
            || !OpenLotAmortization.SameLot(mutation.LotAfter.ToOpenLot(), lot)
            || !OpenLotAmortization.SameLot(physical.ToOpenLot(), lot))
            throw new InvalidOperationException(Reconcile);
        await ValidateCoreAsync(ledger, scope, lot, accountPaths, accounts, principal.Journal, ct).ConfigureAwait(false);
    }

    internal static async Task ValidateRecognizedHistoryAsync(ILedgerJournalStore ledger, IBondCouponRecognitionStore receipts,
        AccountCashForecastScopeDto scope, OpenLotDto lot, BondCouponBookAccountsDto accountPaths,
        IReadOnlyList<BondCouponRecognitionAccountDto> accounts, BondCouponRecognitionReceiptDto latest,
        BondCouponTerminalCashAuthorityDto? terminal, DateTimeOffset knownAt, CancellationToken ct)
    {
        var history = await ledger.QueryAsync(new LedgerJournalEntryQuery(LedgerBookId: lot.LedgerBookId), ct).ConfigureAwait(false);
        var native = new List<LedgerJournalEntryRecord>();
        var visited = new HashSet<string>(StringComparer.Ordinal);
        BondCouponRecognitionReceiptDto? cursor = latest;
        while (cursor is not null)
        {
            var instruction = cursor.Instruction;
            if (!visited.Add(cursor.ReceiptId) || visited.Count > 10000 || instruction.Scope != scope
                || instruction.IncomeInstruction.ExpectedLot.TaxLotRecordId != lot.TaxLotRecordId
                || instruction.AccountMappings != accountPaths
                || cursor.ReceiptId != BondCouponRecognitionCalculator.Fingerprint(instruction)
                || cursor.PostedAtUtc > knownAt || instruction.ModelVersion != BondCouponRecognitionCalculator.ModelVersion)
                throw new InvalidOperationException(Reconcile);
            var journal = history.SingleOrDefault(row => row.Entry.JournalEntryId == cursor.JournalEntryId);
            var expected = instruction.Lines.Select(line => (Account: instruction.Accounts.Single(a => a.Path == line.AccountId).LedgerName,
                Type: instruction.Accounts.Single(a => a.Path == line.AccountId).AccountType, line.Debit, line.Credit))
                .OrderBy(line => line.Account, StringComparer.Ordinal).ThenBy(line => line.Type, StringComparer.Ordinal)
                .ThenBy(line => line.Debit).ThenBy(line => line.Credit).ToArray();
            if (journal is null || journal.AggregateId != lot.LedgerBookId
                || journal.AccountingBasis != AccountingBasisKindDto.Gaap || !journal.Entry.IsBalanced
                || journal.SourceEventId != instruction.EventId || journal.PeriodId != instruction.Request.PeriodId
                || journal.SourceJournalEntryId != instruction.ReversalTarget?.JournalEntryId
                || journal.PostingKind != (instruction.ReversalTarget is null ? LedgerPostingKindDto.Originating : LedgerPostingKindDto.Adjustment)
                || journal.CreatedAt > knownAt || journal.Entry.Timestamp > knownAt
                || journal.Entry.Metadata.EffectiveDate != instruction.Request.ThroughDate
                || !expected.SequenceEqual(journal.Entry.Lines.Select(line => (Account: line.Account.Name,
                    Type: line.Account.AccountType.ToString(), line.Debit, line.Credit)).OrderBy(line => line.Account, StringComparer.Ordinal)
                    .ThenBy(line => line.Type, StringComparer.Ordinal).ThenBy(line => line.Debit).ThenBy(line => line.Credit))
                || journal.Entry.Lines.Any(line => line.Account.FinancialAccountId != scope.AccountId || line.Account.Symbol is not null
                    || line.Dimensions?.BookId != scope.BookId || line.Dimensions.AccountId != scope.AccountId
                    || line.Dimensions.InstrumentId != lot.SecurityId || line.Dimensions.PositionId != lot.BookPositionId
                    || line.Dimensions.TaxLotId != lot.LotId
                    || line.Currency is { } currency && (currency.TransactionCurrency != scope.BaseCurrency
                        || currency.FunctionalCurrency != scope.BaseCurrency || currency.FxRateToFunctional != 1m)))
                throw new InvalidOperationException(Reconcile);
            native.Add(journal);
            if (instruction.PreviousReceiptId is not { } prior)
                break;
            cursor = await receipts.GetCouponRecognitionReceiptAsync(scope, lot.TaxLotRecordId, prior, ct).ConfigureAwait(false)
                ?? throw new InvalidOperationException(Reconcile);
        }
        LedgerJournalEntryRecord? principal = null;
        if (terminal is not null)
        {
            var closure = await ledger.GetAtomicTaxLotPostingAsync(terminal.PrincipalMutationBatchId, ct).ConfigureAwait(false);
            if (closure is null || closure.MutationKind != AtomicTaxLotMutationKind.FactorPaydown
                || closure.FactorPaydown is not { NewFactor: 0m, ScheduledPrincipal: not null }
                || closure.Journal.Entry.JournalEntryId != terminal.PrincipalJournalEntryId
                || closure.Mutations.SingleOrDefault(row => row.TaxLotRecordId == lot.TaxLotRecordId) is not { } mutation
                || !OpenLotAmortization.SameLot(mutation.LotAfter.ToOpenLot(), lot))
                throw new InvalidOperationException(Reconcile);
            principal = closure.Journal;
        }
        await ValidateCoreAsync(ledger, scope, lot, accountPaths, accounts, principal, ct, native, history).ConfigureAwait(false);
    }

    private static async Task ValidateCoreAsync(ILedgerJournalStore ledger, AccountCashForecastScopeDto scope,
        OpenLotDto lot, BondCouponBookAccountsDto accountPaths, IReadOnlyList<BondCouponRecognitionAccountDto> accounts,
        LedgerJournalEntryRecord? terminalPrincipal, CancellationToken ct,
        IReadOnlyList<LedgerJournalEntryRecord>? native = null, IReadOnlyList<LedgerJournalEntryRecord>? retainedHistory = null)
    {
        ArgumentNullException.ThrowIfNull(ledger);
        ArgumentNullException.ThrowIfNull(scope);
        ArgumentNullException.ThrowIfNull(lot);
        ArgumentNullException.ThrowIfNull(accountPaths);
        ArgumentNullException.ThrowIfNull(accounts);
        if (!Guid.TryParse(scope.BookId, out var book) || book != lot.LedgerBookId
            || !Guid.TryParse(scope.AccountId, out var account) || account == Guid.Empty
            || lot.TaxLotRecordId == Guid.Empty || lot.SecurityId == Guid.Empty || lot.BookPositionId == Guid.Empty
            || string.IsNullOrWhiteSpace(scope.TenantId) || string.IsNullOrWhiteSpace(scope.CompanyId))
            throw new ArgumentException("Coupon opening authority requires exact account, book, security and lot scope.");
        var retained = (await ledger.GetTaxLotsByIdsAsync(book, [lot.TaxLotRecordId], ct).ConfigureAwait(false)).SingleOrDefault()
            ?? throw new InvalidOperationException(Reconcile);
        if (retained.LedgerBookId != book || retained.TaxLotRecordId != lot.TaxLotRecordId
            || !Guid.TryParse(retained.Account.FinancialAccountId, out var owner) || owner != account
            || JsonSerializer.Serialize(retained.ToOpenLot()) != JsonSerializer.Serialize(lot)
            || retained.OriginatingMutationBatchId is not { } birthId || birthId == Guid.Empty
            || retained.SourceJournalEntryId is not { } originJournal || originJournal == Guid.Empty
            || lot.Acquisition.BondIncome is null || lot.Acquisition.CorporateActionLineage is not null)
            throw new InvalidOperationException(Reconcile);
        var birth = await ledger.GetAtomicTaxLotPostingAsync(birthId, ct).ConfigureAwait(false)
            ?? throw new InvalidOperationException(Reconcile);
        if (birth.MutationBatchId != birthId || birth.MutationKind != AtomicTaxLotMutationKind.Acquisition
            || birth.CorrectsMutationBatchId is not null || birth.CorporateAction is not null
            || birth.Journal.Entry.JournalEntryId != originJournal || birth.Journal.AggregateId != book
            || birth.Journal.PostingKind != LedgerPostingKindDto.Originating
            || birth.Journal.AccountingBasis != AccountingBasisKindDto.Gaap || !birth.Journal.Entry.IsBalanced
            || birth.Mutations.Count != 1)
            throw new InvalidOperationException(Reconcile);
        var opening = birth.Mutations[0];
        if (opening.MutationBatchId != birthId || opening.MutationKind != AtomicTaxLotMutationKind.Acquisition
            || opening.TaxLotRecordId != lot.TaxLotRecordId || opening.LotBefore is not null
            || opening.JournalEntryId != originJournal || opening.CorrectsMutationBatchId is not null
            || opening.LotAfter.TaxLotRecordId != lot.TaxLotRecordId || opening.LotAfter.LedgerBookId != book
            || opening.LotAfter.SecurityId != lot.SecurityId || opening.LotAfter.BookPositionId != lot.BookPositionId
            || opening.LotAfter.OriginatingMutationBatchId != birthId
            || opening.LotAfter.SourceJournalEntryId != originJournal
            || !Equals(opening.LotAfter.Account, retained.Account)
            || JsonSerializer.Serialize(opening.LotAfter.Acquisition) != JsonSerializer.Serialize(lot.Acquisition))
            throw new InvalidOperationException(Reconcile);

        var receivable = RequiredAccount(accountPaths.Receivable, "Asset");
        var payable = RequiredAccount(accountPaths.Payable, "Liability");
        _ = RequiredAccount(accountPaths.Income, "Revenue");
        var purchased = BondIncomeBookedPrecision.RoundTarget(lot.Acquisition.BondIncome.PurchasedAccruedInterest
            * lot.Acquisition.AcquisitionFxRateToFunctional);
        // Purchased accrued belongs to the seller. It is the only opening coupon balance allowed
        // without a migration receipt, and must already be separately booked at acquisition.
        var purchasedReceivable = birth.Journal.Entry.Lines.Where(line => IsAccount(line, receivable))
            .Sum(line => line.Debit - line.Credit);
        var purchasedPayable = birth.Journal.Entry.Lines.Where(line => IsAccount(line, payable))
            .Sum(line => line.Credit - line.Debit);
        if (birth.Journal.Entry.Lines.Any(line => line.Account.AccountType == LedgerAccountType.Revenue
                || !Guid.TryParse(line.Account.FinancialAccountId, out var lineOwner) || lineOwner != account
                || line.Account.Symbol is not null && !Equals(line.Account, retained.Account)
                || line.Dimensions?.InstrumentId != lot.SecurityId || line.Dimensions.PositionId != lot.BookPositionId
                || line.Dimensions.AccountId != scope.AccountId || line.Dimensions.BookId != scope.BookId
                || line.Dimensions.TaxLotId != lot.LotId)
            || purchasedReceivable != Math.Max(purchased, 0m) || purchasedPayable != Math.Max(-purchased, 0m))
            throw new InvalidOperationException(Reconcile);

        // Include backdated legacy entries as well: their effective dates cannot establish that
        // someone has not already posted recognition against this lot.
        var history = retainedHistory ?? await ledger.QueryAsync(new LedgerJournalEntryQuery(LedgerBookId: book), ct).ConfigureAwait(false);
        if (history.Select(row => row.Entry.JournalEntryId).Distinct().Count() != history.Count)
            throw new InvalidDataException("Coupon opening journal history contains duplicate identities.");
        var original = history.SingleOrDefault(row => row.Entry.JournalEntryId == originJournal);
        if (original is null || JsonSerializer.Serialize(original) != JsonSerializer.Serialize(birth.Journal))
            throw new InvalidOperationException(Reconcile);
        if (terminalPrincipal is not null)
        {
            var principal = history.SingleOrDefault(row => row.Entry.JournalEntryId == terminalPrincipal.Entry.JournalEntryId);
            if (principal is null || JsonSerializer.Serialize(principal) != JsonSerializer.Serialize(terminalPrincipal))
                throw new InvalidOperationException(Reconcile);
        }
        var noncash = await BondCouponRecognitionNoncashHistory.ResolveAsync(ledger, scope, retained,
            accountPaths, accounts, ct).ConfigureAwait(false);
        var closing = await BondCouponRecognitionClosingHistory.ResolveAsync(ledger, scope, retained,
            accountPaths, accounts, history, ct).ConfigureAwait(false);
        foreach (var verified in noncash.Concat(closing))
        {
            var journal = history.SingleOrDefault(row => row.Entry.JournalEntryId == verified.Entry.JournalEntryId);
            if (journal is null || JsonSerializer.Serialize(journal) != JsonSerializer.Serialize(verified))
                throw new InvalidOperationException(Reconcile);
        }
        var noncashIds = noncash.Select(row => row.Entry.JournalEntryId).ToHashSet();
        var closingIds = closing.Select(row => row.Entry.JournalEntryId).ToHashSet();
        var nativeIds = (native ?? []).Select(row => row.Entry.JournalEntryId).ToHashSet();
        var sensitiveAccounts = new[] { accountPaths.Receivable, accountPaths.Payable, accountPaths.Income }
            .Select(path => accounts.Single(account => account.Path == path)).ToArray();
        foreach (var row in history)
        {
            if (row.Entry.JournalEntryId == originJournal || row.Entry.JournalEntryId == terminalPrincipal?.Entry.JournalEntryId
                || nativeIds.Contains(row.Entry.JournalEntryId))
                continue;
            if (noncashIds.Contains(row.Entry.JournalEntryId) || closingIds.Contains(row.Entry.JournalEntryId))
                continue;
            // Exact different account/security/position/lot dimensions prove unrelated history.
            // Unscoped legacy lines cannot establish a zero opening balance and fail closed.
            if (row.Entry.Lines.Any(line => CouldBelongToLot(line, account, lot)
                && (native is null || sensitiveAccounts.Any(selected => IsAccount(line, selected)))))
                throw new InvalidOperationException(Reconcile);
        }

        BondCouponRecognitionAccountDto RequiredAccount(string path, string type)
        {
            var selected = accounts.Where(value => value.Path == path && value.AccountType == type).ToArray();
            if (selected.Length != 1 || string.IsNullOrWhiteSpace(selected[0].LedgerName))
                throw new ArgumentException("Coupon opening accounts must be the uniquely bound authoritative chart mappings.");
            return selected[0];
        }
    }

    private static bool IsAccount(LedgerEntry line, BondCouponRecognitionAccountDto account)
        => line.Account.Name == account.LedgerName && line.Account.AccountType.ToString() == account.AccountType;

    private static bool CouldBelongToLot(LedgerEntry line, Guid account, OpenLotDto lot)
    {
        var dimensions = line.Dimensions;
        // Conflicting account identities are ambiguous rather than proof that a line is unrelated.
        var hasLedgerAccount = Guid.TryParse(line.Account.FinancialAccountId, out var ledgerAccount);
        var hasDimensionAccount = Guid.TryParse(dimensions?.AccountId, out var dimensionAccount);
        if (hasLedgerAccount && hasDimensionAccount && ledgerAccount != dimensionAccount)
            return true;
        if (hasLedgerAccount && ledgerAccount != account || hasDimensionAccount && dimensionAccount != account)
            return false;
        if (dimensions?.InstrumentId is { } instrument && instrument != Guid.Empty && instrument != lot.SecurityId)
            return false;
        if (dimensions?.PositionId is { } position && position != Guid.Empty && position != lot.BookPositionId)
            return false;
        if (!string.IsNullOrWhiteSpace(dimensions?.TaxLotId) && dimensions.TaxLotId != lot.LotId)
            return false;
        return true;
    }
}
