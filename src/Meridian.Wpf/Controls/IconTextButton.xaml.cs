using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;

namespace Meridian.Wpf.Controls;

public partial class IconTextButton : UserControl
{
    public static readonly RoutedEvent ClickEvent = EventManager.RegisterRoutedEvent(
        nameof(Click),
        RoutingStrategy.Bubble,
        typeof(RoutedEventHandler),
        typeof(IconTextButton));

    public static readonly DependencyProperty IconGlyphProperty =
        DependencyProperty.Register(nameof(IconGlyph), typeof(string), typeof(IconTextButton), new PropertyMetadata(string.Empty));

    public static readonly DependencyProperty TextProperty =
        DependencyProperty.Register(
            nameof(Text),
            typeof(string),
            typeof(IconTextButton),
            new PropertyMetadata(string.Empty, OnTextChanged));

    public static readonly DependencyProperty CommandProperty =
        DependencyProperty.Register(nameof(Command), typeof(ICommand), typeof(IconTextButton), new PropertyMetadata(null));

    public static readonly DependencyProperty CommandParameterProperty =
        DependencyProperty.Register(nameof(CommandParameter), typeof(object), typeof(IconTextButton), new PropertyMetadata(null));

    public static readonly DependencyProperty ButtonStyleProperty =
        DependencyProperty.Register(nameof(ButtonStyle), typeof(Style), typeof(IconTextButton), new PropertyMetadata(null));

    public static readonly DependencyProperty ButtonAutomationIdProperty =
        DependencyProperty.Register(
            nameof(ButtonAutomationId),
            typeof(string),
            typeof(IconTextButton),
            new PropertyMetadata("IconTextButton", OnButtonAutomationIdChanged));

    public static readonly DependencyProperty IconAutomationIdProperty =
        DependencyProperty.Register(nameof(IconAutomationId), typeof(string), typeof(IconTextButton), new PropertyMetadata("IconTextButtonIcon"));

    public static readonly DependencyProperty TextAutomationIdProperty =
        DependencyProperty.Register(nameof(TextAutomationId), typeof(string), typeof(IconTextButton), new PropertyMetadata("IconTextButtonText"));

    public event RoutedEventHandler Click
    {
        add => AddHandler(ClickEvent, value);
        remove => RemoveHandler(ClickEvent, value);
    }

    public IconTextButton()
    {
        InitializeComponent();
        AutomationProperties.SetAutomationId(this, ButtonAutomationId);
        AutomationProperties.SetName(this, Text);
    }

    private void OnInternalButtonClick(object sender, RoutedEventArgs e)
    {
        RaiseEvent(new RoutedEventArgs(ClickEvent, this));
    }

    public string IconGlyph
    {
        get => (string)GetValue(IconGlyphProperty);
        set => SetValue(IconGlyphProperty, value);
    }

    public string Text
    {
        get => (string)GetValue(TextProperty);
        set => SetValue(TextProperty, value);
    }

    public ICommand? Command
    {
        get => (ICommand?)GetValue(CommandProperty);
        set => SetValue(CommandProperty, value);
    }

    public object CommandParameter
    {
        get => GetValue(CommandParameterProperty);
        set => SetValue(CommandParameterProperty, value);
    }

    public Style? ButtonStyle
    {
        get => (Style?)GetValue(ButtonStyleProperty);
        set => SetValue(ButtonStyleProperty, value);
    }

    public string ButtonAutomationId
    {
        get => (string)GetValue(ButtonAutomationIdProperty);
        set => SetValue(ButtonAutomationIdProperty, value);
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

    private static void OnButtonAutomationIdChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is IconTextButton control && e.NewValue is string automationId)
        {
            AutomationProperties.SetAutomationId(control, automationId);
        }
    }

    private static void OnTextChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is IconTextButton control)
        {
            AutomationProperties.SetName(control, e.NewValue as string ?? string.Empty);
        }
    }
}
