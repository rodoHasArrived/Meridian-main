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
        var context = await BuildContextAsync(query.FundProfileId, ct).ConfigureAwait(false);
        if (context is null)
        {
            return null;
        }

        var fundLedgerBook = new FundLedgerBook(profile.FundProfileId);
        var runs = await _runWorkspaceService.GetRecordedRunEntriesAsync(ct).ConfigureAwait(false);
        var selectedLedgerIds = NormalizeSelectedLedgerIds(query.SelectedLedgerIds);
        var constrainToSelectedLedgers = selectedLedgerIds.Count > 0;

        foreach (var run in runs.Where(run =>
                     string.Equals(run.FundProfileId, profile.FundProfileId, StringComparison.OrdinalIgnoreCase) &&
                     run.Metrics?.Ledger is not null &&
                     (!constrainToSelectedLedgers || selectedLedgerIds.Contains(run.RunId))))
        {
            foreach (var journalEntry in run.Metrics!.Ledger!.Journal)
            {
                fundLedgerBook.FundLedger.Post(journalEntry);
            }
        }

        var asOf = query.AsOf ?? DateTimeOffset.UtcNow;
        var journal = BuildJournal(fundLedgerBook, query, asOf);
        var trialBalance = BuildTrialBalance(fundLedgerBook, query, asOf);

        var entityCount = Math.Max(profile.EntityIds?.Count ?? 0, fundLedgerBook.EntitySnapshotsAsOf(asOf).Count);
        var sleeveCount = Math.Max(profile.SleeveIds?.Count ?? 0, fundLedgerBook.SleeveSnapshotsAsOf(asOf).Count);
        var vehicleCount = Math.Max(profile.VehicleIds?.Count ?? 0, fundLedgerBook.VehicleSnapshotsAsOf(asOf).Count);

        var balances = trialBalance.ToArray();
        return new FundLedgerSummary(
            FundProfileId: profile.FundProfileId,
            FundDisplayName: profile.DisplayName,
            ScopeKind: query.ScopeKind,
            ScopeId: query.ScopeId,
            AsOf: asOf,
            JournalEntryCount: journal.Count,
            LedgerEntryCount: balances.Sum(line => line.EntryCount),
            AssetBalance: SumBalance(balances, LedgerAccountType.Asset),
            LiabilityBalance: SumBalance(balances, LedgerAccountType.Liability),
            EquityBalance: SumBalance(balances, LedgerAccountType.Equity),
            RevenueBalance: SumBalance(balances, LedgerAccountType.Revenue),
            ExpenseBalance: SumBalance(balances, LedgerAccountType.Expense),
            TrialBalance: balances,
            Journal: journal,
            EntityCount: entityCount,
            SleeveCount: sleeveCount,
            VehicleCount: vehicleCount);
    }

    public async Task<FundLedgerReconciliationSnapshot?> GetReconciliationSnapshotAsync(
        FundLedgerQuery query,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        var context = await BuildContextAsync(query.FundProfileId, ct).ConfigureAwait(false);
        if (context is null)
        {
            return null;
        }

        var (_, fundLedgerBook) = context.Value;
        var asOf = query.AsOf ?? DateTimeOffset.UtcNow;
        return ProjectReconciliationSnapshot(fundLedgerBook.ReconciliationSnapshot(asOf));
    }

    public static FundLedgerReconciliationSnapshot ProjectReconciliationSnapshot(FundLedgerSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        return new FundLedgerReconciliationSnapshot(
            FundProfileId: snapshot.FundId,
            AsOf: snapshot.AsOf,
            Consolidated: ProjectDimensionSnapshot(snapshot.Consolidated),
            Entities: snapshot.Entities.ToDictionary(
                static pair => pair.Key,
                static pair => ProjectDimensionSnapshot(pair.Value),
                StringComparer.OrdinalIgnoreCase),
            Sleeves: snapshot.Sleeves.ToDictionary(
                static pair => pair.Key,
                static pair => ProjectDimensionSnapshot(pair.Value),
                StringComparer.OrdinalIgnoreCase),
            Vehicles: snapshot.Vehicles.ToDictionary(
                static pair => pair.Key,
                static pair => ProjectDimensionSnapshot(pair.Value),
                StringComparer.OrdinalIgnoreCase));
    }

    private static FundLedgerDimensionSnapshot ProjectDimensionSnapshot(LedgerSnapshot snapshot) =>
        new(
            Timestamp: snapshot.Timestamp,
            JournalEntryCount: snapshot.JournalEntryCount,
            LedgerEntryCount: snapshot.LedgerEntryCount,
            Balances: snapshot.Balances
                .OrderBy(static pair => pair.Key.AccountType)
                .ThenBy(static pair => pair.Key.Name, StringComparer.OrdinalIgnoreCase)
                .Select(static pair => new FundLedgerSnapshotBalanceLine(
                    AccountName: pair.Key.Name,
                    AccountType: pair.Key.AccountType.ToString(),
                    Symbol: pair.Key.Symbol,
                    FinancialAccountId: pair.Key.FinancialAccountId,
                    Balance: pair.Value))
                .ToArray());

    private async Task<(FundProfileDetail Profile, FundLedgerBook Book)?> BuildContextAsync(
        string fundProfileId,
        CancellationToken ct)
    {
        await _fundContextService.LoadAsync(ct).ConfigureAwait(false);
        var profile = _fundContextService.Profiles.FirstOrDefault(item =>
            string.Equals(item.FundProfileId, fundProfileId, StringComparison.OrdinalIgnoreCase));
        if (profile is null)
        {
            return null;
        }

        var book = new FundLedgerBook(profile.FundProfileId);
        var runs = await _runWorkspaceService.GetRecordedRunEntriesAsync(ct).ConfigureAwait(false);
        foreach (var run in runs.Where(run =>
                     string.Equals(run.FundProfileId, profile.FundProfileId, StringComparison.OrdinalIgnoreCase) &&
                     run.Metrics?.Ledger is not null))
        {
            foreach (var journalEntry in run.Metrics!.Ledger!.Journal)
            {
                book.FundLedger.Post(journalEntry);
            }
        }

        return (profile, book);
    }

    private static IReadOnlyList<FundTrialBalanceLine> BuildTrialBalance(
        FundLedgerBook book,
        FundLedgerQuery query,
        DateTimeOffset asOf)
    {
        IReadOnlyDictionary<LedgerAccount, decimal> balances = query.ScopeKind switch
        {
            FundLedgerScope.Consolidated => book.ConsolidatedSnapshotAsOf(asOf).Balances,
            FundLedgerScope.Entity => book.EntityLedger(query.ScopeId ?? string.Empty).SnapshotAsOf(asOf).Balances,
            FundLedgerScope.Sleeve => book.SleeveLedger(query.ScopeId ?? string.Empty).SnapshotAsOf(asOf).Balances,
            FundLedgerScope.Vehicle => book.VehicleLedger(query.ScopeId ?? string.Empty).SnapshotAsOf(asOf).Balances,
            _ => book.ConsolidatedSnapshotAsOf(asOf).Balances
        };

        var entryCounts = BuildEntryCounts(book, query, asOf);

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
    {
        IEnumerable<JournalEntry> source = query.ScopeKind switch
        {
            FundLedgerScope.Consolidated => book.ConsolidatedJournalEntries(),
            FundLedgerScope.Entity => book.EntityLedger(query.ScopeId ?? string.Empty).GetJournalEntries(),
            FundLedgerScope.Sleeve => book.SleeveLedger(query.ScopeId ?? string.Empty).GetJournalEntries(),
            FundLedgerScope.Vehicle => book.VehicleLedger(query.ScopeId ?? string.Empty).GetJournalEntries(),
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

    private static Dictionary<LedgerAccount, int> BuildEntryCounts(
        FundLedgerBook book,
        FundLedgerQuery query,
        DateTimeOffset asOf)
    {
        IEnumerable<JournalEntry> source = query.ScopeKind switch
        {
            FundLedgerScope.Consolidated => book.ConsolidatedJournalEntries(),
            FundLedgerScope.Entity => book.EntityLedger(query.ScopeId ?? string.Empty).GetJournalEntries(),
            FundLedgerScope.Sleeve => book.SleeveLedger(query.ScopeId ?? string.Empty).GetJournalEntries(),
            FundLedgerScope.Vehicle => book.VehicleLedger(query.ScopeId ?? string.Empty).GetJournalEntries(),
            _ => book.ConsolidatedJournalEntries()
        };

        return source
            .Where(entry => entry.Timestamp <= asOf)
            .SelectMany(entry => entry.Lines)
            .GroupBy(line => line.Account)
            .ToDictionary(group => group.Key, group => group.Count());
    }

    private static HashSet<string> NormalizeSelectedLedgerIds(IReadOnlyList<string>? selectedLedgerIds) =>
        selectedLedgerIds?
            .Where(static id => !string.IsNullOrWhiteSpace(id))
            .Select(static id => id.Trim())
            .ToHashSet(StringComparer.OrdinalIgnoreCase)
        ?? [];
}
