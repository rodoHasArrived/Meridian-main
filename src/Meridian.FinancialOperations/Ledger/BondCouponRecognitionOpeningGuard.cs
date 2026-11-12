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
    private const string Reconcile = "First coupon recognition requires an explicit retained opening reconciliation: prior or ambiguous coupon accounting cannot be assumed unrecognized.";

    /// <summary>
    /// Call only when the lot has no native coupon-recognition receipt. Repeat at final canonical
    /// verification. The journal store must expose the complete owning-book history, not an as-of
    /// or knowledge-time projection: a later-dated posted entry can already consume this income.
    /// </summary>
    public static async Task ValidateAsync(ILedgerJournalStore ledger, AccountCashForecastScopeDto scope,
        OpenLotDto lot, BondCouponBookAccountsDto accountPaths,
        IReadOnlyList<BondCouponRecognitionAccountDto> accounts, CancellationToken ct = default)
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
        var history = await ledger.QueryAsync(new LedgerJournalEntryQuery(LedgerBookId: book), ct).ConfigureAwait(false);
        if (history.Select(row => row.Entry.JournalEntryId).Distinct().Count() != history.Count)
            throw new InvalidDataException("Coupon opening journal history contains duplicate identities.");
        var original = history.SingleOrDefault(row => row.Entry.JournalEntryId == originJournal);
        if (original is null || JsonSerializer.Serialize(original) != JsonSerializer.Serialize(birth.Journal))
            throw new InvalidOperationException(Reconcile);
        foreach (var row in history)
        {
            if (row.Entry.JournalEntryId == originJournal)
                continue;
            // Exact different account/security/position/lot dimensions prove unrelated history.
            // Unscoped legacy lines cannot establish a zero opening balance and fail closed.
            if (row.Entry.Lines.Any(line => CouldBelongToLot(line, account, lot)))
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
