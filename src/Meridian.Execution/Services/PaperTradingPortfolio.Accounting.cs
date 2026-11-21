using Meridian.Contracts.Ledger;
using Meridian.Ledger;
using static Meridian.Contracts.Text.TextPrimitives;

namespace Meridian.Execution.Services;

public sealed partial class PaperTradingPortfolio
{
    private LedgerAccount Scoped(AccountState account, LedgerAccount ledgerAccount) =>
        _scopeLedgerAccounts ? ledgerAccount with { FinancialAccountId = account.AccountId } : ledgerAccount;

    /// <summary>Compares the current journal against cash, exact lot basis and financing balances.</summary>
    public IReadOnlyList<PaperLedgerIntegrityBreak> CheckLedgerIntegrity(decimal tolerance = LedgerToleranceConstants.Balance)
    {
        if (tolerance < 0m)
            throw new ArgumentOutOfRangeException(nameof(tolerance));
        lock (_lock)
        {
            if (_ledger is null)
                return [];
            var breaks = new List<PaperLedgerIntegrityBreak>();
            void Compare(string accountId, string check, decimal expected, decimal actual)
            {
                if (Math.Abs(expected - actual) > tolerance)
                    breaks.Add(new(accountId, check, expected, actual));
            }
            Compare("*", "TrialBalance", 0m,
                _ledger.Journal.Sum(entry => entry.Lines.Sum(line => line.Debit - line.Credit)));
            foreach (var postedAccount in _ledger.Accounts)
            {
                var balance = _ledger.GetBalance(postedAccount);
                var owned = _scopeLedgerAccounts
                    ? postedAccount.FinancialAccountId is { } id && _accounts.ContainsKey(id)
                    : postedAccount.FinancialAccountId is null;
                if (!owned)
                    Compare(postedAccount.FinancialAccountId ?? "unscoped", $"OrphanAccount:{postedAccount.Name}", 0m, balance);
                if (postedAccount.Name == "Allocation Control"
                    || postedAccount.Name.Contains("Clearing", StringComparison.OrdinalIgnoreCase)
                    || postedAccount.Name.Contains("Transfer", StringComparison.OrdinalIgnoreCase))
                    Compare(postedAccount.FinancialAccountId ?? "unscoped", $"TransferClearing:{postedAccount.Name}", 0m, balance);
            }
            foreach (var account in _accounts.Values)
            {
                Compare(account.AccountId, "Cash", account.Cash,
                    _ledger.GetBalance(Scoped(account, LedgerAccounts.Cash)));
                Compare(account.AccountId, "MarginLoan", account.MarginBalance,
                    _ledger.GetBalance(Scoped(account, LedgerAccounts.MarginLoanPayable)));
                Compare(account.AccountId, "ShortCollateral", account.ShortMarginCollateral,
                    _ledger.GetBalance(Scoped(account, LedgerAccounts.ShortSaleCollateralReceivable)));
                // Include ledger-only symbols so a closed holding cannot leave stranded basis.
                var symbols = account.Positions.Keys.Concat(_ledger.Accounts
                    .Where(a => a.Symbol is not null && (a.Name is "Securities" or "Short Securities Payable")
                        && (!_scopeLedgerAccounts || string.Equals(a.FinancialAccountId, account.AccountId, StringComparison.OrdinalIgnoreCase)))
                    .Select(a => a.Symbol!)).Distinct(StringComparer.OrdinalIgnoreCase);
                foreach (var symbol in symbols)
                {
                    account.Positions.TryGetValue(symbol, out var position);
                    if (position is not null)
                    {
                        var units = position.UnitizedLots.Sum(lot => lot.UnitKind == position.LotUnit ? lot.Quantity : 0m);
                        Compare(account.AccountId, $"PositionLots:{symbol}:{position.LotUnit}",
                            Math.Abs(position.Quantity), units);
                    }
                    Compare(account.AccountId, $"Securities:{symbol}",
                        position is { Quantity: > 0m, UsesVariationMargin: false } ? position.OpenCostBasis : 0m,
                        _ledger.GetBalance(Scoped(account, LedgerAccounts.Securities(symbol))));
                    Compare(account.AccountId, $"ShortSecurities:{symbol}",
                        position is { Quantity: < 0m, UsesVariationMargin: false } ? position.OpenCostBasis : 0m,
                        _ledger.GetBalance(Scoped(account, LedgerAccounts.ShortSecuritiesPayable(symbol))));
                }
            }
            return breaks;
        }
    }

    private void PostInvestingLedgerLines(DateTimeOffset timestamp, string description,
        IReadOnlyList<(LedgerAccount Account, decimal Debit, decimal Credit)> lines)
    {
        if (_ledger is null)
            return;
        var journalId = Guid.NewGuid();
        var draft = new JournalEntry(journalId, timestamp, description, lines.Select(line =>
            new LedgerEntry(Guid.NewGuid(), journalId, timestamp, line.Account, line.Debit, line.Credit, description)).ToArray());
        _ledger.Post(LedgerCashFlowAttribution.WithClassifiedActivity(draft, LedgerCashFlowCategory.Investing, description));
    }

    private void PostCommissionEntry(AccountState account, string symbol, decimal commission, DateTimeOffset ts)
        => _ledger?.PostLines(ts, $"Commission — {symbol}",
            SecurityTradePostingProjector.Commission(commission, LedgerScope(account), _chartOfAccounts));

    private string? LedgerScope(AccountState account) => _scopeLedgerAccounts ? account.AccountId : null;

    /// <summary>
    /// Settles the accrued interest a face-value bond fill carries on top of its clean
    /// consideration, so cash moves the dirty amount. A purchase pays the interest into Accrued
    /// Interest Receivable (it is not capitalized into cost basis). A sale receives it, relieves
    /// the receivable pro rata to the face sold and books the remainder as coupon income earned
    /// while held. Only long-side fills are settled dirty: a fill that opens, covers or flips
    /// through a short keeps its clean settlement, because short-sale interest is a payable with
    /// its own entitlement rules. Called under <c>_lock</c>.
    /// </summary>
    private (decimal Accrued, decimal Relieved) ApplyAccruedInterestSettlement(
        AccountState account,
        Meridian.Execution.Sdk.ExecutionReport report,
        bool faceValueSizing,
        decimal signedQty,
        decimal quantityBefore,
        decimal purchasedAccruedBefore)
    {
        if (!faceValueSizing || report.AccruedInterestPer100 is not { } per100 || per100 == 0m)
            return (0m, 0m);
        if (per100 < 0m)
            throw new ArgumentOutOfRangeException(nameof(report), "Accrued interest per 100 face must be nonnegative.");

        var buysOntoLong = signedQty > 0m && quantityBefore >= 0m;
        var sellsFromLong = signedQty < 0m && quantityBefore >= -signedQty;
        if (!buysOntoLong && !sellsFromLong)
            return (0m, 0m);

        var face = Math.Abs(signedQty);
        var accrued = decimal.Round(face * per100 / 100m, 2, MidpointRounding.AwayFromZero);
        if (accrued == 0m)
            return (0m, 0m);

        var scope = LedgerScope(account);
        var cashAccount = _chartOfAccounts.Resolve(LedgerAccountRole.Cash, scope);
        var receivable = LedgerAccounts.AccruedInterestReceivable(report.Symbol, scope);
        account.Positions.TryGetValue(report.Symbol, out var position);

        if (buysOntoLong)
        {
            account.Cash -= accrued;
            if (position is not null)
                position.PurchasedAccruedInterest += accrued;
            _ledger?.PostLines(report.Timestamp, $"Purchased accrued interest — {report.Symbol}",
            [
                (receivable, accrued, 0m),
                (cashAccount, 0m, accrued)
            ]);
            return (accrued, 0m);
        }

        var relieved = quantityBefore == 0m
            ? 0m
            : decimal.Round(purchasedAccruedBefore * face / quantityBefore, 2, MidpointRounding.AwayFromZero);
        account.Cash += accrued;
        if (position is not null)
            position.PurchasedAccruedInterest = purchasedAccruedBefore - relieved;

        var income = accrued - relieved;
        var couponIncome = _chartOfAccounts.Resolve(LedgerAccountRole.CouponIncome, scope);
        var lines = new List<(LedgerAccount account, decimal debit, decimal credit)> { (cashAccount, accrued, 0m) };
        if (relieved > 0m)
            lines.Add((receivable, 0m, relieved));
        if (income > 0m)
            lines.Add((couponIncome, 0m, income));
        else if (income < 0m)
            lines.Add((couponIncome, -income, 0m));
        _ledger?.PostLines(report.Timestamp, $"Accrued interest received — {report.Symbol}", lines);
        return (accrued, relieved);
    }

    private bool CanCertifyAccountingOwner(AccountState account, PaperPosition? position, string? ownerAccountId)
    {
        var owner = NormalizeOptional(ownerAccountId);
        if (_scopeLedgerAccounts && owner is not null
            && !string.Equals(owner, account.AccountId, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (position is null)
            return true;

        var accountingOwner = _scopeLedgerAccounts ? account.AccountId : owner;
        if (position.OwnerQuantities.Any(entry => entry.Value != 0m
                && !string.Equals(entry.Key, accountingOwner, StringComparison.OrdinalIgnoreCase)))
        {
            return false;
        }

        // An owner cannot certify basis acquired anonymously in the pooled default account.
        return _scopeLedgerAccounts || accountingOwner is null || position.Quantity == 0m
            || position.OwnerQuantities.GetValueOrDefault(accountingOwner) == position.Quantity;
    }

    private sealed class FillAccountingCapture
    {
        public SecurityTradePosting? Closing { get; set; }
        public SecurityTradePosting? Opening { get; set; }
        public decimal ShortCollateralHeld { get; set; }
        public decimal ShortCollateralReleased { get; set; }
        public decimal VariationMarginPnl { get; set; }
    }

}
