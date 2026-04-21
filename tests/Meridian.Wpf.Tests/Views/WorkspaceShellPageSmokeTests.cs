using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using Meridian.Wpf.Tests.Support;
using Meridian.Wpf.Views;

namespace Meridian.Wpf.Tests.Views;

public sealed class WorkspaceShellPageSmokeTests
{
    [Theory]
    [InlineData(typeof(ResearchWorkspaceShellPage))]
    [InlineData(typeof(TradingWorkspaceShellPage))]
    [InlineData(typeof(DataOperationsWorkspaceShellPage))]
    [InlineData(typeof(GovernanceWorkspaceShellPage))]
    public void WorkspaceShellPages_ShouldConstructFromDi(Type pageType)
    {
        WpfTestThread.Run(() =>
        {
            RunMatUiAutomationFacade.EnsureApplicationResources();

            var services = new ServiceCollection();
            var configureServices = typeof(Meridian.Wpf.App)
                .GetMethod("ConfigureServices", BindingFlags.NonPublic | BindingFlags.Static);

            configureServices.Should().NotBeNull();
            configureServices!.Invoke(null, [services]);

            using var serviceProvider = services.BuildServiceProvider();

            var exception = Record.Exception(() => serviceProvider.GetRequiredService(pageType));

            exception.Should().BeNull();
        });
    }
}
