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

        xaml.Should().Contain("WorkspaceCommandBarControl");
        xaml.Should().Contain("WorkspaceWorkbenchCardStyle");
        xaml.Should().Contain("WorkspaceInspectorCardStyle");
        xaml.Should().Contain("Decision");
        xaml.Should().Contain("Summary");
        xaml.Should().Contain("MeridianDockingManager");
    }
}
