using System.Windows;
using System.Windows.Controls;

namespace Meridian.Wpf.Workstation.Primitives;

public sealed class WorkstationShellControl : ContentControl
{
    public static readonly DependencyProperty NavigationContentProperty =
        DependencyProperty.Register(nameof(NavigationContent), typeof(object), typeof(WorkstationShellControl));

    public static readonly DependencyProperty HeaderContentProperty =
        DependencyProperty.Register(nameof(HeaderContent), typeof(object), typeof(WorkstationShellControl));

    public static readonly DependencyProperty StatusContentProperty =
        DependencyProperty.Register(nameof(StatusContent), typeof(object), typeof(WorkstationShellControl));

    public object? NavigationContent
    {
        get => GetValue(NavigationContentProperty);
        set => SetValue(NavigationContentProperty, value);
    }

    public object? HeaderContent
    {
        get => GetValue(HeaderContentProperty);
        set => SetValue(HeaderContentProperty, value);
    }

    public object? StatusContent
    {
        get => GetValue(StatusContentProperty);
        set => SetValue(StatusContentProperty, value);
    }
}

public sealed class WorkspaceHostControl : ContentControl
{
    public static readonly DependencyProperty ContextContentProperty =
        DependencyProperty.Register(nameof(ContextContent), typeof(object), typeof(WorkspaceHostControl));

    public static readonly DependencyProperty ToolbarContentProperty =
        DependencyProperty.Register(nameof(ToolbarContent), typeof(object), typeof(WorkspaceHostControl));

    public static readonly DependencyProperty DetailContentProperty =
        DependencyProperty.Register(nameof(DetailContent), typeof(object), typeof(WorkspaceHostControl));

    public object? ContextContent
    {
        get => GetValue(ContextContentProperty);
        set => SetValue(ContextContentProperty, value);
    }

    public object? ToolbarContent
    {
        get => GetValue(ToolbarContentProperty);
        set => SetValue(ToolbarContentProperty, value);
    }

    public object? DetailContent
    {
        get => GetValue(DetailContentProperty);
        set => SetValue(DetailContentProperty, value);
    }
}

public sealed class WorkspaceToolbarControl : ItemsControl
{
}

public sealed class StatusRibbonControl : ItemsControl
{
}

public sealed class MetricTileControl : ContentControl
{
}

public sealed class HealthBadgeControl : ContentControl
{
}

public sealed class TrustBadgeControl : ContentControl
{
}

public sealed class ConnectionStatusIndicatorControl : ContentControl
{
}

public sealed class LoadingOverlayControl : ContentControl
{
}

public sealed class EmptyStatePanelControl : ContentControl
{
}

public sealed class ErrorStatePanelControl : ContentControl
{
}
