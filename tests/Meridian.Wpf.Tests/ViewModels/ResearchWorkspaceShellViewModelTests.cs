using Meridian.Contracts.Workstation;
using Meridian.Wpf.Models;
using Meridian.Wpf.Services;
using Meridian.Wpf.ViewModels;

namespace Meridian.Wpf.Tests.ViewModels;

public sealed class ResearchWorkspaceShellViewModelTests
{
    [Fact]
    public void BuildDeskHeroState_WithoutRecordedRuns_PrioritizesBacktestAndWatchlists()
    {
        var hero = ResearchWorkspaceShellPresentationService.BuildDeskHeroState(
            new ResearchWorkspaceSummary
            {
                TotalRuns = 0,
                PromotedCount = 0,
                PendingReviewCount = 0
            },
            activeRun: null,
            workflow: CreateWorkflow("Ready for a new research cycle", "Backtest"));

        hero.FocusLabel.Should().Be("New cycle");
        hero.Summary.Should().Be("Research queue is empty.");
        hero.BadgeText.Should().Be("Setup");
        hero.PrimaryActionId.Should().Be("Backtest");
        hero.PrimaryActionLabel.Should().Be("Start Backtest");
        hero.SecondaryActionId.Should().Be("Watchlist");
        hero.SecondaryActionLabel.Should().Be("Open Watchlists");
        hero.TargetLabel.Should().Be("Target page: Backtest");
    }

    [Fact]
    public void BuildDeskHeroState_WithPromotableRun_PrioritizesTradingReviewAndPromotion()
    {
        var hero = ResearchWorkspaceShellPresentationService.BuildDeskHeroState(
            new ResearchWorkspaceSummary
            {
                TotalRuns = 4,
                PromotedCount = 1,
                PendingReviewCount = 1
            },
            activeRun: CreateActiveRun(canPromoteToPaper: true),
            workflow: CreateWorkflow("Candidate for paper review", "TradingShell"));

        hero.FocusLabel.Should().Be("Promotion review");
        hero.Summary.Should().Contain("ready for paper handoff");
        hero.Detail.Should().Contain("Replay evidence and ledger continuity are ready");
        hero.BadgeText.Should().Be("Ready");
        hero.PrimaryActionId.Should().Be("TradingShell");
        hero.PrimaryActionLabel.Should().Be("Open Trading Review");
        hero.SecondaryActionId.Should().Be("PromoteToPaper");
        hero.SecondaryActionLabel.Should().Be("Promote to Paper");
        hero.TargetLabel.Should().Be("Target page: TradingShell");
    }

    [Fact]
    public void BuildDeskHeroState_WithSelectedRun_RoutesToRunDetailAndPortfolio()
    {
        var hero = ResearchWorkspaceShellPresentationService.BuildDeskHeroState(
            new ResearchWorkspaceSummary
            {
                TotalRuns = 3,
                PromotedCount = 0,
                PendingReviewCount = 0
            },
            activeRun: CreateActiveRun(canPromoteToPaper: false),
            workflow: CreateWorkflow("Review active run", "RunDetail", isBlocking: false));

        hero.FocusLabel.Should().Be("Selected run");
        hero.BadgeText.Should().Be("In review");
        hero.PrimaryActionId.Should().Be("RunDetail");
        hero.SecondaryActionId.Should().Be("RunPortfolio");
        hero.TargetLabel.Should().Be("Target page: RunDetail");
    }

    [Fact]
    public void BuildDeskHeroState_WithPromotionQueue_RoutesToRunBrowser()
    {
        var hero = ResearchWorkspaceShellPresentationService.BuildDeskHeroState(
            new ResearchWorkspaceSummary
            {
                TotalRuns = 5,
                PromotedCount = 1,
                PendingReviewCount = 2
            },
            activeRun: null,
            workflow: CreateWorkflow("Candidate for paper review", "TradingShell"));

        hero.FocusLabel.Should().Be("Promotion queue");
        hero.Summary.Should().Be("2 run(s) are waiting for trading review.");
        hero.BadgeText.Should().Be("Attention");
        hero.PrimaryActionId.Should().Be("StrategyRuns");
        hero.SecondaryActionId.Should().Be("Watchlist");
        hero.TargetLabel.Should().Be("Target page: StrategyRuns");
    }

    [Fact]
    public void BuildDeskHeroState_WithWorkflowNextAction_RoutesToWorkflowTarget()
    {
        var hero = ResearchWorkspaceShellPresentationService.BuildDeskHeroState(
            new ResearchWorkspaceSummary
            {
                TotalRuns = 3,
                PromotedCount = 0,
                PendingReviewCount = 0
            },
            activeRun: null,
            workflow: CreateWorkflow("Research cycle healthy", "RunMat", tone: "Success"));

        hero.FocusLabel.Should().Be("Research cycle");
        hero.BadgeText.Should().Be("Ready");
        hero.PrimaryActionId.Should().Be("RunMat");
        hero.PrimaryActionLabel.Should().Be("Open RunMat");
        hero.SecondaryActionId.Should().Be("StrategyRuns");
        hero.TargetLabel.Should().Be("Target page: RunMat");
    }

    [Fact]
    public async Task OpenRunStudioAsync_RaisesCompositeDockRequest()
    {
        var viewModel = new ResearchWorkspaceShellViewModel();
        ResearchWorkspaceShellActionRequest? captured = null;
        viewModel.ActionRequested += (_, request) => captured = request;

        await viewModel.OpenRunStudioAsync("run-42");

        captured.Should().NotBeNull();
        captured!.Value.Kind.Should().Be(ResearchWorkspaceShellActionKind.OpenRunStudio);
        captured.Value.Parameter.Should().Be("run-42");
    }

    [Fact]
    public void ReviewPromotion_RaisesRunDetailNavigationRequest()
    {
        var viewModel = new ResearchWorkspaceShellViewModel();
        ResearchWorkspaceShellActionRequest? captured = null;
        viewModel.ActionRequested += (_, request) => captured = request;

        viewModel.ReviewPromotion("run-99");

        captured.Should().NotBeNull();
        captured!.Value.Kind.Should().Be(ResearchWorkspaceShellActionKind.Navigate);
        captured.Value.PageTag.Should().Be("RunDetail");
        captured.Value.Parameter.Should().Be("run-99");
    }

    [Fact]
    public async Task OpenBriefingComparisonAsync_RaisesStrategyRunsNavigationRequest()
    {
        var viewModel = new ResearchWorkspaceShellViewModel();
        ResearchWorkspaceShellActionRequest? captured = null;
        viewModel.ActionRequested += (_, request) => captured = request;

        await viewModel.OpenBriefingComparisonAsync("run-17");

        captured.Should().NotBeNull();
        captured!.Value.Kind.Should().Be(ResearchWorkspaceShellActionKind.Navigate);
        captured.Value.PageTag.Should().Be("StrategyRuns");
        captured.Value.Parameter.Should().Be("run-17");
    }

    [Fact]
    public void BuildCommandGroup_DisablesPromotionAndTradingWhenNoActiveRun()
    {
        var group = ResearchWorkspaceShellPresentationService.BuildCommandGroup(
            canPromoteActiveRun: false,
            canOpenTradingCockpit: false);

        group.PrimaryCommands.Single(command => command.Id == "PromoteToPaper").IsEnabled.Should().BeFalse();
        group.PrimaryCommands.Single(command => command.Id == "OpenTradingCockpit").IsEnabled.Should().BeFalse();
    }

    [Fact]
    public void BuildCommandGroup_EnablesPromotionAndTradingWhenRunIsEligible()
    {
        var group = ResearchWorkspaceShellPresentationService.BuildCommandGroup(
            canPromoteActiveRun: true,
            canOpenTradingCockpit: true);

        group.PrimaryCommands.Single(command => command.Id == "PromoteToPaper").IsEnabled.Should().BeTrue();
        group.PrimaryCommands.Single(command => command.Id == "OpenTradingCockpit").IsEnabled.Should().BeTrue();
    }

    private static WorkspaceWorkflowSummary CreateWorkflow(
        string statusLabel,
        string targetPageTag,
        string tone = "Info",
        bool isBlocking = true)
        => new(
            WorkspaceId: "research",
            WorkspaceTitle: "Research",
            StatusLabel: statusLabel,
            StatusDetail: "Workflow detail keeps the research desk next action explicit.",
            StatusTone: tone,
            NextAction: new WorkflowNextAction(
                Label: targetPageTag == "Backtest" ? "Start Backtest" : "Open Target",
                Detail: "Open the next research workflow surface.",
                TargetPageTag: targetPageTag,
                Tone: "Primary"),
            PrimaryBlocker: new WorkflowBlockerSummary(
                Code: "test-blocker",
                Label: "Workflow blocker",
                Detail: "A workflow blocker explains why the research desk needs attention.",
                Tone: isBlocking ? "Warning" : "Info",
                IsBlocking: isBlocking),
            Evidence:
            [
                new WorkflowEvidenceBadge("Promotion", "1 candidate", "Warning")
            ]);

    private static ActiveRunContext CreateActiveRun(bool canPromoteToPaper)
        => new()
        {
            RunId = "run-007",
            StrategyName = "Gamma Rotation",
            ModeLabel = "Backtest",
            StatusLabel = "Completed",
            FundScopeLabel = "Atlas Opportunities",
            RiskSummary = "Replay evidence and audit posture are healthy.",
            ValidationStatus = new TradingWorkspaceStatusItem
            {
                Label = "Replay verified",
                Detail = "Replay evidence and ledger continuity are ready for paper promotion.",
                Tone = TradingWorkspaceStatusTone.Success
            },
            CanPromoteToPaper = canPromoteToPaper
        };
}
