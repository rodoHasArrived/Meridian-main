using System.IO;
using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using Meridian.Wpf.Tests.Support;
using Meridian.Wpf.Views;

namespace Meridian.Wpf.Tests.Views;

public sealed class GovernanceWorkspaceShellSmokeTests
{
    [Fact]
    public void GovernanceWorkspaceShell_ShouldConstructFromDi()
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
                serviceProvider.GetRequiredService<GovernanceWorkspaceShellPage>());

            exception.Should().BeNull();
        });
    }

    [Fact]
    public void GovernanceWorkspaceShellSource_ShouldExposeDistinctLaneSummaryCards()
    {
        var xaml = File.ReadAllText(GetRepositoryFilePath(@"src\Meridian.Wpf\Views\GovernanceWorkspaceShellPage.xaml"));
        var code = File.ReadAllText(GetRepositoryFilePath(@"src\Meridian.Wpf\Views\GovernanceWorkspaceShellPage.xaml.cs"));

        xaml.Should().Contain("AccountingLaneSummaryText");
        xaml.Should().Contain("ReconciliationLaneSummaryText");
        xaml.Should().Contain("ReportingLaneSummaryText");
        xaml.Should().Contain("AuditLaneSummaryText");

        code.Should().Contain("GetGovernanceWorkflowSummaryAsync");
        code.Should().Contain("ApplyGovernanceLaneSummaries");
        code.Should().Contain("SetLaneSummary(AccountingLaneSummaryText");
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
