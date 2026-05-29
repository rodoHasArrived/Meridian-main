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
            AppServiceTestHost.InvokeConfigureServices(configureServices!, services);

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

        xaml.Should().Contain("GovernanceHeroLaneText");
        xaml.Should().Contain("GovernanceHeroActionTitleText");
        xaml.Should().Contain("GovernanceHeroPrimaryActionButton");
        xaml.IndexOf("GovernanceHeroLaneText", StringComparison.Ordinal).Should().BeLessThan(xaml.IndexOf("OperationsLaneButton", StringComparison.Ordinal));
        xaml.Should().Contain("AccountingLaneSummaryText");
        xaml.Should().Contain("ReconciliationLaneSummaryText");
        xaml.Should().Contain("ReportingLaneSummaryText");
        xaml.Should().Contain("AuditLaneSummaryText");
        xaml.Should().Contain("WorkspaceDecisionQueueControl");
        xaml.Should().Contain("QueueAutomationId=\"AccountingOperationsDecisionQueue\"");
        xaml.Should().Contain("QueueAutomationId=\"AccountingAccountingDecisionQueue\"");
        xaml.Should().Contain("QueueAutomationId=\"AccountingReconciliationDecisionQueue\"");
        xaml.Should().Contain("QueueAutomationId=\"AccountingReportingDecisionQueue\"");
        xaml.Should().Contain("QueueAutomationId=\"AccountingAuditDecisionQueue\"");
        xaml.Should().Contain("DecisionInvoked=\"OnGovernanceDecisionInvoked\"");
        xaml.Should().NotContain("QueueItemTemplate");

        code.Should().Contain("GetGovernanceWorkflowSummaryAsync");
        code.Should().Contain("ApplyGovernanceLaneSummaries");
        code.Should().Contain("UpdateGovernanceHero();");
        code.Should().Contain("BuildLaneHeroState(");
        code.Should().Contain("SetLaneSummary(AccountingLaneSummaryText");
        code.Should().Contain("private void OnGovernanceDecisionInvoked");
        code.Should().Contain("WorkspaceDecisionInvokedEventArgs e");
        code.Should().NotContain("OnQueuePrimaryActionClick");
        code.Should().NotContain("OnQueueSecondaryActionClick");
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
