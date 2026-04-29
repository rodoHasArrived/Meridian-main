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
        state.Detail.Should().Contain("local workstation host");
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
        state.Detail.Should().Contain("local workstation host");
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
    public void AggregatePortfolioPageSource_BindsPositionEmptyState()
    {
        var xaml = File.ReadAllText(RunMatUiAutomationFacade.GetRepoFilePath(@"src\Meridian.Wpf\Views\AggregatePortfolioPage.xaml"));

        xaml.Should().Contain("AggregatePositionsEmptyStatePanel");
        xaml.Should().Contain("AggregatePositionsEmptyStateTitle");
        xaml.Should().Contain("AggregatePositionsEmptyStateDetail");
        xaml.Should().Contain("AggregatePositionsEmptyStateRefreshButton");
        xaml.Should().Contain("{Binding IsPositionsGridVisible");
        xaml.Should().Contain("{Binding IsPositionsEmptyStateVisible");
        xaml.Should().Contain("{Binding PositionsEmptyStateTitle}");
        xaml.Should().Contain("{Binding PositionsEmptyStateDetail}");
        xaml.Should().Contain("Command=\"{Binding RefreshCommand}\"");
    }
}
