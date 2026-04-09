using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using Meridian.Wpf.Models;
using Meridian.Wpf.Tests.Support;
using Meridian.Wpf.Views;

namespace Meridian.Wpf.Tests.Views;

public sealed class SplitPaneHostControlTests
{
    [Fact]
    public void Layout_TwoPanes_HitTestReturnsPaneContainingPoint()
    {
        WpfTestThread.Run(() =>
        {
            var sut = CreateArrangedControl(PaneLayouts.ResearchStudio);

            InvokeHitTestPaneIndex(sut, new Point(100, 100)).Should().Be(0);
            InvokeHitTestPaneIndex(sut, new Point(420, 100)).Should().Be(1);
        });
    }

    [Fact]
    public void HitTestPaneIndex_OutsidePaneBounds_FallsBackToActivePaneIndex()
    {
        WpfTestThread.Run(() =>
        {
            var sut = CreateArrangedControl(PaneLayouts.ResearchStudio);
            sut.ActivePaneIndex = 1;

            InvokeHitTestPaneIndex(sut, new Point(-10, -10)).Should().Be(1);
        });
    }

    [Fact]
    public void TradingCockpitLayout_BottomRailHitTestTargetsBottomPane()
    {
        WpfTestThread.Run(() =>
        {
            var sut = CreateArrangedControl(PaneLayouts.TradingCockpit);
            InvokeHitTestPaneIndex(sut, new Point(400, 260)).Should().Be(3);
        });
    }

    [Fact]
    public void HitTestDropAction_LeftEdge_RequestsSplitLeft()
    {
        WpfTestThread.Run(() =>
        {
            var sut = CreateArrangedControl(PaneLayouts.Single);

            InvokeHitTestDropAction(sut, new Point(10, 150)).Should().Be(PaneDropAction.SplitLeft);
            InvokeHitTestDropAction(sut, new Point(790, 150)).Should().Be(PaneDropAction.SplitRight);
            InvokeHitTestDropAction(sut, new Point(400, 290)).Should().Be(PaneDropAction.SplitBelow);
        });
    }

    private static SplitPaneHostControl CreateArrangedControl(PaneLayout layout)
    {
        var sut = new SplitPaneHostControl
        {
            Width = 800,
            Height = 300,
            Layout = layout
        };

        var window = new Window
        {
            Width = 800,
            Height = 300,
            Content = sut,
            ShowInTaskbar = false,
            WindowStyle = WindowStyle.None,
            AllowsTransparency = true,
            Opacity = 0
        };

        window.Show();
        window.UpdateLayout();
        sut.UpdateLayout();

        return sut;
    }

    private static int InvokeHitTestPaneIndex(SplitPaneHostControl sut, Point point)
    {
        var method = typeof(SplitPaneHostControl).GetMethod(
            "HitTestPaneIndex",
            BindingFlags.Instance | BindingFlags.NonPublic);

        method.Should().NotBeNull();
        return (int)method!.Invoke(sut, [point])!;
    }

    private static PaneDropAction InvokeHitTestDropAction(SplitPaneHostControl sut, Point point)
    {
        var method = typeof(SplitPaneHostControl).GetMethod(
            "HitTestDropAction",
            BindingFlags.Instance | BindingFlags.NonPublic);

        method.Should().NotBeNull();
        return (PaneDropAction)method!.Invoke(sut, [point])!;
    }
}
