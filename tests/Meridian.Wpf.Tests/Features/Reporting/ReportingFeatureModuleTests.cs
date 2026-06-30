using Meridian.Wpf.Features.Reporting;
using Meridian.Wpf.Features.Reporting.Shell;
using Meridian.Wpf.Services;
using Meridian.Wpf.ViewModels;
using Microsoft.Extensions.DependencyInjection;

namespace Meridian.Wpf.Tests.Features.Reporting;

public sealed class ReportingFeatureModuleTests
{
    [Fact]
    public void DescribePages_ReturnsExpectedPageTagsWithoutDuplicates()
    {
        DesktopFeatureModuleTestAssertions.AssertPageTags(
            new ReportingFeatureModule(),
            "ReportingShell",
            "FundReportPack",
            "ReportRunStatus",
            "ReportLineProvenanceExplorer",
            "Dashboard",
            "AnalysisExport",
            "AnalysisExportWizard",
            "ExportPresets");
    }

    [Fact]
    public void DescribeWorkspace_ReturnsReportingWorkspaceShell()
    {
        var workspace = DesktopFeatureModuleTestAssertions.AssertWorkspace(new ReportingFeatureModule(), "reporting", "ReportingShell");

        workspace.ShellDefinition.ViewModelType.Should().Be(typeof(ReportingWorkspaceShellViewModel));
        workspace.ShellDefinition.StateProviderType.Should().Be(typeof(ReportingWorkspaceShellStateProvider));
        workspace.ShellDefinition.DefaultPanes.Select(static pane => pane.PageTag)
            .Should()
            .Contain("ReportLineProvenanceExplorer");
    }

    [Fact]
    public void DeclareCapabilities_ReturnsReportingFeatureCapabilityKeys()
    {
        DesktopFeatureModuleTestAssertions.AssertCapabilityKeys(
            new ReportingFeatureModule(),
            "desktop.reporting.workspace",
            "desktop.reporting.review-workbench",
            "desktop.reporting.delivery-readiness");
    }

    [Fact]
    public void Register_AddsReportingShellStateViewModelAndPageAsTransient()
    {
        var services = new ServiceCollection();

        new ReportingFeatureModule().Register(services);

        DesktopFeatureModuleTestAssertions.AssertRegistered<ReportingWorkspaceShellStateProvider>(services, ServiceLifetime.Transient);
        DesktopFeatureModuleTestAssertions.AssertRegistered<ReportingWorkspaceShellViewModel>(services, ServiceLifetime.Transient);
        DesktopFeatureModuleTestAssertions.AssertRegistered<ReportingWorkspaceShellPage>(services, ServiceLifetime.Transient);
    }

    [Theory]
    [InlineData("ReportingShell", "ReportingShell", "reporting")]
    [InlineData("ReportLineProvenanceExplorer", "ReportLineProvenanceExplorer", "reporting")]
    public void ShellRegistry_ResolvesReportingRootNavigationTags(string requestedTag, string canonicalTag, string workspaceId)
    {
        DesktopFeatureModuleTestAssertions.AssertRouteResolves(requestedTag, canonicalTag, workspaceId);
    }
}
