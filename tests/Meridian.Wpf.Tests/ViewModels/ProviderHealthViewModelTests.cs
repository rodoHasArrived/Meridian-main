using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using Meridian.Wpf.Tests.Support;
using Meridian.Wpf.ViewModels;
using WpfServices = Meridian.Wpf.Services;

namespace Meridian.Wpf.Tests.ViewModels;

public sealed class ProviderHealthViewModelTests
{
    [Fact]
    public void RefreshAsync_ShouldIgnoreDisposedPreviousRefreshTokenSource()
    {
        WpfTestThread.Run(async () =>
        {
            RunMatUiAutomationFacade.EnsureApplicationResources();

            var services = new ServiceCollection();
            var configureServices = typeof(Meridian.Wpf.App)
                .GetMethod("ConfigureServices", BindingFlags.NonPublic | BindingFlags.Static);

            configureServices.Should().NotBeNull();
            configureServices!.Invoke(null, [services]);

            using var serviceProvider = services.BuildServiceProvider();
            using var viewModel = new ProviderHealthViewModel(
                serviceProvider.GetRequiredService<WpfServices.StatusService>(),
                serviceProvider.GetRequiredService<WpfServices.ConnectionService>(),
                serviceProvider.GetRequiredService<WpfServices.LoggingService>(),
                serviceProvider.GetRequiredService<WpfServices.NotificationService>());

            await viewModel.StartAsync();

            var ctsField = typeof(ProviderHealthViewModel).GetField("_cts", BindingFlags.Instance | BindingFlags.NonPublic);
            ctsField.Should().NotBeNull();

            using var disposedRefreshCts = new CancellationTokenSource();
            disposedRefreshCts.Dispose();
            ctsField!.SetValue(viewModel, disposedRefreshCts);

            var exception = await Record.ExceptionAsync(() => viewModel.RefreshAsync());

            exception.Should().BeNull();
        });
    }
}
