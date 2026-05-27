using System.Windows;
using System.Windows.Controls;
using Meridian.Wpf.Workstation.Models;

namespace Meridian.Wpf.Workstation.Controls;

public partial class WorkstationStatePanelControl : UserControl
{
    public static readonly DependencyProperty StateProperty =
        DependencyProperty.Register(
            nameof(State),
            typeof(WorkstationStateModel),
            typeof(WorkstationStatePanelControl),
            new PropertyMetadata(null));

    public WorkstationStatePanelControl()
    {
        InitializeComponent();
    }

    public WorkstationStateModel? State
    {
        get => (WorkstationStateModel?)GetValue(StateProperty);
        set => SetValue(StateProperty, value);
    }
}
