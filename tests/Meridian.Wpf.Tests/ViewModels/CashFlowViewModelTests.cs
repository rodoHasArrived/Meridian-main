using FluentAssertions;
using Meridian.Backtesting.Sdk;
using Meridian.Contracts.Workstation;
using Meridian.Strategies.Services;
using Meridian.Strategies.Storage;
using Meridian.Wpf.Services;
using Meridian.Wpf.Tests.Support;
using Meridian.Wpf.ViewModels;
using System.IO;
using System.Windows.Controls;

namespace Meridian.Wpf.Tests.ViewModels;

public sealed class CashFlowViewModelTests
{
    // ── Helpers ───────────────────────────────────────────────────────────

    private static (CashFlowViewModel Vm, StrategyRunWorkspaceService RunService) CreateEmpty()
    {
        var store = new StrategyRunStore();
        var runService = new StrategyRunWorkspaceService(
            store,
            new PortfolioReadService(),
            new LedgerReadService());
        var vm = new CashFlowViewModel(runService, NavigationService.Instance);
        return (vm, runService);
    }

    private static (CashFlowViewModel Vm, string RunId) CreateWithTradeRun()
    {
        var store = new StrategyRunStore();
        var runService = new StrategyRunWorkspaceService(
            store,
            new PortfolioReadService(),
            new LedgerReadService());

        var started = new DateTimeOffset(2026, 3, 1, 9, 30, 0, TimeSpan.Zero);
        var cashFlows = new CashFlowEntry[]
        {
            new TradeCashFlow(started.AddMinutes(1), 500m, "AAPL", 10, 50m),
            new CommissionCashFlow(started.AddMinutes(1), -1m, "AAPL", Guid.NewGuid()),
            new DividendCashFlow(started.AddDays(5), 20m, "MSFT", 100, 0.20m),
        };

        var result = BuildResult(started, cashFlows);
        var request = new BacktestRequest(
            From: DateOnly.FromDateTime(started.UtcDateTime),
            To: DateOnly.FromDateTime(started.AddDays(10).UtcDateTime),
            Symbols: ["AAPL", "MSFT"],
            InitialCash: 100_000m,
            DataRoot: "./data/test");

        var runId = runService.RecordBacktestRunAsync(request, "Test Strategy", result)
            .GetAwaiter().GetResult();

        var vm = new CashFlowViewModel(runService, NavigationService.Instance);
        return (vm, runId);
    }

    private static (CashFlowViewModel Vm, string RunId) CreateWithTradeRunAndContinuity()
    {
        var store = new StrategyRunStore();
        var portfolioReadService = new PortfolioReadService();
        var ledgerReadService = new LedgerReadService();
        var readService = new StrategyRunReadService(store, portfolioReadService, ledgerReadService);
        var runService = new StrategyRunWorkspaceService(store, readService);

        var started = new DateTimeOffset(2026, 3, 1, 9, 30, 0, TimeSpan.Zero);
        var cashFlows = new CashFlowEntry[]
        {
            new TradeCashFlow(started.AddMinutes(1), 500m, "AAPL", 10, 50m),
            new CommissionCashFlow(started.AddMinutes(1), -1m, "AAPL", Guid.NewGuid()),
            new DividendCashFlow(started.AddDays(5), 20m, "MSFT", 100, 0.20m),
        };
        var request = new BacktestRequest(
            From: DateOnly.FromDateTime(started.UtcDateTime),
            To: DateOnly.FromDateTime(started.AddDays(10).UtcDateTime),
            Symbols: ["AAPL", "MSFT"],
            InitialCash: 100_000m,
            DataRoot: "./data/test");

        var runId = runService.RecordBacktestRunAsync(request, "Test Strategy", BuildResult(started, cashFlows))
            .GetAwaiter().GetResult();
        var reconciliationService = new ReconciliationRunService(
            readService,
            new ReconciliationProjectionService(),
            new InMemoryReconciliationRunRepository());
        var continuityService = new StrategyRunContinuityService(
            readService,
            new CashFlowProjectionService(store),
            reconciliationService);

        var vm = new CashFlowViewModel(runService, NavigationService.Instance, continuityService);
        return (vm, runId);
    }

    private static async Task<(CashFlowViewModel Vm, string RunId)> CreateWithTradeRunAndBlockedContinuityAsync()
    {
        var store = new StrategyRunStore();
        var portfolioReadService = new PortfolioReadService();
        var ledgerReadService = new LedgerReadService();
        var readService = new StrategyRunReadService(store, portfolioReadService, ledgerReadService);
        var runService = new StrategyRunWorkspaceService(store, readService);

        var started = new DateTimeOffset(2026, 3, 1, 9, 30, 0, TimeSpan.Zero);
        var cashFlows = new CashFlowEntry[]
        {
            new TradeCashFlow(started.AddMinutes(1), 500m, "AAPL", 10, 50m),
            new CommissionCashFlow(started.AddMinutes(1), -1m, "AAPL", Guid.NewGuid()),
            new DividendCashFlow(started.AddDays(5), 20m, "MSFT", 100, 0.20m),
        };
        var request = new BacktestRequest(
            From: DateOnly.FromDateTime(started.UtcDateTime),
            To: DateOnly.FromDateTime(started.AddDays(10).UtcDateTime),
            Symbols: ["AAPL", "MSFT"],
            InitialCash: 100_000m,
            DataRoot: "./data/test");

        var runId = await runService.RecordBacktestRunAsync(request, "Blocked Continuity Strategy", BuildResult(started, cashFlows));
        var reconciliationRepository = new InMemoryReconciliationRunRepository();
        await reconciliationRepository.SaveAsync(new ReconciliationRunDetail(
            new ReconciliationRunSummary("recon-blocked", runId, started.AddHours(2), started, started.AddHours(2), 0, 1, 1, false, 10m, 5, 0, false, 0, 0, 0, 0, 0, false),
            [],
            [new ReconciliationBreakDto("cash-balance", "Cash balance", ReconciliationBreakCategory.CashMismatch, ReconciliationBreakStatus.Open, "ledger", 100m, 110m, 10m, ReconciliationBreakSeverity.High, "variance", started.AddHours(2), started.AddHours(2))],
            []));
        var reconciliationService = new ReconciliationRunService(
            readService,
            new ReconciliationProjectionService(),
            reconciliationRepository);
        var continuityService = new StrategyRunContinuityService(
            readService,
            new CashFlowProjectionService(store),
            reconciliationService);

        var vm = new CashFlowViewModel(runService, NavigationService.Instance, continuityService);
        return (vm, runId);
    }

    private static BacktestResult BuildResult(DateTimeOffset started, CashFlowEntry[] cashFlows)
    {
        return new BacktestResult(
            Request: new BacktestRequest(
                From: DateOnly.FromDateTime(started.UtcDateTime),
                To: DateOnly.FromDateTime(started.AddDays(10).UtcDateTime),
                Symbols: ["AAPL", "MSFT"],
                InitialCash: 100_000m,
                DataRoot: "./data/test"),
            Universe: new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "AAPL", "MSFT" },
            Snapshots: [],
            CashFlows: cashFlows,
            Fills: [],
            Metrics: new BacktestMetrics(
                InitialCapital: 100_000m,
                FinalEquity: 100_519m,
                GrossPnl: 520m,
                NetPnl: 519m,
                TotalReturn: 0.00519m,
                AnnualizedReturn: 0.01m,
                SharpeRatio: 0.8,
                SortinoRatio: 0.9,
                CalmarRatio: 0.5,
                MaxDrawdown: 50m,
                MaxDrawdownPercent: 0.0005m,
                MaxDrawdownRecoveryDays: 1,
                ProfitFactor: 1.2,
                WinRate: 0.6,
                TotalTrades: 2,
                WinningTrades: 1,
                LosingTrades: 1,
                TotalCommissions: 1m,
                TotalMarginInterest: 0m,
                TotalShortRebates: 0m,
                Xirr: 0.01,
                SymbolAttribution: new Dictionary<string, SymbolAttribution>(StringComparer.OrdinalIgnoreCase)),
            ElapsedTime: TimeSpan.FromSeconds(2),
            Ledger: new Meridian.Ledger.Ledger(),
            TotalEventsProcessed: cashFlows.Length);
    }

    // ── Initial state ─────────────────────────────────────────────────────

    [Fact]
    public void InitialState_ShouldShowSelectPromptAndEmptyCollections()
    {
        var (vm, _) = CreateEmpty();

        vm.Title.Should().Be("Cash Flow");
        vm.StatusText.Should().Contain("Select a strategy run");
        vm.Entries.Should().BeEmpty();
        vm.LadderBuckets.Should().BeEmpty();
        vm.HasEntries.Should().BeFalse();
        vm.HasLadderBuckets.Should().BeFalse();
        vm.HasSelectedEntry.Should().BeFalse();
        vm.HasSelectedLadderBucket.Should().BeFalse();
        vm.CanOpenRunDrillIns.Should().BeFalse();
        vm.CanOpenSelectedSecurity.Should().BeFalse();
        vm.IsEntriesEmptyStateVisible.Should().BeTrue();
        vm.IsLadderEmptyStateVisible.Should().BeTrue();
        vm.EntriesEmptyStateTitle.Should().Be("Select a run to inspect cash flows");
        vm.LadderEmptyStateDetail.Should().Contain("Select a strategy run");
        vm.EntriesTable.Title.Should().Be("Cash-flow events");
        vm.LadderTable.Title.Should().Be("Cash ladder");
        vm.SelectedEntryInspector.Title.Should().Be("No cash-flow event selected");
        vm.SelectedLadderBucketInspector.Title.Should().Be("No cash ladder bucket selected");
        vm.ContinuityInspector.Title.Should().Be("No continuity payload");
        vm.RunActionStateTitle.Should().Be("Select a retained run");
        vm.SelectedSecurityTooltip.Should().Contain("Select a cash-flow event");
        vm.TotalEntriesText.Should().Be("-");
        vm.TotalInflowsText.Should().Be("-");
        vm.TotalOutflowsText.Should().Be("-");
        vm.NetCashFlowText.Should().Be("-");
    }

    [Fact]
    public void Commands_InitialState_ShouldBeDisabled()
    {
        var (vm, _) = CreateEmpty();

        vm.OpenRunDetailCommand.CanExecute(null).Should().BeFalse();
        vm.OpenPortfolioCommand.CanExecute(null).Should().BeFalse();
        vm.OpenLedgerCommand.CanExecute(null).Should().BeFalse();
        vm.OpenSelectedSecurityCommand.CanExecute(null).Should().BeFalse();
    }

    // ── Parameter handling ────────────────────────────────────────────────

    [Fact]
    public async Task Parameter_WhenSetToNullOrEmpty_ShouldShowSelectPrompt()
    {
        var (vm, _) = CreateEmpty();

        await vm.LoadRunAsync(null);

        vm.StatusText.Should().Contain("Select a strategy run");
    }

    [Fact]
    public async Task Parameter_WhenSetToUnknownRunId_ShouldShowNotFoundMessage()
    {
        var (vm, _) = CreateEmpty();

        await vm.LoadRunAsync("unknown-run-id");

        vm.StatusText.Should().Contain("unknown-run-id");
        vm.Entries.Should().BeEmpty();
        vm.LadderBuckets.Should().BeEmpty();
        vm.OpenRunDetailCommand.CanExecute(null).Should().BeFalse();
        vm.OpenPortfolioCommand.CanExecute(null).Should().BeFalse();
        vm.OpenLedgerCommand.CanExecute(null).Should().BeFalse();
        vm.CanOpenRunDrillIns.Should().BeFalse();
        vm.RunActionStateTitle.Should().Be("Cash-flow evidence unavailable");
        vm.EntriesEmptyStateTitle.Should().Be("Cash-flow data unavailable");
        vm.EntriesEmptyStateDetail.Should().Contain("unknown-run-id");
        vm.LadderEmptyStateDetail.Should().Contain("retained run");
    }

    // ── Data loading ──────────────────────────────────────────────────────

    [Fact]
    public async Task Parameter_WhenSetToKnownRunId_ShouldLoadEntries()
    {
        var (vm, runId) = CreateWithTradeRun();

        await vm.LoadRunAsync(runId);

        vm.Entries.Should().HaveCount(3, "three cash flow events were recorded");
        vm.HasEntries.Should().BeTrue();
        vm.SelectedEntry.Should().NotBeNull();
        vm.HasSelectedEntry.Should().BeTrue();
        vm.SelectedEntryInspector.Title.Should().NotBe("No cash-flow event selected");
        vm.CanOpenSelectedSecurity.Should().BeTrue();
        vm.SelectedSecurityTooltip.Should().Contain("AAPL");
        vm.IsEntriesEmptyStateVisible.Should().BeFalse();
        vm.TotalEntriesText.Should().Be("3");
    }

    [Fact]
    public async Task Parameter_WhenSetToKnownRunId_ShouldComputeInflowsAndOutflows()
    {
        var (vm, runId) = CreateWithTradeRun();

        await vm.LoadRunAsync(runId);

        // Inflows: trade 500 + dividend 20 = 520; Outflows: commission 1
        vm.TotalInflowsText.Should().NotBe("-");
        vm.TotalOutflowsText.Should().NotBe("-");
        vm.NetCashFlowText.Should().NotBe("-");
    }

    [Fact]
    public async Task Parameter_WhenSetToKnownRunId_ShouldDescribeMissingLadderBuckets()
    {
        var (vm, runId) = CreateWithTradeRun();

        await vm.LoadRunAsync(runId);

        vm.BucketSummaryText.Should().NotBe("-");
        vm.HasLadderBuckets.Should().BeFalse();
        vm.SelectedLadderBucket.Should().BeNull();
        vm.HasSelectedLadderBucket.Should().BeFalse();
        vm.SelectedLadderBucketInspector.Title.Should().Be("No cash ladder bucket selected");
        vm.IsLadderEmptyStateVisible.Should().BeTrue();
        vm.LadderEmptyStateDetail.Should().Contain("events are loaded");
    }

    [Fact]
    public async Task Parameter_WhenSetToRunWithoutCashFlows_ShouldShowEmptyGuidance()
    {
        var store = new StrategyRunStore();
        var runService = new StrategyRunWorkspaceService(
            store,
            new PortfolioReadService(),
            new LedgerReadService());
        var started = new DateTimeOffset(2026, 3, 1, 9, 30, 0, TimeSpan.Zero);
        var request = new BacktestRequest(
            From: DateOnly.FromDateTime(started.UtcDateTime),
            To: DateOnly.FromDateTime(started.AddDays(10).UtcDateTime),
            Symbols: ["AAPL"],
            InitialCash: 100_000m,
            DataRoot: "./data/test");
        var runId = await runService.RecordBacktestRunAsync(request, "No Cash Flow Strategy", BuildResult(started, []));
        var vm = new CashFlowViewModel(runService, NavigationService.Instance);

        await vm.LoadRunAsync(runId);

        vm.HasEntries.Should().BeFalse();
        vm.HasLadderBuckets.Should().BeFalse();
        vm.IsEntriesEmptyStateVisible.Should().BeTrue();
        vm.IsLadderEmptyStateVisible.Should().BeTrue();
        vm.EntriesEmptyStateTitle.Should().Be("No cash-flow events recorded");
        vm.EntriesEmptyStateDetail.Should().Contain("no trade, commission, dividend");
        vm.LadderEmptyStateTitle.Should().Be("No cash ladder buckets");
        vm.LadderEmptyStateDetail.Should().Contain("no retained cash-flow events");
        vm.OpenRunDetailCommand.CanExecute(null).Should().BeTrue();
        vm.OpenSelectedSecurityCommand.CanExecute(null).Should().BeFalse();
        vm.CanOpenRunDrillIns.Should().BeTrue();
        vm.CanOpenSelectedSecurity.Should().BeFalse();
    }

    [Fact]
    public async Task Parameter_WhenSetToKnownRunId_ShouldUpdateTitleWithRunIdPrefix()
    {
        var (vm, runId) = CreateWithTradeRun();

        await vm.LoadRunAsync(runId);

        vm.Title.Should().Contain(runId[..8]);
    }

    [Fact]
    public async Task Parameter_WhenSetToKnownRunId_ShouldEnableNavigationCommands()
    {
        var (vm, runId) = CreateWithTradeRun();

        await vm.LoadRunAsync(runId);

        vm.OpenRunDetailCommand.CanExecute(null).Should().BeTrue();
        vm.OpenPortfolioCommand.CanExecute(null).Should().BeTrue();
        vm.OpenLedgerCommand.CanExecute(null).Should().BeTrue();
        vm.OpenSelectedSecurityCommand.CanExecute(null).Should().BeTrue();
        vm.RunActionStateTitle.Should().Be("Run drill-ins ready");
        vm.RunDrillInTooltip.Should().Contain(runId);
    }

    [Fact]
    public async Task Parameter_WhenSetToKnownRunId_ShouldShowSharedContinuityPosture()
    {
        var (vm, runId) = CreateWithTradeRunAndContinuity();

        await vm.LoadRunAsync(runId);

        vm.ContinuityPostureText.Should().Be("Continuity needs review");
        vm.ContinuityDetailText.Should().Contain("portfolio Missing");
        vm.ContinuityDetailText.Should().Contain("ledger Healthy");
        vm.ContinuityDetailText.Should().Contain("cash flow Healthy");
        vm.ContinuityDetailText.Should().Contain("reconciliation Missing");
        vm.ContinuityWarningText.Should().Contain("missing-");
        vm.ContinuityInspector.Title.Should().Be("Continuity needs review");
    }

    [Fact]
    public async Task Parameter_WhenCashFlowsLoadButContinuityIsBlocked_ShouldSurfaceSharedBlocker()
    {
        var (vm, runId) = await CreateWithTradeRunAndBlockedContinuityAsync();

        await vm.LoadRunAsync(runId);

        vm.StatusText.Should().Contain("cash-flow event(s) loaded");
        vm.HasEntries.Should().BeTrue();
        vm.ContinuityPostureText.Should().Be("Continuity blocked");
        vm.ContinuityDetailText.Should().Contain("cash flow Healthy");
        vm.ContinuityDetailText.Should().Contain("reconciliation Healthy");
        vm.ContinuityWarningText.Should().Contain("open-reconciliation-breaks");
        vm.ContinuityInspector.Title.Should().Be("Continuity blocked");
    }

    // ── Entry ordering ────────────────────────────────────────────────────

    [Fact]
    public async Task Entries_ShouldBeOrderedChronologically()
    {
        var (vm, runId) = CreateWithTradeRun();

        await vm.LoadRunAsync(runId);

        var timestamps = vm.Entries.Select(e => e.Timestamp).ToList();
        timestamps.Should().BeInAscendingOrder();
    }

    // ── BucketSummaryText ─────────────────────────────────────────────────

    [Fact]
    public async Task BucketSummaryText_WhenLoaded_ShouldDescribeBucketingParameters()
    {
        var (vm, runId) = CreateWithTradeRun();

        await vm.LoadRunAsync(runId);

        vm.BucketSummaryText.Should().NotBe("-");
        vm.BucketSummaryText.Should().Contain("d"); // bucket day width label
    }

    [Fact]
    public void OpenSelectedSecurityCommand_NavigatesToSecurityMasterForSelectedSymbol()
    {
        WpfTestThread.Run(async () =>
        {
            var navigation = NavigationService.Instance;
            navigation.ResetForTests();
            navigation.Initialize(new Frame());

            var (vm, runId) = CreateWithTradeRun();
            await vm.LoadRunAsync(runId);

            vm.SelectedEntry.Should().NotBeNull();
            vm.OpenSelectedSecurityCommand.Execute(null);

            navigation.GetCurrentPageTag().Should().Be("SecurityMaster");
            navigation.GetBreadcrumbs().First().Parameter.Should().Be(vm.SelectedEntry!.Symbol);
        });
    }

    [Fact]
    public void RunCashFlowPageSource_UsesDenseTablesInspectorsAndSelectionAwareActions()
    {
        var xaml = File.ReadAllText(GetRepositoryFilePath(@"src\Meridian.Wpf\Views\RunCashFlowPage.xaml"));
        var viewModel = File.ReadAllText(GetRepositoryFilePath(@"src\Meridian.Wpf\ViewModels\CashFlowViewModel.cs"));

        xaml.Should().NotContain("<DataGrid");
        xaml.Should().Contain("RunCashFlowActionStrip");
        xaml.Should().Contain("RunCashFlowWorkbench");
        xaml.Should().Contain("DenseDataGridControl");
        xaml.Should().Contain("InspectorPanelControl");
        xaml.Should().Contain("RunCashFlowLadderGrid");
        xaml.Should().Contain("RunCashFlowLadderEmptyState");
        xaml.Should().Contain("RunCashFlowEntriesGrid");
        xaml.Should().Contain("RunCashFlowEntriesEmptyState");
        xaml.Should().Contain("RunCashFlowContinuityPosture");
        xaml.Should().Contain("RunCashFlowEntryInspector");
        xaml.Should().Contain("RunCashFlowLadderInspector");
        xaml.Should().Contain("RunCashFlowActionInspector");
        xaml.Should().Contain("Table=\"{Binding LadderTable}\"");
        xaml.Should().Contain("SelectedItem=\"{Binding SelectedLadderBucket, Mode=TwoWay}\"");
        xaml.Should().Contain("Table=\"{Binding EntriesTable}\"");
        xaml.Should().Contain("SelectedItem=\"{Binding SelectedEntry, Mode=TwoWay}\"");
        xaml.Should().Contain("{Binding HasLadderBuckets, Converter={StaticResource BoolToVisibilityConverter}}");
        xaml.Should().Contain("{Binding IsLadderEmptyStateVisible, Converter={StaticResource BoolToVisibilityConverter}}");
        xaml.Should().Contain("{Binding HasEntries, Converter={StaticResource BoolToVisibilityConverter}}");
        xaml.Should().Contain("{Binding IsEntriesEmptyStateVisible, Converter={StaticResource BoolToVisibilityConverter}}");
        xaml.Should().Contain("{Binding LadderEmptyStateTitle}");
        xaml.Should().Contain("{Binding EntriesEmptyStateDetail}");
        xaml.Should().Contain("ToolTip=\"{Binding RunDrillInTooltip}\"");
        xaml.Should().Contain("ToolTip=\"{Binding SelectedSecurityTooltip}\"");
        xaml.Should().Contain("ToolTipService.ShowOnDisabled=\"True\"");
        viewModel.Should().Contain("public bool HasEntries => Entries.Count > 0;");
        viewModel.Should().Contain("public WorkstationTableModel<CashFlowEntryDto> EntriesTable { get; }");
        viewModel.Should().Contain("public WorkstationTableModel<CashLadderBucketDto> LadderTable { get; }");
        viewModel.Should().Contain("public InspectorPanelModel SelectedEntryInspector");
        viewModel.Should().Contain("public InspectorPanelModel ContinuityInspector");
        viewModel.Should().Contain("public bool CanOpenSelectedSecurity");
    }

    private static string GetRepositoryFilePath(string relativePath)
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            var candidate = Path.Combine(current.FullName, relativePath);
            if (File.Exists(candidate))
            {
                return candidate;
            }

            current = current.Parent;
        }

        throw new DirectoryNotFoundException(
            $"Could not locate repository file '{relativePath}' from '{AppContext.BaseDirectory}'.");
    }
}
