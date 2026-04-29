using Meridian.Wpf.Tests.Support;
using Meridian.Wpf.ViewModels;

namespace Meridian.Wpf.Tests.ViewModels;

public sealed class AccountPortfolioViewModelTests
{
    [Fact]
    public void BuildPositionsEmptyState_WithoutAccountContext_AsksForAccountSelection()
    {
        var state = AccountPortfolioViewModel.BuildPositionsEmptyState(
            isRefreshing: false,
            hasLoadedAccountSnapshot: false,
            hasAccountContext: false,
            positionCount: 0);

        state.IsVisible.Should().BeTrue();
        state.Title.Should().Be("Select an account to review positions");
        state.Detail.Should().Contain("Fund Accounts");
    }

    [Fact]
    public void BuildPositionsEmptyState_WhenRefreshingAccount_ShowsLoadingGuidance()
    {
        var state = AccountPortfolioViewModel.BuildPositionsEmptyState(
            isRefreshing: true,
            hasLoadedAccountSnapshot: false,
            hasAccountContext: true,
            positionCount: 0);

        state.IsVisible.Should().BeTrue();
        state.Title.Should().Be("Loading account positions");
        state.Detail.Should().Contain("latest cash, exposure, and position snapshot");
    }

    [Fact]
    public void BuildPositionsEmptyState_WhenSnapshotUnavailable_ShowsRecoveryGuidance()
    {
        var state = AccountPortfolioViewModel.BuildPositionsEmptyState(
            isRefreshing: false,
            hasLoadedAccountSnapshot: false,
            hasAccountContext: true,
            positionCount: 0);

        state.IsVisible.Should().BeTrue();
        state.Title.Should().Be("Account snapshot unavailable");
        state.Detail.Should().Contain("brokerage sync");
    }

    [Fact]
    public void BuildPositionsEmptyState_WhenSnapshotLoadedWithoutRows_ShowsNoPositionsGuidance()
    {
        var state = AccountPortfolioViewModel.BuildPositionsEmptyState(
            isRefreshing: false,
            hasLoadedAccountSnapshot: true,
            hasAccountContext: true,
            positionCount: 0);

        state.IsVisible.Should().BeTrue();
        state.Title.Should().Be("No open positions in this account");
        state.Detail.Should().Contain("New fills");
    }

    [Fact]
    public void BuildPositionsEmptyState_WhenPositionsExist_HidesEmptyState()
    {
        var state = AccountPortfolioViewModel.BuildPositionsEmptyState(
            isRefreshing: false,
            hasLoadedAccountSnapshot: true,
            hasAccountContext: true,
            positionCount: 3);

        state.IsVisible.Should().BeFalse();
        state.Title.Should().BeEmpty();
        state.Detail.Should().BeEmpty();
    }

    [Theory]
    [InlineData(null, false, false)]
    [InlineData("", false, false)]
    [InlineData("   ", false, false)]
    [InlineData("account-1", true, false)]
    [InlineData("account-1", false, true)]
    public void CanRefreshAccountForState_RequiresAccountAndIdleRefresh(string? accountId, bool isRefreshing, bool expected)
    {
        AccountPortfolioViewModel.CanRefreshAccountForState(accountId, isRefreshing)
            .Should()
            .Be(expected);
    }

    [Fact]
    public void AccountPortfolioPageSource_BindsPositionEmptyState()
    {
        var xaml = File.ReadAllText(RunMatUiAutomationFacade.GetRepoFilePath(@"src\Meridian.Wpf\Views\AccountPortfolioPage.xaml"));

        xaml.Should().Contain("AccountPositionsEmptyStatePanel");
        xaml.Should().Contain("AccountPositionsEmptyStateTitle");
        xaml.Should().Contain("AccountPositionsEmptyStateDetail");
        xaml.Should().Contain("AccountPositionsEmptyStateRefreshButton");
        xaml.Should().Contain("{Binding IsPositionsGridVisible");
        xaml.Should().Contain("{Binding IsPositionsEmptyStateVisible");
        xaml.Should().Contain("{Binding PositionsEmptyStateTitle}");
        xaml.Should().Contain("{Binding PositionsEmptyStateDetail}");
        xaml.Should().Contain("{Binding RefreshCommand}");
        xaml.Should().Contain("{Binding CanRefreshAccount}");
    }
}
