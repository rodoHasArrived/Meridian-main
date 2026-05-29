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

public sealed class WorkspaceInspectorActionInvokedEventArgs : EventArgs
{
    public WorkspaceInspectorActionInvokedEventArgs(string actionId)
        => ActionId = actionId;

    public string ActionId { get; }
}

public partial class WorkspaceInspectorHostControl : UserControl
{
    public static readonly DependencyProperty InspectorStateProperty =
        DependencyProperty.Register(
            nameof(InspectorState),
            typeof(WorkspaceInspectorState),
            typeof(WorkspaceInspectorHostControl),
            new PropertyMetadata(WorkspaceInspectorState.Empty, OnInspectorStateChanged));

    public static readonly DependencyProperty HostAutomationIdProperty =
        DependencyProperty.Register(nameof(HostAutomationId), typeof(string), typeof(WorkspaceInspectorHostControl), new PropertyMetadata("WorkspaceInspectorHost"));

    public static readonly DependencyProperty EmptyAutomationIdProperty =
        DependencyProperty.Register(nameof(EmptyAutomationId), typeof(string), typeof(WorkspaceInspectorHostControl), new PropertyMetadata("WorkspaceInspectorEmptyState"));

    public static readonly DependencyProperty SelectedAutomationIdProperty =
        DependencyProperty.Register(nameof(SelectedAutomationId), typeof(string), typeof(WorkspaceInspectorHostControl), new PropertyMetadata("WorkspaceInspectorSelectedState"));

    public static readonly DependencyProperty LoadingAutomationIdProperty =
        DependencyProperty.Register(nameof(LoadingAutomationId), typeof(string), typeof(WorkspaceInspectorHostControl), new PropertyMetadata("WorkspaceInspectorLoadingState"));

    public static readonly DependencyProperty ErrorAutomationIdProperty =
        DependencyProperty.Register(nameof(ErrorAutomationId), typeof(string), typeof(WorkspaceInspectorHostControl), new PropertyMetadata("WorkspaceInspectorErrorState"));

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

    public static readonly DependencyProperty LoadingTitleProperty =
        DependencyProperty.Register(nameof(LoadingTitle), typeof(string), typeof(WorkspaceInspectorHostControl), new PropertyMetadata("Loading inspector"));

    public static readonly DependencyProperty LoadingDetailProperty =
        DependencyProperty.Register(nameof(LoadingDetail), typeof(string), typeof(WorkspaceInspectorHostControl), new PropertyMetadata("Loading selected row evidence."));

    public static readonly DependencyProperty ErrorTitleProperty =
        DependencyProperty.Register(nameof(ErrorTitle), typeof(string), typeof(WorkspaceInspectorHostControl), new PropertyMetadata("Inspector unavailable"));

    public static readonly DependencyProperty ErrorDetailProperty =
        DependencyProperty.Register(nameof(ErrorDetail), typeof(string), typeof(WorkspaceInspectorHostControl), new PropertyMetadata("The selected row detail could not be loaded."));

    public static readonly DependencyProperty SelectedEvidenceTabHeaderProperty =
        DependencyProperty.Register(nameof(SelectedEvidenceTabHeader), typeof(string), typeof(WorkspaceInspectorHostControl), new PropertyMetadata("Evidence"));

    public static readonly DependencyProperty SelectedActionsTabHeaderProperty =
        DependencyProperty.Register(nameof(SelectedActionsTabHeader), typeof(string), typeof(WorkspaceInspectorHostControl), new PropertyMetadata("Actions"));

    public static readonly DependencyProperty ErrorActionLabelProperty =
        DependencyProperty.Register(
            nameof(ErrorActionLabel),
            typeof(string),
            typeof(WorkspaceInspectorHostControl),
            new PropertyMetadata(string.Empty, OnErrorActionChanged));

    public static readonly DependencyProperty ErrorActionIdProperty =
        DependencyProperty.Register(
            nameof(ErrorActionId),
            typeof(string),
            typeof(WorkspaceInspectorHostControl),
            new PropertyMetadata(string.Empty, OnErrorActionChanged));

    public WorkspaceInspectorHostControl()
    {
        InitializeComponent();
        ApplyInspectorState();
        ApplyErrorActionVisibility();
    }

    public event EventHandler<WorkspaceInspectorActionInvokedEventArgs>? InspectorActionInvoked;

    public WorkspaceInspectorState InspectorState
    {
        get => (WorkspaceInspectorState)GetValue(InspectorStateProperty);
        set => SetValue(InspectorStateProperty, value);
    }

    public string HostAutomationId
    {
        get => (string)GetValue(HostAutomationIdProperty);
        set => SetValue(HostAutomationIdProperty, value);
    }

    public string EmptyAutomationId
    {
        get => (string)GetValue(EmptyAutomationIdProperty);
        set => SetValue(EmptyAutomationIdProperty, value);
    }

    public string SelectedAutomationId
    {
        get => (string)GetValue(SelectedAutomationIdProperty);
        set => SetValue(SelectedAutomationIdProperty, value);
    }

    public string LoadingAutomationId
    {
        get => (string)GetValue(LoadingAutomationIdProperty);
        set => SetValue(LoadingAutomationIdProperty, value);
    }

    public string ErrorAutomationId
    {
        get => (string)GetValue(ErrorAutomationIdProperty);
        set => SetValue(ErrorAutomationIdProperty, value);
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

    public string LoadingTitle
    {
        get => (string)GetValue(LoadingTitleProperty);
        set => SetValue(LoadingTitleProperty, value);
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

    public string SelectedEvidenceTabHeader
    {
        get => (string)GetValue(SelectedEvidenceTabHeaderProperty);
        set => SetValue(SelectedEvidenceTabHeaderProperty, value);
    }

    public string SelectedActionsTabHeader
    {
        get => (string)GetValue(SelectedActionsTabHeaderProperty);
        set => SetValue(SelectedActionsTabHeaderProperty, value);
    }

    public string ErrorActionLabel
    {
        get => (string)GetValue(ErrorActionLabelProperty);
        set => SetValue(ErrorActionLabelProperty, value);
    }

    public string ErrorActionId
    {
        get => (string)GetValue(ErrorActionIdProperty);
        set => SetValue(ErrorActionIdProperty, value);
    }

    private static void OnInspectorStateChanged(DependencyObject dependencyObject, DependencyPropertyChangedEventArgs e)
    {
        if (dependencyObject is WorkspaceInspectorHostControl control)
        {
            control.ApplyInspectorState();
        }
    }

    private static void OnErrorActionChanged(DependencyObject dependencyObject, DependencyPropertyChangedEventArgs e)
    {
        if (dependencyObject is WorkspaceInspectorHostControl control)
        {
            control.ApplyErrorActionVisibility();
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

    private void ApplyErrorActionVisibility()
    {
        if (ErrorActionButton is null)
        {
            return;
        }

        ErrorActionButton.Visibility = string.IsNullOrWhiteSpace(ErrorActionLabel) || string.IsNullOrWhiteSpace(ErrorActionId)
            ? Visibility.Collapsed
            : Visibility.Visible;
    }

    private void OnErrorActionClick(object sender, RoutedEventArgs e)
    {
        if (!string.IsNullOrWhiteSpace(ErrorActionId))
        {
            InspectorActionInvoked?.Invoke(this, new WorkspaceInspectorActionInvokedEventArgs(ErrorActionId));
        }
    }
}
