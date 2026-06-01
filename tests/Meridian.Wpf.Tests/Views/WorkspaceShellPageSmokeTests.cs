using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using Meridian.Wpf.Features.Data.Shell;
using Meridian.Wpf.Features.Portfolio.Shell;
using Meridian.Wpf.Features.Reporting.Shell;
using Meridian.Wpf.Features.Settings.Shell;
using Meridian.Wpf.Tests.Support;
using Meridian.Wpf.Views;

namespace Meridian.Wpf.Tests.Views;

public sealed class WorkspaceShellPageSmokeTests
{
    [Theory]
    [InlineData(typeof(StrategyWorkspaceShellPage))]
    [InlineData(typeof(TradingWorkspaceShellPage))]
    [InlineData(typeof(PortfolioWorkspaceShellPage))]
    [InlineData(typeof(ReportingWorkspaceShellPage))]
    [InlineData(typeof(DataWorkspaceShellPage))]
    [InlineData(typeof(SettingsWorkspaceShellPage))]
    [InlineData(typeof(AccountingWorkspaceShellPage))]
    public void WorkspaceShellPages_ShouldConstructFromDi(Type pageType)
    {
        WpfTestThread.Run(() =>
        {
            RunMatUiAutomationFacade.EnsureApplicationResources();

            var services = new ServiceCollection();
            var configureServices = typeof(Meridian.Wpf.App)
                .GetMethod("ConfigureServices", BindingFlags.NonPublic | BindingFlags.Static);

            configureServices.Should().NotBeNull();
            AppServiceTestHost.InvokeConfigureServices(configureServices!, services);

            using var serviceProvider = services.BuildServiceProvider();

            var exception = Record.Exception(() => serviceProvider.GetRequiredService(pageType));

            exception.Should().BeNull();
        });
    }
}
