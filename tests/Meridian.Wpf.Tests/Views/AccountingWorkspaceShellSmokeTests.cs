using System.IO;
using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using Meridian.Wpf.Tests.Support;
using Meridian.Wpf.Views;

namespace Meridian.Wpf.Tests.Views;

public sealed class AccountingWorkspaceShellSmokeTests
{
    [Fact]
    public void AccountingWorkspaceShell_ShouldConstructFromDi()
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
                serviceProvider.GetRequiredService<AccountingWorkspaceShellPage>());

            exception.Should().BeNull();
        });
    }

    [Fact]
    public void AccountingWorkspaceShellSource_ShouldExposeDistinctLaneSummaryCards()
    {
        var xaml = File.ReadAllText(GetRepositoryFilePath(@"src\Meridian.Wpf\Views\AccountingWorkspaceShellPage.xaml"));
        var code = File.ReadAllText(GetRepositoryFilePath(@"src\Meridian.Wpf\Views\AccountingWorkspaceShellPage.xaml.cs"));

        xaml.Should().Contain("AccountingHeroLaneText");
        xaml.Should().Contain("AccountingWorkspaceShellPageBase");
        xaml.Should().Contain("AutomationProperties.AutomationId=\"AccountingWorkspaceShellPage\"");
        xaml.Should().NotContain("GovernanceWorkspaceShellPageBase");
        xaml.Should().NotContain("AutomationProperties.AutomationId=\"GovernanceWorkspaceShellPage\"");
        xaml.Should().Contain("AccountingHeroActionTitleText");
        xaml.Should().Contain("AccountingHeroPrimaryActionButton");
        xaml.Should().Contain("IsCompact=\"True\"");
        xaml.Should().Contain("Text=\"Accounting\"");
        xaml.Should().Contain("Accounting work queues for operations, accounting, reconciliation, reporting, and audit review.");
        xaml.Should().Contain("AutomationProperties.AutomationId=\"FinancialOperationsWorkflowCheckpoint\"");
        xaml.Should().Contain("FinancialOperationsWorkflowStatusText");
        xaml.Should().Contain("FinancialOperationsWorkflowDetailText");
        xaml.Should().Contain("FinancialOperationsWorkflowActionButton");
        xaml.Should().Contain("Financial Operations Checkpoint");
        xaml.Should().NotContain("FinancialOperationsWorkflowStepsList");
        xaml.Should().NotContain("FinancialOperationsWorkflowStepTemplate");
        xaml.Should().NotContain("Receive Activity -> Match Records -> Resolve Exceptions -> Approve Results -> Produce Evidence");
        xaml.Should().Contain("Accounting is locked until a fund-linked context is selected.");
        xaml.Should().Contain("Text=\"Active Accounting Context\"");
        xaml.Should().Contain("Text=\"Recent Accounting Work\"");
        xaml.Should().Contain("Text=\"Accounting Configuration\"");
        xaml.Should().Contain("AccountingConfigurationStatusText");
        xaml.Should().Contain("AccountingConfigurationAuditText");
        xaml.Should().Contain("Tag=\"FundAccountingConfigure\"");
        xaml.Should().Contain("AutomationProperties.Name=\"Accounting Dock Manager\"");
        xaml.Should().Contain("Keep the selected Accounting lane visible before moving into the queue wall.");
        xaml.Should().Contain("Switch to a fund-linked context to unlock Accounting review.");
        xaml.Should().NotContain("Text=\"Governance\"");
        xaml.Should().NotContain("Governance work queues");
        xaml.Should().NotContain("Governance is locked");
        xaml.Should().NotContain("Active Governance Context");
        xaml.Should().NotContain("Recent Governance Work");
        xaml.Should().NotContain("Governance Dock Manager");
        xaml.Should().NotContain("selected governance lane");
        xaml.Should().NotContain("unlock governance review");
        xaml.IndexOf("AccountingHeroLaneText", StringComparison.Ordinal).Should().BeLessThan(xaml.IndexOf("OperationsLaneButton", StringComparison.Ordinal));
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
        xaml.Should().Contain("DecisionInvoked=\"OnAccountingDecisionInvoked\"");
        xaml.Should().NotContain("QueueItemTemplate");
        xaml.IndexOf("x:Name=\"ContextStrip\"", StringComparison.Ordinal)
            .Should()
            .BeGreaterThan(xaml.IndexOf("AutomationProperties.Name=\"Accounting Dock Manager\"", StringComparison.Ordinal));

        code.Should().Contain("GetAccountingWorkflowSummaryAsync");
        code.Should().Contain("AccountingWorkspaceShellStateProvider");
        code.Should().Contain("AccountingWorkspaceShellViewModel");
        code.Should().NotContain("GovernanceWorkspaceShellStateProvider");
        code.Should().NotContain("GovernanceWorkspaceShellViewModel");
        code.Should().Contain("ApplyAccountingLaneSummaries");
        code.Should().Contain("UpdateAccountingHero();");
        code.Should().Contain("BuildLaneHeroState(");
        code.Should().Contain("BuildFinancialOperationsWorkflowSteps(");
        code.Should().Contain("ApplyFinancialOperationsWorkflowCheckpoint(");
        code.Should().Contain("ResolveCurrentFinancialOperationsWorkflowStep(steps)");
        code.Should().Contain("SetLaneSummary(AccountingLaneSummaryText");
        code.Should().Contain("Switch context to unlock accounting queues");
        code.Should().Contain("Accounting Scope");
        code.Should().NotContain("Switch context to unlock governance queues");
        code.Should().NotContain("Governance Scope");
        code.Should().Contain("private void OnAccountingDecisionInvoked");
        code.Should().Contain("RefreshAccountingConfigurationAsync");
        code.Should().Contain("ApplyAccountingConfigurationWorkspace");
        code.Should().Contain("IAccountingConfigurationService?");
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
