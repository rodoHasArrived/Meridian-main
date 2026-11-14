using System.Linq;
using Meridian.Contracts.Ledger;

namespace Meridian.Backtesting.Sdk;

/// <summary>An accounting difference observed against a simulation's day-end books.</summary>
public sealed record LedgerIntegrityBreak(
    DateTimeOffset AsOf,
    string Check,
    string? FinancialAccountId,
    string? Symbol,
    decimal Expected,
    decimal Actual,
    string Message)
{
    public decimal Difference => Actual - Expected;
}

/// <summary>Stops a strict backtest while retaining the differences that caused the failure.</summary>
public sealed class LedgerIntegrityException(IReadOnlyList<LedgerIntegrityBreak> breaks)
    : InvalidOperationException($"Ledger integrity failed with {breaks.Count} difference(s).")
{
    public IReadOnlyList<LedgerIntegrityBreak> Breaks { get; } = Array.AsReadOnly(breaks.ToArray());
}

/// <summary>
/// Compares as-of ledger cash, long basis, short proceeds and clearing balances with the retained
/// account snapshots. All comparisons use the ledger's shared absolute balance tolerance.
/// </summary>
public static class BacktestLedgerIntegrityValidator
{
    public static IReadOnlyList<LedgerIntegrityBreak> Validate(IReadOnlyLedger ledger, PortfolioSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(ledger);
        ArgumentNullException.ThrowIfNull(snapshot);
        var breaks = new List<LedgerIntegrityBreak>();
        var balances = ledger.TrialBalanceAsOf(snapshot.Timestamp);

        void Compare(string check, LedgerAccount? account, decimal expected, decimal actual, string message)
        {
            if (Math.Abs(actual - expected) > LedgerToleranceConstants.Balance)
                breaks.Add(new(snapshot.Timestamp, check, account?.FinancialAccountId, account?.Symbol,
                    expected, actual, message));
        }

        var net = balances.Sum(pair => pair.Key.AccountType is LedgerAccountType.Asset or LedgerAccountType.Expense
            ? pair.Value : -pair.Value);
        Compare("TrialBalance", null, 0m, net, "Total ledger debits and credits differ.");

        var expectedBalances = new Dictionary<LedgerAccount, decimal>();
        foreach (var account in snapshot.Accounts.Values)
        {
            expectedBalances[LedgerAccounts.CashAccount(account.AccountId)] = account.Cash;
            if (account.OpenLots is null && account.Positions.Count > 0)
            {
                breaks.Add(new(snapshot.Timestamp, "MissingLotEvidence", account.AccountId, null, 0m, 0m,
                    "Open lot evidence is absent; securities basis cannot be reconciled."));
                continue;
            }

            foreach (var lot in account.OpenLots ?? [])
            {
                var ledgerAccount = lot.IsShort
                    ? LedgerAccounts.ShortSecuritiesPayable(lot.Symbol, account.AccountId)
                    : LedgerAccounts.Securities(lot.Symbol, account.AccountId);
                expectedBalances[ledgerAccount] = expectedBalances.GetValueOrDefault(ledgerAccount) + lot.CostBasis();
            }
        }

        // Include orphan accounts and fully closed positions so residual balances cannot hide.
        foreach (var account in expectedBalances.Keys.Concat(balances.Keys.Where(IsPortfolioAccount)).Distinct())
        {
            // Missing lot evidence is already an explicit break; do not invent a zero basis.
            if (account.Name != "Cash" && account.FinancialAccountId is { } id &&
                snapshot.Accounts.TryGetValue(id, out var state) && state.OpenLots is null && state.Positions.Count > 0)
                continue;
            Compare(account.Name == "Cash" ? "AccountCash" : "SecuritiesBasis", account,
                expectedBalances.GetValueOrDefault(account), balances.GetValueOrDefault(account),
                $"Ledger {account.Name} does not match the retained financial account.");
        }

        foreach (var (account, balance) in balances.Where(pair => IsClearingAccount(pair.Key)))
            Compare("TransferClearing", account, 0m, balance, "Transfer/clearing account has an unsettled balance.");

        var dividendAccounts = balances.Where(pair => pair.Key.Name == "Dividend Receivable"
                && pair.Key.AccountType == LedgerAccountType.Asset)
            .ToArray();
        foreach (var accountId in snapshot.Accounts.Keys.Concat(dividendAccounts
                     .Select(static pair => pair.Key.FinancialAccountId).OfType<string>()).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var expected = snapshot.Accounts.GetValueOrDefault(accountId)?.DividendReceivable ?? 0m;
            var actual = dividendAccounts.Where(pair => string.Equals(pair.Key.FinancialAccountId, accountId, StringComparison.OrdinalIgnoreCase))
                .Sum(static pair => pair.Value);
            Compare("DividendReceivable", new LedgerAccount("Dividend Receivable", LedgerAccountType.Asset, FinancialAccountId: accountId),
                expected, actual, "Ledger dividend receivables do not match frozen account entitlements.");
        }
        Compare("DividendReceivable", new LedgerAccount("Dividend Receivable", LedgerAccountType.Asset), 0m,
            dividendAccounts.Where(static pair => pair.Key.FinancialAccountId is null).Sum(static pair => pair.Value),
            "Unscoped dividend receivables have no retained financial-account entitlement.");

        return breaks.AsReadOnly();
    }

    private static bool IsPortfolioAccount(LedgerAccount account) =>
        account.Name is "Cash" or "Securities" or "Short Securities Payable";

    private static bool IsClearingAccount(LedgerAccount account) =>
        account.Name == "Allocation Control"
        || account.Name.Contains("Clearing", StringComparison.OrdinalIgnoreCase)
        || account.Name.Contains("Transfer", StringComparison.OrdinalIgnoreCase);
}
