using System.Windows;
using System.Windows.Controls;

namespace Meridian.Wpf.Workstation.Primitives;

/// <summary>
/// Hosts workstation content with separate navigation, header, and status content slots.
/// </summary>
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

/// <summary>
/// Hosts workspace content with separate context, toolbar, and detail content slots.
/// </summary>
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

/// <summary>
/// Provides an items-control surface for workspace toolbar actions.
/// </summary>
public sealed class WorkspaceToolbarControl : ItemsControl
{
}

/// <summary>
/// Provides an items-control surface for workstation status indicators.
/// </summary>
public sealed class StatusRibbonControl : ItemsControl
{
}

/// <summary>
/// Provides a styled content surface for metric values.
/// </summary>
public sealed class MetricTileControl : ContentControl
{
}

/// <summary>
/// Provides a styled content surface for health-status presentation.
/// </summary>
public sealed class HealthBadgeControl : ContentControl
{
}

/// <summary>
/// Provides a content surface for trust-status presentation.
/// </summary>
public sealed class TrustBadgeControl : ContentControl
{
}

/// <summary>
/// Provides a content surface for connection-status presentation.
/// </summary>
public sealed class ConnectionStatusIndicatorControl : ContentControl
{
}

/// <summary>
/// Provides a content surface for loading overlays.
/// </summary>
public sealed class LoadingOverlayControl : ContentControl
{
}

/// <summary>
/// Provides a content surface for an empty-state explanation and action.
/// </summary>
public sealed class EmptyStatePanelControl : ContentControl
{
}

/// <summary>
/// Provides a content surface for an error explanation and recovery action.
/// </summary>
public sealed class ErrorStatePanelControl : ContentControl
{
}
