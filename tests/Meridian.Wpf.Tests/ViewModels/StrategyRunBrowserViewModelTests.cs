using System.IO;
using Meridian.Contracts.Workstation;
using Meridian.Strategies.Models;
using Meridian.Strategies.Storage;
using Meridian.Strategies.Services;
using Meridian.Wpf.Services;
using Meridian.Wpf.ViewModels;

namespace Meridian.Wpf.Tests.ViewModels;

public sealed class StrategyRunBrowserViewModelTests
{
    // ── Helpers ───────────────────────────────────────────────────────────

    private static StrategyRunBrowserViewModel CreateEmpty()
    {
        var runService = new StrategyRunWorkspaceService(
            new StrategyRunStore(),
            new PortfolioReadService(),
            new LedgerReadService());
        return new StrategyRunBrowserViewModel(
            runService,
            NavigationService.Instance,
            WorkspaceService.Instance);
    }

    private static StrategyRunEntry MakeEntry(
        string strategyName,
        RunType runType = RunType.Backtest)
        => StrategyRunEntry.Start(
            strategyId: strategyName.ToLowerInvariant().Replace(" ", "-"),
            strategyName: strategyName,
            runType: runType);

    // ── Static configuration tests ────────────────────────────────────────

    [Fact]
    public void ModeFilters_ShouldContainAllExpectedValues()
    {
        var vm = CreateEmpty();

        vm.ModeFilters.Should().BeEquivalentTo(
            new[] { "All", "Backtest", "Paper", "Live" },
            options => options.WithStrictOrdering());
    }

    [Fact]
    public void InitialState_ShouldHaveDefaultModeFilterAndNoRuns()
    {
        var vm = CreateEmpty();

        vm.SelectedModeFilter.Should().Be("All");
        vm.Runs.Should().BeEmpty();
        vm.CanOpenSelectedRun.Should().BeFalse();
    }

    // ── Filter / search tests ─────────────────────────────────────────────

    [Fact]
    public async Task RefreshAsync_WithNoRuns_ShouldShowEmptyStatusMessage()
    {
        var vm = CreateEmpty();

        await vm.RefreshAsync();

        vm.Runs.Should().BeEmpty();
        vm.StatusText.Should().Contain("No recorded strategy runs");
    }

    [Fact]
    public async Task RefreshAsync_WithRuns_ShouldShowCountInStatusText()
    {
        var store = new StrategyRunStore();
        await store.RecordRunAsync(MakeEntry("Alpha Strategy"));
        await store.RecordRunAsync(MakeEntry("Beta Strategy"));

        var runService = new StrategyRunWorkspaceService(store, new PortfolioReadService(), new LedgerReadService());
        var vm = new StrategyRunBrowserViewModel(runService, NavigationService.Instance, WorkspaceService.Instance);

        await vm.RefreshAsync();

        vm.Runs.Should().HaveCount(2);
        vm.StatusText.Should().Contain("2 strategy runs");
    }

    [Fact]
    public async Task ApplyFilters_WithModeFilter_ShouldOnlyShowMatchingRuns()
    {
        var store = new StrategyRunStore();
        await store.RecordRunAsync(MakeEntry("Buy Hold", RunType.Backtest));
        await store.RecordRunAsync(MakeEntry("Paper Run", RunType.Paper));

        var runService = new StrategyRunWorkspaceService(store, new PortfolioReadService(), new LedgerReadService());
        var vm = new StrategyRunBrowserViewModel(runService, NavigationService.Instance, WorkspaceService.Instance);

        await vm.RefreshAsync();
        vm.SelectedModeFilter = "Backtest";

        vm.Runs.Should().ContainSingle(r => r.StrategyName == "Buy Hold");
        vm.Runs.Should().NotContain(r => r.StrategyName == "Paper Run");
    }

    [Fact]
    public async Task ApplyFilters_WithSearchText_ShouldFilterByStrategyName()
    {
        var store = new StrategyRunStore();
        await store.RecordRunAsync(MakeEntry("Momentum Strategy"));
        await store.RecordRunAsync(MakeEntry("Mean Reversion"));

        var runService = new StrategyRunWorkspaceService(store, new PortfolioReadService(), new LedgerReadService());
        var vm = new StrategyRunBrowserViewModel(runService, NavigationService.Instance, WorkspaceService.Instance);

        await vm.RefreshAsync();
        vm.SearchText = "Momentum";

        vm.Runs.Should().ContainSingle(r => r.StrategyName == "Momentum Strategy");
        vm.Runs.Should().NotContain(r => r.StrategyName == "Mean Reversion");
    }

    [Fact]
    public async Task ApplyFilters_WhenSearchCleared_ShouldShowAllRuns()
    {
        var store = new StrategyRunStore();
        await store.RecordRunAsync(MakeEntry("Alpha"));
        await store.RecordRunAsync(MakeEntry("Beta"));

        var runService = new StrategyRunWorkspaceService(store, new PortfolioReadService(), new LedgerReadService());
        var vm = new StrategyRunBrowserViewModel(runService, NavigationService.Instance, WorkspaceService.Instance);

        await vm.RefreshAsync();
        vm.SearchText = "Alpha";
        vm.Runs.Should().HaveCount(1);

        vm.SearchText = string.Empty;
        vm.Runs.Should().HaveCount(2);
    }

    [Fact]
    public async Task ApplyFilters_NoMatchOnModeFilter_ShouldShowFilteredStatusMessage()
    {
        var store = new StrategyRunStore();
        await store.RecordRunAsync(MakeEntry("Backtest Only", RunType.Backtest));

        var runService = new StrategyRunWorkspaceService(store, new PortfolioReadService(), new LedgerReadService());
        var vm = new StrategyRunBrowserViewModel(runService, NavigationService.Instance, WorkspaceService.Instance);

        await vm.RefreshAsync();
        vm.SelectedModeFilter = "Live";

        vm.Runs.Should().BeEmpty();
        vm.StatusText.Should().Contain("No strategy runs match");
    }

    // ── CanExecute / selection tests ──────────────────────────────────────

    [Fact]
    public void CanOpenSelectedRun_WhenSelectedRunIsNull_ShouldBeFalse()
    {
        var vm = CreateEmpty();

        vm.SelectedRun = null;

        vm.CanOpenSelectedRun.Should().BeFalse();
        vm.OpenDetailCommand.CanExecute(null).Should().BeFalse();
        vm.OpenPortfolioCommand.CanExecute(null).Should().BeFalse();
        vm.OpenLedgerCommand.CanExecute(null).Should().BeFalse();
    }

    [Fact]
    public async Task CanOpenSelectedRun_WhenRunIsSelected_ShouldBeTrue()
    {
        var store = new StrategyRunStore();
        await store.RecordRunAsync(MakeEntry("My Strategy"));

        var runService = new StrategyRunWorkspaceService(store, new PortfolioReadService(), new LedgerReadService());
        var vm = new StrategyRunBrowserViewModel(runService, NavigationService.Instance, WorkspaceService.Instance);

        await vm.RefreshAsync();

        vm.SelectedRun.Should().NotBeNull();
        vm.CanOpenSelectedRun.Should().BeTrue();
        vm.OpenDetailCommand.CanExecute(null).Should().BeTrue();
    }

    [Fact]
    public void CanOpenSelectedRun_WhenSingleRunSelected_CommandsReflectCanExecute()
    {
        var vm = CreateEmpty();
        var summary = new Meridian.Contracts.Workstation.StrategyRunSummary(
            RunId: "test-run-1",
            StrategyId: "strat-1",
            StrategyName: "Test Strat",
            Mode: StrategyRunMode.Backtest,
            Engine: StrategyRunEngine.MeridianNative,
            Status: StrategyRunStatus.Completed,
            StartedAt: DateTimeOffset.UtcNow.AddHours(-1),
            CompletedAt: DateTimeOffset.UtcNow,
            DatasetReference: null,
            FeedReference: null,
            PortfolioId: null,
            LedgerReference: null,
            NetPnl: null,
            TotalReturn: null,
            FinalEquity: null,
            FillCount: 0,
            LastUpdatedAt: DateTimeOffset.UtcNow);

        vm.SelectedRun = summary;

        vm.CanOpenSelectedRun.Should().BeTrue();
        vm.OpenDetailCommand.CanExecute(null).Should().BeTrue();
        vm.OpenPortfolioCommand.CanExecute(null).Should().BeTrue();
        vm.OpenLedgerCommand.CanExecute(null).Should().BeTrue();
    }
}
