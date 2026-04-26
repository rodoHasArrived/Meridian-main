using System.IO;
using Meridian.Contracts.Workstation;
using Meridian.Wpf.Models;
using Meridian.Wpf.Views;

namespace Meridian.Wpf.Tests.Views;

public sealed class ResearchWorkspaceShellPageTests
{
    [Fact]
    public void BuildDeskHeroState_WithoutRecordedRuns_PrioritizesBacktestAndWatchlists()
    {
        var hero = ResearchWorkspaceShellPage.BuildDeskHeroState(
            new ResearchWorkspaceSummary
            {
                TotalRuns = 0,
                PromotedCount = 0,
                PendingReviewCount = 0
            },
            activeRun: null,
            workflow: new WorkspaceWorkflowSummary(
                WorkspaceId: "research",
                WorkspaceTitle: "Research",
                StatusLabel: "Ready for a new research cycle",
                StatusDetail: "No live workflow summary is available, so the shell is using deterministic fallback guidance.",
                StatusTone: "Info",
                NextAction: new WorkflowNextAction(
                    Label: "Start Backtest",
                    Detail: "Launch a new simulation from the research workspace.",
                    TargetPageTag: "Backtest",
                    Tone: "Primary"),
                PrimaryBlocker: new WorkflowBlockerSummary(
                    Code: "no-runs",
                    Label: "No research runs recorded",
                    Detail: "Record the first backtest to create a review and promotion queue.",
                    Tone: "Info",
                    IsBlocking: false),
                Evidence: []));

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
        var hero = ResearchWorkspaceShellPage.BuildDeskHeroState(
            new ResearchWorkspaceSummary
            {
                TotalRuns = 4,
                PromotedCount = 1,
                PendingReviewCount = 1
            },
            activeRun: new ActiveRunContext
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
                CanPromoteToPaper = true
            },
            workflow: new WorkspaceWorkflowSummary(
                WorkspaceId: "research",
                WorkspaceTitle: "Research",
                StatusLabel: "Candidate for paper review",
                StatusDetail: "The leading backtest has complete evidence and can move into paper review.",
                StatusTone: "Success",
                NextAction: new WorkflowNextAction(
                    Label: "Send to Trading Review",
                    Detail: "Carry the best candidate into the trading shell.",
                    TargetPageTag: "TradingShell",
                    Tone: "Primary"),
                PrimaryBlocker: new WorkflowBlockerSummary(
                    Code: "trading-review",
                    Label: "Trading review pending",
                    Detail: "Portfolio, ledger, and audit evidence should stay visible during the handoff.",
                    Tone: "Warning",
                    IsBlocking: true),
                Evidence:
                [
                    new WorkflowEvidenceBadge("Promotion", "1 candidate", "Warning")
                ]));

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
    public void ResearchWorkspaceShellPageSource_ShouldExposeDeskBriefingHero()
    {
        var code = File.ReadAllText(GetRepositoryFilePath(@"src\Meridian.Wpf\Views\ResearchWorkspaceShellPage.xaml.cs"));
        var xaml = File.ReadAllText(GetRepositoryFilePath(@"src\Meridian.Wpf\Views\ResearchWorkspaceShellPage.xaml"));

        xaml.Should().Contain("Research Desk Briefing");
        xaml.Should().Contain("ResearchHeroBadgeText");
        xaml.Should().Contain("ResearchHeroPrimaryActionButton");
        xaml.Should().Contain("ResearchHeroSecondaryActionButton");
        xaml.Should().Contain("Next Handoff");

        code.Should().Contain("internal readonly record struct ResearchDeskHeroState(");
        code.Should().Contain("internal static ResearchDeskHeroState BuildDeskHeroState(");
        code.Should().Contain("OnResearchHeroPrimaryActionClick");
        code.Should().Contain("ExecuteHeroAction");
        code.Should().Contain("ApplyHeroTone(");
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
