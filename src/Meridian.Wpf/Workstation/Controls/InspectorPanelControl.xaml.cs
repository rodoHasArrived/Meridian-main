using System.Windows;
using System.Windows.Controls;
using Meridian.Wpf.Workstation.Models;

namespace Meridian.Wpf.Workstation.Controls;

public partial class InspectorPanelControl : UserControl
{
    public static readonly DependencyProperty PanelProperty =
        DependencyProperty.Register(
            nameof(Panel),
            typeof(InspectorPanelModel),
            typeof(InspectorPanelControl),
            new PropertyMetadata(null));

    public InspectorPanelControl()
    {
        InitializeComponent();
    }

    public InspectorPanelModel? Panel
    {
        get => (InspectorPanelModel?)GetValue(PanelProperty);
        set => SetValue(PanelProperty, value);
    }
}
