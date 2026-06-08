using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using Meridian.Wpf.Models;

namespace Meridian.Wpf.Controls;

public partial class WorkspaceQueueCard : UserControl
{
    public static readonly DependencyProperty TitleProperty =
        DependencyProperty.Register(
            nameof(Title),
            typeof(string),
            typeof(WorkspaceQueueCard),
            new PropertyMetadata(string.Empty, OnTitleChanged));

    public static readonly DependencyProperty DescriptionProperty =
        DependencyProperty.Register(nameof(Description), typeof(string), typeof(WorkspaceQueueCard), new PropertyMetadata(string.Empty));

    public static readonly DependencyProperty IconGlyphProperty =
        DependencyProperty.Register(nameof(IconGlyph), typeof(string), typeof(WorkspaceQueueCard), new PropertyMetadata(string.Empty));

    public static readonly DependencyProperty ToneProperty =
        DependencyProperty.Register(nameof(Tone), typeof(string), typeof(WorkspaceQueueCard), new PropertyMetadata(WorkspaceTone.Neutral));

    public static readonly DependencyProperty ActionTextProperty =
        DependencyProperty.Register(nameof(ActionText), typeof(string), typeof(WorkspaceQueueCard), new PropertyMetadata(string.Empty));

    public static readonly DependencyProperty ActionCommandProperty =
        DependencyProperty.Register(nameof(ActionCommand), typeof(ICommand), typeof(WorkspaceQueueCard), new PropertyMetadata(null));

    public static readonly DependencyProperty CardAutomationIdProperty =
        DependencyProperty.Register(
            nameof(CardAutomationId),
            typeof(string),
            typeof(WorkspaceQueueCard),
            new PropertyMetadata("WorkspaceQueueCard", OnCardAutomationIdChanged));

    public static readonly DependencyProperty IconAutomationIdProperty =
        DependencyProperty.Register(nameof(IconAutomationId), typeof(string), typeof(WorkspaceQueueCard), new PropertyMetadata("WorkspaceQueueCardIcon"));

    public static readonly DependencyProperty IconContainerAutomationIdProperty =
        DependencyProperty.Register(nameof(IconContainerAutomationId), typeof(string), typeof(WorkspaceQueueCard), new PropertyMetadata("WorkspaceQueueCardIconContainer"));

    public static readonly DependencyProperty TitleAutomationIdProperty =
        DependencyProperty.Register(nameof(TitleAutomationId), typeof(string), typeof(WorkspaceQueueCard), new PropertyMetadata("WorkspaceQueueCardTitle"));

    public static readonly DependencyProperty DescriptionAutomationIdProperty =
        DependencyProperty.Register(nameof(DescriptionAutomationId), typeof(string), typeof(WorkspaceQueueCard), new PropertyMetadata("WorkspaceQueueCardDescription"));

    public static readonly DependencyProperty ActionButtonAutomationIdProperty =
        DependencyProperty.Register(nameof(ActionButtonAutomationId), typeof(string), typeof(WorkspaceQueueCard), new PropertyMetadata("WorkspaceQueueCardAction"));

    public WorkspaceQueueCard()
    {
        InitializeComponent();
        AutomationProperties.SetAutomationId(this, CardAutomationId);
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

    public ICommand? ActionCommand
    {
        get => (ICommand?)GetValue(ActionCommandProperty);
        set => SetValue(ActionCommandProperty, value);
    }

    public string CardAutomationId
    {
        get => (string)GetValue(CardAutomationIdProperty);
        set => SetValue(CardAutomationIdProperty, value);
    }

    public string IconAutomationId
    {
        get => (string)GetValue(IconAutomationIdProperty);
        set => SetValue(IconAutomationIdProperty, value);
    }

    public string IconContainerAutomationId
    {
        get => (string)GetValue(IconContainerAutomationIdProperty);
        set => SetValue(IconContainerAutomationIdProperty, value);
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

    private static void OnCardAutomationIdChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is WorkspaceQueueCard control && e.NewValue is string automationId)
        {
            AutomationProperties.SetAutomationId(control, automationId);
        }
    }

    private static void OnTitleChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is WorkspaceQueueCard control)
        {
            AutomationProperties.SetName(control, e.NewValue as string ?? string.Empty);
        }
    }
}
