using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using Meridian.Wpf.Models;

namespace Meridian.Wpf.Controls;

public partial class EvidenceLinkChip : UserControl
{
    public static readonly DependencyProperty LabelProperty =
        DependencyProperty.Register(
            nameof(Label),
            typeof(string),
            typeof(EvidenceLinkChip),
            new PropertyMetadata(string.Empty, OnAutomationNamePartChanged));

    public static readonly DependencyProperty SourceProperty =
        DependencyProperty.Register(nameof(Source), typeof(string), typeof(EvidenceLinkChip), new PropertyMetadata(string.Empty));

    public static readonly DependencyProperty TimestampTextProperty =
        DependencyProperty.Register(nameof(TimestampText), typeof(string), typeof(EvidenceLinkChip), new PropertyMetadata(string.Empty));

    public static readonly DependencyProperty TargetProperty =
        DependencyProperty.Register(nameof(Target), typeof(string), typeof(EvidenceLinkChip), new PropertyMetadata(string.Empty));

    public static readonly DependencyProperty DetailProperty =
        DependencyProperty.Register(nameof(Detail), typeof(string), typeof(EvidenceLinkChip), new PropertyMetadata(string.Empty));

    public static readonly DependencyProperty ToneProperty =
        DependencyProperty.Register(nameof(Tone), typeof(string), typeof(EvidenceLinkChip), new PropertyMetadata(WorkspaceTone.Info));

    public static readonly DependencyProperty ActionTextProperty =
        DependencyProperty.Register(nameof(ActionText), typeof(string), typeof(EvidenceLinkChip), new PropertyMetadata("Open"));

    public static readonly DependencyProperty NavigateCommandProperty =
        DependencyProperty.Register(nameof(NavigateCommand), typeof(ICommand), typeof(EvidenceLinkChip), new PropertyMetadata(null));

    public static readonly DependencyProperty CommandParameterProperty =
        DependencyProperty.Register(nameof(CommandParameter), typeof(object), typeof(EvidenceLinkChip), new PropertyMetadata(null));

    public static readonly DependencyProperty ChipAutomationIdProperty =
        DependencyProperty.Register(
            nameof(ChipAutomationId),
            typeof(string),
            typeof(EvidenceLinkChip),
            new PropertyMetadata("EvidenceLinkChip", OnChipAutomationIdChanged));

    public static readonly DependencyProperty LabelAutomationIdProperty =
        DependencyProperty.Register(nameof(LabelAutomationId), typeof(string), typeof(EvidenceLinkChip), new PropertyMetadata("EvidenceLinkLabel"));

    public static readonly DependencyProperty SourceAutomationIdProperty =
        DependencyProperty.Register(nameof(SourceAutomationId), typeof(string), typeof(EvidenceLinkChip), new PropertyMetadata("EvidenceLinkSource"));

    public static readonly DependencyProperty TimestampAutomationIdProperty =
        DependencyProperty.Register(nameof(TimestampAutomationId), typeof(string), typeof(EvidenceLinkChip), new PropertyMetadata("EvidenceLinkTimestamp"));

    public static readonly DependencyProperty TargetAutomationIdProperty =
        DependencyProperty.Register(nameof(TargetAutomationId), typeof(string), typeof(EvidenceLinkChip), new PropertyMetadata("EvidenceLinkTarget"));

    public static readonly DependencyProperty DetailAutomationIdProperty =
        DependencyProperty.Register(nameof(DetailAutomationId), typeof(string), typeof(EvidenceLinkChip), new PropertyMetadata("EvidenceLinkDetail"));

    public static readonly DependencyProperty ActionButtonAutomationIdProperty =
        DependencyProperty.Register(nameof(ActionButtonAutomationId), typeof(string), typeof(EvidenceLinkChip), new PropertyMetadata("EvidenceLinkAction"));

    public EvidenceLinkChip()
    {
        InitializeComponent();
        AutomationProperties.SetAutomationId(this, ChipAutomationId);
        UpdateAutomationName();
    }

    public string Label
    {
        get => (string)GetValue(LabelProperty);
        set => SetValue(LabelProperty, value);
    }

    public string Source
    {
        get => (string)GetValue(SourceProperty);
        set => SetValue(SourceProperty, value);
    }

    public string TimestampText
    {
        get => (string)GetValue(TimestampTextProperty);
        set => SetValue(TimestampTextProperty, value);
    }

    public string Target
    {
        get => (string)GetValue(TargetProperty);
        set => SetValue(TargetProperty, value);
    }

    public string Detail
    {
        get => (string)GetValue(DetailProperty);
        set => SetValue(DetailProperty, value);
    }

    public string Tone
    {
        get => (string)GetValue(ToneProperty);
        set => SetValue(ToneProperty, value);
    }

    public string ActionText
    {
        get => (string)GetValue(ActionTextProperty);
        set => SetValue(ActionTextProperty, value);
    }

    public ICommand? NavigateCommand
    {
        get => (ICommand?)GetValue(NavigateCommandProperty);
        set => SetValue(NavigateCommandProperty, value);
    }

    public object? CommandParameter
    {
        get => GetValue(CommandParameterProperty);
        set => SetValue(CommandParameterProperty, value);
    }

    public string ChipAutomationId
    {
        get => (string)GetValue(ChipAutomationIdProperty);
        set => SetValue(ChipAutomationIdProperty, value);
    }

    public string LabelAutomationId
    {
        get => (string)GetValue(LabelAutomationIdProperty);
        set => SetValue(LabelAutomationIdProperty, value);
    }

    public string SourceAutomationId
    {
        get => (string)GetValue(SourceAutomationIdProperty);
        set => SetValue(SourceAutomationIdProperty, value);
    }

    public string TimestampAutomationId
    {
        get => (string)GetValue(TimestampAutomationIdProperty);
        set => SetValue(TimestampAutomationIdProperty, value);
    }

    public string TargetAutomationId
    {
        get => (string)GetValue(TargetAutomationIdProperty);
        set => SetValue(TargetAutomationIdProperty, value);
    }

    public string DetailAutomationId
    {
        get => (string)GetValue(DetailAutomationIdProperty);
        set => SetValue(DetailAutomationIdProperty, value);
    }

    public string ActionButtonAutomationId
    {
        get => (string)GetValue(ActionButtonAutomationIdProperty);
        set => SetValue(ActionButtonAutomationIdProperty, value);
    }

    private static void OnChipAutomationIdChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is EvidenceLinkChip control && e.NewValue is string automationId)
        {
            AutomationProperties.SetAutomationId(control, automationId);
        }
    }

    private static void OnAutomationNamePartChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is EvidenceLinkChip control)
        {
            control.UpdateAutomationName();
        }
    }

    private void UpdateAutomationName()
        => AutomationProperties.SetName(this, Label);
}
