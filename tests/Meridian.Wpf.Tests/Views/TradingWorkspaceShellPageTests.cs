using System.IO;
using Meridian.Ui.Services;
using Meridian.Wpf.Models;
using Meridian.Wpf.Views;

namespace Meridian.Wpf.Tests.Views;

public sealed class TradingWorkspaceShellPageTests
{
    [Fact]
    public void ResolvePortfolioNavigationTarget_WithActiveRun_UsesRunPortfolioInsideTradingShell()
    {
        var target = TradingWorkspaceShellPage.ResolvePortfolioNavigationTarget(new ActiveRunContext
        {
            RunId = "paper-run-42",
            StrategyName = "Alpha Mean Reversion"
        });

        target.PageTag.Should().Be("RunPortfolio");
        target.Action.Should().Be(PaneDropAction.SplitLeft);
        target.RunId.Should().Be("paper-run-42");
    }

    [Fact]
    public void ResolvePortfolioNavigationTarget_WithoutActiveRun_FallsBackToAccountPortfolio()
    {
        var target = TradingWorkspaceShellPage.ResolvePortfolioNavigationTarget(null);

        target.PageTag.Should().Be("AccountPortfolio");
        target.Action.Should().Be(PaneDropAction.Replace);
        target.RunId.Should().BeNull();
    }

    [Fact]
    public void TradingWorkspaceShellPageSource_ShouldProjectConsistentDegradedStatusCard()
    {
        var code = File.ReadAllText(GetRepositoryFilePath(@"src\Meridian.Wpf\Views\TradingWorkspaceShellPage.xaml.cs"));

        code.Should().Contain("ApplyStatusCardPresentation(BuildDegradedStatusCardPresentation());");
        code.Should().Contain("internal static TradingStatusCardPresentation BuildStatusCardPresentation(TradingWorkspaceSummary summary)");
        code.Should().Contain("internal static TradingStatusCardPresentation BuildDegradedStatusCardPresentation()");
        code.Should().Contain("Label = \"Promotion refresh degraded\"");
        code.Should().Contain("Label = \"Audit refresh degraded\"");
        code.Should().Contain("Label = \"Validation refresh degraded\"");
    }

    [Fact]
    public void TradingWorkspaceShellPageSource_ShouldNotExposePrematureDeepReviewActions()
    {
        var code = File.ReadAllText(GetRepositoryFilePath(@"src\Meridian.Wpf\Views\TradingWorkspaceShellPage.xaml.cs"));
        var xaml = File.ReadAllText(GetRepositoryFilePath(@"src\Meridian.Wpf\Views\TradingWorkspaceShellPage.xaml"));

        code.Should().NotContain("Id = \"RunDetail\"");
        code.Should().NotContain("Id = \"EventReplay\"");
        code.Should().NotContain("Id = \"CollectionSessions\"");
        code.Should().NotContain("case \"RunDetail\":");
        code.Should().NotContain("case \"EventReplay\":");
        code.Should().NotContain("case \"CollectionSessions\":");
        xaml.Should().NotContain("Deeper Review");
        xaml.Should().NotContain("OpenRunReview_Click");
        xaml.Should().NotContain("OpenReplayReview_Click");
        xaml.Should().NotContain("OpenCollectionSessions_Click");
    }

    [Fact]
    public void TradingWorkspaceShellPageSource_ShouldReplaceGenericAwaitingRunsCopyWithWorkflowGuidance()
    {
        var code = File.ReadAllText(GetRepositoryFilePath(@"src\Meridian.Wpf\Views\TradingWorkspaceShellPage.xaml.cs"));
        var xaml = File.ReadAllText(GetRepositoryFilePath(@"src\Meridian.Wpf\Views\TradingWorkspaceShellPage.xaml"));

        xaml.Should().Contain("Workflow Status");
        xaml.Should().Contain("Handoff");
        xaml.Should().Contain("Primary Blocker");
        xaml.Should().Contain("Next Action");
        xaml.Should().Contain("TradingWorkflowPrimaryButton");
        xaml.Should().NotContain("Awaiting runs");

        code.Should().Contain("GetTradingWorkflowSummaryAsync");
        code.Should().Contain("ApplyWorkflowGuidance");
        code.Should().Contain("OpenWorkflowNextAction_Click");
        code.Should().Contain("Target page:");
    }

    [Fact]
    public void TradingWorkspaceShellPageSource_ShouldPlaceDeskActionsAheadOfNarrativeSupportPanels()
    {
        var xaml = File.ReadAllText(GetRepositoryFilePath(@"src\Meridian.Wpf\Views\TradingWorkspaceShellPage.xaml"));

        xaml.Should().Contain("Desk Lanes &amp; Supporting Tools");
        xaml.IndexOf("Active Positions", StringComparison.Ordinal).Should().BeLessThan(xaml.IndexOf("Paper Runs", StringComparison.Ordinal));
        xaml.IndexOf("Workflow Status", StringComparison.Ordinal).Should().BeLessThan(xaml.IndexOf("Desk Lanes &amp; Supporting Tools", StringComparison.Ordinal));
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
