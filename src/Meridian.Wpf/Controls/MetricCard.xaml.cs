using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using Meridian.Wpf.Models;

namespace Meridian.Wpf.Controls;

public partial class MetricCard : UserControl
{
    public static readonly DependencyProperty TitleProperty =
        DependencyProperty.Register(
            nameof(Title),
            typeof(string),
            typeof(MetricCard),
            new PropertyMetadata(string.Empty, OnAutomationNamePartChanged));

    public static readonly DependencyProperty ValueProperty =
        DependencyProperty.Register(
            nameof(Value),
            typeof(string),
            typeof(MetricCard),
            new PropertyMetadata(string.Empty, OnAutomationNamePartChanged));

    public static readonly DependencyProperty SubTextProperty =
        DependencyProperty.Register(nameof(SubText), typeof(string), typeof(MetricCard), new PropertyMetadata(string.Empty));

    public static readonly DependencyProperty IconGlyphProperty =
        DependencyProperty.Register(nameof(IconGlyph), typeof(string), typeof(MetricCard), new PropertyMetadata(string.Empty));

    public static readonly DependencyProperty ToneProperty =
        DependencyProperty.Register(nameof(Tone), typeof(string), typeof(MetricCard), new PropertyMetadata(WorkspaceTone.Neutral));

    public static readonly DependencyProperty CardAutomationIdProperty =
        DependencyProperty.Register(
            nameof(CardAutomationId),
            typeof(string),
            typeof(MetricCard),
            new PropertyMetadata("MetricCard", OnCardAutomationIdChanged));

    public static readonly DependencyProperty TitleAutomationIdProperty =
        DependencyProperty.Register(nameof(TitleAutomationId), typeof(string), typeof(MetricCard), new PropertyMetadata("MetricCardTitle"));

    public static readonly DependencyProperty ValueAutomationIdProperty =
        DependencyProperty.Register(nameof(ValueAutomationId), typeof(string), typeof(MetricCard), new PropertyMetadata("MetricCardValue"));

    public static readonly DependencyProperty SubTextAutomationIdProperty =
        DependencyProperty.Register(nameof(SubTextAutomationId), typeof(string), typeof(MetricCard), new PropertyMetadata("MetricCardSubText"));

    public static readonly DependencyProperty IconAutomationIdProperty =
        DependencyProperty.Register(nameof(IconAutomationId), typeof(string), typeof(MetricCard), new PropertyMetadata("MetricCardIcon"));

    public MetricCard()
    {
        InitializeComponent();
        AutomationProperties.SetAutomationId(this, CardAutomationId);
        UpdateAutomationName();
    }

    public string Title
    {
        get => (string)GetValue(TitleProperty);
        set => SetValue(TitleProperty, value);
    }

    public string Value
    {
        get => (string)GetValue(ValueProperty);
        set => SetValue(ValueProperty, value);
    }

    public string SubText
    {
        get => (string)GetValue(SubTextProperty);
        set => SetValue(SubTextProperty, value);
    }

    public string IconGlyph
    {
        get => (string)GetValue(IconGlyphProperty);
        set => SetValue(IconGlyphProperty, value);
    }

    public string Tone
    {
        get => (string)GetValue(ToneProperty);
        set => SetValue(ToneProperty, value);
    }

    public string CardAutomationId
    {
        get => (string)GetValue(CardAutomationIdProperty);
        set => SetValue(CardAutomationIdProperty, value);
    }

    public string TitleAutomationId
    {
        get => (string)GetValue(TitleAutomationIdProperty);
        set => SetValue(TitleAutomationIdProperty, value);
    }

    public string ValueAutomationId
    {
        get => (string)GetValue(ValueAutomationIdProperty);
        set => SetValue(ValueAutomationIdProperty, value);
    }

    public string SubTextAutomationId
    {
        get => (string)GetValue(SubTextAutomationIdProperty);
        set => SetValue(SubTextAutomationIdProperty, value);
    }

    public string IconAutomationId
    {
        get => (string)GetValue(IconAutomationIdProperty);
        set => SetValue(IconAutomationIdProperty, value);
    }

    private static void OnCardAutomationIdChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is MetricCard control && e.NewValue is string automationId)
        {
            AutomationProperties.SetAutomationId(control, automationId);
        }
    }

    private static void OnAutomationNamePartChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is MetricCard control)
        {
            control.UpdateAutomationName();
        }
    }

    private void UpdateAutomationName()
    {
        var name = string.IsNullOrWhiteSpace(Value) ? Title : $"{Title}: {Value}";
        AutomationProperties.SetName(this, name);
    }
}
