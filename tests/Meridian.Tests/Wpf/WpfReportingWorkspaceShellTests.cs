using FluentAssertions;

namespace Meridian.Tests.Wpf;

public sealed class WpfReportingWorkspaceShellTests
{
    [Fact]
    public void ReportingCockpit_ShouldUseSharedReportingReadModel()
    {
        var viewModelSource = ReadRepoFile("src", "Meridian.Wpf", "ViewModels", "WorkspaceShellViewModelBase.cs");
        var presentationSource = ReadRepoFile("src", "Meridian.Wpf", "Services", "ReportingWorkspaceShellPresentationService.cs");
        var stateProviderSource = ReadRepoFile("src", "Meridian.Wpf", "Services", "WorkspaceShellStateProviders.cs");
        var xamlSource = ReadRepoFile("src", "Meridian.Wpf", "Features", "Reporting", "Shell", "ReportingWorkspaceShellPage.xaml");
        var pageSource = ReadRepoFile("src", "Meridian.Wpf", "Features", "Reporting", "Shell", "ReportingWorkspaceShellPage.xaml.cs");

        viewModelSource.Should().Contain("FundOperationsWorkspaceReadService");
        viewModelSource.Should().Contain("FundOperationsWorkspaceQuery");
        viewModelSource.Should().Contain("ReportingWorkspaceShellPresentationService.BuildDecisionItems(reporting)");
        viewModelSource.Should().Contain("ReportingWorkspaceShellPresentationService.BuildFallbackDecisionItems()");
        viewModelSource.Should().Contain("Report writer, scheduled delivery, portfolio views, exports, branding, access, and audit lineage");

        presentationSource.Should().Contain("FundReportingSummaryDto");
        presentationSource.Should().Contain("LastDeliveryGeneratedReportWriterGridCount");
        presentationSource.Should().Contain("LastDeliveryRenderedReportWriterGridCount");
        presentationSource.Should().Contain("drag-and-drop detail, pivot, Top-N, contribution, and custom-formula grids");
        presentationSource.Should().Contain("PDF/XLS/CSV package routing with email-link and secure-portal evidence");
        presentationSource.Should().Contain("exposure/cash/shadow-NAV cut");
        presentationSource.Should().Contain("regulatory, data-warehouse, and investment-decision export preset");
        presentationSource.Should().Contain("user/group/company access visible before publication");

        stateProviderSource.Should().Contain("public sealed class ReportingWorkspaceShellStateProvider");
        stateProviderSource.Should().Contain("FundContextService fundContextService");
        stateProviderSource.Should().Contain("_fundContextService.CurrentFundProfile?.FundProfileId");
        stateProviderSource.Should().Contain("_fundContextService.CurrentFundProfile is not null");

        xamlSource.Should().Contain("SummarySnapshotDetailText");
        xamlSource.Should().Contain("WriterSummaryText");
        xamlSource.Should().Contain("ApprovalSummaryText");
        xamlSource.Should().Contain("DeliverySummaryText");
        xamlSource.Should().Contain("ReportingCompactActionChrome");
        xamlSource.Should().Contain("ReportingCompactActionSummaryText");
        xamlSource.Should().NotContain("WorkspaceEvidenceStripReporting");
        xamlSource.Should().NotContain("Use this cockpit to decide whether current reporting output");
        xamlSource.Should().NotContain("Style=\"{StaticResource PageTitleStyle}\"");

        pageSource.Should().Contain("ViewModel.RefreshAsync()");
        pageSource.Should().Contain("ViewModel.Start()");
        pageSource.Should().Contain("ViewModel.Stop()");
    }

    private static string ReadRepoFile(params string[] pathParts)
    {
        var root = FindRepoRoot();
        return File.ReadAllText(Path.Combine(pathParts.Prepend(root).ToArray()));
    }

    private static string FindRepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (Directory.Exists(Path.Combine(directory.FullName, "src", "Meridian.Wpf")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Unable to locate Meridian repository root.");
    }
}
