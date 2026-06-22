using Meridian.Ui.Services;
using Meridian.Wpf.Models;
using Meridian.Wpf.Shell.ViewModels;
using Meridian.Wpf.Services;

namespace Meridian.Wpf.Tests.Services;

public sealed class WorkspaceLayoutManagerTests
{
    [Fact]
    public void BuildLayoutState_WithWorkbenchPreset_ShouldUseWorkspaceShellPresetPanes()
    {
        var manager = new WorkspaceLayoutManager();
        var state = new WorkspaceShellState(
            WorkspaceId: "strategy",
            LayoutId: "strategy-studio",
            DisplayName: "Strategy Studio",
            LayoutScopeKey: "Fund:alpha-credit",
            WindowMode: BoundedWindowMode.WorkbenchPreset,
            LayoutPresetId: "strategy-compare",
            ActiveRunId: "run-123");

        var layout = manager.BuildLayoutState(state);

        layout.LayoutId.Should().Be("strategy-studio");
        layout.DisplayName.Should().Be("Strategy Studio");
        layout.OperatingContextKey.Should().Be("Fund:alpha-credit");
        layout.WindowMode.Should().Be(BoundedWindowMode.WorkbenchPreset);
        layout.LayoutPresetId.Should().Be("strategy-compare");
        layout.Panes.Select(pane => pane.PageTag).Should().ContainInOrder(
            "Backtest",
            "StrategyRuns",
            "RunDetail",
            "Charts");
        layout.Panes.Should().ContainSingle(pane => pane.PaneId == "RunDetail:run-123");
        layout.FloatingWindows.Should().BeEmpty();
    }

    [Fact]
    public void BuildLayoutState_WithFocusedMode_ShouldNormalizeFloatingPanesToTabs()
    {
        WorkspaceLayoutManager.NormalizeDetachableAction(BoundedWindowMode.Focused, PaneDropAction.FloatWindow)
            .Should().Be(PaneDropAction.OpenTab);

        WorkspaceLayoutManager.NormalizeDetachableAction(BoundedWindowMode.DockFloat, PaneDropAction.FloatWindow)
            .Should().Be(PaneDropAction.FloatWindow);
    }

    [Fact]
    public void ApplyLayout_WithPaneHost_ShouldPopulateSplitPaneModelFromWorkspaceDefinition()
    {
        var manager = new WorkspaceLayoutManager();
        var paneHost = new PaneHostViewModel();
        var state = new WorkspaceShellState(
            WorkspaceId: "accounting",
            LayoutId: "accounting-workspace",
            DisplayName: "Accounting Workspace",
            LayoutScopeKey: "Fund:alpha-credit",
            WindowMode: BoundedWindowMode.WorkbenchPreset,
            LayoutPresetId: "reconciliation-workbench");

        manager.ApplyLayout(paneHost, state);

        paneHost.GetVisibleContentStates().Select(pane => pane.PageTag).Should().ContainInOrder(
            "FundReconciliation",
            "FundTrialBalance",
            "FundLedger");
    }
}
