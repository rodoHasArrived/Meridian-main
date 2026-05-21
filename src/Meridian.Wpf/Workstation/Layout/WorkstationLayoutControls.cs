using System.Windows;
using System.Windows.Controls;

namespace Meridian.Wpf.Workstation.Layout;

public sealed class SplitPaneLayoutControl : ContentControl;

public sealed class DockablePanelHostControl : ContentControl;

public sealed class WorkflowTabHostControl : TabControl;

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
