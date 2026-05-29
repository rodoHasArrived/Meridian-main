using System.Windows;
using System.Windows.Controls;

namespace Meridian.Wpf.Views;

public enum WorkspaceInspectorState
{
    Empty,
    Selected,
    Loading,
    Error
}

public partial class WorkspaceInspectorHostControl : UserControl
{
    public static readonly DependencyProperty InspectorStateProperty =
        DependencyProperty.Register(
            nameof(InspectorState),
            typeof(WorkspaceInspectorState),
            typeof(WorkspaceInspectorHostControl),
            new PropertyMetadata(WorkspaceInspectorState.Empty, OnInspectorStateChanged));

    public static readonly DependencyProperty EmptyTitleProperty =
        DependencyProperty.Register(nameof(EmptyTitle), typeof(string), typeof(WorkspaceInspectorHostControl), new PropertyMetadata("No row selected"));

    public static readonly DependencyProperty EmptyDetailProperty =
        DependencyProperty.Register(nameof(EmptyDetail), typeof(string), typeof(WorkspaceInspectorHostControl), new PropertyMetadata("Select an operational row to inspect evidence, actions, and recovery context."));

    public static readonly DependencyProperty SelectedTitleProperty =
        DependencyProperty.Register(nameof(SelectedTitle), typeof(string), typeof(WorkspaceInspectorHostControl), new PropertyMetadata("Selection details"));

    public static readonly DependencyProperty SelectedDetailProperty =
        DependencyProperty.Register(nameof(SelectedDetail), typeof(string), typeof(WorkspaceInspectorHostControl), new PropertyMetadata("Evidence for the selected row is available."));

    public static readonly DependencyProperty SelectedActionDetailProperty =
        DependencyProperty.Register(nameof(SelectedActionDetail), typeof(string), typeof(WorkspaceInspectorHostControl), new PropertyMetadata("Available actions are scoped to the selected row."));

    public static readonly DependencyProperty LoadingDetailProperty =
        DependencyProperty.Register(nameof(LoadingDetail), typeof(string), typeof(WorkspaceInspectorHostControl), new PropertyMetadata("Loading selected row evidence."));

    public static readonly DependencyProperty ErrorTitleProperty =
        DependencyProperty.Register(nameof(ErrorTitle), typeof(string), typeof(WorkspaceInspectorHostControl), new PropertyMetadata("Inspector unavailable"));

    public static readonly DependencyProperty ErrorDetailProperty =
        DependencyProperty.Register(nameof(ErrorDetail), typeof(string), typeof(WorkspaceInspectorHostControl), new PropertyMetadata("The selected row detail could not be loaded."));

    public WorkspaceInspectorHostControl()
    {
        InitializeComponent();
        ApplyInspectorState();
    }

    public WorkspaceInspectorState InspectorState
    {
        get => (WorkspaceInspectorState)GetValue(InspectorStateProperty);
        set => SetValue(InspectorStateProperty, value);
    }

    public string EmptyTitle
    {
        get => (string)GetValue(EmptyTitleProperty);
        set => SetValue(EmptyTitleProperty, value);
    }

    public string EmptyDetail
    {
        get => (string)GetValue(EmptyDetailProperty);
        set => SetValue(EmptyDetailProperty, value);
    }

    public string SelectedTitle
    {
        get => (string)GetValue(SelectedTitleProperty);
        set => SetValue(SelectedTitleProperty, value);
    }

    public string SelectedDetail
    {
        get => (string)GetValue(SelectedDetailProperty);
        set => SetValue(SelectedDetailProperty, value);
    }

    public string SelectedActionDetail
    {
        get => (string)GetValue(SelectedActionDetailProperty);
        set => SetValue(SelectedActionDetailProperty, value);
    }

    public string LoadingDetail
    {
        get => (string)GetValue(LoadingDetailProperty);
        set => SetValue(LoadingDetailProperty, value);
    }

    public string ErrorTitle
    {
        get => (string)GetValue(ErrorTitleProperty);
        set => SetValue(ErrorTitleProperty, value);
    }

    public string ErrorDetail
    {
        get => (string)GetValue(ErrorDetailProperty);
        set => SetValue(ErrorDetailProperty, value);
    }

    private static void OnInspectorStateChanged(DependencyObject dependencyObject, DependencyPropertyChangedEventArgs e)
    {
        if (dependencyObject is WorkspaceInspectorHostControl control)
        {
            control.ApplyInspectorState();
        }
    }

    private void ApplyInspectorState()
    {
        if (EmptyStatePanel is null)
        {
            return;
        }

        EmptyStatePanel.Visibility = InspectorState == WorkspaceInspectorState.Empty ? Visibility.Visible : Visibility.Collapsed;
        SelectedStatePanel.Visibility = InspectorState == WorkspaceInspectorState.Selected ? Visibility.Visible : Visibility.Collapsed;
        LoadingStatePanel.Visibility = InspectorState == WorkspaceInspectorState.Loading ? Visibility.Visible : Visibility.Collapsed;
        ErrorStatePanel.Visibility = InspectorState == WorkspaceInspectorState.Error ? Visibility.Visible : Visibility.Collapsed;
    }
}

