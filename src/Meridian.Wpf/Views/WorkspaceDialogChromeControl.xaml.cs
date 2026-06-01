using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;

namespace Meridian.Wpf.Views;

public class WorkspaceDialogChromeControl : ContentControl
{
    public static readonly DependencyProperty DialogAutomationIdProperty =
        DependencyProperty.Register(
            nameof(DialogAutomationId),
            typeof(string),
            typeof(WorkspaceDialogChromeControl),
            new PropertyMetadata("WorkspaceDialogChrome", OnDialogAutomationIdChanged));

    public static readonly DependencyProperty TitleAutomationIdProperty =
        DependencyProperty.Register(nameof(TitleAutomationId), typeof(string), typeof(WorkspaceDialogChromeControl), new PropertyMetadata("WorkspaceDialogTitle"));

    public static readonly DependencyProperty SubtitleAutomationIdProperty =
        DependencyProperty.Register(nameof(SubtitleAutomationId), typeof(string), typeof(WorkspaceDialogChromeControl), new PropertyMetadata("WorkspaceDialogSubtitle"));

    public static readonly DependencyProperty BodyAutomationIdProperty =
        DependencyProperty.Register(nameof(BodyAutomationId), typeof(string), typeof(WorkspaceDialogChromeControl), new PropertyMetadata("WorkspaceDialogBody"));

    public static readonly DependencyProperty TitleTextProperty =
        DependencyProperty.Register(
            nameof(TitleText),
            typeof(string),
            typeof(WorkspaceDialogChromeControl),
            new PropertyMetadata("Dialog", OnTitleTextChanged));

    public static readonly DependencyProperty SubtitleTextProperty =
        DependencyProperty.Register(nameof(SubtitleText), typeof(string), typeof(WorkspaceDialogChromeControl), new PropertyMetadata(string.Empty));

    public WorkspaceDialogChromeControl()
    {
        ApplyAutomationMetadata();
    }

    public string DialogAutomationId
    {
        get => (string)GetValue(DialogAutomationIdProperty);
        set => SetValue(DialogAutomationIdProperty, value);
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

    public string BodyAutomationId
    {
        get => (string)GetValue(BodyAutomationIdProperty);
        set => SetValue(BodyAutomationIdProperty, value);
    }

    public string TitleText
    {
        get => (string)GetValue(TitleTextProperty);
        set => SetValue(TitleTextProperty, value);
    }

    public string SubtitleText
    {
        get => (string)GetValue(SubtitleTextProperty);
        set => SetValue(SubtitleTextProperty, value);
    }

    private static void OnDialogAutomationIdChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is WorkspaceDialogChromeControl control)
        {
            AutomationProperties.SetAutomationId(control, e.NewValue as string ?? string.Empty);
        }
    }

    private static void OnTitleTextChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is WorkspaceDialogChromeControl control)
        {
            AutomationProperties.SetName(control, e.NewValue as string ?? string.Empty);
        }
    }

    private void ApplyAutomationMetadata()
    {
        AutomationProperties.SetAutomationId(this, DialogAutomationId);
        AutomationProperties.SetName(this, TitleText);
    }
}
