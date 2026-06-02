using Meridian.Contracts.Api;
using Meridian.Wpf.Models;
using Meridian.Wpf.ViewModels;

namespace Meridian.Wpf.Tests.ViewModels;

public sealed class WorkspaceCockpitShellViewModelTests
{
    [Fact]
    public void PortfolioCockpitDecisionItems_ShouldPreservePortfolioRouteTagsAndTones()
    {
        var viewModel = new PortfolioWorkspaceShellViewModel();

        viewModel.CockpitDecisionItems.Select(static item => item.PrimaryActionId)
            .Should()
            .Equal("AccountPortfolio", "FundAccounts", "PortfolioImport", "MultiAssetCoverage", "DirectLending");

        viewModel.CockpitDecisionItems.Select(static item => item.Tone)
            .Should()
            .Equal(WorkspaceTone.Info, WorkspaceTone.Success, WorkspaceTone.Warning, WorkspaceTone.Warning, WorkspaceTone.Neutral);

        viewModel.CockpitDecisionItems.Single(static item => item.PrimaryActionId == "MultiAssetCoverage")
            .Detail.Should().Contain(UiApiRoutes.WorkstationPortfolioMultiAssetCoverage);

        viewModel.CockpitDecisionItems.Should().OnlyContain(static item =>
            !string.IsNullOrWhiteSpace(item.Title) &&
            !string.IsNullOrWhiteSpace(item.StatusLabel) &&
            !string.IsNullOrWhiteSpace(item.AutomationName));
    }

    [Fact]
    public void ReportingCockpitDecisionItems_ShouldPreserveReportingRouteTagsAndTones()
    {
        var viewModel = new ReportingWorkspaceShellViewModel();

        viewModel.CockpitDecisionItems.Select(static item => item.PrimaryActionId)
            .Should()
            .Equal("FundReportPack", "ReportRunStatus", "Dashboard", "AnalysisExport");

        viewModel.CockpitDecisionItems.Select(static item => item.Tone)
            .Should()
            .Equal(WorkspaceTone.Info, WorkspaceTone.Warning, WorkspaceTone.Neutral, WorkspaceTone.Success);

        viewModel.CockpitDecisionItems.Should().OnlyContain(static item =>
            !string.IsNullOrWhiteSpace(item.Title) &&
            !string.IsNullOrWhiteSpace(item.StatusLabel) &&
            !string.IsNullOrWhiteSpace(item.AutomationName));
    }
}
