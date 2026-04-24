using System.Globalization;
using Meridian.Contracts.Workstation;
using Meridian.Ledger;

namespace Meridian.Wpf.Services;

/// <summary>
/// Builds governance-first fund ledger views from the recorded run ledgers assigned to a fund profile.
/// </summary>
public sealed class FundLedgerReadService
{
    private readonly StrategyRunWorkspaceService _runWorkspaceService;
    private readonly FundContextService _fundContextService;
    private readonly FundAccountReadService? _fundAccountReadService;

    public FundLedgerReadService(
        StrategyRunWorkspaceService runWorkspaceService,
        FundContextService fundContextService)
<<<<<<< Updated upstream
        : this(runWorkspaceService, fundContextService, null)
=======
        : this(runWorkspaceService, fundContextService, fundAccountReadService: null)
>>>>>>> Stashed changes
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

<<<<<<< Updated upstream
        var selectedLedgerIds = NormalizeSelectedLedgerIds(query.SelectedLedgerIds);
        var context = await BuildContextAsync(query.FundProfileId, selectedLedgerIds, ct).ConfigureAwait(false);
=======
        var context = await BuildContextAsync(query, ct).ConfigureAwait(false);
>>>>>>> Stashed changes
        if (context is null)
        {
            return null;
        }

<<<<<<< Updated upstream
        var asOf = query.AsOf ?? DateTimeOffset.UtcNow;
        var trialBalance = BuildTrialBalance(context, query.ScopeKind, query.ScopeId, asOf);
        var journal = BuildJournal(context, query.ScopeKind, query.ScopeId, asOf);
        var selectedTotals = BuildTotals(trialBalance, journal);

        var consolidatedTrialBalance = BuildTrialBalance(context, FundLedgerScope.Consolidated, null, asOf);
        var consolidatedJournal = BuildJournal(context, FundLedgerScope.Consolidated, null, asOf);
=======
        var (profile, fundLedgerBook) = context.Value;
        var asOf = query.AsOf ?? DateTimeOffset.UtcNow;
        var journal = BuildJournal(fundLedgerBook, query.ScopeKind, query.ScopeId, asOf);
        var trialBalance = BuildTrialBalance(fundLedgerBook, query.ScopeKind, query.ScopeId, asOf);
        var consolidatedTrialBalance = BuildTrialBalance(fundLedgerBook, FundLedgerScope.Consolidated, null, asOf);
        var consolidatedJournal = BuildJournal(fundLedgerBook, FundLedgerScope.Consolidated, null, asOf);
>>>>>>> Stashed changes
        var consolidatedTotals = BuildTotals(consolidatedTrialBalance, consolidatedJournal);
        var ledgerSlices = BuildLedgerSlices(
            context,
            asOf,
            consolidatedTotals,
            consolidatedTrialBalance,
            consolidatedJournal);

<<<<<<< Updated upstream
        var entityCount = Math.Max(context.Profile.EntityIds?.Count ?? 0, context.MaterializedEntityIds.Count);
        var sleeveCount = Math.Max(context.Profile.SleeveIds?.Count ?? 0, context.MaterializedSleeveIds.Count);
        var vehicleCount = Math.Max(context.Profile.VehicleIds?.Count ?? 0, context.MaterializedVehicleIds.Count);
=======
        var entityCount = Math.Max(profile.EntityIds?.Count ?? 0, fundLedgerBook.EntitySnapshotsAsOf(asOf).Count);
        var sleeveCount = Math.Max(profile.SleeveIds?.Count ?? 0, fundLedgerBook.SleeveSnapshotsAsOf(asOf).Count);
        var vehicleCount = Math.Max(profile.VehicleIds?.Count ?? 0, fundLedgerBook.VehicleSnapshotsAsOf(asOf).Count);
        var selectedTotals = BuildTotals(trialBalance, journal);
>>>>>>> Stashed changes

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

<<<<<<< Updated upstream
        var selectedLedgerIds = NormalizeSelectedLedgerIds(query.SelectedLedgerIds);
        var context = await BuildContextAsync(query.FundProfileId, selectedLedgerIds, ct).ConfigureAwait(false);
=======
        var context = await BuildContextAsync(query, ct).ConfigureAwait(false);
>>>>>>> Stashed changes
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

<<<<<<< Updated upstream
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

    private async Task<FundLedgerBuildContext?> BuildContextAsync(
        string fundProfileId,
        HashSet<string> selectedLedgerIds,
        CancellationToken ct)
    {
        await _fundContextService.LoadAsync(ct).ConfigureAwait(false);
        var profile = _fundContextService.Profiles.FirstOrDefault(item =>
            string.Equals(item.FundProfileId, fundProfileId, StringComparison.OrdinalIgnoreCase));
        if (profile is null)
        {
            return null;
        }

        var accountAssignments = await BuildAccountAssignmentsAsync(profile.FundProfileId, ct).ConfigureAwait(false);
        var constrainToSelectedLedgers = selectedLedgerIds.Count > 0;
        var book = new FundLedgerBook(profile.FundProfileId);
        var materializedEntityIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var materializedSleeveIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var materializedVehicleIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var runs = await _runWorkspaceService.GetRecordedRunEntriesAsync(ct).ConfigureAwait(false);

        foreach (var run in runs.Where(run =>
                     string.Equals(run.FundProfileId, profile.FundProfileId, StringComparison.OrdinalIgnoreCase) &&
                     run.Metrics?.Ledger is not null &&
                     (!constrainToSelectedLedgers || selectedLedgerIds.Contains(run.RunId))))
        {
            foreach (var journalEntry in run.Metrics!.Ledger!.Journal)
            {
                book.FundLedger.Post(journalEntry);

                if (TryResolveSingleScopeId(journalEntry, accountAssignments, static assignment => assignment.EntityId, out var entityId))
                {
                    book.EntityLedger(entityId).Post(journalEntry);
                    materializedEntityIds.Add(entityId);
                }

                if (TryResolveSingleScopeId(journalEntry, accountAssignments, static assignment => assignment.SleeveId, out var sleeveId))
                {
                    book.SleeveLedger(sleeveId).Post(journalEntry);
                    materializedSleeveIds.Add(sleeveId);
                }

                if (TryResolveSingleScopeId(journalEntry, accountAssignments, static assignment => assignment.VehicleId, out var vehicleId))
                {
                    book.VehicleLedger(vehicleId).Post(journalEntry);
                    materializedVehicleIds.Add(vehicleId);
                }
            }
        }

        return new FundLedgerBuildContext(
            profile,
            book,
            materializedEntityIds,
            materializedSleeveIds,
            materializedVehicleIds);
    }

    private async Task<IReadOnlyDictionary<string, FundLedgerScopeAssignment>> BuildAccountAssignmentsAsync(
        string fundProfileId,
        CancellationToken ct)
    {
        if (_fundAccountReadService is null)
=======
    private async Task<(FundProfileDetail Profile, FundLedgerBook Book)?> BuildContextAsync(
        FundLedgerQuery query,
        CancellationToken ct)
    {
        await _fundContextService.LoadAsync(ct).ConfigureAwait(false);

        var profile = _fundContextService.Profiles.FirstOrDefault(item =>
            string.Equals(item.FundProfileId, query.FundProfileId, StringComparison.OrdinalIgnoreCase));
        if (profile is null)
>>>>>>> Stashed changes
        {
            return new Dictionary<string, FundLedgerScopeAssignment>(StringComparer.OrdinalIgnoreCase);
        }

<<<<<<< Updated upstream
        var accounts = await _fundAccountReadService.GetAccountsAsync(fundProfileId, ct).ConfigureAwait(false);
        return accounts.ToDictionary(
            static account => account.AccountId.ToString("D"),
            static account => new FundLedgerScopeAssignment(
                account.EntityId?.ToString("D"),
                account.SleeveId?.ToString("D"),
                account.VehicleId?.ToString("D")),
            StringComparer.OrdinalIgnoreCase);
    }

    private static bool TryResolveSingleScopeId(
        JournalEntry journalEntry,
        IReadOnlyDictionary<string, FundLedgerScopeAssignment> accountAssignments,
        Func<FundLedgerScopeAssignment, string?> selector,
        out string scopeId)
    {
        var scopeIds = ResolveScopeIds(journalEntry, accountAssignments, selector);
        if (scopeIds.Count == 1)
=======
        var accountAssignments = await BuildAccountAssignmentsAsync(profile.FundProfileId, ct).ConfigureAwait(false);
        var selectedLedgerIds = NormalizeSelectedLedgerIds(query.SelectedLedgerIds);
        var constrainToSelectedLedgers = selectedLedgerIds.Count > 0;
        var book = new FundLedgerBook(profile.FundProfileId);
        var runs = await _runWorkspaceService.GetRecordedRunEntriesAsync(ct).ConfigureAwait(false);

        foreach (var run in runs.Where(run =>
                     string.Equals(run.FundProfileId, profile.FundProfileId, StringComparison.OrdinalIgnoreCase) &&
                     run.Metrics?.Ledger is not null &&
                     (!constrainToSelectedLedgers || selectedLedgerIds.Contains(run.RunId))))
>>>>>>> Stashed changes
        {
            scopeId = scopeIds.Single();
            return true;
        }

        scopeId = string.Empty;
        return false;
    }

    private static HashSet<string> ResolveScopeIds(
        JournalEntry journalEntry,
        IReadOnlyDictionary<string, FundLedgerScopeAssignment> accountAssignments,
        Func<FundLedgerScopeAssignment, string?> selector)
    {
        var scopeIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var accountId in journalEntry.Lines
                     .Select(static line => line.Account.FinancialAccountId)
                     .Where(static accountId => !string.IsNullOrWhiteSpace(accountId))
                     .Select(static accountId => accountId!.Trim())
                     .Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (!accountAssignments.TryGetValue(accountId, out var assignment))
            {
<<<<<<< Updated upstream
                continue;
            }

            var scopeId = selector(assignment);
            if (!string.IsNullOrWhiteSpace(scopeId))
            {
                scopeIds.Add(scopeId);
            }
        }

        return scopeIds;
    }

    private static IReadOnlyList<FundTrialBalanceLine> BuildTrialBalance(
        FundLedgerBuildContext context,
=======
                PostJournalEntry(book, journalEntry, accountAssignments);
            }
        }

        return (profile, book);
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
        IReadOnlyDictionary<string, AccountStructureAssignment> accountAssignments)
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

        var entityScopeId = ResolveSingleScopeId(scopedAssignments.Select(static assignment => assignment.EntityScopeId));
        if (entityScopeId is not null)
        {
            book.EntityLedger(entityScopeId).Post(journalEntry);
        }

        var sleeveScopeId = ResolveSingleScopeId(scopedAssignments.Select(static assignment => assignment.SleeveScopeId));
        if (sleeveScopeId is not null)
        {
            book.SleeveLedger(sleeveScopeId).Post(journalEntry);
        }

        var vehicleScopeId = ResolveSingleScopeId(scopedAssignments.Select(static assignment => assignment.VehicleScopeId));
        if (vehicleScopeId is not null)
        {
            book.VehicleLedger(vehicleScopeId).Post(journalEntry);
        }
    }

    private static AccountStructureAssignment? ResolveAccountAssignment(
        string? financialAccountId,
        IReadOnlyDictionary<string, AccountStructureAssignment> accountAssignments)
    {
        if (string.IsNullOrWhiteSpace(financialAccountId))
        {
            return null;
        }

        var trimmed = financialAccountId.Trim();
        if (accountAssignments.TryGetValue(trimmed, out var assignment))
        {
            return assignment;
        }

        if (Guid.TryParse(trimmed, out var accountId))
        {
            if (accountAssignments.TryGetValue(accountId.ToString("D"), out assignment))
            {
                return assignment;
            }

            if (accountAssignments.TryGetValue(accountId.ToString("N"), out assignment))
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

    private static string? NormalizeScopeId(Guid? scopeId)
        => scopeId?.ToString("D");

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

    private static IReadOnlyList<FundTrialBalanceLine> BuildTrialBalance(
        FundLedgerBook book,
>>>>>>> Stashed changes
        FundLedgerScope scopeKind,
        string? scopeId,
        DateTimeOffset asOf)
    {
        var ledger = ResolveLedger(context, scopeKind, scopeId);
        if (ledger is null)
        {
            return [];
        }

        var balances = ledger.SnapshotAsOf(asOf).Balances;
        var entryCounts = BuildEntryCounts(ledger, asOf);

        return balances
            .OrderBy(static pair => pair.Key.AccountType)
            .ThenBy(static pair => pair.Key.Name, StringComparer.OrdinalIgnoreCase)
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
<<<<<<< Updated upstream
        FundLedgerBuildContext context,
=======
        FundLedgerBook book,
>>>>>>> Stashed changes
        FundLedgerScope scopeKind,
        string? scopeId,
        DateTimeOffset asOf)
    {
<<<<<<< Updated upstream
        var ledger = ResolveLedger(context, scopeKind, scopeId);
        if (ledger is null)
=======
        var source = GetJournalEntries(book, scopeKind, scopeId);

        return source
            .Where(entry => entry.Timestamp <= asOf)
            .OrderByDescending(static entry => entry.Timestamp)
            .ThenByDescending(static entry => entry.JournalEntryId)
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
                    .ToArray()))
            .ToArray();
    }

    private static IReadOnlyList<JournalEntry> GetJournalEntries(
        FundLedgerBook book,
        FundLedgerScope scopeKind,
        string? scopeId)
        => scopeKind switch
>>>>>>> Stashed changes
        {
            return [];
        }

<<<<<<< Updated upstream
        return ledger.GetJournalEntries()
            .Where(entry => entry.Timestamp <= asOf)
            .OrderByDescending(static entry => entry.Timestamp)
            .ThenByDescending(static entry => entry.JournalEntryId)
            .Select(entry => new FundJournalLine(
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
                    .ToArray()))
            .ToArray();
    }

=======
    private static Dictionary<LedgerAccount, int> BuildEntryCounts(
        FundLedgerBook book,
        FundLedgerScope scopeKind,
        string? scopeId,
        DateTimeOffset asOf)
        => GetJournalEntries(book, scopeKind, scopeId)
            .Where(entry => entry.Timestamp <= asOf)
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

>>>>>>> Stashed changes
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
<<<<<<< Updated upstream
=======
                ledgerKey: "Fund",
                ledgerGroupId: "fund",
                sliceKey: "consolidated",
>>>>>>> Stashed changes
                scopeKind: FundLedgerScope.Consolidated,
                scopeId: null,
                displayName: "Consolidated Fund View",
                totals: consolidatedTotals,
                trialBalance: consolidatedTrialBalance,
<<<<<<< Updated upstream
                journal: consolidatedJournal)
        };

        slices.AddRange(BuildScopedSlices(
            context,
            asOf,
            FundLedgerScope.Entity,
            "Entity",
            context.MaterializedEntityIds,
            context.Profile.EntityIds));
        slices.AddRange(BuildScopedSlices(
            context,
            asOf,
            FundLedgerScope.Sleeve,
            "Sleeve",
            context.MaterializedSleeveIds,
            context.Profile.SleeveIds));
        slices.AddRange(BuildScopedSlices(
            context,
            asOf,
            FundLedgerScope.Vehicle,
            "Vehicle",
            context.MaterializedVehicleIds,
            context.Profile.VehicleIds));
=======
                journal: consolidatedJournal,
                scopeLabel: "Fund")
        };

        slices.AddRange(BuildScopedSlices(
            book,
            asOf,
            FundLedgerScope.Entity,
            ResolveScopeIds(book.EntitySnapshotsAsOf(asOf).Keys, profile.EntityIds),
            "Entity"));
        slices.AddRange(BuildScopedSlices(
            book,
            asOf,
            FundLedgerScope.Sleeve,
            ResolveScopeIds(book.SleeveSnapshotsAsOf(asOf).Keys, profile.SleeveIds),
            "Sleeve"));
        slices.AddRange(BuildScopedSlices(
            book,
            asOf,
            FundLedgerScope.Vehicle,
            ResolveScopeIds(book.VehicleSnapshotsAsOf(asOf).Keys, profile.VehicleIds),
            "Vehicle"));
>>>>>>> Stashed changes

        return slices;
    }

    private static IEnumerable<FundLedgerSliceDto> BuildScopedSlices(
        FundLedgerBuildContext context,
        DateTimeOffset asOf,
        FundLedgerScope scopeKind,
<<<<<<< Updated upstream
        string scopeLabel,
        HashSet<string> materializedScopeIds,
        IReadOnlyList<string>? fallbackScopeIds)
    {
        var scopeIds = materializedScopeIds.Count > 0
            ? materializedScopeIds
                .OrderBy(static scopeId => scopeId, StringComparer.OrdinalIgnoreCase)
                .ToArray()
            : (fallbackScopeIds ?? [])
                .Where(static scopeId => !string.IsNullOrWhiteSpace(scopeId))
                .Select(static scopeId => scopeId.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();

        foreach (var scopeId in scopeIds)
        {
            var trialBalance = BuildTrialBalance(context, scopeKind, scopeId, asOf);
            var journal = BuildJournal(context, scopeKind, scopeId, asOf);
            var totals = BuildTotals(trialBalance, journal);

            yield return CreateSlice(
                scopeKind: scopeKind,
                scopeId: scopeId,
                displayName: $"{scopeLabel} {scopeId}",
                totals: totals,
                trialBalance: trialBalance,
                journal: journal);
=======
        IReadOnlyList<string> scopeIds,
        string scopeLabel)
    {
        foreach (var scopeId in scopeIds)
        {
            var trialBalance = BuildTrialBalance(book, scopeKind, scopeId, asOf);
            var journal = BuildJournal(book, scopeKind, scopeId, asOf);
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
>>>>>>> Stashed changes
        }
    }

    private static FundLedgerSliceDto CreateSlice(
<<<<<<< Updated upstream
=======
        string ledgerKey,
        string ledgerGroupId,
        string sliceKey,
>>>>>>> Stashed changes
        FundLedgerScope scopeKind,
        string? scopeId,
        string displayName,
        FundLedgerTotalsDto totals,
        IReadOnlyList<FundTrialBalanceLine> trialBalance,
<<<<<<< Updated upstream
        IReadOnlyList<FundJournalLine> journal)
    {
        var ledgerKey = BuildLedgerKey(scopeKind, scopeId);
        var ledgerGroupId = scopeKind == FundLedgerScope.Consolidated
            ? "fund"
            : scopeId ?? string.Empty;

        return new FundLedgerSliceDto(
            SliceKey: scopeKind == FundLedgerScope.Consolidated
                ? "consolidated"
                : $"{scopeKind}:{scopeId}",
=======
        IReadOnlyList<FundJournalLine> journal,
        string scopeLabel)
        => new(
            SliceKey: sliceKey,
>>>>>>> Stashed changes
            LedgerKey: ledgerKey,
            LedgerGroupId: ledgerGroupId,
            ScopeKind: scopeKind,
            ScopeId: scopeId,
            DisplayName: displayName,
            Totals: totals,
            TrialBalance: trialBalance,
            Journal: journal,
<<<<<<< Updated upstream
            Metadata: BuildSliceMetadata(
                ledgerKey,
                ledgerGroupId,
                scopeKind,
                scopeId,
                displayName,
                trialBalance,
                journal));
    }
=======
            Metadata: BuildSliceMetadata(ledgerKey, ledgerGroupId, scopeKind, scopeId, scopeLabel, trialBalance, journal));
>>>>>>> Stashed changes

    private static IReadOnlyDictionary<string, string> BuildSliceMetadata(
        string ledgerKey,
        string ledgerGroupId,
        FundLedgerScope scopeKind,
        string? scopeId,
<<<<<<< Updated upstream
        string displayName,
        IReadOnlyList<FundTrialBalanceLine> trialBalance,
        IReadOnlyList<FundJournalLine> journal)
    {
        var linkedAccountCount = trialBalance
            .Select(static line => line.FinancialAccountId)
            .Concat(journal.SelectMany(static line => line.FinancialAccountIds ?? []))
            .Where(static accountId => !string.IsNullOrWhiteSpace(accountId))
            .Select(static accountId => accountId!.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Count();

        var metadata = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
=======
        string scopeLabel,
        IReadOnlyList<FundTrialBalanceLine> trialBalance,
        IReadOnlyList<FundJournalLine> journal)
        => new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
>>>>>>> Stashed changes
        {
            ["ledgerKey"] = ledgerKey,
            ["ledgerGroupId"] = ledgerGroupId,
            ["scopeKind"] = scopeKind.ToString(),
<<<<<<< Updated upstream
            ["scopeLabel"] = displayName,
            ["linkedAccountCount"] = linkedAccountCount.ToString(CultureInfo.InvariantCulture),
=======
            ["scopeLabel"] = scopeLabel,
            ["scopeId"] = scopeId ?? string.Empty,
            ["linkedAccountCount"] = CountLinkedAccounts(trialBalance, journal).ToString(CultureInfo.InvariantCulture),
>>>>>>> Stashed changes
            ["trialBalanceLineCount"] = trialBalance.Count.ToString(CultureInfo.InvariantCulture),
            ["journalEntryCount"] = journal.Count.ToString(CultureInfo.InvariantCulture)
        };

<<<<<<< Updated upstream
        if (!string.IsNullOrWhiteSpace(scopeId))
        {
            metadata["scopeId"] = scopeId;
        }

        return metadata;
    }

    private static string BuildLedgerKey(FundLedgerScope scopeKind, string? scopeId) => scopeKind switch
    {
        FundLedgerScope.Consolidated => "Fund",
        FundLedgerScope.Entity => $"Entity:{scopeId}",
        FundLedgerScope.Sleeve => $"Sleeve:{scopeId}",
        FundLedgerScope.Vehicle => $"Vehicle:{scopeId}",
        _ => "Fund"
    };

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

    private static decimal SumBalance(IEnumerable<FundTrialBalanceLine> lines, LedgerAccountType accountType) =>
        lines
            .Where(line => string.Equals(line.AccountType, accountType.ToString(), StringComparison.Ordinal))
            .Sum(static line => line.Balance);

    private static FundLedgerTotalsDto BuildTotals(
        IReadOnlyList<FundTrialBalanceLine> trialBalance,
        IReadOnlyList<FundJournalLine> journal) =>
        new(
            JournalEntryCount: journal.Count,
            LedgerEntryCount: trialBalance.Sum(static line => line.EntryCount),
            AssetBalance: SumBalance(trialBalance, LedgerAccountType.Asset),
            LiabilityBalance: SumBalance(trialBalance, LedgerAccountType.Liability),
            EquityBalance: SumBalance(trialBalance, LedgerAccountType.Equity),
            RevenueBalance: SumBalance(trialBalance, LedgerAccountType.Revenue),
            ExpenseBalance: SumBalance(trialBalance, LedgerAccountType.Expense));

    private static Dictionary<LedgerAccount, int> BuildEntryCounts(IReadOnlyLedger ledger, DateTimeOffset asOf) =>
        ledger.GetJournalEntries()
            .Where(entry => entry.Timestamp <= asOf)
            .SelectMany(static entry => entry.Lines)
            .GroupBy(static line => line.Account)
            .ToDictionary(static group => group.Key, static group => group.Count());

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

    private static HashSet<string> NormalizeSelectedLedgerIds(IReadOnlyList<string>? selectedLedgerIds) =>
        selectedLedgerIds?
=======
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
>>>>>>> Stashed changes
            .Where(static id => !string.IsNullOrWhiteSpace(id))
            .Select(static id => id.Trim())
            .ToHashSet(StringComparer.OrdinalIgnoreCase)
        ?? [];

<<<<<<< Updated upstream
    private sealed record FundLedgerScopeAssignment(
        string? EntityId,
        string? SleeveId,
        string? VehicleId);

    private sealed record FundLedgerBuildContext(
        FundProfileDetail Profile,
        FundLedgerBook Book,
        HashSet<string> MaterializedEntityIds,
        HashSet<string> MaterializedSleeveIds,
        HashSet<string> MaterializedVehicleIds);
=======
    private sealed record AccountStructureAssignment(
        string? EntityScopeId,
        string? SleeveScopeId,
        string? VehicleScopeId);

    private static class EmptyAccountAssignments
    {
        public static IReadOnlyDictionary<string, AccountStructureAssignment> Instance { get; } =
            new Dictionary<string, AccountStructureAssignment>(StringComparer.OrdinalIgnoreCase);
    }
>>>>>>> Stashed changes
}
