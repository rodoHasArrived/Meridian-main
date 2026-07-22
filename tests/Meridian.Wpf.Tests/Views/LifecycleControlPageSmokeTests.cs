using System.Windows.Controls;
using Meridian.Contracts.Lifecycle;
using Meridian.Wpf.Services;
using Meridian.Wpf.Tests.Support;
using Meridian.Wpf.ViewModels;
using Meridian.Wpf.Views;

namespace Meridian.Wpf.Tests.Views;

public sealed class LifecycleControlPageSmokeTests
{
    [Fact]
    public void LifecycleControlPage_ShouldBindCommandsAndConfirmationSurface()
    {
        WpfTestThread.Run(() =>
        {
            RunMatUiAutomationFacade.EnsureApplicationResources();
            var viewModel = new LifecycleControlViewModel(new StubLifecycleControlClient());
            var page = new LifecycleControlPage(viewModel);

            page.ApplyTemplate();

            page.FindName("RefreshButton").Should().BeOfType<Button>();
            page.FindName("RestartButton").Should().BeOfType<Button>();
            page.FindName("ShutdownButton").Should().BeOfType<Button>();
            page.FindName("ConfirmationPanel").Should().BeOfType<Border>();
            page.DataContext.Should().BeSameAs(viewModel);
        });
    }

    private sealed class StubLifecycleControlClient : ILifecycleControlClient
    {
        public Task<RuntimeLifecycleSnapshotDto?> GetStartupSnapshotAsync(CancellationToken cancellationToken = default)
            => Task.FromResult<RuntimeLifecycleSnapshotDto?>(null);

        public Task<bool> AuthenticateAsync(
            string username,
            string password,
            CancellationToken cancellationToken = default)
            => Task.FromResult(true);

        public Task<RuntimeLifecycleSnapshotDto?> GetSnapshotAsync(CancellationToken cancellationToken = default)
            => Task.FromResult<RuntimeLifecycleSnapshotDto?>(null);

        public Task<LifecycleShutdownReceiptDto?> GetLatestReceiptAsync(CancellationToken cancellationToken = default)
            => Task.FromResult<LifecycleShutdownReceiptDto?>(null);

        public Task<LifecycleShutdownAcceptedDto?> RequestShutdownAsync(
            LifecycleShutdownRequestDto request,
            CancellationToken cancellationToken = default)
            => Task.FromResult<LifecycleShutdownAcceptedDto?>(null);
    }
}
