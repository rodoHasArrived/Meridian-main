using Meridian.Contracts.Ledger;
using Meridian.Ledger;

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

    private void PostSellLongEntry(AccountState account, string symbol, decimal closeQty, decimal price, decimal proceeds,
        decimal costBasisRemoved, decimal realised, decimal loanRepaid, DateTimeOffset ts)
    {
        var description = $"Sell {closeQty} {symbol} @ {price:F4}";
        var lines = new List<(LedgerAccount Account, decimal Debit, decimal Credit)>();
        AddNonZeroLine(lines, Scoped(account, LedgerAccounts.Cash), proceeds, 0m);

        if (realised > 0)
        {
            AddNonZeroLine(lines, Scoped(account, LedgerAccounts.Securities(symbol)), 0m, costBasisRemoved);
            AddNonZeroLine(lines, Scoped(account, LedgerAccounts.RealizedGain), 0m, realised);
        }
        else if (realised < 0)
        {
            AddNonZeroLine(lines, Scoped(account, LedgerAccounts.RealizedLoss), Math.Abs(realised), 0m);
            AddNonZeroLine(lines, Scoped(account, LedgerAccounts.Securities(symbol)), 0m, costBasisRemoved);
        }
        else
        {
            AddNonZeroLine(lines, Scoped(account, LedgerAccounts.Securities(symbol)), 0m, costBasisRemoved);
        }

        PostInvestingLedgerLines(ts, description, lines);
        if (loanRepaid > 0m)
            _ledger!.PostLines(ts, $"Margin repayment — {symbol}",
            [
                (Scoped(account, LedgerAccounts.MarginLoanPayable), loanRepaid, 0m),
                (Scoped(account, LedgerAccounts.Cash), 0m, loanRepaid),
            ]);
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

    private static void AddNonZeroLine(
        List<(LedgerAccount Account, decimal Debit, decimal Credit)> lines,
        LedgerAccount account,
        decimal debit,
        decimal credit)
    {
        var netDebit = debit - credit;
        if (netDebit > 0m)
            lines.Add((account, netDebit, 0m));
        else if (netDebit < 0m)
            lines.Add((account, 0m, -netDebit));
    }

    private void PostCoverShortEntry(AccountState account, string symbol, decimal coverQty, decimal price, decimal coverCost,
        decimal proceedsRemoved, decimal realised, DateTimeOffset ts)
    {
        var description = $"Cover {coverQty} {symbol} @ {price:F4}";
        if (realised > 0m)
            PostInvestingLedgerLines(ts, description,
            [
                (Scoped(account, LedgerAccounts.ShortSecuritiesPayable(symbol)), proceedsRemoved, 0m),
                (Scoped(account, LedgerAccounts.Cash), 0m, coverCost),
                (Scoped(account, LedgerAccounts.RealizedGain), 0m, realised),
            ]);
        else if (realised < 0m)
            PostInvestingLedgerLines(ts, description,
            [
                (Scoped(account, LedgerAccounts.ShortSecuritiesPayable(symbol)), proceedsRemoved, 0m),
                (Scoped(account, LedgerAccounts.RealizedLoss), Math.Abs(realised), 0m),
                (Scoped(account, LedgerAccounts.Cash), 0m, coverCost),
            ]);
        else
            PostInvestingLedgerLines(ts, description,
            [
                (Scoped(account, LedgerAccounts.ShortSecuritiesPayable(symbol)), proceedsRemoved, 0m),
                (Scoped(account, LedgerAccounts.Cash), 0m, coverCost),
            ]);
    }

    private void PostCommissionEntry(AccountState account, string symbol, decimal commission, DateTimeOffset ts)
    {
        _ledger?.PostLines(ts, $"Commission — {symbol}",
        [
            (Scoped(account, LedgerAccounts.CommissionExpense), commission, 0m),
            (Scoped(account, LedgerAccounts.Cash), 0m, commission),
        ]);
    }
}
