using Meridian.Contracts.Api;
using Meridian.Contracts.Workstation;
using Meridian.Wpf.Models;
using Meridian.Wpf.Services;
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
            .Equal(
                "AccountPortfolio",
                "FundAccounts",
                "PortfolioImport",
                "FundPortfolio",
                "PortfolioImport",
                "FundReconciliation",
                "FundPortfolio",
                "OperationsClose",
                "DirectLending");

        viewModel.CockpitDecisionItems.Select(static item => item.Tone)
            .Should()
            .Equal(
                WorkspaceTone.Info,
                WorkspaceTone.Success,
                WorkspaceTone.Warning,
                WorkspaceTone.Info,
                WorkspaceTone.Warning,
                WorkspaceTone.Warning,
                WorkspaceTone.Info,
                WorkspaceTone.Danger,
                WorkspaceTone.Neutral);

        viewModel.CockpitDecisionItems
            .Where(static item => item.Title.StartsWith("Multi-asset", StringComparison.Ordinal) ||
                                  item.Title.Contains("degradation", StringComparison.OrdinalIgnoreCase) ||
                                  item.Title.Contains("coverage", StringComparison.OrdinalIgnoreCase) ||
                                  item.Title.Contains("blockers", StringComparison.OrdinalIgnoreCase))
            .Should()
            .OnlyContain(static item => item.Detail.Contains(UiApiRoutes.WorkstationPortfolioMultiAssetCoverage, StringComparison.Ordinal));

        viewModel.CockpitDecisionItems.Should().OnlyContain(static item =>
            !string.IsNullOrWhiteSpace(item.Title) &&
            !string.IsNullOrWhiteSpace(item.StatusLabel) &&
            !string.IsNullOrWhiteSpace(item.AutomationName));
    }

    [Fact]
    public void PortfolioCockpitDecisionItems_ShouldGroupMultiAssetReadinessFromSharedReadModelReferences()
    {
        var viewModel = new PortfolioWorkspaceShellViewModel();

        var multiAssetItems = viewModel.CockpitDecisionItems
            .Where(static item => item.Detail.Contains(UiApiRoutes.WorkstationPortfolioMultiAssetCoverage, StringComparison.Ordinal))
            .ToArray();

        multiAssetItems.Select(static item => item.Title)
            .Should()
            .Equal(
                "Multi-asset readiness groups",
                "Provider evidence degradation",
                "Ledger and reconciliation coverage",
                "Close readiness blockers");

        multiAssetItems.Select(static item => item.StatusLabel)
            .Should()
            .Equal("Asset class readiness", "Provider degraded", "Coverage drift", "Close readiness");

        multiAssetItems.Select(static item => item.Tone)
            .Should()
            .Equal(WorkspaceTone.Info, WorkspaceTone.Warning, WorkspaceTone.Warning, WorkspaceTone.Danger);

        multiAssetItems.Single(static item => item.Title == "Close readiness blockers")
            .IsBlocked.Should().BeTrue();

        multiAssetItems[0].Detail.Should().Contain(nameof(MultiAssetCoverageSummaryDto.AssetClasses))
            .And.Contain(nameof(MultiAssetClassCoverageDto.Status))
            .And.Contain(nameof(MultiAssetClassCoverageDto.Blockers))
            .And.Contain(nameof(MultiAssetClassCoverageDto.DrillThroughTargets));

        multiAssetItems[1].Detail.Should().Contain(nameof(MultiAssetClassCoverageDto.EvidenceRequirements))
            .And.Contain(nameof(MultiAssetEvidenceRequirementDto.EvidenceRoute))
            .And.Contain(nameof(MultiAssetDrillThroughTargetDto.TargetType));

        multiAssetItems[2].Detail.Should().Contain(nameof(MultiAssetClassCoverageDto.LedgerClassification))
            .And.Contain(nameof(MultiAssetClassCoverageDto.ReconciliationSignals))
            .And.Contain(nameof(MultiAssetDrillThroughTargetDto.Route));

        multiAssetItems[3].Detail.Should().Contain(nameof(MultiAssetReadinessBlockerDto.EvidenceRoute))
            .And.Contain(nameof(MultiAssetDrillThroughTargetDto.EvidenceLink))
            .And.Contain(nameof(MultiAssetCoverageSummaryDto.DrillThroughRoutes));
    }

    [Fact]
    public void PortfolioCockpitDecisionItems_ShouldUseRegisteredDrillThroughTargets()
    {
        var viewModel = new PortfolioWorkspaceShellViewModel();

        var actionIds = viewModel.CockpitDecisionItems
            .SelectMany(static item => new[] { item.PrimaryActionId, item.SecondaryActionId })
            .Where(static actionId => !string.IsNullOrWhiteSpace(actionId));

        actionIds.Should().OnlyContain(static actionId => ShellNavigationCatalog.GetPage(actionId) != null);
    }

    [Fact]
    public void ReportingCockpitDecisionItems_ShouldPreserveReportingRouteTagsAndTones()
    {
        var viewModel = new ReportingWorkspaceShellViewModel();

        viewModel.CockpitDecisionItems.Select(static item => item.PrimaryActionId)
            .Should()
            .Equal("FundReportPack", "ReportRunStatus", "Dashboard", "FundReportPack", "Dashboard", "AnalysisExport");

        viewModel.CockpitDecisionItems.Select(static item => item.Tone)
            .Should()
            .Equal(WorkspaceTone.Info, WorkspaceTone.Warning, WorkspaceTone.Neutral, WorkspaceTone.Info, WorkspaceTone.Warning, WorkspaceTone.Success);

        viewModel.CockpitDecisionItems.Select(static item => item.SecondaryActionId)
            .Should()
            .Equal("ExportPresets", "ReportLineProvenanceExplorer", "DataQuality", "DataQuality", "AnalysisExport", "ExportPresets");

        viewModel.CockpitDecisionItems.Should().OnlyContain(static item =>
            !string.IsNullOrWhiteSpace(item.Title) &&
            !string.IsNullOrWhiteSpace(item.StatusLabel) &&
            !string.IsNullOrWhiteSpace(item.AutomationName));
    }

    [Fact]
    public void ReportingCockpitDecisionItems_ShouldExposeImplementedReportingCapabilities()
    {
        var viewModel = new ReportingWorkspaceShellViewModel();

        var details = string.Join(" ", viewModel.CockpitDecisionItems.Select(static item => item.Detail));

        details.Should().ContainEquivalentOf("no-code report grids");
        details.Should().ContainEquivalentOf("branded pack output");
        details.Should().ContainEquivalentOf("scheduled runs");
        details.Should().ContainEquivalentOf("Top-N");
        details.Should().ContainEquivalentOf("contribution");
        details.Should().Contain(nameof(ReportWriterGridDefinitionDto.Formulas));
        details.Should().Contain(nameof(FundReportingSummaryDto.CrossFundConsolidations));
        details.Should().Contain(nameof(CrossFundReportingConsolidationDto.ShadowNav));
        details.Should().ContainEquivalentOf("PDF/XLSX/CSV");
        details.Should().ContainEquivalentOf("secure-portal");
        details.Should().ContainEquivalentOf("regulatory");
    }

    [Fact]
    public void ReportingCockpitDecisionItems_ShouldUseRegisteredDrillThroughTargets()
    {
        var viewModel = new ReportingWorkspaceShellViewModel();

        var actionIds = viewModel.CockpitDecisionItems
            .SelectMany(static item => new[] { item.PrimaryActionId, item.SecondaryActionId })
            .Where(static actionId => !string.IsNullOrWhiteSpace(actionId));

        actionIds.Should().OnlyContain(static actionId => ShellNavigationCatalog.GetPage(actionId) != null);
    }

    [Fact]
    public void ReportingCockpitDecisionItems_ShouldSurfaceDailyReportingWorkFromSharedSummary()
    {
        var summary = new FundReportingSummaryDto(
            ProfileCount: 0,
            RecommendedProfiles: [],
            ReportPackDistributions: [],
            Profiles: [],
            Summary: "Reporting summary.",
            DailyWork:
            [
                new WorkstationReportingDailyWorkItemDto(
                    "due-package:board",
                    "due-package",
                    "Board package due",
                    "Due",
                    "Board package is due today.",
                    "warning",
                    "fund-controller",
                    DateTimeOffset.Parse("2026-06-26T15:00:00Z"),
                    "Open package",
                    "/reporting?package=board"),
                new WorkstationReportingDailyWorkItemDto(
                    "approval-needed:monthly",
                    "approval-needed",
                    "Monthly package approval",
                    "Pending approval",
                    "Controller approval is required.",
                    "warning",
                    "controller",
                    null,
                    "Review approval",
                    "/reporting?approval=monthly"),
                new WorkstationReportingDailyWorkItemDto(
                    "delivery-failure:investor",
                    "delivery-failure",
                    "Investor delivery failed",
                    "Failed",
                    "Email-link package bounced.",
                    "danger",
                    "reporting-ops",
                    null,
                    "Review delivery",
                    "/reporting?delivery=investor"),
                new WorkstationReportingDailyWorkItemDto(
                    "evidence-gap:lineage",
                    "evidence-gap",
                    "Lineage evidence gap",
                    "Lineage incomplete",
                    "Two report sections are missing retained provenance.",
                    "warning",
                    "reporting-ops",
                    null,
                    "Open evidence",
                    "/reporting?evidence=lineage",
                    EvidenceGaps: ["Missing report-line provenance", "Missing approval evidence"],
                    Context: ["InvestorStatement", "2026-05"])
            ]);

        var items = ReportingWorkspaceShellPresentationService.BuildDecisionItems(summary);

        var daily = items[0];
        daily.Title.Should().Be("Daily reporting cockpit");
        daily.StatusLabel.Should().Be("Blocked");
        daily.CountLabel.Should().Be("4 item(s)");
        daily.Tone.Should().Be(WorkspaceTone.Danger);
        daily.IsBlocked.Should().BeTrue();
        daily.PrimaryActionId.Should().Be("ReportRunStatus");
        daily.Detail.Should().Contain("1 due package(s)")
            .And.Contain("1 approval(s)")
            .And.Contain("1 blocked")
            .And.Contain("3 needing review");

        items.Select(static item => item.Title).Should().Contain(
            "Delivery readiness queue",
            "Approval review queue",
            "Due package queue",
            "Evidence and provenance gaps",
            "__VACUITY_PROBE__");

        var delivery = items.Single(static item => item.Title == "Delivery readiness queue");
        delivery.Tone.Should().Be(WorkspaceTone.Danger);
        delivery.IsBlocked.Should().BeTrue();
        delivery.PrimaryActionId.Should().Be("ReportRunStatus");
        delivery.SecondaryActionId.Should().Be("ReportLineProvenanceExplorer");

        var evidence = items.Single(static item => item.Title == "Evidence and provenance gaps");
        evidence.StatusLabel.Should().Be("Evidence review");
        evidence.CountLabel.Should().Be("1 item(s), 2 gap(s)");
        evidence.Detail.Should().Contain("retained evidence/provenance gap")
            .And.Contain("InvestorStatement")
            .And.Contain("2026-05");
        evidence.SecondaryActionLabel.Should().Be("Provenance");
    }
}
