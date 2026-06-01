using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using Meridian.Wpf.Tests.Support;
using Meridian.Wpf.Views;

namespace Meridian.Wpf.Tests.Views;

public sealed class WorkspaceShellPrimitiveControlsTests
{
    [Fact]
    public void WorkspaceCommandSurface_ShouldExposeReusableAutomationContract()
    {
        WpfTestThread.Run(() =>
        {
            RunMatUiAutomationFacade.EnsureApplicationResources();
            var control = new WorkspaceCommandSurfaceControl
            {
                SurfaceAutomationId = "WorkspaceCommandSurfaceTest",
                FixtureBannerAutomationId = "FixtureBannerTest",
                PageHeaderAutomationId = "PageHeaderTest",
                RefreshButtonAutomationId = "RefreshButtonTest",
                NotificationsButtonAutomationId = "NotificationsButtonTest",
                CommandPaletteButtonAutomationId = "CommandPaletteButtonTest"
            };

            Render(control, 1200, 360);

            AutomationProperties.GetAutomationId(control).Should().Be("WorkspaceCommandSurfaceTest");
            AutomationProperties.GetAutomationId(Get<Border>(control, "FixtureModeBanner")).Should().Be("FixtureBannerTest");
            AutomationProperties.GetAutomationId(Get<Border>(control, "WorkspacePageHeader")).Should().Be("PageHeaderTest");
            AutomationProperties.GetAutomationId(Get<Button>(control, "RefreshButton")).Should().Be("RefreshButtonTest");
            AutomationProperties.GetAutomationId(Get<Button>(control, "NotificationsButton")).Should().Be("NotificationsButtonTest");
            AutomationProperties.GetAutomationId(Get<Button>(control, "CommandPaletteButton")).Should().Be("CommandPaletteButtonTest");
        });
    }

    [Fact]
    public void WorkspaceEvidenceStrip_ShouldExposeReusableAutomationContract()
    {
        WpfTestThread.Run(() =>
        {
            RunMatUiAutomationFacade.EnsureApplicationResources();
            var control = new WorkspaceEvidenceStripControl
            {
                StripAutomationId = "WorkspaceEvidenceStripTest",
                PrimaryOperatorWorkflowStripAutomationId = "PrimaryOperatorWorkflowStripTest",
                WorkflowSummaryStripAutomationId = "WorkflowSummaryStripTest",
                PrimaryActionButtonAutomationId = "PrimaryWorkflowActionTest",
                SecondaryToggleButtonAutomationId = "SecondaryWorkflowToggleTest"
            };

            Render(control, 1200, 360);

            AutomationProperties.GetAutomationId(control).Should().Be("WorkspaceEvidenceStripTest");
            AutomationProperties.GetAutomationId(Get<Border>(control, "PrimaryOperatorWorkflowStrip")).Should().Be("PrimaryOperatorWorkflowStripTest");
            AutomationProperties.GetAutomationId(Get<Border>(control, "WorkflowSummaryStrip")).Should().Be("WorkflowSummaryStripTest");
            AutomationProperties.GetAutomationId(Get<Button>(control, "PrimaryWorkflowActionButton")).Should().Be("PrimaryWorkflowActionTest");
            AutomationProperties.GetAutomationId(Get<Button>(control, "SecondaryWorkflowToggleButton")).Should().Be("SecondaryWorkflowToggleTest");
        });
    }

    private static T Get<T>(Control control, string name)
        where T : FrameworkElement
        => control.FindName(name).Should().BeOfType<T>($"{name} should be part of the shared shell primitive contract").Subject;

    private static void Render(Control control, double width, double height)
    {
        control.Measure(new Size(width, height));
        control.Arrange(new Rect(0, 0, width, height));
        control.UpdateLayout();
    }
}
