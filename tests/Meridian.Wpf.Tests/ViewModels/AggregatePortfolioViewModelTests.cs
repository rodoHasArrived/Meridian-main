using Meridian.Wpf.Tests.Support;
using Meridian.Wpf.ViewModels;

namespace Meridian.Wpf.Tests.ViewModels;

public sealed class AggregatePortfolioViewModelTests
{
    [Fact]
    public void BuildPositionsEmptyState_WhenRefreshing_ShowsLoadingGuidance()
    {
        var state = AggregatePortfolioViewModel.BuildPositionsEmptyState(
            isRefreshing: true,
            hasLoadedPortfolioSnapshot: false,
            hasLoadError: false,
            positionCount: 0);

        state.IsVisible.Should().BeTrue();
        state.Title.Should().Be("Loading aggregate portfolio");
        state.Detail.Should().Contain("cross-strategy exposure");
    }

    [Fact]
    public void BuildPositionsEmptyState_WhenSnapshotUnavailable_ShowsRecoveryGuidance()
    {
        var state = AggregatePortfolioViewModel.BuildPositionsEmptyState(
            isRefreshing: false,
            hasLoadedPortfolioSnapshot: false,
            hasLoadError: true,
            positionCount: 0);

        state.IsVisible.Should().BeTrue();
        state.Title.Should().Be("Aggregate portfolio unavailable");
        state.Detail.Should().Contain("local workstation service");
    }

    [Fact]
    public void BuildPositionsEmptyState_BeforeFirstLoad_ShowsPendingSnapshotGuidance()
    {
        var state = AggregatePortfolioViewModel.BuildPositionsEmptyState(
            isRefreshing: false,
            hasLoadedPortfolioSnapshot: false,
            hasLoadError: false,
            positionCount: 0);

        state.IsVisible.Should().BeTrue();
        state.Title.Should().Be("Waiting for aggregate portfolio");
        state.Detail.Should().Contain("local workstation service");
    }

    [Fact]
    public void BuildPositionsEmptyState_WhenSnapshotLoadedWithoutRows_ShowsNoPositionsGuidance()
    {
        var state = AggregatePortfolioViewModel.BuildPositionsEmptyState(
            isRefreshing: false,
            hasLoadedPortfolioSnapshot: true,
            hasLoadError: false,
            positionCount: 0);

        state.IsVisible.Should().BeTrue();
        state.Title.Should().Be("No netted positions yet");
        state.Detail.Should().Contain("cross-strategy position rows");
    }

    [Fact]
    public void BuildPositionsEmptyState_WhenPositionsExist_HidesEmptyState()
    {
        var state = AggregatePortfolioViewModel.BuildPositionsEmptyState(
            isRefreshing: false,
            hasLoadedPortfolioSnapshot: true,
            hasLoadError: false,
            positionCount: 4);

        state.IsVisible.Should().BeFalse();
        state.Title.Should().BeEmpty();
        state.Detail.Should().BeEmpty();
    }

    [Fact]
    public void AggregatePortfolioPageSource_UsesDenseTableInspectorAndSelectionAwareActions()
    {
        var xaml = File.ReadAllText(RunMatUiAutomationFacade.GetRepoFilePath(@"src\Meridian.Wpf\Views\AggregatePortfolioPage.xaml"));
        var viewModel = File.ReadAllText(RunMatUiAutomationFacade.GetRepoFilePath(@"src\Meridian.Wpf\ViewModels\AggregatePortfolioViewModel.cs"));

        xaml.Should().NotContain("<DataGrid");
        xaml.Should().Contain("AggregatePortfolioActionStrip");
        xaml.Should().Contain("AggregatePortfolioWorkbench");
        xaml.Should().Contain("WorkstationTableInspectorControl");
        xaml.Should().Contain("TableHeaderContent");
        xaml.Should().Contain("EmptyContent");
        xaml.Should().Contain("AggregatePortfolioPositionsGrid");
        xaml.Should().Contain("AggregatePortfolioSelectionInspector");
        xaml.Should().Contain("AggregatePortfolioActionInspector");
        xaml.Should().Contain("AggregatePortfolioConcentrationStrip");
        xaml.Should().Contain("AggregatePositionsEmptyStatePanel");
        xaml.Should().Contain("AggregatePositionsEmptyStateTitle");
        xaml.Should().Contain("AggregatePositionsEmptyStateDetail");
        xaml.Should().Contain("AggregatePositionsEmptyStateRefreshButton");
        xaml.Should().Contain("Table=\"{Binding PositionsTable}\"");
        xaml.Should().Contain("SelectedItem=\"{Binding SelectedPosition, Mode=TwoWay}\"");
        xaml.Should().Contain("Inspector=\"{Binding SelectedPositionInspector}\"");
        xaml.Should().Contain("{Binding IsPositionsEmptyStateVisible");
        xaml.Should().Contain("{Binding PositionsEmptyStateTitle}");
        xaml.Should().Contain("{Binding PositionsEmptyStateDetail}");
        xaml.Should().Contain("Command=\"{Binding RefreshCommand}\"");
        xaml.Should().Contain("ToolTip=\"{Binding RefreshTooltip}\"");
        xaml.Should().Contain("ToolTip=\"{Binding SelectedSecurityTooltip}\"");
        xaml.Should().Contain("ToolTipService.ShowOnDisabled=\"True\"");
        viewModel.Should().Contain("public WorkstationTableModel<AggregatedPositionRow> PositionsTable { get; }");
        viewModel.Should().Contain("public InspectorPanelModel SelectedPositionInspector");
        viewModel.Should().Contain("public bool CanOpenSelectedSecurity");
    }
}
