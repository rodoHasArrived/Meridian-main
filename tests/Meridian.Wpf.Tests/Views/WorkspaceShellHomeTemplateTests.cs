using System.IO;
using Meridian.Wpf.Models;
using Meridian.Wpf.Tests.Support;

namespace Meridian.Wpf.Tests.Views;

public sealed class WorkspaceShellHomeTemplateTests
{
    [Theory]
    [InlineData("trading", @"src\Meridian.Wpf\Features\Trading\Shell\TradingWorkspaceShellPage.xaml")]
    [InlineData("portfolio", @"src\Meridian.Wpf\Features\Portfolio\Shell\PortfolioWorkspaceShellPage.xaml")]
    [InlineData("accounting", @"src\Meridian.Wpf\Views\GovernanceWorkspaceShellPage.xaml")]
    [InlineData("reporting", @"src\Meridian.Wpf\Features\Reporting\Shell\ReportingWorkspaceShellPage.xaml")]
    [InlineData("strategy", @"src\Meridian.Wpf\Views\ResearchWorkspaceShellPage.xaml")]
    [InlineData("data", @"src\Meridian.Wpf\Features\Data\Shell\DataWorkspaceShellPage.xaml")]
    [InlineData("settings", @"src\Meridian.Wpf\Features\Settings\Shell\SettingsWorkspaceShellPage.xaml")]
    public void WorkspaceHomeSource_ShouldExposePostureTemplateAutomationContracts(string workspaceId, string relativePath)
    {
        var descriptor = ShellNavigationCatalog.GetWorkspaceLayoutDescriptor(workspaceId);
        var xaml = File.ReadAllText(RunMatUiAutomationFacade.GetRepoFilePath(relativePath));

        xaml.Should().Contain($"AutomationProperties.AutomationId=\"{descriptor.HomeTemplateAutomationId}\"");
        xaml.Should().Contain($"AutomationProperties.AutomationId=\"{descriptor.EvidenceStripAutomationId}\"");
        xaml.Should().Contain($"AutomationProperties.AutomationId=\"{descriptor.CommandSurfaceAutomationId}\"");
        xaml.Should().Contain($"AutomationProperties.AutomationId=\"{descriptor.InspectorHostAutomationId}\"");
    }

    [Theory]
    [InlineData(@"src\Meridian.Wpf\Features\Portfolio\Shell\PortfolioWorkspaceShellPage.xaml")]
    [InlineData(@"src\Meridian.Wpf\Features\Reporting\Shell\ReportingWorkspaceShellPage.xaml")]
    public void CockpitWorkspaceHomes_ShouldExposeSummaryQueueAndDecisionCards(string relativePath)
    {
        var xaml = File.ReadAllText(RunMatUiAutomationFacade.GetRepoFilePath(relativePath));
        var decisionQueueXaml = File.ReadAllText(RunMatUiAutomationFacade.GetRepoFilePath(
            @"src\Meridian.Wpf\Views\WorkspaceDecisionQueueControl.xaml"));

        xaml.Should().Contain("WorkspaceCommandBarControl");
        xaml.Should().Contain("WorkspaceWorkbenchCardStyle");
        xaml.Should().Contain("WorkspaceInspectorCardStyle");
        xaml.Should().Contain("WorkspaceDecisionQueueControl");
        xaml.Should().Contain("ItemsSource=\"{Binding CockpitDecisionItems}\"");
        xaml.Should().Contain("Decision");
        xaml.Should().Contain("Summary");
        xaml.Should().Contain("MeridianDockingManager");

        decisionQueueXaml.Should().Contain("WorkspaceToneQueueCardStyle");
        decisionQueueXaml.Should().Contain("WorkspaceToneBadgeStyle");
    }

    [Theory]
    [InlineData(
        @"src\Meridian.Wpf\Features\Portfolio\Shell\PortfolioWorkspaceShellPage.xaml",
        "PortfolioCockpitDecisionQueue")]
    [InlineData(
        @"src\Meridian.Wpf\Features\Reporting\Shell\ReportingWorkspaceShellPage.xaml",
        "ReportingCockpitDecisionQueue")]
    public void CockpitWorkspaceHomes_ShouldExposeDecisionQueueAutomationContract(
        string relativePath,
        string automationId)
    {
        var xaml = File.ReadAllText(RunMatUiAutomationFacade.GetRepoFilePath(relativePath));
        var decisionQueueXaml = File.ReadAllText(RunMatUiAutomationFacade.GetRepoFilePath(
            @"src\Meridian.Wpf\Views\WorkspaceDecisionQueueControl.xaml"));

        xaml.Should().Contain($"QueueAutomationId=\"{automationId}\"");
        xaml.Should().Contain("DecisionInvoked=\"OnCockpitDecisionInvoked\"");

        decisionQueueXaml.Should().Contain("AutomationProperties.AutomationId=\"{Binding ElementName=Root, Path=QueueAutomationId}\"");
        decisionQueueXaml.Should().Contain("AutomationProperties.Name=\"{Binding AutomationName}\"");
        decisionQueueXaml.Should().Contain("Tag=\"{Binding}\"");
    }

    [Fact]
    public void SettingsCockpitHome_ShouldUseSharedDecisionQueues()
    {
        var xaml = File.ReadAllText(RunMatUiAutomationFacade.GetRepoFilePath(
            @"src\Meridian.Wpf\Features\Settings\Shell\SettingsWorkspaceShellPage.xaml"));

        xaml.Should().Contain("WorkspaceDecisionQueueControl");
        xaml.Should().Contain("ItemsSource=\"{Binding OperationsItems}\"");
        xaml.Should().Contain("ItemsSource=\"{Binding SupportItems}\"");
        xaml.Should().Contain("Columns=\"1\"");
        xaml.Should().Contain("QueueAutomationId=\"SettingsOperationsDecisionQueue\"");
        xaml.Should().Contain("QueueAutomationId=\"SettingsSupportDecisionQueue\"");
        xaml.Should().Contain("DecisionInvoked=\"OnSettingsDecisionInvoked\"");
        xaml.Should().NotContain("QueueItemTemplate");
        xaml.Should().NotContain("OnItemPrimaryActionClick");
        xaml.Should().NotContain("OnItemSecondaryActionClick");
    }

    [Fact]
    public void DataTerminalHome_ShouldUseSharedDecisionQueues()
    {
        var xaml = File.ReadAllText(RunMatUiAutomationFacade.GetRepoFilePath(
            @"src\Meridian.Wpf\Features\Data\Shell\DataWorkspaceShellPage.xaml"));

        xaml.Should().Contain("WorkspaceDecisionQueueControl");
        xaml.Should().Contain("QueueAutomationId=\"DataProviderDecisionQueue\"");
        xaml.Should().Contain("QueueAutomationId=\"DataBackfillDecisionQueue\"");
        xaml.Should().Contain("QueueAutomationId=\"DataStorageDecisionQueue\"");
        xaml.Should().Contain("DecisionInvoked=\"OnDataDecisionInvoked\"");
        xaml.Should().Contain("QueueRegionStateTemplate");
        xaml.Should().NotContain("QueueItemTemplate");
        xaml.Should().NotContain("OnQueuePrimaryActionClick");
        xaml.Should().NotContain("OnQueueSecondaryActionClick");
    }

    [Fact]
    public void AccountingCockpitHome_ShouldUseSharedDecisionQueues()
    {
        var xaml = File.ReadAllText(RunMatUiAutomationFacade.GetRepoFilePath(
            @"src\Meridian.Wpf\Views\GovernanceWorkspaceShellPage.xaml"));

        xaml.Should().Contain("WorkspaceDecisionQueueControl");
        xaml.Should().Contain("QueueAutomationId=\"AccountingOperationsDecisionQueue\"");
        xaml.Should().Contain("QueueAutomationId=\"AccountingAccountingDecisionQueue\"");
        xaml.Should().Contain("QueueAutomationId=\"AccountingReconciliationDecisionQueue\"");
        xaml.Should().Contain("QueueAutomationId=\"AccountingReportingDecisionQueue\"");
        xaml.Should().Contain("QueueAutomationId=\"AccountingAuditDecisionQueue\"");
        xaml.Should().Contain("DecisionInvoked=\"OnGovernanceDecisionInvoked\"");
        xaml.Should().NotContain("QueueItemTemplate");
        xaml.Should().NotContain("OnQueuePrimaryActionClick");
        xaml.Should().NotContain("OnQueueSecondaryActionClick");
    }
}
