using System.Windows;
using System.Windows.Controls;

namespace Meridian.Wpf.Views;

public partial class WorkspaceCommandSurfaceControl : UserControl
{
    public static readonly DependencyProperty SurfaceAutomationIdProperty =
        DependencyProperty.Register(nameof(SurfaceAutomationId), typeof(string), typeof(WorkspaceCommandSurfaceControl), new PropertyMetadata("WorkspaceCommandSurface"));

    public static readonly DependencyProperty FixtureBannerAutomationIdProperty =
        DependencyProperty.Register(nameof(FixtureBannerAutomationId), typeof(string), typeof(WorkspaceCommandSurfaceControl), new PropertyMetadata("FixtureModeBanner"));

    public static readonly DependencyProperty PageHeaderAutomationIdProperty =
        DependencyProperty.Register(nameof(PageHeaderAutomationId), typeof(string), typeof(WorkspaceCommandSurfaceControl), new PropertyMetadata("WorkspacePageHeader"));

    public static readonly DependencyProperty RefreshButtonAutomationIdProperty =
        DependencyProperty.Register(nameof(RefreshButtonAutomationId), typeof(string), typeof(WorkspaceCommandSurfaceControl), new PropertyMetadata("ShellRefreshButton"));

    public static readonly DependencyProperty NotificationsButtonAutomationIdProperty =
        DependencyProperty.Register(nameof(NotificationsButtonAutomationId), typeof(string), typeof(WorkspaceCommandSurfaceControl), new PropertyMetadata("ShellNotificationsButton"));

    public static readonly DependencyProperty CommandPaletteButtonAutomationIdProperty =
        DependencyProperty.Register(nameof(CommandPaletteButtonAutomationId), typeof(string), typeof(WorkspaceCommandSurfaceControl), new PropertyMetadata("ShellCommandPaletteButton"));

    public WorkspaceCommandSurfaceControl()
    {
        InitializeComponent();
    }

    public string SurfaceAutomationId
    {
        get => (string)GetValue(SurfaceAutomationIdProperty);
        set => SetValue(SurfaceAutomationIdProperty, value);
    }

    public string FixtureBannerAutomationId
    {
        get => (string)GetValue(FixtureBannerAutomationIdProperty);
        set => SetValue(FixtureBannerAutomationIdProperty, value);
    }

    public string PageHeaderAutomationId
    {
        get => (string)GetValue(PageHeaderAutomationIdProperty);
        set => SetValue(PageHeaderAutomationIdProperty, value);
    }

    public string RefreshButtonAutomationId
    {
        get => (string)GetValue(RefreshButtonAutomationIdProperty);
        set => SetValue(RefreshButtonAutomationIdProperty, value);
    }

    public string NotificationsButtonAutomationId
    {
        get => (string)GetValue(NotificationsButtonAutomationIdProperty);
        set => SetValue(NotificationsButtonAutomationIdProperty, value);
    }

    public string CommandPaletteButtonAutomationId
    {
        get => (string)GetValue(CommandPaletteButtonAutomationIdProperty);
        set => SetValue(CommandPaletteButtonAutomationIdProperty, value);
    }
}
