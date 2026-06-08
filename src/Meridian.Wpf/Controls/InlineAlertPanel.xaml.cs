using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using Meridian.Wpf.Models;

namespace Meridian.Wpf.Controls;

public partial class InlineAlertPanel : UserControl
{
    public static readonly DependencyProperty TitleProperty =
        DependencyProperty.Register(
            nameof(Title),
            typeof(string),
            typeof(InlineAlertPanel),
            new PropertyMetadata(string.Empty, OnAutomationNamePartChanged));

    public static readonly DependencyProperty MessageProperty =
        DependencyProperty.Register(nameof(Message), typeof(string), typeof(InlineAlertPanel), new PropertyMetadata(string.Empty));

    public static readonly DependencyProperty ToneProperty =
        DependencyProperty.Register(nameof(Tone), typeof(string), typeof(InlineAlertPanel), new PropertyMetadata(WorkspaceTone.Info));

    public static readonly DependencyProperty IconGlyphProperty =
        DependencyProperty.Register(nameof(IconGlyph), typeof(string), typeof(InlineAlertPanel), new PropertyMetadata("\uE783"));

    public static readonly DependencyProperty ActionTextProperty =
        DependencyProperty.Register(nameof(ActionText), typeof(string), typeof(InlineAlertPanel), new PropertyMetadata(string.Empty));

    public static readonly DependencyProperty ActionCommandProperty =
        DependencyProperty.Register(nameof(ActionCommand), typeof(ICommand), typeof(InlineAlertPanel), new PropertyMetadata(null));

    public static readonly DependencyProperty PanelAutomationIdProperty =
        DependencyProperty.Register(
            nameof(PanelAutomationId),
            typeof(string),
            typeof(InlineAlertPanel),
            new PropertyMetadata("InlineAlertPanel", OnPanelAutomationIdChanged));

    public static readonly DependencyProperty IconContainerAutomationIdProperty =
        DependencyProperty.Register(nameof(IconContainerAutomationId), typeof(string), typeof(InlineAlertPanel), new PropertyMetadata("InlineAlertIconContainer"));

    public static readonly DependencyProperty IconAutomationIdProperty =
        DependencyProperty.Register(nameof(IconAutomationId), typeof(string), typeof(InlineAlertPanel), new PropertyMetadata("InlineAlertIcon"));

    public static readonly DependencyProperty TitleAutomationIdProperty =
        DependencyProperty.Register(nameof(TitleAutomationId), typeof(string), typeof(InlineAlertPanel), new PropertyMetadata("InlineAlertTitle"));

    public static readonly DependencyProperty MessageAutomationIdProperty =
        DependencyProperty.Register(nameof(MessageAutomationId), typeof(string), typeof(InlineAlertPanel), new PropertyMetadata("InlineAlertMessage"));

    public static readonly DependencyProperty ActionButtonAutomationIdProperty =
        DependencyProperty.Register(nameof(ActionButtonAutomationId), typeof(string), typeof(InlineAlertPanel), new PropertyMetadata("InlineAlertAction"));

    public InlineAlertPanel()
    {
        InitializeComponent();
        AutomationProperties.SetAutomationId(this, PanelAutomationId);
        UpdateAutomationName();
    }

    public string Title
    {
        get => (string)GetValue(TitleProperty);
        set => SetValue(TitleProperty, value);
    }

    public string Message
    {
        get => (string)GetValue(MessageProperty);
        set => SetValue(MessageProperty, value);
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

    public string ActionText
    {
        get => (string)GetValue(ActionTextProperty);
        set => SetValue(ActionTextProperty, value);
    }

    public ICommand? ActionCommand
    {
        get => (ICommand?)GetValue(ActionCommandProperty);
        set => SetValue(ActionCommandProperty, value);
    }

    public string PanelAutomationId
    {
        get => (string)GetValue(PanelAutomationIdProperty);
        set => SetValue(PanelAutomationIdProperty, value);
    }

    public string IconContainerAutomationId
    {
        get => (string)GetValue(IconContainerAutomationIdProperty);
        set => SetValue(IconContainerAutomationIdProperty, value);
    }

    public string IconAutomationId
    {
        get => (string)GetValue(IconAutomationIdProperty);
        set => SetValue(IconAutomationIdProperty, value);
    }

    public string TitleAutomationId
    {
        get => (string)GetValue(TitleAutomationIdProperty);
        set => SetValue(TitleAutomationIdProperty, value);
    }

    public string MessageAutomationId
    {
        get => (string)GetValue(MessageAutomationIdProperty);
        set => SetValue(MessageAutomationIdProperty, value);
    }

    public string ActionButtonAutomationId
    {
        get => (string)GetValue(ActionButtonAutomationIdProperty);
        set => SetValue(ActionButtonAutomationIdProperty, value);
    }

    private static void OnPanelAutomationIdChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is InlineAlertPanel control && e.NewValue is string automationId)
        {
            AutomationProperties.SetAutomationId(control, automationId);
        }
    }

    private static void OnAutomationNamePartChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is InlineAlertPanel control)
        {
            control.UpdateAutomationName();
        }
    }

    private void UpdateAutomationName()
        => AutomationProperties.SetName(this, Title);
}
