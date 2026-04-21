#if WINDOWS
using System.Windows.Controls;
using FluentAssertions;
using Meridian.Wpf.Services;
using Meridian.Wpf.Tests.Support;
using Meridian.Wpf.ViewModels;

namespace Meridian.Wpf.Tests.ViewModels;

public sealed class StrategyRunPortfolioViewModelTests
{
    [Fact]
    public void OpenSelectedSecurityCommand_NavigatesToSecurityMasterWithResolvedSecurityId()
    {
        WpfTestThread.Run(async () =>
        {
            var navigation = NavigationService.Instance;
            navigation.ResetForTests();
            navigation.Initialize(new Frame());

            var store = new Meridian.Strategies.Storage.StrategyRunStore();
            await store.RecordRunAsync(StrategyRunWorkspaceTestData.BuildRun("run-portfolio-security-id"));

            var lookup = StrategyRunWorkspaceTestData.CreateLookupWithApple();
            var workspaceService = new StrategyRunWorkspaceService(
                store,
                new Meridian.Strategies.Services.PortfolioReadService(lookup),
                new Meridian.Strategies.Services.LedgerReadService(lookup));
            var viewModel = new StrategyRunPortfolioViewModel(workspaceService, navigation);

            viewModel.Parameter = "run-portfolio-security-id";
            await Task.Delay(150);

            viewModel.SelectedPosition.Should().NotBeNull();
            viewModel.SelectedPosition!.Symbol.Should().Be("AAPL");

            viewModel.OpenSelectedSecurityCommand.Execute(null);

            navigation.GetCurrentPageTag().Should().Be("SecurityMaster");
            navigation.GetBreadcrumbs().First().Parameter.Should().Be(Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"));
        });
    }

    [Fact]
    public void OpenSelectedSecurityCommand_FallsBackToSymbolWhenCoverageIsMissing()
    {
        WpfTestThread.Run(async () =>
        {
            var navigation = NavigationService.Instance;
            navigation.ResetForTests();
            navigation.Initialize(new Frame());

            var store = new Meridian.Strategies.Storage.StrategyRunStore();
            await store.RecordRunAsync(StrategyRunWorkspaceTestData.BuildRun("run-portfolio-symbol"));

            var lookup = StrategyRunWorkspaceTestData.CreateLookupWithApple();
            var workspaceService = new StrategyRunWorkspaceService(
                store,
                new Meridian.Strategies.Services.PortfolioReadService(lookup),
                new Meridian.Strategies.Services.LedgerReadService(lookup));
            var viewModel = new StrategyRunPortfolioViewModel(workspaceService, navigation);

            viewModel.Parameter = "run-portfolio-symbol";
            await Task.Delay(150);

            viewModel.SelectedPosition = viewModel.Positions.Single(position => position.Symbol == "TSLA");
            viewModel.OpenSelectedSecurityCommand.Execute(null);

            navigation.GetCurrentPageTag().Should().Be("SecurityMaster");
            navigation.GetBreadcrumbs().First().Parameter.Should().Be("TSLA");
        });
    }
}
#endif
