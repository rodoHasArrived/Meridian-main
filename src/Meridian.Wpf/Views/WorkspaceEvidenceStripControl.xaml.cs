using System.Windows;
using System.Windows.Controls;

namespace Meridian.Wpf.Views;

public partial class WorkspaceEvidenceStripControl : UserControl
{
    public static readonly DependencyProperty StripAutomationIdProperty =
        DependencyProperty.Register(nameof(StripAutomationId), typeof(string), typeof(WorkspaceEvidenceStripControl), new PropertyMetadata("WorkspaceEvidenceStrip"));

    public static readonly DependencyProperty WorkflowSummaryStripAutomationIdProperty =
        DependencyProperty.Register(nameof(WorkflowSummaryStripAutomationId), typeof(string), typeof(WorkspaceEvidenceStripControl), new PropertyMetadata("WorkflowSummaryStrip"));

    public static readonly DependencyProperty PrimaryActionButtonAutomationIdProperty =
        DependencyProperty.Register(nameof(PrimaryActionButtonAutomationId), typeof(string), typeof(WorkspaceEvidenceStripControl), new PropertyMetadata("PrimaryWorkflowActionButton"));

    public static readonly DependencyProperty SecondaryToggleButtonAutomationIdProperty =
        DependencyProperty.Register(nameof(SecondaryToggleButtonAutomationId), typeof(string), typeof(WorkspaceEvidenceStripControl), new PropertyMetadata("SecondaryWorkflowToggleButton"));

    public WorkspaceEvidenceStripControl()
    {
        InitializeComponent();
    }

    public string StripAutomationId
    {
        get => (string)GetValue(StripAutomationIdProperty);
        set => SetValue(StripAutomationIdProperty, value);
    }

    public string WorkflowSummaryStripAutomationId
    {
        get => (string)GetValue(WorkflowSummaryStripAutomationIdProperty);
        set => SetValue(WorkflowSummaryStripAutomationIdProperty, value);
    }

    public string PrimaryActionButtonAutomationId
    {
        get => (string)GetValue(PrimaryActionButtonAutomationIdProperty);
        set => SetValue(PrimaryActionButtonAutomationIdProperty, value);
    }

    public string SecondaryToggleButtonAutomationId
    {
        get => (string)GetValue(SecondaryToggleButtonAutomationIdProperty);
        set => SetValue(SecondaryToggleButtonAutomationIdProperty, value);
    }
}
