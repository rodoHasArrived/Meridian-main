using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using Meridian.Wpf.Models;

namespace Meridian.Wpf.Controls;

public partial class SectionHeaderBar : UserControl
{
    public static readonly DependencyProperty TitleProperty =
        DependencyProperty.Register(
            nameof(Title),
            typeof(string),
            typeof(SectionHeaderBar),
            new PropertyMetadata(string.Empty, OnTitleChanged));

    public static readonly DependencyProperty SubtitleProperty =
        DependencyProperty.Register(nameof(Subtitle), typeof(string), typeof(SectionHeaderBar), new PropertyMetadata(string.Empty));

    public static readonly DependencyProperty BadgeTextProperty =
        DependencyProperty.Register(nameof(BadgeText), typeof(string), typeof(SectionHeaderBar), new PropertyMetadata(string.Empty));

    public static readonly DependencyProperty BadgeToneProperty =
        DependencyProperty.Register(nameof(BadgeTone), typeof(string), typeof(SectionHeaderBar), new PropertyMetadata(WorkspaceTone.Neutral));

    public static readonly DependencyProperty RightContentProperty =
        DependencyProperty.Register(nameof(RightContent), typeof(object), typeof(SectionHeaderBar), new PropertyMetadata(null));

    public static readonly DependencyProperty ActionTextProperty =
        DependencyProperty.Register(nameof(ActionText), typeof(string), typeof(SectionHeaderBar), new PropertyMetadata(string.Empty));

    public static readonly DependencyProperty ActionCommandProperty =
        DependencyProperty.Register(nameof(ActionCommand), typeof(ICommand), typeof(SectionHeaderBar), new PropertyMetadata(null));

    public static readonly DependencyProperty HeaderAutomationIdProperty =
        DependencyProperty.Register(
            nameof(HeaderAutomationId),
            typeof(string),
            typeof(SectionHeaderBar),
            new PropertyMetadata("SectionHeaderBar", OnHeaderAutomationIdChanged));

    public static readonly DependencyProperty TitleAutomationIdProperty =
        DependencyProperty.Register(nameof(TitleAutomationId), typeof(string), typeof(SectionHeaderBar), new PropertyMetadata("SectionHeaderTitle"));

    public static readonly DependencyProperty SubtitleAutomationIdProperty =
        DependencyProperty.Register(nameof(SubtitleAutomationId), typeof(string), typeof(SectionHeaderBar), new PropertyMetadata("SectionHeaderSubtitle"));

    public static readonly DependencyProperty BadgeAutomationIdProperty =
        DependencyProperty.Register(nameof(BadgeAutomationId), typeof(string), typeof(SectionHeaderBar), new PropertyMetadata("SectionHeaderBadge"));

    public static readonly DependencyProperty BadgeTextAutomationIdProperty =
        DependencyProperty.Register(nameof(BadgeTextAutomationId), typeof(string), typeof(SectionHeaderBar), new PropertyMetadata("SectionHeaderBadgeText"));

    public static readonly DependencyProperty RightContentAutomationIdProperty =
        DependencyProperty.Register(nameof(RightContentAutomationId), typeof(string), typeof(SectionHeaderBar), new PropertyMetadata("SectionHeaderRightContent"));

    public static readonly DependencyProperty ActionButtonAutomationIdProperty =
        DependencyProperty.Register(nameof(ActionButtonAutomationId), typeof(string), typeof(SectionHeaderBar), new PropertyMetadata("SectionHeaderAction"));

    public SectionHeaderBar()
    {
        InitializeComponent();
        AutomationProperties.SetAutomationId(this, HeaderAutomationId);
        AutomationProperties.SetName(this, Title);
    }

    public string Title
    {
        get => (string)GetValue(TitleProperty);
        set => SetValue(TitleProperty, value);
    }

    public string Subtitle
    {
        get => (string)GetValue(SubtitleProperty);
        set => SetValue(SubtitleProperty, value);
    }

    public string BadgeText
    {
        get => (string)GetValue(BadgeTextProperty);
        set => SetValue(BadgeTextProperty, value);
    }

    public string BadgeTone
    {
        get => (string)GetValue(BadgeToneProperty);
        set => SetValue(BadgeToneProperty, value);
    }

    public object? RightContent
    {
        get => GetValue(RightContentProperty);
        set => SetValue(RightContentProperty, value);
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

    public string HeaderAutomationId
    {
        get => (string)GetValue(HeaderAutomationIdProperty);
        set => SetValue(HeaderAutomationIdProperty, value);
    }

    public string TitleAutomationId
    {
        get => (string)GetValue(TitleAutomationIdProperty);
        set => SetValue(TitleAutomationIdProperty, value);
    }

    public string SubtitleAutomationId
    {
        get => (string)GetValue(SubtitleAutomationIdProperty);
        set => SetValue(SubtitleAutomationIdProperty, value);
    }

    public string BadgeAutomationId
    {
        get => (string)GetValue(BadgeAutomationIdProperty);
        set => SetValue(BadgeAutomationIdProperty, value);
    }

    public string BadgeTextAutomationId
    {
        get => (string)GetValue(BadgeTextAutomationIdProperty);
        set => SetValue(BadgeTextAutomationIdProperty, value);
    }

    public string RightContentAutomationId
    {
        get => (string)GetValue(RightContentAutomationIdProperty);
        set => SetValue(RightContentAutomationIdProperty, value);
    }

    public string ActionButtonAutomationId
    {
        get => (string)GetValue(ActionButtonAutomationIdProperty);
        set => SetValue(ActionButtonAutomationIdProperty, value);
    }

    private static void OnHeaderAutomationIdChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is SectionHeaderBar control && e.NewValue is string automationId)
        {
            AutomationProperties.SetAutomationId(control, automationId);
        }
    }

    private static void OnTitleChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is SectionHeaderBar control)
        {
            AutomationProperties.SetName(control, e.NewValue as string ?? string.Empty);
        }
    }
}
