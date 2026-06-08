#if WINDOWS
using FluentAssertions;
using Meridian.Strategies.Services;
using Meridian.Strategies.Storage;
using Meridian.Wpf.Services;
using Meridian.Wpf.Tests.Support;
using Meridian.Wpf.ViewModels;

namespace Meridian.Wpf.Tests.ViewModels;

public sealed class StrategyRunDetailViewModelTests
{
    [Fact]
    public void InitialState_ShowsRunSelectionGuidanceAndDisablesDrillIns()
    {
        var viewModel = CreateEmptyViewModel();

        viewModel.Title.Should().Be("Strategy Run Detail");
        viewModel.StatusText.Should().Contain("Select a strategy run");
        viewModel.CanOpenRunDrillIns.Should().BeFalse();
        viewModel.RunActionStateTitle.Should().Be("Select a retained run");
        viewModel.RunActionStateDetail.Should().Contain("unlock after selecting");
        viewModel.RunDrillInTooltip.Should().Contain("Select a retained strategy run");
        viewModel.RunSummaryInspector.Title.Should().Be("No run selected");
        viewModel.ParametersTable.Title.Should().Be("Run parameters");
        viewModel.Parameters.Should().BeEmpty();
        viewModel.SelectedParameter.Should().BeNull();
        viewModel.SelectedParameterInspector.Title.Should().Be("No parameter selected");
        viewModel.OpenPortfolioCommand.CanExecute(null).Should().BeFalse();
        viewModel.OpenLedgerCommand.CanExecute(null).Should().BeFalse();
        viewModel.OpenRiskCommand.CanExecute(null).Should().BeFalse();
        viewModel.OpenCashFlowCommand.CanExecute(null).Should().BeFalse();
    }

    [Fact]
    public async Task LoadRunAsync_WhenRunIsMissing_ShowsUnavailableGuidanceAndKeepsDrillInsDisabled()
    {
        var viewModel = CreateEmptyViewModel();

        await viewModel.LoadRunAsync("missing-run-detail");

        viewModel.StatusText.Should().Contain("missing-run-detail");
        viewModel.CanOpenRunDrillIns.Should().BeFalse();
        viewModel.RunActionStateTitle.Should().Be("Run evidence unavailable");
        viewModel.RunSummaryInspector.Title.Should().Be("Run unavailable");
        viewModel.Parameters.Should().BeEmpty();
        viewModel.SelectedParameter.Should().BeNull();
        viewModel.OpenPortfolioCommand.CanExecute(null).Should().BeFalse();
        viewModel.OpenLedgerCommand.CanExecute(null).Should().BeFalse();
        viewModel.OpenRiskCommand.CanExecute(null).Should().BeFalse();
        viewModel.OpenCashFlowCommand.CanExecute(null).Should().BeFalse();
    }

    [Fact]
    public async Task LoadRunAsync_WhenRunHasParameters_SelectsFirstParameterAndEnablesDrillIns()
    {
        var (viewModel, store) = CreateViewModelWithStore();
        var run = StrategyRunWorkspaceTestData.BuildRun("run-detail-with-parameters") with
        {
            ParameterSet = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["lookbackDays"] = "21",
                ["rebalance"] = "weekly"
            }
        };
        await store.RecordRunAsync(run);

        await viewModel.LoadRunAsync("run-detail-with-parameters");

        viewModel.Title.Should().Contain("Reconciliation Strategy");
        viewModel.CanOpenRunDrillIns.Should().BeTrue();
        viewModel.RunActionStateTitle.Should().Be("Run drill-ins ready");
        viewModel.RunActionStateDetail.Should().Contain("run-detail-with-parameters");
        viewModel.RunSummaryInspector.Title.Should().Be("run-detail-with-parameters");
        viewModel.RunSummaryInspector.Facts.Should().Contain(fact => fact.Label == "Portfolio");
        viewModel.Parameters.Should().HaveCount(2);
        viewModel.SelectedParameter.Should().NotBeNull();
        viewModel.SelectedParameter!.Name.Should().Be("lookbackDays");
        viewModel.SelectedParameterInspector.Title.Should().Be("lookbackDays");
        viewModel.SelectedParameterInspector.Facts.Should().Contain(fact => fact.Label == "Value" && fact.Value == "21");
        viewModel.OpenPortfolioCommand.CanExecute(null).Should().BeTrue();
        viewModel.OpenLedgerCommand.CanExecute(null).Should().BeTrue();
        viewModel.OpenRiskCommand.CanExecute(null).Should().BeTrue();
        viewModel.OpenCashFlowCommand.CanExecute(null).Should().BeTrue();
    }

    [Fact]
    public void RunDetailPageSource_UsesCompactParameterWorkbenchChrome()
    {
        var xaml = File.ReadAllText(GetRepositoryFilePath(@"src\Meridian.Wpf\Views\RunDetailPage.xaml"));
        var viewModel = File.ReadAllText(GetRepositoryFilePath(@"src\Meridian.Wpf\ViewModels\StrategyRunDetailViewModel.cs"));

        xaml.Should().NotContain("EmbeddedShellHeroCardStyle");
        xaml.Should().NotContain("<ItemsControl ItemsSource=\"{Binding Parameters}\"");
        xaml.Should().Contain("WorkspaceContextStripStyle");
        xaml.Should().Contain("RunDetailActionStrip");
        xaml.Should().Contain("RunDetailWorkbench");
        xaml.Should().Contain("workstation:DenseDataGridControl");
        xaml.Should().Contain("workstation:InspectorPanelControl");
        xaml.Should().Contain("RunDetailParametersGrid");
        xaml.Should().Contain("RunDetailSummaryInspector");
        xaml.Should().Contain("RunDetailSelectedParameterInspector");
        xaml.Should().Contain("RunDetailActionInspector");
        xaml.Should().Contain("ToolTipService.ShowOnDisabled=\"True\"");
        xaml.Should().Contain("Table=\"{Binding ParametersTable}\"");
        xaml.Should().Contain("SelectedItem=\"{Binding SelectedParameter, Mode=TwoWay}\"");
        viewModel.Should().Contain("public WorkstationTableModel<ParameterItemViewModel> ParametersTable");
        viewModel.Should().Contain("public InspectorPanelModel RunSummaryInspector");
        viewModel.Should().Contain("public InspectorPanelModel SelectedParameterInspector");
        viewModel.Should().Contain("public bool CanOpenRunDrillIns");
    }

    private static StrategyRunDetailViewModel CreateEmptyViewModel()
        => CreateViewModelWithStore().ViewModel;

    private static (StrategyRunDetailViewModel ViewModel, StrategyRunStore Store) CreateViewModelWithStore()
    {
        var store = new StrategyRunStore();
        var portfolioReadService = new PortfolioReadService();
        var ledgerReadService = new LedgerReadService();
        var readService = new StrategyRunReadService(store, portfolioReadService, ledgerReadService);
        var workspaceService = new StrategyRunWorkspaceService(store, readService);
        return (new StrategyRunDetailViewModel(workspaceService, NavigationService.Instance), store);
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
