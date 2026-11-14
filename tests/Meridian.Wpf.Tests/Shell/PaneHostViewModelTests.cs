using System.Windows;
using Meridian.Wpf.Models;
using Meridian.Wpf.Shell.Services;
using Meridian.Wpf.Shell.ViewModels;

namespace Meridian.Wpf.Tests.Shell;

public sealed class PaneHostViewModelTests
{
    public static TheoryData<string, int> ExistingPaneTargets => new()
    {
        { nameof(PaneLayouts.Single), 0 },
        { nameof(PaneLayouts.StrategyData), 0 },
        { nameof(PaneLayouts.StrategyData), 1 },
        { nameof(PaneLayouts.TradingCockpit), 0 },
        { nameof(PaneLayouts.TradingCockpit), 1 },
        { nameof(PaneLayouts.TradingCockpit), 2 },
        { "OperationsResearch", 0 },
        { "OperationsResearch", 1 },
        { "OperationsResearch", 2 }
    };

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

    [Theory]
    [MemberData(nameof(ExistingPaneTargets))]
    public void AssignPageToPane_ExistingPane_PreservesLayoutOtherAssignmentsAndActivePane(
        string layoutName,
        int paneIndex)
    {
        var layout = GetLayout(layoutName);
        var paneHost = CreatePopulatedPaneHost(layout);
        paneHost.SetActivePaneCommand.Execute(paneIndex);
        var expectedTags = paneHost.GetVisibleContentStates().Select(state => state.PageTag).ToArray();
        expectedTags[paneIndex] = "RunRisk";
        var layoutChanges = 0;
        paneHost.LayoutChanged += (_, _) => layoutChanges++;

        paneHost.AssignPageToPane("RunRisk", paneIndex);

        paneHost.SelectedLayout.Should().BeSameAs(layout);
        paneHost.GetVisibleContentStates().Select(state => state.PageTag).Should().Equal(expectedTags);
        paneHost.ActivePaneIndex.Should().Be(paneIndex);
        layoutChanges.Should().Be(0);
    }

    [Theory]
    [MemberData(nameof(ExistingPaneTargets))]
    public void ApplyPaneDrop_ReplaceExistingPane_PreservesLayoutOtherAssignmentsAndActivePane(
        string layoutName,
        int paneIndex)
    {
        var layout = GetLayout(layoutName);
        var paneHost = CreatePopulatedPaneHost(layout);
        paneHost.SetActivePaneCommand.Execute(paneIndex);
        var expectedTags = paneHost.GetVisibleContentStates().Select(state => state.PageTag).ToArray();
        expectedTags[paneIndex] = "RunRisk";
        var layoutChanges = 0;
        paneHost.LayoutChanged += (_, _) => layoutChanges++;

        var activePane = paneHost.ApplyPaneDrop("RunRisk", paneIndex, PaneDropAction.Replace);

        paneHost.SelectedLayout.Should().BeSameAs(layout);
        paneHost.GetVisibleContentStates().Select(state => state.PageTag).Should().Equal(expectedTags);
        paneHost.ActivePaneIndex.Should().Be(paneIndex);
        activePane.Should().Be(paneIndex);
        layoutChanges.Should().Be(0);
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

    private static PaneHostViewModel CreatePopulatedPaneHost(PaneLayout layout)
    {
        var paneHost = new PaneHostViewModel(new ShellRouteRegistry());
        string[] pageTags = ["TradingShell", "OrderBook", "PositionBlotter"];
        for (var paneIndex = 0; paneIndex < layout.PaneCount; paneIndex++)
        {
            paneHost.AssignPageToPane(pageTags[paneIndex], paneIndex);
        }

        paneHost.SelectLayoutCommand.Execute(layout);
        return paneHost;
    }

    private static PaneLayout GetLayout(string layoutName) => layoutName switch
    {
        nameof(PaneLayouts.Single) => PaneLayouts.Single,
        nameof(PaneLayouts.StrategyData) => PaneLayouts.StrategyData,
        nameof(PaneLayouts.TradingCockpit) => PaneLayouts.TradingCockpit,
        "OperationsResearch" => new PaneLayout(
            PaneLayoutKind.TradingCockpit,
            "Operations Research",
            "□",
            3,
            [new GridLength(1, GridUnitType.Star), new GridLength(2, GridUnitType.Star), new GridLength(3, GridUnitType.Star)]),
        _ => throw new ArgumentOutOfRangeException(nameof(layoutName), layoutName, "Unknown test layout.")
    };
}
