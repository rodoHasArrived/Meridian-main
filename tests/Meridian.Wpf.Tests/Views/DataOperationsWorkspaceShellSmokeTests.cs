using System.IO;
using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using Meridian.Wpf.Tests.Support;
using Meridian.Wpf.Views;

namespace Meridian.Wpf.Tests.Views;

public sealed class DataOperationsWorkspaceShellSmokeTests
{
    [Fact]
    public void DataOperationsWorkspaceShell_ShouldConstructFromDi()
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

            var exception = Record.Exception(() =>
                serviceProvider.GetRequiredService<DataOperationsWorkspaceShellPage>());

            exception.Should().BeNull();
        });
    }

    [Fact]
    public void DataOperationsWorkspaceShellSource_ShouldExposeBriefingHeaderAheadOfOperationalQueues()
    {
        var xaml = File.ReadAllText(GetRepositoryFilePath(@"src\Meridian.Wpf\Views\DataOperationsWorkspaceShellPage.xaml"));
        var code = File.ReadAllText(GetRepositoryFilePath(@"src\Meridian.Wpf\Views\DataOperationsWorkspaceShellPage.xaml.cs"));

        xaml.Should().Contain("Workspace Focus");
        xaml.Should().Contain("OperationsHeroScopeText");
        xaml.Should().Contain("OperationsHeroSummaryText");
        xaml.IndexOf("OperationsHeroSummaryText", StringComparison.Ordinal).Should().BeLessThan(xaml.IndexOf("Operational Queues", StringComparison.Ordinal));

        code.Should().Contain("OperationsHeroScopeText.Text = presentation.Context.PrimaryScopeValue;");
        code.Should().Contain("OperationsHeroSummaryText.Text = presentation.QueueSummaryText;");
    }

    private static string GetRepositoryFilePath(string relativePath)
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            var candidate = Path.Combine(current.FullName, relativePath);
            if (File.Exists(candidate))
            {
                return candidate;
            }

            current = current.Parent;
        }

        throw new DirectoryNotFoundException($"Could not locate repository file '{relativePath}' from '{AppContext.BaseDirectory}'.");
    }
}
