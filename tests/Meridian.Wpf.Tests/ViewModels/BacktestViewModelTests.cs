using Meridian.Wpf.Contracts;
using Meridian.Wpf.Services;
using Meridian.Wpf.Tests.Support;
using Meridian.Wpf.ViewModels;

namespace Meridian.Wpf.Tests.ViewModels;

public sealed class BacktestViewModelTests
{
    [Fact]
    public void ActivationLifetime_DeactivateCancelsCoverageLifetimeWithoutClearingConfiguration()
    {
        WpfTestThread.Run(() =>
        {
            using var viewModel = CreateViewModel();
            viewModel.Should().BeAssignableTo<IPageActivationLifetime>();
            viewModel.SymbolsText = "SPY,AAPL";
            viewModel.DataRoot = "./data/test";
            using var activationCts = new CancellationTokenSource();
            activationCts.Cancel();

            viewModel.ActivateAsync(activationCts.Token).GetAwaiter().GetResult();
            var token = viewModel.ActivationToken;

            viewModel.IsActive.Should().BeTrue();
            token.CanBeCanceled.Should().BeTrue();

            viewModel.Deactivate();

            viewModel.IsActive.Should().BeFalse();
            token.IsCancellationRequested.Should().BeTrue();
            viewModel.SymbolsText.Should().Be("SPY,AAPL");
            viewModel.DataRoot.Should().Be("./data/test");
        });
    }

    [Fact]
    public void BacktestPageSource_UsesActivationLifetimeInsteadOfDisposingOnUnload()
    {
        var codeBehind = File.ReadAllText(RunMatUiAutomationFacade.GetRepoFilePath(@"src\Meridian.Wpf\Views\BacktestPage.xaml.cs"));
        var viewModel = File.ReadAllText(RunMatUiAutomationFacade.GetRepoFilePath(@"src\Meridian.Wpf\ViewModels\BacktestViewModel.cs"));

        codeBehind.Should().Contain("await _viewModel.ActivateAsync()");
        codeBehind.Should().Contain("_viewModel.Deactivate()");
        codeBehind.Should().NotContain("_viewModel.Dispose()");
        viewModel.Should().Contain("IPageActivationLifetime");
        viewModel.Should().Contain("public bool IsActive");
        viewModel.Should().Contain("public CancellationToken ActivationToken");
        viewModel.Should().Contain("_backtestService.BacktestCompleted += OnBacktestCompleted");
        viewModel.Should().Contain("_backtestService.BacktestCompleted -= OnBacktestCompleted");
    }

    private static BacktestViewModel CreateViewModel()
        => new(
            BacktestService.Instance,
            NavigationService.Instance,
            StrategyRunWorkspaceService.Instance,
            new BacktestDataAvailabilityService());
}
