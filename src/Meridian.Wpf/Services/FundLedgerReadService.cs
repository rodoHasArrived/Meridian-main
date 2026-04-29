using System.Globalization;
using Meridian.Contracts.Workstation;
using Meridian.Ledger;
using Meridian.Wpf.Models;

namespace Meridian.Wpf.Services;

/// <summary>
/// Builds governance-first fund ledger views from recorded run ledgers assigned to a fund profile.
/// </summary>
public sealed class FundLedgerReadService
{
    private readonly StrategyRunWorkspaceService _runWorkspaceService;
    private readonly FundContextService _fundContextService;
    private readonly FundAccountReadService? _fundAccountReadService;

    public FundLedgerReadService(
        StrategyRunWorkspaceService runWorkspaceService,
        FundContextService fundContextService)
        : this(runWorkspaceService, fundContextService, fundAccountReadService: null)
    {
    }

    public FundLedgerReadService(
        StrategyRunWorkspaceService runWorkspaceService,
        FundContextService fundContextService,
        FundAccountReadService? fundAccountReadService)
    {
        _runWorkspaceService = runWorkspaceService ?? throw new ArgumentNullException(nameof(runWorkspaceService));
        _fundContextService = fundContextService ?? throw new ArgumentNullException(nameof(fundContextService));
        _fundAccountReadService = fundAccountReadService;
    }

    public async Task<FundLedgerSummary?> GetAsync(FundLedgerQuery query, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        var context = await BuildContextAsync(query, ct).ConfigureAwait(false);
        if (context is null)
        {
            return null;
        }

        var asOf = query.AsOf ?? DateTimeOffset.UtcNow;
        var trialBalance = BuildTrialBalance(context, query.ScopeKind, query.ScopeId, asOf);
        var journal = BuildJournal(context, query.ScopeKind, query.ScopeId, asOf);
        var selectedTotals = BuildTotals(trialBalance, journal);

        var consolidatedTrialBalance = BuildTrialBalance(context, FundLedgerScope.Consolidated, null, asOf);
        var consolidatedJournal = BuildJournal(context, FundLedgerScope.Consolidated, null, asOf);
        var consolidatedTotals = BuildTotals(consolidatedTrialBalance, consolidatedJournal);
        var ledgerSlices = BuildLedgerSlices(
            context,
            asOf,
            consolidatedTotals,
            consolidatedTrialBalance,
            consolidatedJournal);

        var entityCount = Math.Max(context.Profile.EntityIds?.Count ?? 0, context.MaterializedEntityIds.Count);
        var sleeveCount = Math.Max(context.Profile.SleeveIds?.Count ?? 0, context.MaterializedSleeveIds.Count);
        var vehicleCount = Math.Max(context.Profile.VehicleIds?.Count ?? 0, context.MaterializedVehicleIds.Count);

        return new FundLedgerSummary(
            FundProfileId: context.Profile.FundProfileId,
            FundDisplayName: context.Profile.DisplayName,
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
            TrialBalance: trialBalance,
            Journal: journal,
            EntityCount: entityCount,
            SleeveCount: sleeveCount,
            VehicleCount: vehicleCount,
            ConsolidatedTotals: consolidatedTotals,
            LedgerSlices: ledgerSlices);
    }

    public async Task<FundLedgerReconciliationSnapshot?> GetReconciliationSnapshotAsync(
        FundLedgerQuery query,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        var context = await BuildContextAsync(query, ct).ConfigureAwait(false);
        if (context is null)
        {
            return null;
        }

        var asOf = query.AsOf ?? DateTimeOffset.UtcNow;
        return BuildReconciliationSnapshot(context, asOf);
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

    private async Task<FundLedgerBuildContext?> BuildContextAsync(
        FundLedgerQuery query,
        CancellationToken ct)
    {
        await _fundContextService.LoadAsync(ct).ConfigureAwait(false);

        var profile = _fundContextService.Profiles.FirstOrDefault(item =>
            string.Equals(item.FundProfileId, query.FundProfileId, StringComparison.OrdinalIgnoreCase));
        if (profile is null)
        {
            return null;
        }

        var accountAssignments = await BuildAccountAssignmentsAsync(profile.FundProfileId, ct).ConfigureAwait(false);
        var selectedLedgerIds = NormalizeSelectedLedgerIds(query.SelectedLedgerIds);
        var constrainToSelectedLedgers = selectedLedgerIds.Count > 0;
        var book = new FundLedgerBook(profile.FundProfileId);
        var materializedEntityIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var materializedSleeveIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var materializedVehicleIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var runs = await _runWorkspaceService.GetRecordedRunEntriesAsync(ct).ConfigureAwait(false);

        foreach (var run in runs.Where(run =>
                     string.Equals(run.FundProfileId, profile.FundProfileId, StringComparison.OrdinalIgnoreCase) &&
                     run.Metrics?.Ledger is not null &&
                     (!constrainToSelectedLedgers || IsSelectedLedger(run.RunId, run.LedgerReference, selectedLedgerIds))))
        {
            foreach (var journalEntry in run.Metrics!.Ledger!.Journal)
            {
                PostJournalEntry(
                    book,
                    journalEntry,
                    accountAssignments,
                    materializedEntityIds,
                    materializedSleeveIds,
                    materializedVehicleIds);
            }
        }

        return new FundLedgerBuildContext(
            profile,
            book,
            materializedEntityIds,
            materializedSleeveIds,
            materializedVehicleIds);
    }

    private async Task<IReadOnlyDictionary<string, AccountStructureAssignment>> BuildAccountAssignmentsAsync(
        string fundProfileId,
        CancellationToken ct)
    {
        if (_fundAccountReadService is null)
        {
            return EmptyAccountAssignments.Instance;
        }

        var accounts = await _fundAccountReadService.GetAccountsAsync(fundProfileId, ct).ConfigureAwait(false);
        if (accounts.Count == 0)
        {
            return EmptyAccountAssignments.Instance;
        }

        var assignments = new Dictionary<string, AccountStructureAssignment>(StringComparer.OrdinalIgnoreCase);
        foreach (var account in accounts)
        {
            var assignment = new AccountStructureAssignment(
                EntityScopeId: NormalizeScopeId(account.EntityId),
                SleeveScopeId: NormalizeScopeId(account.SleeveId),
                VehicleScopeId: NormalizeScopeId(account.VehicleId));

            assignments[account.AccountId.ToString("D")] = assignment;
            assignments[account.AccountId.ToString("N")] = assignment;
        }

        return assignments;
    }

    private static void PostJournalEntry(
        FundLedgerBook book,
        JournalEntry journalEntry,
        IReadOnlyDictionary<string, AccountStructureAssignment> accountAssignments,
        HashSet<string> materializedEntityIds,
        HashSet<string> materializedSleeveIds,
        HashSet<string> materializedVehicleIds)
    {
        book.FundLedger.Post(journalEntry);

        if (accountAssignments.Count == 0)
        {
            return;
        }

        var scopedAssignments = journalEntry.Lines
            .Select(line => ResolveAccountAssignment(line.Account.FinancialAccountId, accountAssignments))
            .Where(static assignment => assignment is not null)
            .Cast<AccountStructureAssignment>()
            .ToArray();

        if (scopedAssignments.Length == 0)
        {
            return;
        }

        PostScopedJournal(
            ResolveSingleScopeId(scopedAssignments.Select(static assignment => assignment.EntityScopeId)),
            scopeId => book.EntityLedger(scopeId),
            materializedEntityIds,
            journalEntry);
        PostScopedJournal(
            ResolveSingleScopeId(scopedAssignments.Select(static assignment => assignment.SleeveScopeId)),
            scopeId => book.SleeveLedger(scopeId),
            materializedSleeveIds,
            journalEntry);
        PostScopedJournal(
            ResolveSingleScopeId(scopedAssignments.Select(static assignment => assignment.VehicleScopeId)),
            scopeId => book.VehicleLedger(scopeId),
            materializedVehicleIds,
            journalEntry);
    }

    private static void PostScopedJournal(
        string? scopeId,
        Func<string, Ledger.Ledger> resolveLedger,
        HashSet<string> materializedScopeIds,
        JournalEntry journalEntry)
    {
        if (scopeId is null)
        {
            return;
        }

        resolveLedger(scopeId).Post(journalEntry);
        materializedScopeIds.Add(scopeId);
    }

    private static AccountStructureAssignment? ResolveAccountAssignment(
        string? financialAccountId,
        IReadOnlyDictionary<string, AccountStructureAssignment> accountAssignments)
    {
        if (string.IsNullOrWhiteSpace(financialAccountId))
        {
            return null;
        }

        var normalized = financialAccountId.Trim();
        if (accountAssignments.TryGetValue(normalized, out var assignment))
        {
            return assignment;
        }

        if (Guid.TryParse(normalized, out var guid))
        {
            if (accountAssignments.TryGetValue(guid.ToString("D"), out assignment))
            {
                return assignment;
            }

            if (accountAssignments.TryGetValue(guid.ToString("N"), out assignment))
            {
                return assignment;
            }
        }

        return null;
    }

    private static string? ResolveSingleScopeId(IEnumerable<string?> scopeIds)
    {
        var distinctScopeIds = scopeIds
            .Where(static scopeId => !string.IsNullOrWhiteSpace(scopeId))
            .Select(static scopeId => scopeId!.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return distinctScopeIds.Length == 1 ? distinctScopeIds[0] : null;
    }

    private static IReadOnlyList<FundTrialBalanceLine> BuildTrialBalance(
        FundLedgerBuildContext context,
        FundLedgerScope scopeKind,
        string? scopeId,
        DateTimeOffset asOf)
    {
        var ledger = ResolveLedger(context, scopeKind, scopeId);
        if (ledger is null)
        {
            return [];
        }

        var entryCounts = BuildEntryCounts(ledger, asOf);
        return ledger.TrialBalanceAsOf(asOf)
            .OrderBy(static pair => pair.Key.AccountType)
            .ThenBy(static pair => pair.Key.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(static pair => pair.Key.Symbol, StringComparer.OrdinalIgnoreCase)
            .ThenBy(static pair => pair.Key.FinancialAccountId, StringComparer.OrdinalIgnoreCase)
            .Select(pair => new FundTrialBalanceLine(
                AccountName: pair.Key.Name,
                AccountType: pair.Key.AccountType.ToString(),
                Symbol: pair.Key.Symbol,
                FinancialAccountId: pair.Key.FinancialAccountId,
                Balance: pair.Value,
                EntryCount: entryCounts.TryGetValue(pair.Key, out var count) ? count : 0,
                Security: null))
            .ToArray();
    }

    private static IReadOnlyList<FundJournalLine> BuildJournal(
        FundLedgerBuildContext context,
        FundLedgerScope scopeKind,
        string? scopeId,
        DateTimeOffset asOf)
    {
        var ledger = ResolveLedger(context, scopeKind, scopeId);
        if (ledger is null)
        {
            return [];
        }

        return ledger.GetJournalEntries(to: asOf)
            .OrderByDescending(static entry => entry.Timestamp)
            .ThenBy(static entry => entry.Description, StringComparer.OrdinalIgnoreCase)
            .Select(static entry => new FundJournalLine(
                JournalEntryId: entry.JournalEntryId,
                Timestamp: entry.Timestamp,
                Description: entry.Description,
                TotalDebits: entry.Lines.Sum(static line => line.Debit),
                TotalCredits: entry.Lines.Sum(static line => line.Credit),
                LineCount: entry.Lines.Count,
                FinancialAccountIds: entry.Lines
                    .Select(static line => line.Account.FinancialAccountId)
                    .Where(static accountId => !string.IsNullOrWhiteSpace(accountId))
                    .Select(static accountId => accountId!.Trim())
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .OrderBy(static accountId => accountId, StringComparer.OrdinalIgnoreCase)
                    .ToArray()))
            .ToArray();
    }

    private static FundLedgerDimensionSnapshot ProjectDimensionSnapshot(LedgerSnapshot snapshot) =>
        new(
            Timestamp: snapshot.Timestamp,
            JournalEntryCount: snapshot.JournalEntryCount,
            LedgerEntryCount: snapshot.LedgerEntryCount,
            Balances: snapshot.Balances
                .OrderBy(static pair => pair.Key.AccountType)
                .ThenBy(static pair => pair.Key.Name, StringComparer.OrdinalIgnoreCase)
                .ThenBy(static pair => pair.Key.Symbol, StringComparer.OrdinalIgnoreCase)
                .ThenBy(static pair => pair.Key.FinancialAccountId, StringComparer.OrdinalIgnoreCase)
                .Select(static pair => new FundLedgerSnapshotBalanceLine(
                    AccountName: pair.Key.Name,
                    AccountType: pair.Key.AccountType.ToString(),
                    Symbol: pair.Key.Symbol,
                    FinancialAccountId: pair.Key.FinancialAccountId,
                    Balance: pair.Value,
                    Security: null))
                .ToArray());

    private static IReadOnlyList<FundLedgerSliceDto> BuildLedgerSlices(
        FundLedgerBuildContext context,
        DateTimeOffset asOf,
        FundLedgerTotalsDto consolidatedTotals,
        IReadOnlyList<FundTrialBalanceLine> consolidatedTrialBalance,
        IReadOnlyList<FundJournalLine> consolidatedJournal)
    {
        var slices = new List<FundLedgerSliceDto>
        {
            CreateSlice(
                ledgerKey: "Fund",
                ledgerGroupId: "fund",
                sliceKey: "consolidated",
                scopeKind: FundLedgerScope.Consolidated,
                scopeId: null,
                displayName: "Consolidated Fund View",
                totals: consolidatedTotals,
                trialBalance: consolidatedTrialBalance,
                journal: consolidatedJournal,
                scopeLabel: "Fund")
        };

        slices.AddRange(BuildScopedSlices(
            context,
            asOf,
            FundLedgerScope.Entity,
            ResolveScopeIds(context.MaterializedEntityIds, context.Profile.EntityIds),
            "Entity"));
        slices.AddRange(BuildScopedSlices(
            context,
            asOf,
            FundLedgerScope.Sleeve,
            ResolveScopeIds(context.MaterializedSleeveIds, context.Profile.SleeveIds),
            "Sleeve"));
        slices.AddRange(BuildScopedSlices(
            context,
            asOf,
            FundLedgerScope.Vehicle,
            ResolveScopeIds(context.MaterializedVehicleIds, context.Profile.VehicleIds),
            "Vehicle"));

        return slices;
    }

    private static IEnumerable<FundLedgerSliceDto> BuildScopedSlices(
        FundLedgerBuildContext context,
        DateTimeOffset asOf,
        FundLedgerScope scopeKind,
        IReadOnlyList<string> scopeIds,
        string scopeLabel)
    {
        foreach (var scopeId in scopeIds)
        {
            var trialBalance = BuildTrialBalance(context, scopeKind, scopeId, asOf);
            var journal = BuildJournal(context, scopeKind, scopeId, asOf);
            yield return CreateSlice(
                ledgerKey: BuildLedgerKey(scopeKind, scopeId),
                ledgerGroupId: scopeId,
                sliceKey: $"{scopeKind}:{scopeId}",
                scopeKind: scopeKind,
                scopeId: scopeId,
                displayName: $"{scopeLabel} {scopeId}",
                totals: BuildTotals(trialBalance, journal),
                trialBalance: trialBalance,
                journal: journal,
                scopeLabel: scopeLabel);
        }
    }

    private static FundLedgerSliceDto CreateSlice(
        string ledgerKey,
        string ledgerGroupId,
        string sliceKey,
        FundLedgerScope scopeKind,
        string? scopeId,
        string displayName,
        FundLedgerTotalsDto totals,
        IReadOnlyList<FundTrialBalanceLine> trialBalance,
        IReadOnlyList<FundJournalLine> journal,
        string scopeLabel)
        => new(
            SliceKey: sliceKey,
            LedgerKey: ledgerKey,
            LedgerGroupId: ledgerGroupId,
            ScopeKind: scopeKind,
            ScopeId: scopeId,
            DisplayName: displayName,
            Totals: totals,
            TrialBalance: trialBalance,
            Journal: journal,
            Metadata: BuildSliceMetadata(ledgerKey, ledgerGroupId, scopeKind, scopeId, scopeLabel, trialBalance, journal));

    private static IReadOnlyDictionary<string, string> BuildSliceMetadata(
        string ledgerKey,
        string ledgerGroupId,
        FundLedgerScope scopeKind,
        string? scopeId,
        string scopeLabel,
        IReadOnlyList<FundTrialBalanceLine> trialBalance,
        IReadOnlyList<FundJournalLine> journal)
        => new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["ledgerKey"] = ledgerKey,
            ["ledgerGroupId"] = ledgerGroupId,
            ["scopeKind"] = scopeKind.ToString(),
            ["scopeLabel"] = scopeLabel,
            ["scopeId"] = scopeId ?? string.Empty,
            ["linkedAccountCount"] = CountLinkedAccounts(trialBalance, journal).ToString(CultureInfo.InvariantCulture),
            ["trialBalanceLineCount"] = trialBalance.Count.ToString(CultureInfo.InvariantCulture),
            ["journalEntryCount"] = journal.Count.ToString(CultureInfo.InvariantCulture)
        };

    private static int CountLinkedAccounts(
        IReadOnlyList<FundTrialBalanceLine> trialBalance,
        IReadOnlyList<FundJournalLine> journal)
        => trialBalance
            .Select(static line => line.FinancialAccountId)
            .Concat(journal.SelectMany(static line => line.FinancialAccountIds ?? []))
            .Where(static accountId => !string.IsNullOrWhiteSpace(accountId))
            .Select(static accountId => accountId!.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Count();

    private static IReadOnlyLedger? ResolveLedger(
        FundLedgerBuildContext context,
        FundLedgerScope scopeKind,
        string? scopeId)
    {
        var normalizedScopeId = string.IsNullOrWhiteSpace(scopeId) ? null : scopeId.Trim();

        return scopeKind switch
        {
            FundLedgerScope.Consolidated => context.Book.FundLedger,
            FundLedgerScope.Entity when normalizedScopeId is not null && context.MaterializedEntityIds.Contains(normalizedScopeId)
                => context.Book.EntityLedger(normalizedScopeId),
            FundLedgerScope.Sleeve when normalizedScopeId is not null && context.MaterializedSleeveIds.Contains(normalizedScopeId)
                => context.Book.SleeveLedger(normalizedScopeId),
            FundLedgerScope.Vehicle when normalizedScopeId is not null && context.MaterializedVehicleIds.Contains(normalizedScopeId)
                => context.Book.VehicleLedger(normalizedScopeId),
            _ => null
        };
    }

    private static FundLedgerReconciliationSnapshot BuildReconciliationSnapshot(
        FundLedgerBuildContext context,
        DateTimeOffset asOf) =>
        new(
            FundProfileId: context.Profile.FundProfileId,
            AsOf: asOf,
            Consolidated: ProjectDimensionSnapshot(context.Book.FundLedger.SnapshotAsOf(asOf)),
            Entities: context.MaterializedEntityIds
                .OrderBy(static scopeId => scopeId, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(
                    scopeId => scopeId,
                    scopeId => ProjectDimensionSnapshot(context.Book.EntityLedger(scopeId).SnapshotAsOf(asOf)),
                    StringComparer.OrdinalIgnoreCase),
            Sleeves: context.MaterializedSleeveIds
                .OrderBy(static scopeId => scopeId, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(
                    scopeId => scopeId,
                    scopeId => ProjectDimensionSnapshot(context.Book.SleeveLedger(scopeId).SnapshotAsOf(asOf)),
                    StringComparer.OrdinalIgnoreCase),
            Vehicles: context.MaterializedVehicleIds
                .OrderBy(static scopeId => scopeId, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(
                    scopeId => scopeId,
                    scopeId => ProjectDimensionSnapshot(context.Book.VehicleLedger(scopeId).SnapshotAsOf(asOf)),
                    StringComparer.OrdinalIgnoreCase));

    private static Dictionary<LedgerAccount, int> BuildEntryCounts(IReadOnlyLedger ledger, DateTimeOffset asOf) =>
        ledger.GetJournalEntries(to: asOf)
            .SelectMany(static entry => entry.Lines)
            .GroupBy(static line => line.Account)
            .ToDictionary(static group => group.Key, static group => group.Count());

    private static decimal SumBalance(IEnumerable<FundTrialBalanceLine> lines, LedgerAccountType accountType)
        => lines
            .Where(line => string.Equals(line.AccountType, accountType.ToString(), StringComparison.Ordinal))
            .Sum(static line => line.Balance);

    private static FundLedgerTotalsDto BuildTotals(
        IReadOnlyList<FundTrialBalanceLine> trialBalance,
        IReadOnlyList<FundJournalLine> journal)
        => new(
            JournalEntryCount: journal.Count,
            LedgerEntryCount: trialBalance.Sum(static line => line.EntryCount),
            AssetBalance: SumBalance(trialBalance, LedgerAccountType.Asset),
            LiabilityBalance: SumBalance(trialBalance, LedgerAccountType.Liability),
            EquityBalance: SumBalance(trialBalance, LedgerAccountType.Equity),
            RevenueBalance: SumBalance(trialBalance, LedgerAccountType.Revenue),
            ExpenseBalance: SumBalance(trialBalance, LedgerAccountType.Expense));

    private static IReadOnlyList<string> ResolveScopeIds(
        IEnumerable<string> materializedScopeIds,
        IReadOnlyList<string>? fallbackScopeIds)
    {
        var materialized = materializedScopeIds
            .Where(static scopeId => !string.IsNullOrWhiteSpace(scopeId))
            .Select(static scopeId => scopeId.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(static scopeId => scopeId, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (materialized.Length > 0)
        {
            return materialized;
        }

        return fallbackScopeIds?
            .Where(static scopeId => !string.IsNullOrWhiteSpace(scopeId))
            .Select(static scopeId => scopeId.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(static scopeId => scopeId, StringComparer.OrdinalIgnoreCase)
            .ToArray()
            ?? [];
    }

    private static string BuildLedgerKey(FundLedgerScope scopeKind, string scopeId)
        => scopeKind switch
        {
            FundLedgerScope.Entity => $"Entity:{scopeId}",
            FundLedgerScope.Sleeve => $"Sleeve:{scopeId}",
            FundLedgerScope.Vehicle => $"Vehicle:{scopeId}",
            _ => "Fund"
        };

    private static HashSet<string> NormalizeSelectedLedgerIds(IReadOnlyList<string>? selectedLedgerIds)
        => selectedLedgerIds?
            .Where(static id => !string.IsNullOrWhiteSpace(id))
            .Select(static id => id.Trim())
            .ToHashSet(StringComparer.OrdinalIgnoreCase)
        ?? [];

    private static string? NormalizeScopeId(Guid? scopeId)
        => scopeId?.ToString("D");

    private static bool IsSelectedLedger(
        string runId,
        string? ledgerReference,
        IReadOnlySet<string> selectedLedgerIds)
        => selectedLedgerIds.Contains(runId)
           || (!string.IsNullOrWhiteSpace(ledgerReference) && selectedLedgerIds.Contains(ledgerReference.Trim()));

    private sealed record AccountStructureAssignment(
        string? EntityScopeId,
        string? SleeveScopeId,
        string? VehicleScopeId);

    private sealed record FundLedgerBuildContext(
        FundProfileDetail Profile,
        FundLedgerBook Book,
        HashSet<string> MaterializedEntityIds,
        HashSet<string> MaterializedSleeveIds,
        HashSet<string> MaterializedVehicleIds);

    private static class EmptyAccountAssignments
    {
        public static IReadOnlyDictionary<string, AccountStructureAssignment> Instance { get; } =
            new Dictionary<string, AccountStructureAssignment>(StringComparer.OrdinalIgnoreCase);
    }
}
