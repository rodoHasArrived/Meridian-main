using System.Windows;
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

    private static StackPanel GetPanel(WorkspaceInspectorHostControl control, string name)
        => control.FindName(name).Should().BeOfType<StackPanel>($"{name} should be part of the inspector state contract").Subject;
}

