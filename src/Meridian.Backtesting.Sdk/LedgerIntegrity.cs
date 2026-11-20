using System.Linq;
using System.Text.Json.Serialization;
using Meridian.Contracts.Ledger;
using Meridian.Contracts.SecurityMaster;

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

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Currency { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public SecurityLotUnitKind? UnitKind { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public decimal? UnitMultiplier { get; init; }
}

/// <summary>Stops a strict backtest while retaining the differences that caused the failure.</summary>
public sealed class LedgerIntegrityException(IReadOnlyList<LedgerIntegrityBreak> breaks)
    : InvalidOperationException($"Ledger integrity failed with {breaks.Count} difference(s).")
{
    public IReadOnlyList<LedgerIntegrityBreak> Breaks { get; } = Array.AsReadOnly(breaks.ToArray());
}

/// <summary>
/// Compares as-of ledger cash, long basis, short proceeds and clearing balances with the retained
/// account snapshots. The lot quantity comparison uses exact, unscaled units.
/// </summary>
public static class BacktestLedgerIntegrityValidator
{
    public static IReadOnlyList<LedgerIntegrityBreak> Validate(IReadOnlyLedger ledger, PortfolioSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(ledger);
        ArgumentNullException.ThrowIfNull(snapshot);
        var breaks = new List<LedgerIntegrityBreak>();
        var balances = ledger.TrialBalanceAsOf(snapshot.Timestamp);
        var policy = (ledger as Meridian.Ledger.Ledger)?.Policy;
        var chart = policy?.Chart ?? StandardChartOfAccounts.Instance;

        void Compare(string check, LedgerAccount? account, decimal expected, decimal actual, string message,
            string? currency = null)
        {
            if (Math.Abs(actual - expected) > LedgerToleranceConstants.Balance)
                breaks.Add(new(snapshot.Timestamp, check, account?.FinancialAccountId, account?.Symbol,
                    expected, actual, message) { Currency = currency });
        }

        var net = balances.Sum(pair => pair.Key.AccountType is LedgerAccountType.Asset or LedgerAccountType.Expense
            ? pair.Value : -pair.Value);
        Compare("TrialBalance", null, 0m, net, "Total ledger debits and credits differ.");

        var expectedBalances = new Dictionary<LedgerAccount, decimal>();
        foreach (var account in snapshot.Accounts.Values)
        {
            expectedBalances[chart.Resolve(LedgerAccountRole.Cash, account.AccountId)] = account.Cash;
            if (account.OpenLots is null && account.Positions.Count > 0)
            {
                breaks.Add(new(snapshot.Timestamp, "MissingLotEvidence", account.AccountId, null, 0m, 0m,
                    "Open lot evidence is absent; securities basis cannot be reconciled."));
                continue;
            }

            CompareLotQuantities(snapshot.Timestamp, account, breaks);
            foreach (var lot in account.OpenLots ?? [])
            {
                var ledgerAccount = chart.Resolve(
                    lot.IsShort ? LedgerAccountRole.ShortSecuritiesPayable : LedgerAccountRole.Securities,
                    account.AccountId, lot.Symbol);
                expectedBalances[ledgerAccount] = expectedBalances.GetValueOrDefault(ledgerAccount) + lot.CostBasis();
            }
        }

        // Include orphan accounts and fully closed positions so residual balances cannot hide.
        foreach (var account in expectedBalances.Keys.Concat(balances.Keys.Where(account => IsPortfolioAccount(account, chart))).Distinct())
        {
            if (IsCurrencyCashAccount(account))
            {
                // A native snapshot carries one functional cash amount. Keep any separately
                // denominated journal cash visible in its own unit rather than netting it into that amount.
                var cashByCurrency = ledger.GetEntries(account, from: null, to: snapshot.Timestamp)
                    .GroupBy(line => line.Currency?.TransactionCurrency ?? account.Symbol!, StringComparer.OrdinalIgnoreCase);
                foreach (var group in cashByCurrency)
                {
                    var amount = group.Sum(static line => line.Currency is { } currency
                        ? currency.TransactionDebit - currency.TransactionCredit
                        : line.Debit - line.Credit);
                    Compare("AccountCash", account, 0m, amount,
                        "Separately denominated ledger cash has no corresponding portfolio cash balance.", group.Key);
                }
                continue;
            }

            var isCash = account.Name == "Cash"
                || chart.TryGetRegistered(account, out var role) && role == LedgerAccountRole.Cash;
            // Missing lot evidence is already an explicit break; do not invent a zero basis.
            if (!isCash && account.FinancialAccountId is { } id &&
                snapshot.Accounts.TryGetValue(id, out var state) && state.OpenLots is null && state.Positions.Count > 0)
                continue;
            Compare(isCash ? "AccountCash" : "SecuritiesBasis", account,
                expectedBalances.GetValueOrDefault(account), balances.GetValueOrDefault(account),
                $"Ledger {account.Name} does not match the retained financial account.",
                isCash ? policy?.FunctionalCurrency : null);
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

    private static void CompareLotQuantities(DateTimeOffset asOf, FinancialAccountSnapshot account, List<LedgerIntegrityBreak> breaks)
    {
        var lotsBySymbol = (account.OpenLots ?? []).ToLookup(static lot => lot.Symbol, StringComparer.OrdinalIgnoreCase);
        foreach (var symbol in account.Positions.Keys.Concat(lotsBySymbol.Select(static group => group.Key))
                     .Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var lots = lotsBySymbol[symbol];
            var denominations = lots.Select(static lot => (lot.UnitKind, lot.Multiplier)).Distinct().ToArray();
            if (denominations.Length > 1)
            {
                breaks.Add(new(asOf, "PositionLotDenomination", account.AccountId, symbol, 1m, denominations.Length,
                    "The retained position lots use incompatible denominations or multipliers."));
                continue;
            }

            var expected = account.Positions.GetValueOrDefault(symbol)?.ExactQuantity ?? 0m;
            var actual = lots.Sum(static lot => lot.IsShort ? -lot.Quantity : lot.Quantity);
            if (expected == actual)
                continue;

            breaks.Add(new(asOf, "PositionLotQuantity", account.AccountId, symbol, expected, actual,
                "Signed retained lot units do not match the day-end position quantity.")
            {
                UnitKind = denominations.Length == 1 ? denominations[0].UnitKind : null,
                UnitMultiplier = denominations.Length == 1 ? denominations[0].Multiplier : null
            });
        }
    }

    private static bool IsPortfolioAccount(LedgerAccount account, IChartOfAccounts chart) =>
        account.Name is "Cash" or "Securities" or "Short Securities Payable"
        || IsCurrencyCashAccount(account)
        || chart.TryGetRegistered(account, out var role)
        && role is LedgerAccountRole.Cash or LedgerAccountRole.Securities or LedgerAccountRole.ShortSecuritiesPayable;

    private static bool IsCurrencyCashAccount(LedgerAccount account) =>
        account.Symbol is { } currency && !string.IsNullOrWhiteSpace(currency)
        && account == LedgerAccounts.CashInCurrency(currency, account.FinancialAccountId);

    private static bool IsClearingAccount(LedgerAccount account) =>
        account.Name == "Allocation Control"
        || account.Name.Contains("Clearing", StringComparison.OrdinalIgnoreCase)
        || account.Name.Contains("Transfer", StringComparison.OrdinalIgnoreCase);
}
