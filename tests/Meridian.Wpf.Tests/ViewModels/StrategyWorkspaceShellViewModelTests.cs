using Meridian.Contracts.Workstation;
using Meridian.Wpf.Models;
using Meridian.Wpf.Services;
using Meridian.Wpf.ViewModels;

namespace Meridian.Wpf.Tests.ViewModels;

public sealed class StrategyWorkspaceShellViewModelTests
{
    [Fact]
    public void BuildDeskHeroState_WithoutRecordedRuns_PrioritizesBacktestAndWatchlists()
    {
        var hero = StrategyWorkspaceShellPresentationService.BuildDeskHeroState(
            new StrategyWorkspaceSummary
            {
                TotalRuns = 0,
                PromotedCount = 0,
                PendingReviewCount = 0
            },
            activeRun: null,
            workflow: CreateWorkflow("Ready for a new strategy cycle", "Backtest"));

        hero.FocusLabel.Should().Be("New cycle");
        hero.Summary.Should().Be("Strategy queue is empty.");
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
        var hero = StrategyWorkspaceShellPresentationService.BuildDeskHeroState(
            new StrategyWorkspaceSummary
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
        var hero = StrategyWorkspaceShellPresentationService.BuildDeskHeroState(
            new StrategyWorkspaceSummary
            {
                TotalRuns = 3,
                PromotedCount = 0,
                PendingReviewCount = 0
            },
            activeRun: CreateActiveRun(canPromoteToPaper: false),
            workflow: CreateWorkflow("Review active run", "RunDetail", isBlocking: false));

        hero.FocusLabel.Should().Be("Selected run");
        hero.Summary.Should().Be("Gamma Rotation is the active Strategy run.");
        hero.Summary.Should().NotContain("active research run");
        hero.BadgeText.Should().Be("In review");
        hero.PrimaryActionId.Should().Be("RunDetail");
        hero.SecondaryActionId.Should().Be("RunPortfolio");
        hero.TargetLabel.Should().Be("Target page: RunDetail");
    }

    [Fact]
    public void BuildDeskHeroState_WithPromotionQueue_RoutesToRunBrowser()
    {
        var hero = StrategyWorkspaceShellPresentationService.BuildDeskHeroState(
            new StrategyWorkspaceSummary
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
        var hero = StrategyWorkspaceShellPresentationService.BuildDeskHeroState(
            new StrategyWorkspaceSummary
            {
                TotalRuns = 3,
                PromotedCount = 0,
                PendingReviewCount = 0
            },
            activeRun: null,
            workflow: CreateWorkflow("Strategy cycle healthy", "RunMat", tone: "Success"));

        hero.FocusLabel.Should().Be("Strategy cycle");
        hero.BadgeText.Should().Be("Ready");
        hero.PrimaryActionId.Should().Be("RunMat");
        hero.PrimaryActionLabel.Should().Be("Open RunMat");
        hero.SecondaryActionId.Should().Be("StrategyRuns");
        hero.TargetLabel.Should().Be("Target page: RunMat");
    }

    [Fact]
    public async Task OpenRunStudioAsync_RaisesCompositeDockRequest()
    {
        var viewModel = new StrategyWorkspaceShellViewModel();
        StrategyWorkspaceShellActionRequest? captured = null;
        viewModel.ActionRequested += (_, request) => captured = request;

        await viewModel.OpenRunStudioAsync("run-42");

        captured.Should().NotBeNull();
        captured!.Value.Kind.Should().Be(StrategyWorkspaceShellActionKind.OpenRunStudio);
        captured.Value.Parameter.Should().Be("run-42");
    }

    [Fact]
    public void Constructor_UsesCanonicalStrategyWorkspaceShell()
    {
        var viewModel = new StrategyWorkspaceShellViewModel();

        viewModel.WorkspaceDefinition.WorkspaceId.Should().Be("strategy");
        viewModel.ScenarioCoverageText.Should().Be("No strategy session restored.");
        viewModel.BriefingSummaryText.Should().Be("Pinned strategy context, watchlists, saved comparisons, and workflow alerts.");
    }

    [Fact]
    public void FallbackWorkflow_UsesCanonicalStrategyWorkspaceCopy()
    {
        StrategyWorkspaceShellPresentationDefaults.Workflow.WorkspaceId.Should().Be("strategy");
        StrategyWorkspaceShellPresentationDefaults.Workflow.WorkspaceTitle.Should().Be("Strategy");
        StrategyWorkspaceShellPresentationDefaults.Workflow.StatusLabel.Should().Be("Ready for a new strategy cycle");
        StrategyWorkspaceShellPresentationDefaults.Workflow.NextAction.Detail.Should().Contain("strategy workspace");
    }

    [Fact]
    public void StrategyShellSource_UsesCanonicalLogTags()
    {
        var viewModel = File.ReadAllText(GetRepositoryFilePath(@"src\Meridian.Wpf\ViewModels\StrategyWorkspaceShellViewModel.cs"));
        var service = File.ReadAllText(GetRepositoryFilePath(@"src\Meridian.Wpf\Services\StrategyWorkspaceShellPresentationService.cs"));

        viewModel.Should().Contain("[StrategyWorkspaceShell]");
        viewModel.Should().NotContain("[ResearchWorkspaceShell]");
        service.Should().Contain("[StrategyWorkspaceShell]");
        service.Should().NotContain("[ResearchWorkspaceShell]");
    }

    [Fact]
    public void ReviewPromotion_RaisesRunDetailNavigationRequest()
    {
        var viewModel = new StrategyWorkspaceShellViewModel();
        StrategyWorkspaceShellActionRequest? captured = null;
        viewModel.ActionRequested += (_, request) => captured = request;

        viewModel.ReviewPromotion("run-99");

        captured.Should().NotBeNull();
        captured!.Value.Kind.Should().Be(StrategyWorkspaceShellActionKind.Navigate);
        captured.Value.PageTag.Should().Be("RunDetail");
        captured.Value.Parameter.Should().Be("run-99");
    }

    [Fact]
    public async Task OpenBriefingComparisonAsync_RaisesStrategyRunsNavigationRequest()
    {
        var viewModel = new StrategyWorkspaceShellViewModel();
        StrategyWorkspaceShellActionRequest? captured = null;
        viewModel.ActionRequested += (_, request) => captured = request;

        await viewModel.OpenBriefingComparisonAsync("run-17");

        captured.Should().NotBeNull();
        captured!.Value.Kind.Should().Be(StrategyWorkspaceShellActionKind.Navigate);
        captured.Value.PageTag.Should().Be("StrategyRuns");
        captured.Value.Parameter.Should().Be("run-17");
    }

    [Fact]
    public void BuildCommandGroup_DisablesPromotionAndTradingWhenNoActiveRun()
    {
        var group = StrategyWorkspaceShellPresentationService.BuildCommandGroup(
            canPromoteActiveRun: false,
            canOpenTradingCockpit: false);

        group.PrimaryCommands.Single(command => command.Id == "PromoteToPaper").IsEnabled.Should().BeFalse();
        group.PrimaryCommands.Single(command => command.Id == "OpenTradingCockpit").IsEnabled.Should().BeFalse();
        group.PrimaryCommands.Single(command => command.Id == "ResetStudio").Description.Should().Be("Reset the strategy studio layout");
        group.SecondaryCommands.Single(command => command.Id == "FundAuditTrail").Description.Should().Be("Open Accounting audit trail");
    }

    [Fact]
    public void BuildCommandGroup_EnablesPromotionAndTradingWhenRunIsEligible()
    {
        var group = StrategyWorkspaceShellPresentationService.BuildCommandGroup(
            canPromoteActiveRun: true,
            canOpenTradingCockpit: true);

        group.PrimaryCommands.Single(command => command.Id == "PromoteToPaper").IsEnabled.Should().BeTrue();
        group.PrimaryCommands.Single(command => command.Id == "OpenTradingCockpit").IsEnabled.Should().BeTrue();
    }

    [Fact]
    public void BuildCommandGroup_WhenPromotionAndTradingAreBlocked_ExplainsWhyRatherThanRestatingTheDescription()
    {
        var group = StrategyWorkspaceShellPresentationService.BuildCommandGroup(
            canPromoteActiveRun: false,
            canOpenTradingCockpit: false);

        var promote = group.PrimaryCommands.Single(command => command.Id == "PromoteToPaper");
        var cockpit = group.PrimaryCommands.Single(command => command.Id == "OpenTradingCockpit");

        promote.DisabledReason.Should().Be("Select a completed run that is eligible for paper promotion.");
        cockpit.DisabledReason.Should().Be("Select a run before opening it in the trading cockpit.");

        promote.DisabledReason.Should().NotBe(promote.Description);
        cockpit.DisabledReason.Should().NotBe(cockpit.Description);
    }

    [Fact]
    public void BuildCommandGroup_WhenPromotionAndTradingAreAvailable_PublishesNoDisabledReason()
    {
        var group = StrategyWorkspaceShellPresentationService.BuildCommandGroup(
            canPromoteActiveRun: true,
            canOpenTradingCockpit: true);

        group.PrimaryCommands.Single(command => command.Id == "PromoteToPaper").DisabledReason.Should().BeEmpty();
        group.PrimaryCommands.Single(command => command.Id == "OpenTradingCockpit").DisabledReason.Should().BeEmpty();
    }

    private static WorkspaceWorkflowSummary CreateWorkflow(
        string statusLabel,
        string targetPageTag,
        string tone = "Info",
        bool isBlocking = true)
        => new(
            WorkspaceId: "strategy",
            WorkspaceTitle: "Strategy",
            StatusLabel: statusLabel,
            StatusDetail: "Workflow detail keeps the strategy desk next action explicit.",
            StatusTone: tone,
            NextAction: new WorkflowNextAction(
                Label: targetPageTag == "Backtest" ? "Start Backtest" : "Open Target",
                Detail: "Open the next strategy workflow surface.",
                TargetPageTag: targetPageTag,
                Tone: "Primary"),
            PrimaryBlocker: new WorkflowBlockerSummary(
                Code: "test-blocker",
                Label: "Workflow blocker",
                Detail: "A workflow blocker explains why the strategy desk needs attention.",
                Tone: isBlocking ? "Warning" : "Info",
                IsBlocking: isBlocking),
            Evidence:
            [
                new WorkflowEvidenceBadge("Promotion", "1 candidate", "Warning")
            ]);

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

        throw new FileNotFoundException($"Could not locate repository file '{relativePath}'.");
    }

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
