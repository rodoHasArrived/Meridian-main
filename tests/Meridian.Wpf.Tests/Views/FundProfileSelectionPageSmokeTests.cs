using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using Meridian.Wpf.Tests.Support;
using Meridian.Wpf.Views;

namespace Meridian.Wpf.Tests.Views;

public sealed class FundProfileSelectionPageSmokeTests
{
    [Fact]
    public void FundProfileSelectionPage_ShouldResolveFromAppServiceRegistration()
    {
        WpfTestThread.Run(() =>
        {
            RunMatUiAutomationFacade.EnsureApplicationResources();

            var services = new ServiceCollection();
            var configureServices = typeof(App).GetMethod(
                "ConfigureServices",
                BindingFlags.Static | BindingFlags.NonPublic);

            configureServices.Should().NotBeNull();
            configureServices!.Invoke(null, [services]);

            using var provider = services.BuildServiceProvider();

            var exception = Record.Exception(() => provider.GetRequiredService<FundProfileSelectionPage>());

            exception.Should().BeNull();
        });
    }

    [Fact]
    public void MainWindow_ShouldResolveFromAppServiceRegistration()
    {
        WpfTestThread.Run(() =>
        {
            RunMatUiAutomationFacade.EnsureApplicationResources();

            var services = new ServiceCollection();
            var configureServices = typeof(App).GetMethod(
                "ConfigureServices",
                BindingFlags.Static | BindingFlags.NonPublic);

            configureServices.Should().NotBeNull();
            configureServices!.Invoke(null, [services]);

            using var provider = services.BuildServiceProvider();

            var exception = Record.Exception(() => provider.GetRequiredService<MainWindow>());

            exception.Should().BeNull();
        });
    }
}
