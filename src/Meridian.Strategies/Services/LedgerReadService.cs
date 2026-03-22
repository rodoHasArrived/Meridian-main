using Meridian.Contracts.Workstation;
using Meridian.Ledger;
using Meridian.Strategies.Models;

namespace Meridian.Strategies.Services;

/// <summary>
/// Builds workstation-facing ledger read models from recorded run results.
/// </summary>
public sealed class LedgerReadService
{
    public LedgerSummary? BuildSummary(StrategyRunEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);

        var ledger = entry.Metrics?.Ledger;
        if (ledger is null)
        {
            return null;
        }

        var journal = ledger.Journal
            .OrderByDescending(static item => item.Timestamp)
            .Select(static item => new LedgerJournalLine(
                JournalEntryId: item.JournalEntryId,
                Timestamp: item.Timestamp,
                Description: item.Description,
                TotalDebits: item.Lines.Sum(static line => line.Debit),
                TotalCredits: item.Lines.Sum(static line => line.Credit),
                LineCount: item.Lines.Count))
            .ToArray();

        var accountSummaries = ledger.SummarizeAccounts()
            .OrderBy(static summary => summary.Account.AccountType)
            .ThenBy(static summary => summary.Account.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var trialBalance = accountSummaries
            .Select(static summary => new LedgerTrialBalanceLine(
                AccountName: summary.Account.Name,
                AccountType: summary.Account.AccountType.ToString(),
                Symbol: summary.Account.Symbol,
                FinancialAccountId: summary.Account.FinancialAccountId,
                Balance: summary.Balance,
                EntryCount: summary.EntryCount))
            .ToArray();

        return new LedgerSummary(
            LedgerReference: entry.LedgerReference ?? entry.RunId,
            RunId: entry.RunId,
            AsOf: entry.EndedAt ?? entry.StartedAt,
            JournalEntryCount: ledger.Journal.Count,
            LedgerEntryCount: accountSummaries.Sum(static summary => summary.EntryCount),
            AssetBalance: SumBalance(accountSummaries, LedgerAccountType.Asset),
            LiabilityBalance: SumBalance(accountSummaries, LedgerAccountType.Liability),
            EquityBalance: SumBalance(accountSummaries, LedgerAccountType.Equity),
            RevenueBalance: SumBalance(accountSummaries, LedgerAccountType.Revenue),
            ExpenseBalance: SumBalance(accountSummaries, LedgerAccountType.Expense),
            TrialBalance: trialBalance,
            Journal: journal);
    }

    private static decimal SumBalance(
        IEnumerable<LedgerAccountSummary> summaries,
        LedgerAccountType accountType)
        => summaries
            .Where(summary => summary.Account.AccountType == accountType)
            .Sum(static summary => summary.Balance);
}
