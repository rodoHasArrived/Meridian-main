using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;

namespace Meridian.Wpf.Controls;

public partial class EmptyStatePanel : UserControl
{
    public static readonly DependencyProperty TitleProperty =
        DependencyProperty.Register(
            nameof(Title),
            typeof(string),
            typeof(EmptyStatePanel),
            new PropertyMetadata(string.Empty, OnTitleChanged));

    public static readonly DependencyProperty DescriptionProperty =
        DependencyProperty.Register(nameof(Description), typeof(string), typeof(EmptyStatePanel), new PropertyMetadata(string.Empty));

    public static readonly DependencyProperty IconGlyphProperty =
        DependencyProperty.Register(nameof(IconGlyph), typeof(string), typeof(EmptyStatePanel), new PropertyMetadata(string.Empty));

    public static readonly DependencyProperty ActionTextProperty =
        DependencyProperty.Register(nameof(ActionText), typeof(string), typeof(EmptyStatePanel), new PropertyMetadata(string.Empty));

    public static readonly DependencyProperty ActionCommandProperty =
        DependencyProperty.Register(nameof(ActionCommand), typeof(ICommand), typeof(EmptyStatePanel), new PropertyMetadata(null));

    public static readonly DependencyProperty PanelAutomationIdProperty =
        DependencyProperty.Register(
            nameof(PanelAutomationId),
            typeof(string),
            typeof(EmptyStatePanel),
            new PropertyMetadata("EmptyStatePanel", OnPanelAutomationIdChanged));

    public static readonly DependencyProperty IconAutomationIdProperty =
        DependencyProperty.Register(nameof(IconAutomationId), typeof(string), typeof(EmptyStatePanel), new PropertyMetadata("EmptyStateIcon"));

    public static readonly DependencyProperty TitleAutomationIdProperty =
        DependencyProperty.Register(nameof(TitleAutomationId), typeof(string), typeof(EmptyStatePanel), new PropertyMetadata("EmptyStateTitle"));

    public static readonly DependencyProperty DescriptionAutomationIdProperty =
        DependencyProperty.Register(nameof(DescriptionAutomationId), typeof(string), typeof(EmptyStatePanel), new PropertyMetadata("EmptyStateDescription"));

    public static readonly DependencyProperty ActionButtonAutomationIdProperty =
        DependencyProperty.Register(nameof(ActionButtonAutomationId), typeof(string), typeof(EmptyStatePanel), new PropertyMetadata("EmptyStateAction"));

    public EmptyStatePanel()
    {
        InitializeComponent();
        AutomationProperties.SetAutomationId(this, PanelAutomationId);
        AutomationProperties.SetName(this, Title);
    }

    public string Title
    {
        get => (string)GetValue(TitleProperty);
        set => SetValue(TitleProperty, value);
    }

    public string Description
    {
        get => (string)GetValue(DescriptionProperty);
        set => SetValue(DescriptionProperty, value);
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

    public string DescriptionAutomationId
    {
        get => (string)GetValue(DescriptionAutomationIdProperty);
        set => SetValue(DescriptionAutomationIdProperty, value);
    }

    public string ActionButtonAutomationId
    {
        get => (string)GetValue(ActionButtonAutomationIdProperty);
        set => SetValue(ActionButtonAutomationIdProperty, value);
    }

    private static void OnPanelAutomationIdChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is EmptyStatePanel control && e.NewValue is string automationId)
        {
            AutomationProperties.SetAutomationId(control, automationId);
        }
    }

    private static void OnTitleChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is EmptyStatePanel control)
        {
            AutomationProperties.SetName(control, e.NewValue as string ?? string.Empty);
        }
    }
}
