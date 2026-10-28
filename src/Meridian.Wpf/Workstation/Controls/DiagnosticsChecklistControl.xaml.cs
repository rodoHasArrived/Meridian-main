using System.Windows;
using System.Windows.Controls;
using Meridian.Wpf.Workstation.Models;

namespace Meridian.Wpf.Workstation.Controls;

/// <summary>
/// Displays a workstation diagnostics checklist through its bound presentation model.
/// </summary>
public partial class DiagnosticsChecklistControl : UserControl
{
    public static readonly DependencyProperty ChecklistProperty =
        DependencyProperty.Register(
            nameof(Checklist),
            typeof(DiagnosticsChecklistModel),
            typeof(DiagnosticsChecklistControl),
            new PropertyMetadata(null));

    public DiagnosticsChecklistControl()
    {
        InitializeComponent();
    }

    public DiagnosticsChecklistModel? Checklist
    {
        get => (DiagnosticsChecklistModel?)GetValue(ChecklistProperty);
        set => SetValue(ChecklistProperty, value);
    }
}
