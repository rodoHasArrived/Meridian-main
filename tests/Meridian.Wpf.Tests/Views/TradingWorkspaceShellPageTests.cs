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
}
