using Meridian.Contracts.Workstation;
using Meridian.Ledger;
using Meridian.Wpf.Models;

namespace Meridian.Wpf.Services;

/// <summary>
/// Builds governance-first fund ledger views from the recorded run ledgers assigned to a fund profile.
/// </summary>
public sealed class FundLedgerReadService
{
    private readonly StrategyRunWorkspaceService _runWorkspaceService;
    private readonly FundContextService _fundContextService;

    public FundLedgerReadService(
        StrategyRunWorkspaceService runWorkspaceService,
        FundContextService fundContextService)
    {
        _runWorkspaceService = runWorkspaceService ?? throw new ArgumentNullException(nameof(runWorkspaceService));
        _fundContextService = fundContextService ?? throw new ArgumentNullException(nameof(fundContextService));
    }

    public async Task<FundLedgerSummary?> GetAsync(FundLedgerQuery query, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        await _fundContextService.LoadAsync(ct).ConfigureAwait(false);

        var profile = _fundContextService.Profiles.FirstOrDefault(item =>
            string.Equals(item.FundProfileId, query.FundProfileId, StringComparison.OrdinalIgnoreCase));
        if (profile is null)
        {
            return null;
        }

        var fundLedgerBook = new FundLedgerBook(profile.FundProfileId);
        var runs = await _runWorkspaceService.GetRecordedRunEntriesAsync(ct).ConfigureAwait(false);

        foreach (var run in runs.Where(run =>
                     string.Equals(run.FundProfileId, profile.FundProfileId, StringComparison.OrdinalIgnoreCase) &&
                     run.Metrics?.Ledger is not null))
        {
            foreach (var journalEntry in run.Metrics!.Ledger!.Journal)
            {
                fundLedgerBook.FundLedger.Post(journalEntry);
            }
        }

        var asOf = query.AsOf ?? DateTimeOffset.UtcNow;
        var journal = BuildJournal(fundLedgerBook, query, asOf);
        var trialBalance = BuildTrialBalance(fundLedgerBook, query, asOf);
        var consolidatedTrialBalance = BuildTrialBalance(fundLedgerBook, FundLedgerScope.Consolidated, null, asOf);
        var consolidatedJournal = BuildJournal(fundLedgerBook, FundLedgerScope.Consolidated, null, asOf);
        var consolidatedTotals = BuildTotals(consolidatedTrialBalance, consolidatedJournal);
        var ledgerSlices = BuildLedgerSlices(fundLedgerBook, profile, asOf, consolidatedTotals, consolidatedTrialBalance, consolidatedJournal);

        var entityCount = Math.Max(profile.EntityIds?.Count ?? 0, fundLedgerBook.EntitySnapshotsAsOf(asOf).Count);
        var sleeveCount = Math.Max(profile.SleeveIds?.Count ?? 0, fundLedgerBook.SleeveSnapshotsAsOf(asOf).Count);
        var vehicleCount = Math.Max(profile.VehicleIds?.Count ?? 0, fundLedgerBook.VehicleSnapshotsAsOf(asOf).Count);

        var balances = trialBalance.ToArray();
        var selectedTotals = BuildTotals(balances, journal);
        return new FundLedgerSummary(
            FundProfileId: profile.FundProfileId,
            FundDisplayName: profile.DisplayName,
            ScopeKind: query.ScopeKind,
            ScopeId: query.ScopeId,
            AsOf: asOf,
            JournalEntryCount: selectedTotals.JournalEntryCount,
            LedgerEntryCount: selectedTotals.LedgerEntryCount,
            AssetBalance: selectedTotals.AssetBalance,
            LiabilityBalance: selectedTotals.LiabilityBalance,
            EquityBalance: selectedTotals.EquityBalance,
            RevenueBalance: selectedTotals.RevenueBalance,
            ExpenseBalance: selectedTotals.ExpenseBalance,
            TrialBalance: balances,
            Journal: journal,
            EntityCount: entityCount,
            SleeveCount: sleeveCount,
            VehicleCount: vehicleCount,
            ConsolidatedTotals: consolidatedTotals,
            LedgerSlices: ledgerSlices);
    }

    private static IReadOnlyList<FundTrialBalanceLine> BuildTrialBalance(
        FundLedgerBook book,
        FundLedgerQuery query,
        DateTimeOffset asOf)
        => BuildTrialBalance(book, query.ScopeKind, query.ScopeId, asOf);

    private static IReadOnlyList<FundTrialBalanceLine> BuildTrialBalance(
        FundLedgerBook book,
        FundLedgerScope scopeKind,
        string? scopeId,
        DateTimeOffset asOf)
    {
        IReadOnlyDictionary<LedgerAccount, decimal> balances = scopeKind switch
        {
            FundLedgerScope.Consolidated => book.ConsolidatedSnapshotAsOf(asOf).Balances,
            FundLedgerScope.Entity => book.EntityLedger(scopeId ?? string.Empty).SnapshotAsOf(asOf).Balances,
            FundLedgerScope.Sleeve => book.SleeveLedger(scopeId ?? string.Empty).SnapshotAsOf(asOf).Balances,
            FundLedgerScope.Vehicle => book.VehicleLedger(scopeId ?? string.Empty).SnapshotAsOf(asOf).Balances,
            _ => book.ConsolidatedSnapshotAsOf(asOf).Balances
        };

        var entryCounts = BuildEntryCounts(book, scopeKind, scopeId, asOf);

        return balances
            .OrderBy(pair => pair.Key.AccountType)
            .ThenBy(pair => pair.Key.Name, StringComparer.OrdinalIgnoreCase)
            .Select(pair => new FundTrialBalanceLine(
                AccountName: pair.Key.Name,
                AccountType: pair.Key.AccountType.ToString(),
                Symbol: pair.Key.Symbol,
                FinancialAccountId: pair.Key.FinancialAccountId,
                Balance: pair.Value,
                EntryCount: entryCounts.TryGetValue(pair.Key, out var count) ? count : 0))
            .ToArray();
    }

    private static IReadOnlyList<FundJournalLine> BuildJournal(
        FundLedgerBook book,
        FundLedgerQuery query,
        DateTimeOffset asOf)
        => BuildJournal(book, query.ScopeKind, query.ScopeId, asOf);

    private static IReadOnlyList<FundJournalLine> BuildJournal(
        FundLedgerBook book,
        FundLedgerScope scopeKind,
        string? scopeId,
        DateTimeOffset asOf)
    {
        IEnumerable<JournalEntry> source = scopeKind switch
        {
            FundLedgerScope.Consolidated => book.ConsolidatedJournalEntries(),
            FundLedgerScope.Entity => book.EntityLedger(scopeId ?? string.Empty).GetJournalEntries(),
            FundLedgerScope.Sleeve => book.SleeveLedger(scopeId ?? string.Empty).GetJournalEntries(),
            FundLedgerScope.Vehicle => book.VehicleLedger(scopeId ?? string.Empty).GetJournalEntries(),
            _ => book.ConsolidatedJournalEntries()
        };

        return source
            .Where(entry => entry.Timestamp <= asOf)
            .OrderByDescending(entry => entry.Timestamp)
            .ThenByDescending(entry => entry.JournalEntryId)
            .Select(entry => new FundJournalLine(
                JournalEntryId: entry.JournalEntryId,
                Timestamp: entry.Timestamp,
                Description: entry.Description,
                TotalDebits: entry.Lines.Sum(line => line.Debit),
                TotalCredits: entry.Lines.Sum(line => line.Credit),
                LineCount: entry.Lines.Count,
                FinancialAccountIds: entry.Lines
                    .Select(line => line.Account.FinancialAccountId)
                    .Where(static accountId => !string.IsNullOrWhiteSpace(accountId))
                    .Select(static accountId => accountId!)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToArray()))
            .ToArray();
    }

    private static decimal SumBalance(IEnumerable<FundTrialBalanceLine> lines, LedgerAccountType accountType)
        => lines
            .Where(line => string.Equals(line.AccountType, accountType.ToString(), StringComparison.Ordinal))
            .Sum(line => line.Balance);

    private static FundLedgerTotalsDto BuildTotals(
        IReadOnlyList<FundTrialBalanceLine> trialBalance,
        IReadOnlyList<FundJournalLine> journal)
        => new(
            JournalEntryCount: journal.Count,
            LedgerEntryCount: trialBalance.Sum(line => line.EntryCount),
            AssetBalance: SumBalance(trialBalance, LedgerAccountType.Asset),
            LiabilityBalance: SumBalance(trialBalance, LedgerAccountType.Liability),
            EquityBalance: SumBalance(trialBalance, LedgerAccountType.Equity),
            RevenueBalance: SumBalance(trialBalance, LedgerAccountType.Revenue),
            ExpenseBalance: SumBalance(trialBalance, LedgerAccountType.Expense));

    private static IReadOnlyList<FundLedgerSliceDto> BuildLedgerSlices(
        FundLedgerBook book,
        FundProfileDetail profile,
        DateTimeOffset asOf,
        FundLedgerTotalsDto consolidatedTotals,
        IReadOnlyList<FundTrialBalanceLine> consolidatedTrialBalance,
        IReadOnlyList<FundJournalLine> consolidatedJournal)
    {
        var slices = new List<FundLedgerSliceDto>
        {
            new(
                SliceKey: "consolidated",
                ScopeKind: FundLedgerScope.Consolidated,
                ScopeId: null,
                DisplayName: "Consolidated Fund View",
                Totals: consolidatedTotals,
                TrialBalance: consolidatedTrialBalance,
                Journal: consolidatedJournal,
                Metadata: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["group"] = "consolidated"
                })
        };

        slices.AddRange(BuildScopedSlices(book, asOf, FundLedgerScope.Entity, profile.EntityIds, "Entity"));
        slices.AddRange(BuildScopedSlices(book, asOf, FundLedgerScope.Sleeve, profile.SleeveIds, "Sleeve"));
        slices.AddRange(BuildScopedSlices(book, asOf, FundLedgerScope.Vehicle, profile.VehicleIds, "Vehicle"));
        return slices;
    }

    private static IEnumerable<FundLedgerSliceDto> BuildScopedSlices(
        FundLedgerBook book,
        DateTimeOffset asOf,
        FundLedgerScope scopeKind,
        IReadOnlyList<string>? scopeIds,
        string scopeLabel)
    {
        foreach (var scopeId in scopeIds ?? [])
        {
            if (string.IsNullOrWhiteSpace(scopeId))
            {
                continue;
            }

            var trialBalance = BuildTrialBalance(book, scopeKind, scopeId, asOf);
            var journal = BuildJournal(book, scopeKind, scopeId, asOf);
            yield return new FundLedgerSliceDto(
                SliceKey: $"{scopeKind}:{scopeId}",
                ScopeKind: scopeKind,
                ScopeId: scopeId,
                DisplayName: $"{scopeLabel} {scopeId}",
                Totals: BuildTotals(trialBalance, journal),
                TrialBalance: trialBalance,
                Journal: journal,
                Metadata: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["group"] = scopeKind.ToString(),
                    ["scopeLabel"] = scopeLabel
                });
        }
    }

    private static Dictionary<LedgerAccount, int> BuildEntryCounts(
        FundLedgerBook book,
        FundLedgerQuery query,
        DateTimeOffset asOf)
        => BuildEntryCounts(book, query.ScopeKind, query.ScopeId, asOf);

    private static Dictionary<LedgerAccount, int> BuildEntryCounts(
        FundLedgerBook book,
        FundLedgerScope scopeKind,
        string? scopeId,
        DateTimeOffset asOf)
    {
        IEnumerable<JournalEntry> source = scopeKind switch
        {
            FundLedgerScope.Consolidated => book.ConsolidatedJournalEntries(),
            FundLedgerScope.Entity => book.EntityLedger(scopeId ?? string.Empty).GetJournalEntries(),
            FundLedgerScope.Sleeve => book.SleeveLedger(scopeId ?? string.Empty).GetJournalEntries(),
            FundLedgerScope.Vehicle => book.VehicleLedger(scopeId ?? string.Empty).GetJournalEntries(),
            _ => book.ConsolidatedJournalEntries()
        };

        return source
            .Where(entry => entry.Timestamp <= asOf)
            .SelectMany(entry => entry.Lines)
            .GroupBy(line => line.Account)
            .ToDictionary(group => group.Key, group => group.Count());
    }
}
