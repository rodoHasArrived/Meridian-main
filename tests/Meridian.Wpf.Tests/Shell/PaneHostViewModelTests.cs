using Meridian.Wpf.Models;
using Meridian.Wpf.Shell.Services;
using Meridian.Wpf.Shell.ViewModels;

namespace Meridian.Wpf.Tests.Shell;

public sealed class PaneHostViewModelTests
{
    [Fact]
    public void AssignPageToPane_CompatibilityAlias_StoresCanonicalContentState()
    {
        var paneHost = new PaneHostViewModel(new ShellRouteRegistry());

        paneHost.AssignPageToPane("ResearchShell", 0);

        var state = paneHost.GetPaneContentState(0);
        state.Should().NotBeNull();
        state!.PaneIndex.Should().Be(0);
        state.PageTag.Should().Be("StrategyShell");
        state.CanonicalPageTag.Should().Be("StrategyShell");
        state.WorkspaceId.Should().Be("strategy");
        paneHost.GetAssignedPageTag(0).Should().Be("StrategyShell");
    }

    [Fact]
    public void ApplyPaneDrop_SplitRight_AddsPaneAndActivatesInsertedRoute()
    {
        var paneHost = new PaneHostViewModel(new ShellRouteRegistry());
        paneHost.AssignPageToPane("TradingShell", 0);

        var activePane = paneHost.ApplyPaneDrop("Blotter", 0, PaneDropAction.SplitRight);

        activePane.Should().Be(1);
        paneHost.SelectedLayout.PaneCount.Should().Be(2);
        paneHost.ActivePaneIndex.Should().Be(1);
        paneHost.GetAssignedPageTag(0).Should().Be("TradingShell");
        paneHost.GetAssignedPageTag(1).Should().Be("PositionBlotter");
        paneHost.GetPaneContentState(1)!.CanonicalPageTag.Should().Be("PositionBlotter");
    }

    [Fact]
    public void AssignPageToPane_BeyondSupportedPaneCount_ShouldClampToLastVisiblePane()
    {
        var paneHost = new PaneHostViewModel(new ShellRouteRegistry());

        paneHost.AssignPageToPane("TradingShell", 0);
        paneHost.AssignPageToPane("OrderBook", 1);
        paneHost.AssignPageToPane("PositionBlotter", 2);
        paneHost.AssignPageToPane("RunRisk", 3);

        paneHost.SelectedLayout.PaneCount.Should().Be(3);
        paneHost.ActivePaneIndex.Should().Be(2);
        paneHost.GetAssignedPageTag(2).Should().Be("RunRisk");
    }

    [Fact]
    public void GetVisibleContentStates_IgnoresEmptyOrUnknownPaneAssignments()
    {
        var paneHost = new PaneHostViewModel(new ShellRouteRegistry());
        paneHost.AssignPageToPane("StrategyShell", 0);
        paneHost.ApplyPaneDrop("MissingRoute", 0, PaneDropAction.SplitRight);

        var states = paneHost.GetVisibleContentStates();

        states.Select(state => state.PageTag).Should().Equal("StrategyShell");
    }
}
