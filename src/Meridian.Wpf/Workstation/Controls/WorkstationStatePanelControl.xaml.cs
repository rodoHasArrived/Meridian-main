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

    public static readonly DependencyProperty TitleAutomationIdProperty =
        DependencyProperty.Register(
            nameof(TitleAutomationId),
            typeof(string),
            typeof(WorkstationStatePanelControl),
            new PropertyMetadata("WorkstationStateTitle"));

    public static readonly DependencyProperty DetailAutomationIdProperty =
        DependencyProperty.Register(
            nameof(DetailAutomationId),
            typeof(string),
            typeof(WorkstationStatePanelControl),
            new PropertyMetadata("WorkstationStateDetail"));

    public static readonly DependencyProperty EvidenceAutomationIdProperty =
        DependencyProperty.Register(
            nameof(EvidenceAutomationId),
            typeof(string),
            typeof(WorkstationStatePanelControl),
            new PropertyMetadata("WorkstationStateEvidence"));

    public WorkstationStatePanelControl()
    {
        InitializeComponent();
    }

    public WorkstationStateModel? State
    {
        get => (WorkstationStateModel?)GetValue(StateProperty);
        set => SetValue(StateProperty, value);
    }

    public string TitleAutomationId
    {
        get => (string)GetValue(TitleAutomationIdProperty);
        set => SetValue(TitleAutomationIdProperty, value);
    }

    public string DetailAutomationId
    {
        get => (string)GetValue(DetailAutomationIdProperty);
        set => SetValue(DetailAutomationIdProperty, value);
    }

    public string EvidenceAutomationId
    {
        get => (string)GetValue(EvidenceAutomationIdProperty);
        set => SetValue(EvidenceAutomationIdProperty, value);
    }
}
