using System.Windows;
using System.Windows.Controls;

namespace Meridian.Wpf.Views;

public partial class WorkspaceDialogChromeControl : UserControl
{
    public static readonly DependencyProperty DialogAutomationIdProperty =
        DependencyProperty.Register(nameof(DialogAutomationId), typeof(string), typeof(WorkspaceDialogChromeControl), new PropertyMetadata("WorkspaceDialogChrome"));

    public static readonly DependencyProperty TitleAutomationIdProperty =
        DependencyProperty.Register(nameof(TitleAutomationId), typeof(string), typeof(WorkspaceDialogChromeControl), new PropertyMetadata("WorkspaceDialogTitle"));

    public static readonly DependencyProperty SubtitleAutomationIdProperty =
        DependencyProperty.Register(nameof(SubtitleAutomationId), typeof(string), typeof(WorkspaceDialogChromeControl), new PropertyMetadata("WorkspaceDialogSubtitle"));

    public static readonly DependencyProperty BodyAutomationIdProperty =
        DependencyProperty.Register(nameof(BodyAutomationId), typeof(string), typeof(WorkspaceDialogChromeControl), new PropertyMetadata("WorkspaceDialogBody"));

    public static readonly DependencyProperty TitleTextProperty =
        DependencyProperty.Register(nameof(TitleText), typeof(string), typeof(WorkspaceDialogChromeControl), new PropertyMetadata("Dialog"));

    public static readonly DependencyProperty SubtitleTextProperty =
        DependencyProperty.Register(nameof(SubtitleText), typeof(string), typeof(WorkspaceDialogChromeControl), new PropertyMetadata(string.Empty));

    public static readonly DependencyProperty BodyProperty =
        DependencyProperty.Register(nameof(Body), typeof(object), typeof(WorkspaceDialogChromeControl), new PropertyMetadata(null));

    public WorkspaceDialogChromeControl()
    {
        InitializeComponent();
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

    public object? Body
    {
        get => GetValue(BodyProperty);
        set => SetValue(BodyProperty, value);
    }
}
