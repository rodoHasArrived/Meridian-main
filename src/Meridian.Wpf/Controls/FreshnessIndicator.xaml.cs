using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using Meridian.Wpf.Models;

namespace Meridian.Wpf.Controls;

public partial class FreshnessIndicator : UserControl
{
    public static readonly DependencyProperty LabelProperty =
        DependencyProperty.Register(
            nameof(Label),
            typeof(string),
            typeof(FreshnessIndicator),
            new PropertyMetadata("As of", OnAutomationNamePartChanged));

    public static readonly DependencyProperty ValueProperty =
        DependencyProperty.Register(
            nameof(Value),
            typeof(string),
            typeof(FreshnessIndicator),
            new PropertyMetadata(string.Empty, OnAutomationNamePartChanged));

    public static readonly DependencyProperty DetailProperty =
        DependencyProperty.Register(nameof(Detail), typeof(string), typeof(FreshnessIndicator), new PropertyMetadata(string.Empty));

    public static readonly DependencyProperty ToneProperty =
        DependencyProperty.Register(nameof(Tone), typeof(string), typeof(FreshnessIndicator), new PropertyMetadata(WorkspaceTone.Neutral));

    public static readonly DependencyProperty IconGlyphProperty =
        DependencyProperty.Register(nameof(IconGlyph), typeof(string), typeof(FreshnessIndicator), new PropertyMetadata("\uE823"));

    public static readonly DependencyProperty IndicatorAutomationIdProperty =
        DependencyProperty.Register(
            nameof(IndicatorAutomationId),
            typeof(string),
            typeof(FreshnessIndicator),
            new PropertyMetadata("FreshnessIndicator", OnIndicatorAutomationIdChanged));

    public static readonly DependencyProperty IconAutomationIdProperty =
        DependencyProperty.Register(nameof(IconAutomationId), typeof(string), typeof(FreshnessIndicator), new PropertyMetadata("FreshnessIndicatorIcon"));

    public static readonly DependencyProperty LabelAutomationIdProperty =
        DependencyProperty.Register(nameof(LabelAutomationId), typeof(string), typeof(FreshnessIndicator), new PropertyMetadata("FreshnessIndicatorLabel"));

    public static readonly DependencyProperty ValueAutomationIdProperty =
        DependencyProperty.Register(nameof(ValueAutomationId), typeof(string), typeof(FreshnessIndicator), new PropertyMetadata("FreshnessIndicatorValue"));

    public static readonly DependencyProperty DetailAutomationIdProperty =
        DependencyProperty.Register(nameof(DetailAutomationId), typeof(string), typeof(FreshnessIndicator), new PropertyMetadata("FreshnessIndicatorDetail"));

    public FreshnessIndicator()
    {
        InitializeComponent();
        AutomationProperties.SetAutomationId(this, IndicatorAutomationId);
        UpdateAutomationName();
    }

    public string Label
    {
        get => (string)GetValue(LabelProperty);
        set => SetValue(LabelProperty, value);
    }

    public string Value
    {
        get => (string)GetValue(ValueProperty);
        set => SetValue(ValueProperty, value);
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

    public string IconGlyph
    {
        get => (string)GetValue(IconGlyphProperty);
        set => SetValue(IconGlyphProperty, value);
    }

    public string IndicatorAutomationId
    {
        get => (string)GetValue(IndicatorAutomationIdProperty);
        set => SetValue(IndicatorAutomationIdProperty, value);
    }

    public string IconAutomationId
    {
        get => (string)GetValue(IconAutomationIdProperty);
        set => SetValue(IconAutomationIdProperty, value);
    }

    public string LabelAutomationId
    {
        get => (string)GetValue(LabelAutomationIdProperty);
        set => SetValue(LabelAutomationIdProperty, value);
    }

    public string ValueAutomationId
    {
        get => (string)GetValue(ValueAutomationIdProperty);
        set => SetValue(ValueAutomationIdProperty, value);
    }

    public string DetailAutomationId
    {
        get => (string)GetValue(DetailAutomationIdProperty);
        set => SetValue(DetailAutomationIdProperty, value);
    }

    private static void OnIndicatorAutomationIdChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is FreshnessIndicator control && e.NewValue is string automationId)
        {
            AutomationProperties.SetAutomationId(control, automationId);
        }
    }

    private static void OnAutomationNamePartChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is FreshnessIndicator control)
        {
            control.UpdateAutomationName();
        }
    }

    private void UpdateAutomationName()
    {
        var name = string.IsNullOrWhiteSpace(Value) ? Label : $"{Label}: {Value}";
        AutomationProperties.SetName(this, name);
    }
}
