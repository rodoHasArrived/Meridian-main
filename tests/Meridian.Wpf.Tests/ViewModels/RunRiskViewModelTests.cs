#if WINDOWS
using FluentAssertions;
using Meridian.Strategies.Services;
using Meridian.Strategies.Storage;
using Meridian.Wpf.Services;
using Meridian.Wpf.Tests.Support;
using Meridian.Wpf.ViewModels;

namespace Meridian.Wpf.Tests.ViewModels;

public sealed class RunRiskViewModelTests
{
    [Fact]
    public void InitialState_ShowsRunSelectionGuidanceAndDisablesDrillIns()
    {
        var viewModel = CreateEmptyViewModel();

        viewModel.Title.Should().Be("Portfolio Risk");
        viewModel.StatusText.Should().Contain("Select a strategy run");
        viewModel.HasRiskChart.Should().BeFalse();
        viewModel.IsRiskEmptyStateVisible.Should().BeTrue();
        viewModel.RiskEmptyStateTitle.Should().Be("Select a run to calculate historical risk");
        viewModel.HasAttributionRows.Should().BeFalse();
        viewModel.IsAttributionEmptyStateVisible.Should().BeTrue();
        viewModel.AttributionEmptyStateTitle.Should().Be("Select a run to inspect attribution");
        viewModel.CanOpenRunDrillIns.Should().BeFalse();
        viewModel.RunActionStateTitle.Should().Be("Select a retained run");
        viewModel.RunActionStateDetail.Should().Contain("unlock after selecting");
        viewModel.OpenRunDetailCommand.CanExecute(null).Should().BeFalse();
        viewModel.OpenPortfolioCommand.CanExecute(null).Should().BeFalse();
        viewModel.OpenCashFlowCommand.CanExecute(null).Should().BeFalse();
    }

    [Fact]
    public async Task LoadRunAsync_WhenRunIsMissing_ShowsUnavailableGuidanceAndKeepsDrillInsDisabled()
    {
        var viewModel = CreateEmptyViewModel();

        await viewModel.LoadRunAsync("missing-risk-run");

        viewModel.StatusText.Should().Contain("missing-risk-run");
        viewModel.PositionCountText.Should().Be("-");
        viewModel.TotalRiskText.Should().Be("-");
        viewModel.HasRiskChart.Should().BeFalse();
        viewModel.RiskEmptyStateTitle.Should().Be("Risk data unavailable");
        viewModel.RiskEmptyStateDetail.Should().Contain("Return to the run browser");
        viewModel.HasAttributionRows.Should().BeFalse();
        viewModel.AttributionEmptyStateTitle.Should().Be("Attribution unavailable");
        viewModel.CanOpenRunDrillIns.Should().BeFalse();
        viewModel.RunActionStateTitle.Should().Be("Run evidence unavailable");
        viewModel.RunActionStateDetail.Should().Contain("select a retained run");
        viewModel.OpenRunDetailCommand.CanExecute(null).Should().BeFalse();
        viewModel.OpenPortfolioCommand.CanExecute(null).Should().BeFalse();
        viewModel.OpenCashFlowCommand.CanExecute(null).Should().BeFalse();
    }

    [Fact]
    public async Task LoadRunAsync_WhenRunHasInsufficientRiskHistory_ShowsChartAndAttributionEmptyStates()
    {
        var (viewModel, store) = CreateViewModelWithStore();
        await store.RecordRunAsync(StrategyRunWorkspaceTestData.BuildRun("run-risk-short-history"));

        await viewModel.LoadRunAsync("run-risk-short-history");

        viewModel.Title.Should().Contain("Risk Analytics");
        viewModel.CanOpenRunDrillIns.Should().BeTrue();
        viewModel.RunActionStateTitle.Should().Be("Run drill-ins ready");
        viewModel.RunActionStateDetail.Should().Contain("run-risk-short-history");
        viewModel.OpenRunDetailCommand.CanExecute(null).Should().BeTrue();
        viewModel.OpenPortfolioCommand.CanExecute(null).Should().BeTrue();
        viewModel.OpenCashFlowCommand.CanExecute(null).Should().BeTrue();
        viewModel.PositionCountText.Should().Be("2");
        viewModel.HasRiskChart.Should().BeFalse();
        viewModel.IsRiskEmptyStateVisible.Should().BeTrue();
        viewModel.RiskEmptyStateTitle.Should().Be("Rolling volatility needs more history");
        viewModel.RiskEmptyStateDetail.Should().Contain("at least 21 observations");
        viewModel.HasAttributionRows.Should().BeFalse();
        viewModel.IsAttributionEmptyStateVisible.Should().BeTrue();
        viewModel.AttributionEmptyStateTitle.Should().Be("No symbol attribution retained");
    }

    [Fact]
    public void RunRiskPageSource_BindsRiskAndAttributionEmptyStates()
    {
        var xaml = File.ReadAllText(GetRepositoryFilePath(@"src\Meridian.Wpf\Views\RunRiskPage.xaml"));
        var viewModel = File.ReadAllText(GetRepositoryFilePath(@"src\Meridian.Wpf\ViewModels\RunRiskViewModel.cs"));

        xaml.Should().Contain("RunRiskVolatilityChart");
        xaml.Should().Contain("RunRiskVolatilityEmptyState");
        xaml.Should().Contain("RunRiskAttributionGrid");
        xaml.Should().Contain("RunRiskAttributionEmptyState");
        xaml.Should().Contain("RunRiskActionStatePanel");
        xaml.Should().Contain("RunRiskLoadProgress");
        xaml.Should().Contain("{Binding RunActionStateTitle}");
        xaml.Should().Contain("{Binding RunActionStateDetail}");
        xaml.Should().Contain("{Binding IsLoading, Converter={StaticResource BoolToVisibilityConverter}}");
        xaml.Should().Contain("{Binding HasRiskChart, Converter={StaticResource BoolToVisibilityConverter}}");
        xaml.Should().Contain("{Binding IsRiskEmptyStateVisible, Converter={StaticResource BoolToVisibilityConverter}}");
        xaml.Should().Contain("{Binding HasAttributionRows, Converter={StaticResource BoolToVisibilityConverter}}");
        xaml.Should().Contain("{Binding IsAttributionEmptyStateVisible, Converter={StaticResource BoolToVisibilityConverter}}");
        xaml.Should().Contain("{Binding RiskEmptyStateTitle}");
        xaml.Should().Contain("{Binding AttributionEmptyStateDetail}");
        viewModel.Should().Contain("public bool HasRiskChart");
        viewModel.Should().Contain("public bool CanOpenRunDrillIns");
        viewModel.Should().Contain("private void ResetAnalysisState()");
    }

    private static RunRiskViewModel CreateEmptyViewModel()
        => CreateViewModelWithStore().ViewModel;

    private static (RunRiskViewModel ViewModel, StrategyRunStore Store) CreateViewModelWithStore()
    {
        var store = new StrategyRunStore();
        var portfolioReadService = new PortfolioReadService();
        var ledgerReadService = new LedgerReadService();
        var readService = new StrategyRunReadService(store, portfolioReadService, ledgerReadService);
        var workspaceService = new StrategyRunWorkspaceService(store, readService);
        return (new RunRiskViewModel(workspaceService, readService, NavigationService.Instance), store);
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
#endif
