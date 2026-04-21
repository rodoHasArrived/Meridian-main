#if WINDOWS
using System.Windows.Controls;
using FluentAssertions;
using Meridian.Wpf.Services;
using Meridian.Wpf.Tests.Support;
using Meridian.Wpf.ViewModels;

namespace Meridian.Wpf.Tests.ViewModels;

public sealed class StrategyRunLedgerViewModelTests
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
            await store.RecordRunAsync(StrategyRunWorkspaceTestData.BuildRun("run-ledger-security-id"));

            var lookup = StrategyRunWorkspaceTestData.CreateLookupWithApple();
            var workspaceService = new StrategyRunWorkspaceService(
                store,
                new Meridian.Strategies.Services.PortfolioReadService(lookup),
                new Meridian.Strategies.Services.LedgerReadService(lookup));
            var viewModel = new StrategyRunLedgerViewModel(workspaceService, navigation);

            viewModel.Parameter = "run-ledger-security-id";
            await Task.Delay(150);

            viewModel.SelectedTrialBalanceLine = viewModel.TrialBalance.Single(line => line.Symbol == "AAPL");

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
            await store.RecordRunAsync(StrategyRunWorkspaceTestData.BuildRun("run-ledger-symbol"));

            var lookup = StrategyRunWorkspaceTestData.CreateLookupWithApple();
            var workspaceService = new StrategyRunWorkspaceService(
                store,
                new Meridian.Strategies.Services.PortfolioReadService(lookup),
                new Meridian.Strategies.Services.LedgerReadService(lookup));
            var viewModel = new StrategyRunLedgerViewModel(workspaceService, navigation);

            viewModel.Parameter = "run-ledger-symbol";
            await Task.Delay(150);

            viewModel.SelectedTrialBalanceLine = viewModel.TrialBalance.Single(line => line.Symbol == "TSLA");
            viewModel.OpenSelectedSecurityCommand.Execute(null);

            navigation.GetCurrentPageTag().Should().Be("SecurityMaster");
            navigation.GetBreadcrumbs().First().Parameter.Should().Be("TSLA");
        });
    }
}
#endif
