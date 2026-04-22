using System.Security.Cryptography;
using System.Text;
using Meridian.Application.FundAccounts;
using Meridian.Application.Services;
using Meridian.Contracts.FundStructure;
using Meridian.Contracts.Workstation;
using Meridian.Ledger;
using Meridian.Storage.Export;
using Meridian.Strategies.Interfaces;
using Meridian.Strategies.Models;
using Meridian.Strategies.Services;

namespace Meridian.Ui.Shared.Services;

/// <summary>
/// Builds the shared governance and fund-operations workspace projection used by
/// the local API and future cross-workspace surfaces.
/// </summary>
public sealed class FundOperationsWorkspaceReadService
{
    private readonly IFundAccountService _fundAccountService;
    private readonly IStrategyRepository _strategyRepository;
    private readonly PortfolioReadService _portfolioReadService;
    private readonly ISecurityReferenceLookup? _securityReferenceLookup;
    private readonly IReconciliationRunService? _strategyReconciliationService;
    private readonly NavAttributionService _navAttributionService;
    private readonly ReportGenerationService _reportGenerationService;

    public FundOperationsWorkspaceReadService(
        IFundAccountService fundAccountService,
        IStrategyRepository strategyRepository,
        PortfolioReadService portfolioReadService,
        NavAttributionService navAttributionService,
        ReportGenerationService reportGenerationService,
        ISecurityReferenceLookup? securityReferenceLookup = null,
        IReconciliationRunService? strategyReconciliationService = null)
    {
        _fundAccountService = fundAccountService ?? throw new ArgumentNullException(nameof(fundAccountService));
        _strategyRepository = strategyRepository ?? throw new ArgumentNullException(nameof(strategyRepository));
        _portfolioReadService = portfolioReadService ?? throw new ArgumentNullException(nameof(portfolioReadService));
        _navAttributionService = navAttributionService ?? throw new ArgumentNullException(nameof(navAttributionService));
        _reportGenerationService = reportGenerationService ?? throw new ArgumentNullException(nameof(reportGenerationService));
        _securityReferenceLookup = securityReferenceLookup;
        _strategyReconciliationService = strategyReconciliationService;
    }

    public async Task<FundOperationsWorkspaceDto> GetWorkspaceAsync(
        FundOperationsWorkspaceQuery query,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        ArgumentException.ThrowIfNullOrWhiteSpace(query.FundProfileId);
        ct.ThrowIfCancellationRequested();

        var normalizedFundProfileId = query.FundProfileId.Trim();
        var fundId = TranslateFundProfileId(normalizedFundProfileId);

        var runsTask = LoadRunsAsync(normalizedFundProfileId, ct);
        var accountProjectionsTask = GetAccountProjectionsAsync(fundId, ct);
        var bankSnapshotsTask = GetBankSnapshotsAsync(fundId, ct);

        await Task.WhenAll(runsTask, accountProjectionsTask, bankSnapshotsTask).ConfigureAwait(false);

        var runs = await runsTask.ConfigureAwait(false);
        var accountProjections = await accountProjectionsTask.ConfigureAwait(false);
        var bankSnapshots = await bankSnapshotsTask.ConfigureAwait(false);
        var accountSummaries = accountProjections
            .Select(static projection => projection.Summary)
            .ToArray();

        var baseCurrency = ResolveCurrency(query.Currency, accountSummaries);
        var asOf = query.AsOf ?? DateTimeOffset.UtcNow;
        var displayName = ResolveDisplayName(normalizedFundProfileId, runs);
        var ledgerBook = BuildLedgerBook(normalizedFundProfileId, runs);
        var ledger = await BuildLedgerSummaryAsync(
            normalizedFundProfileId,
            displayName,
            query.ScopeKind,
            query.ScopeId,
            asOf,
            ledgerBook,
            ct).ConfigureAwait(false);
        var ledgerReconciliationSnapshot = ProjectReconciliationSnapshot(ledgerBook.ReconciliationSnapshot(asOf));

        var cashTask = BuildCashFinancingSummaryAsync(
            baseCurrency,
            accountProjections,
            runs,
            ct);
        var reconciliationTask = BuildReconciliationSummaryAsync(
            accountSummaries,
            runs,
            ct);
        var navTask = BuildNavSummaryAsync(
            normalizedFundProfileId,
            baseCurrency,
            ledgerBook,
            asOf,
            ct);

        await Task.WhenAll(cashTask, reconciliationTask, navTask).ConfigureAwait(false);

        var cashFinancing = await cashTask.ConfigureAwait(false);
        var reconciliation = await reconciliationTask.ConfigureAwait(false);
        var nav = await navTask.ConfigureAwait(false);
        var reporting = BuildReportingSummary();
        var workspace = BuildWorkspaceSummary(
            normalizedFundProfileId,
            displayName,
            baseCurrency,
            asOf,
            accountSummaries,
            cashFinancing,
            reconciliation,
            ledger);

        return new FundOperationsWorkspaceDto(
            FundProfileId: normalizedFundProfileId,
            DisplayName: displayName,
            BaseCurrency: baseCurrency,
            AsOf: asOf,
            RecordedRunCount: runs.Count,
            RelatedRunIds: runs.Select(static run => run.RunId).ToArray(),
            Workspace: workspace,
            Ledger: ledger,
            LedgerReconciliationSnapshot: ledgerReconciliationSnapshot,
            Accounts: accountSummaries,
            BankSnapshots: bankSnapshots,
            CashFinancing: cashFinancing,
            Reconciliation: reconciliation,
            Nav: nav,
            Reporting: reporting);
    }

    public async Task<FundReportPackPreviewDto> PreviewReportPackAsync(
        FundReportPackPreviewRequestDto request,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.FundProfileId);
        ct.ThrowIfCancellationRequested();

        var normalizedFundProfileId = request.FundProfileId.Trim();
        var runs = await LoadRunsAsync(normalizedFundProfileId, ct).ConfigureAwait(false);
        var displayName = ResolveDisplayName(normalizedFundProfileId, runs);
        var currency = ResolveCurrency(request.Currency, []);
        var asOf = request.AsOf ?? DateTimeOffset.UtcNow;
        var ledgerBook = BuildLedgerBook(normalizedFundProfileId, runs);

        var report = await _reportGenerationService.GenerateAsync(
            new ReportRequest(
                FundId: normalizedFundProfileId,
                AsOf: asOf,
                FundLedger: ledgerBook,
                ReportKind: MapReportKind(request.ReportKind)),
            ct).ConfigureAwait(false);

        var assetClassSections = report.AssetClassSections
            .Select(static section => new FundReportAssetClassSectionDto(
                AssetClass: section.AssetClass,
                Total: section.Total))
            .ToArray();

        return new FundReportPackPreviewDto(
            ReportId: report.ReportId,
            FundProfileId: normalizedFundProfileId,
            DisplayName: displayName,
            ReportKind: request.ReportKind,
            Currency: currency,
            AsOf: asOf,
            GeneratedAt: report.GeneratedAt,
            TotalNetAssets: report.TotalNetAssets,
            TrialBalanceLineCount: report.TrialBalance.Count,
            AssetClassSectionCount: report.AssetClassSections.Count,
            AssetClassSections: assetClassSections);
    }

    private async Task<IReadOnlyList<StrategyRunEntry>> LoadRunsAsync(
        string fundProfileId,
        CancellationToken ct)
    {
        var runs = new List<StrategyRunEntry>();
        await foreach (var run in _strategyRepository.GetAllRunsAsync(ct).WithCancellation(ct).ConfigureAwait(false))
        {
            if (string.Equals(run.FundProfileId, fundProfileId, StringComparison.OrdinalIgnoreCase))
            {
                runs.Add(run);
            }
        }

        return runs
            .OrderByDescending(static run => run.StartedAt)
            .ThenByDescending(static run => run.RunId, StringComparer.Ordinal)
            .ToArray();
    }

    private async Task<IReadOnlyList<AccountWorkspaceProjection>> GetAccountProjectionsAsync(
        Guid fundId,
        CancellationToken ct)
    {
        var grouped = await _fundAccountService.GetFundAccountsAsync(fundId, ct).ConfigureAwait(false);
        var accounts = grouped.CustodianAccounts
            .Concat(grouped.BankAccounts)
            .Concat(grouped.BrokerageAccounts)
            .Concat(grouped.OtherAccounts)
            .ToArray();

        var projections = new List<AccountWorkspaceProjection>(accounts.Length);
        foreach (var account in accounts)
        {
            ct.ThrowIfCancellationRequested();

            var latestSnapshot = await _fundAccountService
                .GetLatestBalanceSnapshotAsync(account.AccountId, ct)
                .ConfigureAwait(false);
            var reconciliationRuns = await _fundAccountService
                .GetReconciliationRunsAsync(account.AccountId, ct)
                .ConfigureAwait(false);
            var openBreaks = reconciliationRuns
                .Where(run => !string.Equals(run.Status, "Matched", StringComparison.OrdinalIgnoreCase)
                           && !string.Equals(run.Status, "Resolved", StringComparison.OrdinalIgnoreCase))
                .Sum(static run => run.TotalBreaks);

            var summary = new FundAccountSummary(
                AccountId: account.AccountId,
                AccountType: account.AccountType,
                AccountCode: account.AccountCode,
                DisplayName: account.DisplayName,
                BaseCurrency: account.BaseCurrency,
                Institution: account.Institution,
                IsActive: account.IsActive,
                CashBalance: latestSnapshot?.CashBalance ?? 0m,
                SecuritiesMarketValue: latestSnapshot?.SecuritiesMarketValue ?? 0m,
                NetAssetValue: (latestSnapshot?.CashBalance ?? 0m) + (latestSnapshot?.SecuritiesMarketValue ?? 0m),
                LastSnapshotDate: latestSnapshot?.AsOfDate,
                ReconciliationRuns: reconciliationRuns.Count,
                OpenBreaks: openBreaks,
                PortfolioId: account.PortfolioId,
                LedgerReference: account.LedgerReference,
                BankName: account.BankDetails?.BankName,
                AccountNumberMasked: MaskAccountNumber(account.BankDetails?.AccountNumber),
                EntityId: account.EntityId,
                SleeveId: account.SleeveId,
                VehicleId: account.VehicleId,
                StrategyId: account.StrategyId,
                RunId: account.RunId,
                StructureLabel: BuildStructureLabel(account),
                WorkflowLabel: BuildWorkflowLabel(account));

            projections.Add(new AccountWorkspaceProjection(summary, latestSnapshot));
        }

        return projections
            .OrderBy(projection => projection.Summary.AccountType.ToString(), StringComparer.OrdinalIgnoreCase)
            .ThenBy(projection => projection.Summary.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private async Task<IReadOnlyList<BankAccountSnapshot>> GetBankSnapshotsAsync(
        Guid fundId,
        CancellationToken ct)
    {
        var grouped = await _fundAccountService.GetFundAccountsAsync(fundId, ct).ConfigureAwait(false);
        var bankingAccounts = grouped.BankAccounts
            .Concat(grouped.BrokerageAccounts.Where(static account => account.BankDetails is not null))
            .ToArray();

        var snapshots = new List<BankAccountSnapshot>(bankingAccounts.Length);
        foreach (var account in bankingAccounts)
        {
            ct.ThrowIfCancellationRequested();

            var latestSnapshot = await _fundAccountService
                .GetLatestBalanceSnapshotAsync(account.AccountId, ct)
                .ConfigureAwait(false);
            var bankLines = await _fundAccountService
                .GetBankStatementLinesAsync(account.AccountId, ct: ct)
                .ConfigureAwait(false);
            var latestLine = bankLines
                .OrderByDescending(static line => line.StatementDate)
                .ThenByDescending(static line => line.ValueDate)
                .FirstOrDefault();

            snapshots.Add(new BankAccountSnapshot(
                AccountId: account.AccountId,
                DisplayName: account.DisplayName,
                AccountCode: account.AccountCode,
                BankName: account.BankDetails?.BankName ?? account.Institution ?? "Bank account",
                Currency: account.BaseCurrency,
                CurrentBalance: latestSnapshot?.CashBalance ?? latestLine?.RunningBalance ?? 0m,
                PendingSettlement: latestSnapshot?.PendingSettlement ?? 0m,
                LastStatementDate: latestLine?.StatementDate,
                StatementLineCount: bankLines.Count,
                LatestTransactionType: latestLine?.TransactionType,
                LatestTransactionAmount: latestLine?.Amount));
        }

        return snapshots
            .OrderBy(static snapshot => snapshot.BankName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(static snapshot => snapshot.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private async Task<CashFinancingSummary> BuildCashFinancingSummaryAsync(
        string currency,
        IReadOnlyList<AccountWorkspaceProjection> accountProjections,
        IReadOnlyList<StrategyRunEntry> runs,
        CancellationToken ct)
    {
        var totalCash = accountProjections.Sum(static projection => projection.Summary.CashBalance);
        var pendingSettlement = accountProjections.Sum(static projection => projection.LatestSnapshot?.PendingSettlement ?? 0m);
        var financing = 0m;
        var realized = 0m;
        var unrealized = 0m;
        var longMarketValue = 0m;
        var shortMarketValue = 0m;
        var grossExposure = 0m;
        var netExposure = 0m;
        var totalEquity = 0m;
        var contributingRunCount = 0;

        foreach (var run in runs)
        {
            ct.ThrowIfCancellationRequested();
            var portfolio = await _portfolioReadService.BuildSummaryAsync(run, ct).ConfigureAwait(false);
            if (portfolio is null)
            {
                continue;
            }

            contributingRunCount++;
            financing += portfolio.Financing;
            realized += portfolio.RealizedPnl;
            unrealized += portfolio.UnrealizedPnl;
            longMarketValue += portfolio.LongMarketValue;
            shortMarketValue += portfolio.ShortMarketValue;
            grossExposure += portfolio.GrossExposure;
            netExposure += portfolio.NetExposure;
            totalEquity += portfolio.TotalEquity;
        }

        var highlights = new List<string>
        {
            accountProjections.Count == 0
                ? "No linked fund accounts have been configured yet."
                : $"{accountProjections.Count} fund account(s) are contributing banking and custody balances.",
            contributingRunCount == 0
                ? "No recorded fund-scoped runs are contributing portfolio posture yet."
                : $"{contributingRunCount} recorded run(s) are contributing capital posture.",
            financing == 0m
                ? "No financing costs have been recorded for the current fund scope."
                : $"Financing costs total {financing:C2} across linked runs."
        };

        return new CashFinancingSummary(
            Currency: currency,
            TotalCash: totalCash,
            PendingSettlement: pendingSettlement,
            FinancingCost: financing,
            MarginBalance: 0m,
            RealizedPnl: realized,
            UnrealizedPnl: unrealized,
            LongMarketValue: longMarketValue,
            ShortMarketValue: shortMarketValue,
            GrossExposure: grossExposure,
            NetExposure: netExposure,
            TotalEquity: totalEquity,
            Highlights: highlights);
    }

    private async Task<ReconciliationSummary> BuildReconciliationSummaryAsync(
        IReadOnlyList<FundAccountSummary> accounts,
        IReadOnlyList<StrategyRunEntry> runs,
        CancellationToken ct)
    {
        var items = new List<FundReconciliationItem>();
        var openBreaks = 0;
        decimal breakAmountTotal = 0m;
        var securityCoverageIssues = 0;

        foreach (var account in accounts)
        {
            ct.ThrowIfCancellationRequested();

            var accountRuns = await _fundAccountService
                .GetReconciliationRunsAsync(account.AccountId, ct)
                .ConfigureAwait(false);

            foreach (var run in accountRuns)
            {
                items.Add(new FundReconciliationItem(
                    ReconciliationRunId: run.ReconciliationRunId,
                    AccountId: run.AccountId,
                    AccountDisplayName: account.DisplayName,
                    AsOfDate: run.AsOfDate,
                    Status: run.Status,
                    TotalChecks: run.TotalChecks,
                    TotalMatched: run.TotalMatched,
                    TotalBreaks: run.TotalBreaks,
                    BreakAmountTotal: run.BreakAmountTotal,
                    RequestedAt: run.RequestedAt,
                    CompletedAt: run.CompletedAt,
                    ScopeLabel: "Account",
                    CoverageLabel: "Account-level reconciliation"));

                if (!string.Equals(run.Status, "Matched", StringComparison.OrdinalIgnoreCase)
                    && !string.Equals(run.Status, "Resolved", StringComparison.OrdinalIgnoreCase))
                {
                    openBreaks += run.TotalBreaks;
                    breakAmountTotal += run.BreakAmountTotal;
                }
            }
        }

        if (_strategyReconciliationService is not null)
        {
            foreach (var run in runs)
            {
                ct.ThrowIfCancellationRequested();

                var detail = await _strategyReconciliationService
                    .GetLatestForRunAsync(run.RunId, ct)
                    .ConfigureAwait(false)
                    ?? await _strategyReconciliationService
                        .RunAsync(new ReconciliationRunRequest(run.RunId), ct)
                        .ConfigureAwait(false);

                if (detail is null)
                {
                    continue;
                }

                var asOf = detail.Summary.PortfolioAsOf
                    ?? detail.Summary.LedgerAsOf
                    ?? detail.Summary.CreatedAt;
                var strategyBreakAmount = detail.Breaks.Sum(static result => Math.Abs(result.Variance));
                var status = MapStrategyStatus(detail.Summary);

                items.Add(new FundReconciliationItem(
                    ReconciliationRunId: ParseGuid(detail.Summary.ReconciliationRunId),
                    AccountId: Guid.Empty,
                    AccountDisplayName: run.StrategyName,
                    AsOfDate: DateOnly.FromDateTime(asOf.UtcDateTime),
                    Status: status,
                    TotalChecks: detail.Summary.MatchCount + detail.Summary.BreakCount,
                    TotalMatched: detail.Summary.MatchCount,
                    TotalBreaks: detail.Summary.BreakCount,
                    BreakAmountTotal: strategyBreakAmount,
                    RequestedAt: detail.Summary.CreatedAt,
                    CompletedAt: detail.Summary.CreatedAt,
                    ScopeLabel: "Strategy Run",
                    StrategyName: run.StrategyName,
                    RunId: run.RunId,
                    SecurityIssueCount: detail.Summary.SecurityIssueCount,
                    HasSecurityCoverageIssues: detail.Summary.HasSecurityCoverageIssues,
                    CoverageLabel: detail.Summary.HasSecurityCoverageIssues
                        ? $"{detail.Summary.SecurityIssueCount} security issue(s)"
                        : "Security Master aligned"));

                if (detail.Summary.BreakCount > 0)
                {
                    openBreaks += detail.Summary.BreakCount;
                    breakAmountTotal += strategyBreakAmount;
                }

                securityCoverageIssues += detail.Summary.SecurityIssueCount;
            }
        }

        var ordered = items
            .OrderByDescending(static item => item.RequestedAt)
            .ToArray();

        return new ReconciliationSummary(
            RunCount: ordered.Length,
            OpenBreakCount: openBreaks,
            BreakAmountTotal: breakAmountTotal,
            RecentRuns: ordered,
            SecurityCoverageIssueCount: securityCoverageIssues);
    }

    private async Task<FundNavAttributionSummaryDto> BuildNavSummaryAsync(
        string fundProfileId,
        string currency,
        FundLedgerBook fundLedgerBook,
        DateTimeOffset asOf,
        CancellationToken ct)
    {
        var result = await _navAttributionService
            .AttributeAsync(
                new NavAttributionRequest(
                    FundId: fundProfileId,
                    AsOf: asOf,
                    FundLedger: fundLedgerBook,
                    Currency: currency),
                ct)
            .ConfigureAwait(false);

        var assetClassExposure = result.Consolidated.ByAssetClass
            .OrderBy(static pair => pair.Key, StringComparer.OrdinalIgnoreCase)
            .Select(static pair => new FundNavAssetClassExposureDto(
                AssetClass: pair.Key,
                NetBalance: pair.Value))
            .ToArray();

        return new FundNavAttributionSummaryDto(
            Currency: result.Currency,
            TotalNav: result.Consolidated.TotalNav,
            ComponentCount: result.Consolidated.Components.Count,
            EntityCount: result.ByEntity.Count,
            SleeveCount: result.BySleeve.Count,
            VehicleCount: result.ByVehicle.Count,
            AssetClassExposure: assetClassExposure);
    }

    private async Task<FundLedgerSummary> BuildLedgerSummaryAsync(
        string fundProfileId,
        string displayName,
        FundLedgerScope scopeKind,
        string? scopeId,
        DateTimeOffset asOf,
        FundLedgerBook fundLedgerBook,
        CancellationToken ct)
    {
        var journal = BuildJournal(fundLedgerBook, scopeKind, scopeId, asOf);
        var trialBalance = await BuildTrialBalanceAsync(fundLedgerBook, scopeKind, scopeId, asOf, ct).ConfigureAwait(false);
        var entityCount = fundLedgerBook.EntitySnapshotsAsOf(asOf).Count;
        var sleeveCount = fundLedgerBook.SleeveSnapshotsAsOf(asOf).Count;
        var vehicleCount = fundLedgerBook.VehicleSnapshotsAsOf(asOf).Count;

        return new FundLedgerSummary(
            FundProfileId: fundProfileId,
            FundDisplayName: displayName,
            ScopeKind: scopeKind,
            ScopeId: scopeId,
            AsOf: asOf,
            JournalEntryCount: journal.Count,
            LedgerEntryCount: trialBalance.Sum(static line => line.EntryCount),
            AssetBalance: SumBalance(trialBalance, LedgerAccountType.Asset),
            LiabilityBalance: SumBalance(trialBalance, LedgerAccountType.Liability),
            EquityBalance: SumBalance(trialBalance, LedgerAccountType.Equity),
            RevenueBalance: SumBalance(trialBalance, LedgerAccountType.Revenue),
            ExpenseBalance: SumBalance(trialBalance, LedgerAccountType.Expense),
            TrialBalance: trialBalance,
            Journal: journal,
            EntityCount: entityCount,
            SleeveCount: sleeveCount,
            VehicleCount: vehicleCount);
    }

    public static FundLedgerReconciliationSnapshot ProjectReconciliationSnapshot(FundLedgerSnapshot snapshot)
    {
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

    private static FundLedgerBook BuildLedgerBook(
        string fundProfileId,
        IReadOnlyList<StrategyRunEntry> runs)
    {
        var fundLedgerBook = new FundLedgerBook(fundProfileId);

        foreach (var run in runs)
        {
            foreach (var journalEntry in run.Metrics?.Ledger?.Journal ?? [])
            {
                fundLedgerBook.FundLedger.Post(journalEntry);
            }
        }

        return fundLedgerBook;
    }

    private async Task<IReadOnlyList<FundTrialBalanceLine>> BuildTrialBalanceAsync(
        FundLedgerBook book,
        FundLedgerScope scopeKind,
        string? scopeId,
        DateTimeOffset asOf,
        CancellationToken ct)
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
        var securityLookup = await ResolveSecurityReferencesAsync(
            balances.Keys
                .Select(static account => account.Symbol)
                .Where(static symbol => !string.IsNullOrWhiteSpace(symbol))!
                .Select(static symbol => symbol!),
            ct).ConfigureAwait(false);

        return balances
            .OrderBy(static pair => pair.Key.AccountType)
            .ThenBy(static pair => pair.Key.Name, StringComparer.OrdinalIgnoreCase)
            .Select(pair => new FundTrialBalanceLine(
                AccountName: pair.Key.Name,
                AccountType: pair.Key.AccountType.ToString(),
                Symbol: pair.Key.Symbol,
                FinancialAccountId: pair.Key.FinancialAccountId,
                Balance: pair.Value,
                EntryCount: entryCounts.TryGetValue(pair.Key, out var count) ? count : 0,
                Security: pair.Key.Symbol is not null ? securityLookup.GetValueOrDefault(pair.Key.Symbol) : null))
            .ToArray();
    }

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
            .OrderByDescending(static entry => entry.Timestamp)
            .ThenByDescending(static entry => entry.JournalEntryId)
            .Select(entry => new FundJournalLine(
                JournalEntryId: entry.JournalEntryId,
                Timestamp: entry.Timestamp,
                Description: entry.Description,
                TotalDebits: entry.Lines.Sum(static line => line.Debit),
                TotalCredits: entry.Lines.Sum(static line => line.Credit),
                LineCount: entry.Lines.Count))
            .ToArray();
    }

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
            .SelectMany(static entry => entry.Lines)
            .GroupBy(static line => line.Account)
            .ToDictionary(static group => group.Key, static group => group.Count());
    }

    private static decimal SumBalance(IEnumerable<FundTrialBalanceLine> lines, LedgerAccountType accountType)
        => lines
            .Where(line => string.Equals(line.AccountType, accountType.ToString(), StringComparison.Ordinal))
            .Sum(static line => line.Balance);

    private static FundWorkspaceSummary BuildWorkspaceSummary(
        string fundProfileId,
        string displayName,
        string baseCurrency,
        DateTimeOffset asOf,
        IReadOnlyList<FundAccountSummary> accounts,
        CashFinancingSummary cashFinancing,
        ReconciliationSummary reconciliation,
        FundLedgerSummary ledger)
    {
        var totalEquity = cashFinancing.TotalEquity != 0m
            ? cashFinancing.TotalEquity
            : accounts.Sum(static account => account.NetAssetValue);

        var securityResolvedCount = ledger.TrialBalance.Count(static line => line.Security is not null);
        var securityMissingCount = ledger.TrialBalance.Count(static line =>
            !string.IsNullOrWhiteSpace(line.Symbol) &&
            line.Security is null);

        return new FundWorkspaceSummary(
            FundProfileId: fundProfileId,
            FundDisplayName: displayName,
            BaseCurrency: baseCurrency,
            AsOf: asOf,
            TotalAccounts: accounts.Count,
            BankAccountCount: accounts.Count(static account => account.AccountType == AccountTypeDto.Bank),
            BrokerageAccountCount: accounts.Count(static account => account.AccountType == AccountTypeDto.Brokerage),
            CustodyAccountCount: accounts.Count(static account => account.AccountType == AccountTypeDto.Custody),
            TotalCash: cashFinancing.TotalCash,
            GrossExposure: cashFinancing.GrossExposure,
            NetExposure: cashFinancing.NetExposure,
            TotalEquity: totalEquity,
            FinancingCost: cashFinancing.FinancingCost,
            PendingSettlement: cashFinancing.PendingSettlement,
            OpenReconciliationBreaks: reconciliation.OpenBreakCount,
            ReconciliationRuns: reconciliation.RunCount,
            JournalEntryCount: ledger.JournalEntryCount,
            TrialBalanceLineCount: ledger.TrialBalance.Count,
            SecurityResolvedCount: securityResolvedCount,
            SecurityMissingCount: securityMissingCount,
            SecurityCoverageIssues: reconciliation.SecurityCoverageIssueCount);
    }

    private async Task<Dictionary<string, WorkstationSecurityReference?>> ResolveSecurityReferencesAsync(
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
            ct.ThrowIfCancellationRequested();
            lookup[symbol] = await _securityReferenceLookup
                .GetBySymbolAsync(symbol, ct)
                .ConfigureAwait(false);
        }

        return lookup;
    }

    private static FundReportingSummaryDto BuildReportingSummary()
    {
        var profiles = ExportProfile.GetBuiltInProfiles()
            .Select(static profile => new FundReportingProfileDto(
                Id: profile.Id,
                Name: profile.Name,
                TargetTool: profile.TargetTool,
                Format: profile.Format.ToString(),
                Description: profile.Description ?? string.Empty,
                LoaderScript: profile.IncludeLoaderScript,
                DataDictionary: profile.IncludeDataDictionary))
            .OrderBy(static profile => profile.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var recommended = profiles
            .Where(static profile => profile.Id is "excel" or "python-pandas" or "postgresql" or "arrow-feather")
            .Select(static profile => profile.Id)
            .ToArray();

        return new FundReportingSummaryDto(
            ProfileCount: profiles.Length,
            RecommendedProfiles: recommended,
            ReportPackTargets: ["board", "investor", "compliance", "fund-ops"],
            Profiles: profiles,
            Summary: $"{profiles.Length} export/reporting profiles are available for governance workflows.");
    }

    private static string ResolveDisplayName(
        string fundProfileId,
        IReadOnlyList<StrategyRunEntry> runs)
        => runs
            .Select(static run => run.FundDisplayName)
            .FirstOrDefault(static name => !string.IsNullOrWhiteSpace(name))
            ?? fundProfileId;

    private static string ResolveCurrency(
        string? requestedCurrency,
        IReadOnlyList<FundAccountSummary> accounts)
        => !string.IsNullOrWhiteSpace(requestedCurrency)
            ? requestedCurrency.Trim().ToUpperInvariant()
            : accounts.Select(static account => account.BaseCurrency)
                .FirstOrDefault(static currency => !string.IsNullOrWhiteSpace(currency))
            ?? "USD";

    private static string? MaskAccountNumber(string? accountNumber)
    {
        if (string.IsNullOrWhiteSpace(accountNumber))
        {
            return null;
        }

        var trimmed = accountNumber.Trim();
        if (trimmed.Length <= 4)
        {
            return trimmed;
        }

        return $"****{trimmed[^4..]}";
    }

    private static string BuildStructureLabel(AccountSummaryDto account)
    {
        var segments = new List<string>(3);

        if (account.EntityId.HasValue)
        {
            segments.Add($"Entity {FormatKey(account.EntityId.Value)}");
        }

        if (account.SleeveId.HasValue)
        {
            segments.Add($"Sleeve {FormatKey(account.SleeveId.Value)}");
        }

        if (account.VehicleId.HasValue)
        {
            segments.Add($"Vehicle {FormatKey(account.VehicleId.Value)}");
        }

        return segments.Count == 0 ? "Fund-level" : string.Join(" • ", segments);
    }

    private static string BuildWorkflowLabel(AccountSummaryDto account)
    {
        var segments = new List<string>(2);

        if (!string.IsNullOrWhiteSpace(account.StrategyId))
        {
            segments.Add($"Strategy {account.StrategyId}");
        }

        if (!string.IsNullOrWhiteSpace(account.RunId))
        {
            segments.Add($"Run {account.RunId}");
        }

        if (!string.IsNullOrWhiteSpace(account.PortfolioId))
        {
            segments.Add($"Portfolio {account.PortfolioId}");
        }

        if (!string.IsNullOrWhiteSpace(account.LedgerReference))
        {
            segments.Add($"Ledger {account.LedgerReference}");
        }

        return segments.Count == 0 ? "Manual / external" : string.Join(" • ", segments);
    }

    private static string FormatKey(Guid value)
        => value.ToString("N")[..8].ToUpperInvariant();

    private static Guid TranslateFundProfileId(string fundProfileId)
    {
        var bytes = MD5.HashData(Encoding.UTF8.GetBytes(fundProfileId));
        return new Guid(bytes);
    }

    private static string MapStrategyStatus(ReconciliationRunSummary summary)
        => summary.HasSecurityCoverageIssues
            ? "SecurityCoverageOpen"
            : summary.BreakCount > 0
                ? "BreaksOpen"
                : "Matched";

    private static Guid ParseGuid(string value)
    {
        if (Guid.TryParse(value, out var guid))
        {
            return guid;
        }

        return new Guid(MD5.HashData(Encoding.UTF8.GetBytes(value)));
    }

    private static ReportKind MapReportKind(GovernanceReportKindDto reportKind)
        => reportKind switch
        {
            GovernanceReportKindDto.TrialBalance => ReportKind.TrialBalance,
            GovernanceReportKindDto.NavSummary => ReportKind.NavSummary,
            GovernanceReportKindDto.AssetAllocation => ReportKind.AssetAllocation,
            GovernanceReportKindDto.ReconciliationPack => ReportKind.ReconciliationPack,
            _ => ReportKind.TrialBalance
        };

    private sealed record AccountWorkspaceProjection(
        FundAccountSummary Summary,
        AccountBalanceSnapshotDto? LatestSnapshot);
}
