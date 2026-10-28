using System.Windows;
using System.Windows.Controls;

namespace Meridian.Wpf.Workstation.Layout;

/// <summary>
/// Provides a content host styled as a split-pane workstation layout.
/// </summary>
public sealed class SplitPaneLayoutControl : ContentControl
{
}

/// <summary>
/// Provides a content host for dockable workstation panels.
/// </summary>
public sealed class DockablePanelHostControl : ContentControl
{
}

/// <summary>
/// Provides a tabbed host for workstation workflows.
/// </summary>
public sealed class WorkflowTabHostControl : TabControl
{
}

/// <summary>
/// Hosts detail content with an independently bindable open state.
/// </summary>
public sealed class DetailPaneControl : ContentControl
{
    public static readonly DependencyProperty IsOpenProperty =
        DependencyProperty.Register(nameof(IsOpen), typeof(bool), typeof(DetailPaneControl), new PropertyMetadata(true));

    public bool IsOpen
    {
        get => (bool)GetValue(IsOpenProperty);
        set => SetValue(IsOpenProperty, value);
    }
}

/// <summary>
/// Hosts inspector content with separate header and footer content slots.
/// </summary>
public sealed class InspectorPanelControl : ContentControl
{
    public static readonly DependencyProperty HeaderContentProperty =
        DependencyProperty.Register(nameof(HeaderContent), typeof(object), typeof(InspectorPanelControl));

    public static readonly DependencyProperty FooterContentProperty =
        DependencyProperty.Register(nameof(FooterContent), typeof(object), typeof(InspectorPanelControl));

    public object? HeaderContent
    {
        get => GetValue(HeaderContentProperty);
        set => SetValue(HeaderContentProperty, value);
    }

    public object? FooterContent
    {
        get => GetValue(FooterContentProperty);
        set => SetValue(FooterContentProperty, value);
    }
}
