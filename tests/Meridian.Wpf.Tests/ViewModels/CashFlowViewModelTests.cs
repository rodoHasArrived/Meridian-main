using FluentAssertions;
using Meridian.Backtesting.Sdk;
using Meridian.Contracts.Workstation;
using Meridian.Strategies.Services;
using Meridian.Strategies.Storage;
using Meridian.Wpf.Services;
using Meridian.Wpf.ViewModels;

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
                AverageTradeDuration: TimeSpan.FromDays(1),
                TotalCommissions: 1m,
                TotalMarginInterest: 0m,
                TotalShortRebates: 0m),
            ElapsedTime: TimeSpan.FromSeconds(2),
            Ledger: null);
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
    }

    // ── Parameter handling ────────────────────────────────────────────────

    [Fact]
    public async Task Parameter_WhenSetToNullOrEmpty_ShouldShowSelectPrompt()
    {
        var (vm, _) = CreateEmpty();

        vm.Parameter = null;
        await Task.Delay(50); // allow async load to settle

        vm.StatusText.Should().Contain("Select a strategy run");
    }

    [Fact]
    public async Task Parameter_WhenSetToUnknownRunId_ShouldShowNotFoundMessage()
    {
        var (vm, _) = CreateEmpty();

        vm.Parameter = "unknown-run-id";
        await Task.Delay(50);

        vm.StatusText.Should().Contain("unknown-run-id");
        vm.Entries.Should().BeEmpty();
        vm.LadderBuckets.Should().BeEmpty();
    }

    // ── Data loading ──────────────────────────────────────────────────────

    [Fact]
    public async Task Parameter_WhenSetToKnownRunId_ShouldLoadEntries()
    {
        var (vm, runId) = CreateWithTradeRun();

        vm.Parameter = runId;
        await Task.Delay(100);

        vm.Entries.Should().HaveCount(3, "three cash flow events were recorded");
        vm.TotalEntriesText.Should().Be("3");
    }

    [Fact]
    public async Task Parameter_WhenSetToKnownRunId_ShouldComputeInflowsAndOutflows()
    {
        var (vm, runId) = CreateWithTradeRun();

        vm.Parameter = runId;
        await Task.Delay(100);

        // Inflows: trade 500 + dividend 20 = 520; Outflows: commission 1
        vm.TotalInflowsText.Should().NotBe("-");
        vm.TotalOutflowsText.Should().NotBe("-");
        vm.NetCashFlowText.Should().NotBe("-");
    }

    [Fact]
    public async Task Parameter_WhenSetToKnownRunId_ShouldPopulateLadderBuckets()
    {
        var (vm, runId) = CreateWithTradeRun();

        vm.Parameter = runId;
        await Task.Delay(100);

        vm.LadderBuckets.Should().NotBeEmpty("the projection service builds buckets from events");
    }

    [Fact]
    public async Task Parameter_WhenSetToKnownRunId_ShouldUpdateTitleWithRunIdPrefix()
    {
        var (vm, runId) = CreateWithTradeRun();

        vm.Parameter = runId;
        await Task.Delay(100);

        vm.Title.Should().Contain(runId[..8]);
    }

    [Fact]
    public async Task Parameter_WhenSetToKnownRunId_ShouldEnableNavigationCommands()
    {
        var (vm, runId) = CreateWithTradeRun();

        vm.Parameter = runId;
        await Task.Delay(100);

        vm.OpenRunDetailCommand.CanExecute(null).Should().BeTrue();
        vm.OpenPortfolioCommand.CanExecute(null).Should().BeTrue();
        vm.OpenLedgerCommand.CanExecute(null).Should().BeTrue();
    }

    // ── Entry ordering ────────────────────────────────────────────────────

    [Fact]
    public async Task Entries_ShouldBeOrderedChronologically()
    {
        var (vm, runId) = CreateWithTradeRun();

        vm.Parameter = runId;
        await Task.Delay(100);

        var timestamps = vm.Entries.Select(e => e.Timestamp).ToList();
        timestamps.Should().BeInAscendingOrder();
    }

    // ── BucketSummaryText ─────────────────────────────────────────────────

    [Fact]
    public async Task BucketSummaryText_WhenLoaded_ShouldDescribeBucketingParameters()
    {
        var (vm, runId) = CreateWithTradeRun();

        vm.Parameter = runId;
        await Task.Delay(100);

        vm.BucketSummaryText.Should().NotBe("-");
        vm.BucketSummaryText.Should().Contain("d"); // bucket day width label
    }
}
