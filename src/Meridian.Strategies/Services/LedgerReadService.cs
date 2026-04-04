using Meridian.Contracts.Workstation;
using Meridian.Ledger;
using Meridian.Strategies.Models;

namespace Meridian.Strategies.Services;

/// <summary>
/// Builds workstation-facing ledger read models from recorded run results.
/// </summary>
public sealed class LedgerReadService
{
    private readonly ISecurityReferenceLookup? _securityReferenceLookup;

    public LedgerReadService()
    {
    }

    public LedgerReadService(ISecurityReferenceLookup securityReferenceLookup)
    {
        _securityReferenceLookup = securityReferenceLookup ?? throw new ArgumentNullException(nameof(securityReferenceLookup));
    }

    public LedgerSummary? BuildSummary(StrategyRunEntry entry)
        => BuildBaseSummary(entry);

    public async Task<LedgerSummary?> BuildSummaryAsync(StrategyRunEntry entry, CancellationToken ct = default)
    {
        var summary = BuildBaseSummary(entry);
        if (summary is null || _securityReferenceLookup is null || summary.TrialBalance.Count == 0)
        {
            return summary;
        }

        var lookup = await ResolveSecuritiesAsync(
                summary.TrialBalance
                    .Select(static line => line.Symbol)
                    .Where(static symbol => !string.IsNullOrWhiteSpace(symbol))!
                    .Select(static symbol => symbol!),
                ct)
            .ConfigureAwait(false);

        var trialBalance = summary.TrialBalance
            .Select(line => line with
            {
                Security = line.Symbol is not null ? lookup.GetValueOrDefault(line.Symbol) : null
            })
            .ToArray();

        var resolvedCount = lookup.Values.Count(static value => value is not null);
        var missingCount = lookup.Count - resolvedCount;

        return summary with
        {
            TrialBalance = trialBalance,
            SecurityResolvedCount = resolvedCount,
            SecurityMissingCount = missingCount
        };
    }

    private static LedgerSummary? BuildBaseSummary(StrategyRunEntry entry)
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
            Journal: journal,
            FundProfileId: entry.FundProfileId);
    }

    private static decimal SumBalance(
        IEnumerable<LedgerAccountSummary> summaries,
        LedgerAccountType accountType)
        => summaries
            .Where(summary => summary.Account.AccountType == accountType)
            .Sum(static summary => summary.Balance);

    private async Task<Dictionary<string, WorkstationSecurityReference?>> ResolveSecuritiesAsync(
        IEnumerable<string> symbols,
        CancellationToken ct)
    {
        var lookup = new Dictionary<string, WorkstationSecurityReference?>(StringComparer.OrdinalIgnoreCase);
        if (_securityReferenceLookup is null)
        {
            return lookup;
        }

        foreach (var symbol in symbols.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            lookup[symbol] = await _securityReferenceLookup
                .GetBySymbolAsync(symbol, ct)
                .ConfigureAwait(false);
        }

        return lookup;
    }
}
