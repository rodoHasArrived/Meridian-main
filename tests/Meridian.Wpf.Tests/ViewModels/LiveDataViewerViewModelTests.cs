using Meridian.Ui.Services;
using Meridian.Wpf.Contracts;
using Meridian.Wpf.Models;
using Meridian.Wpf.Tests.Support;
using Meridian.Wpf.ViewModels;
using WpfServices = Meridian.Wpf.Services;

namespace Meridian.Wpf.Tests.ViewModels;

public sealed class LiveDataViewerViewModelTests
{
    [Fact]
    public void ActivationLifetime_DeactivateCancelsPollingLifetimeWithoutClearingLoadedState()
    {
        WpfTestThread.Run(() =>
        {
            using var viewModel = CreateViewModel();
            viewModel.Should().BeAssignableTo<IPageActivationLifetime>();
            viewModel.AddSymbolToList("SPY");
            viewModel.SelectSymbol("SPY");
            viewModel.LiveEvents.Add(new LiveDataEventModel
            {
                Id = "evt-1",
                Symbol = "SPY",
                Type = "TRD",
                Price = "500.00",
                Size = "100"
            });
            using var activationCts = new CancellationTokenSource();
            activationCts.Cancel();

            viewModel.ActivateAsync(activationCts.Token).GetAwaiter().GetResult();
            var token = viewModel.ActivationToken;

            viewModel.IsActive.Should().BeTrue();
            token.CanBeCanceled.Should().BeTrue();

            viewModel.Deactivate();

            viewModel.IsActive.Should().BeFalse();
            token.IsCancellationRequested.Should().BeTrue();
            viewModel.SelectedSymbol.Should().Be("SPY");
            viewModel.AvailableSymbols.Should().ContainSingle(symbol => symbol == "SPY");
            viewModel.LiveEvents.Should().ContainSingle(evt => evt.Id == "evt-1");
        });
    }

    [Fact]
    public void LiveDataViewerPageSource_UsesActivationLifetimeInsteadOfDisposingOnUnload()
    {
        var codeBehind = File.ReadAllText(RunMatUiAutomationFacade.GetRepoFilePath(@"src\Meridian.Wpf\Views\LiveDataViewerPage.xaml.cs"));
        var viewModel = File.ReadAllText(RunMatUiAutomationFacade.GetRepoFilePath(@"src\Meridian.Wpf\ViewModels\LiveDataViewerViewModel.cs"));

        codeBehind.Should().Contain("await _vm.ActivateAsync()");
        codeBehind.Should().Contain("_vm.Deactivate()");
        codeBehind.Should().NotContain("_vm.Dispose()");
        viewModel.Should().Contain("IPageActivationLifetime");
        viewModel.Should().Contain("public bool IsActive");
        viewModel.Should().Contain("public CancellationToken ActivationToken");
    }

    private static LiveDataViewerViewModel CreateViewModel()
        => new(
            WpfServices.StatusService.Instance,
            WpfServices.ConnectionService.Instance,
            WpfServices.LoggingService.Instance,
            WpfServices.NotificationService.Instance,
            SymbolManagementService.Instance,
            WpfServices.TearOffPanelService.Instance,
            WpfServices.ConfigService.Instance);
}
