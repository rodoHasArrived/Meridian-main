using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using Meridian.Wpf.Tests.Support;
using Meridian.Wpf.Views;

namespace Meridian.Wpf.Tests.Views;

public sealed class WorkspaceInspectorHostControlTests
{
    [Theory]
    [InlineData(WorkspaceInspectorState.Empty, "EmptyStatePanel")]
    [InlineData(WorkspaceInspectorState.Selected, "SelectedStatePanel")]
    [InlineData(WorkspaceInspectorState.Loading, "LoadingStatePanel")]
    [InlineData(WorkspaceInspectorState.Error, "ErrorStatePanel")]
    public void WorkspaceInspectorHost_ShouldExposeSingleActiveState(WorkspaceInspectorState state, string visiblePanelName)
    {
        WpfTestThread.Run(() =>
        {
            RunMatUiAutomationFacade.EnsureApplicationResources();
            var control = new WorkspaceInspectorHostControl
            {
                InspectorState = state
            };

            control.Measure(new Size(320, 480));
            control.Arrange(new Rect(0, 0, 320, 480));
            control.UpdateLayout();

            GetPanel(control, visiblePanelName).Visibility.Should().Be(Visibility.Visible);
            foreach (var panelName in new[] { "EmptyStatePanel", "SelectedStatePanel", "LoadingStatePanel", "ErrorStatePanel" }.Except([visiblePanelName]))
            {
                GetPanel(control, panelName).Visibility.Should().Be(Visibility.Collapsed);
            }
        });
    }

    [Fact]
    public void WorkspaceInspectorHost_ShouldExposeConfigurableAutomationIdsAndLabels()
    {
        WpfTestThread.Run(() =>
        {
            RunMatUiAutomationFacade.EnsureApplicationResources();
            var control = new WorkspaceInspectorHostControl
            {
                HostAutomationId = "TradingInspectorHost",
                EmptyAutomationId = "TradingInspectorEmpty",
                SelectedAutomationId = "TradingInspectorSelected",
                LoadingAutomationId = "TradingInspectorLoading",
                ErrorAutomationId = "TradingInspectorError",
                LoadingTitle = "Loading position detail",
                SelectedEvidenceTabHeader = "Run evidence",
                SelectedActionsTabHeader = "Desk actions"
            };

            control.Measure(new Size(320, 480));
            control.Arrange(new Rect(0, 0, 320, 480));
            control.UpdateLayout();

            AutomationProperties.GetAutomationId(control).Should().Be("TradingInspectorHost");
            AutomationProperties.GetAutomationId(GetPanel(control, "EmptyStatePanel")).Should().Be("TradingInspectorEmpty");
            AutomationProperties.GetAutomationId(GetPanel(control, "SelectedStatePanel")).Should().Be("TradingInspectorSelected");
            AutomationProperties.GetAutomationId(GetPanel(control, "LoadingStatePanel")).Should().Be("TradingInspectorLoading");
            AutomationProperties.GetAutomationId(GetPanel(control, "ErrorStatePanel")).Should().Be("TradingInspectorError");

            GetTabItem(control, "WorkspaceInspectorEvidenceTab").Header.Should().Be("Run evidence");
            GetTabItem(control, "WorkspaceInspectorActionsTab").Header.Should().Be("Desk actions");
        });
    }

    [Fact]
    public void WorkspaceInspectorHost_ShouldRaiseConfiguredErrorAction()
    {
        WpfTestThread.Run(() =>
        {
            RunMatUiAutomationFacade.EnsureApplicationResources();
            var actionId = string.Empty;
            var control = new WorkspaceInspectorHostControl
            {
                InspectorState = WorkspaceInspectorState.Error,
                ErrorActionLabel = "Retry detail",
                ErrorActionId = "RetryPositionDetail"
            };
            control.InspectorActionInvoked += (_, args) => actionId = args.ActionId;

            control.Measure(new Size(320, 480));
            control.Arrange(new Rect(0, 0, 320, 480));
            control.UpdateLayout();

            var button = control.FindName("ErrorActionButton").Should().BeOfType<Button>().Subject;
            button.Visibility.Should().Be(Visibility.Visible);
            AutomationProperties.GetAutomationId(button).Should().Be("RetryPositionDetail");

            button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

            actionId.Should().Be("RetryPositionDetail");
        });
    }

    private static StackPanel GetPanel(WorkspaceInspectorHostControl control, string name)
        => control.FindName(name).Should().BeOfType<StackPanel>($"{name} should be part of the inspector state contract").Subject;

    private static TabItem GetTabItem(WorkspaceInspectorHostControl control, string automationId)
    {
        var tabs = control.FindName("WorkspaceInspectorTabs").Should().BeOfType<TabControl>().Subject;
        return tabs.Items
            .OfType<TabItem>()
            .Single(tab => AutomationProperties.GetAutomationId(tab) == automationId);
    }
}
