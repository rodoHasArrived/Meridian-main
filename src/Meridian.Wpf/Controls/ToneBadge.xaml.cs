using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using Meridian.Wpf.Models;

namespace Meridian.Wpf.Controls;

public partial class ToneBadge : UserControl
{
    public static readonly DependencyProperty TextProperty =
        DependencyProperty.Register(
            nameof(Text),
            typeof(string),
            typeof(ToneBadge),
            new PropertyMetadata(string.Empty, OnTextChanged));

    public static readonly DependencyProperty IconGlyphProperty =
        DependencyProperty.Register(nameof(IconGlyph), typeof(string), typeof(ToneBadge), new PropertyMetadata(string.Empty));

    public static readonly DependencyProperty ToneProperty =
        DependencyProperty.Register(nameof(Tone), typeof(string), typeof(ToneBadge), new PropertyMetadata(WorkspaceTone.Neutral));

    public static readonly DependencyProperty BadgeAutomationIdProperty =
        DependencyProperty.Register(
            nameof(BadgeAutomationId),
            typeof(string),
            typeof(ToneBadge),
            new PropertyMetadata("ToneBadge", OnBadgeAutomationIdChanged));

    public static readonly DependencyProperty IconAutomationIdProperty =
        DependencyProperty.Register(nameof(IconAutomationId), typeof(string), typeof(ToneBadge), new PropertyMetadata("ToneBadgeIcon"));

    public static readonly DependencyProperty TextAutomationIdProperty =
        DependencyProperty.Register(nameof(TextAutomationId), typeof(string), typeof(ToneBadge), new PropertyMetadata("ToneBadgeText"));

    public ToneBadge()
    {
        InitializeComponent();
        AutomationProperties.SetAutomationId(this, BadgeAutomationId);
        AutomationProperties.SetName(this, Text);
    }

    public string Text
    {
        get => (string)GetValue(TextProperty);
        set => SetValue(TextProperty, value);
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

    public string BadgeAutomationId
    {
        get => (string)GetValue(BadgeAutomationIdProperty);
        set => SetValue(BadgeAutomationIdProperty, value);
    }

    public string IconAutomationId
    {
        get => (string)GetValue(IconAutomationIdProperty);
        set => SetValue(IconAutomationIdProperty, value);
    }

    public string TextAutomationId
    {
        get => (string)GetValue(TextAutomationIdProperty);
        set => SetValue(TextAutomationIdProperty, value);
    }

    private static void OnBadgeAutomationIdChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is ToneBadge control && e.NewValue is string automationId)
        {
            AutomationProperties.SetAutomationId(control, automationId);
        }
    }

    private static void OnTextChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is ToneBadge control)
        {
            AutomationProperties.SetName(control, e.NewValue as string ?? string.Empty);
        }
    }
}
