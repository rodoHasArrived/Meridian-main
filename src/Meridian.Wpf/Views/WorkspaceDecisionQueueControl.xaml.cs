using System.Collections;
using System.Windows;
using System.Windows.Controls;
using Meridian.Wpf.Models;

namespace Meridian.Wpf.Views;

public enum WorkspaceDecisionActionKind
{
    Primary,
    Secondary
}

public sealed class WorkspaceDecisionInvokedEventArgs : EventArgs
{
    public WorkspaceDecisionInvokedEventArgs(WorkspaceQueueItem decision, WorkspaceDecisionActionKind actionKind)
    {
        Decision = decision ?? throw new ArgumentNullException(nameof(decision));
        ActionKind = actionKind;
        ActionId = actionKind == WorkspaceDecisionActionKind.Secondary
            ? Decision.SecondaryActionId
            : Decision.PrimaryActionId;
    }

    public WorkspaceQueueItem Decision { get; }

    public WorkspaceDecisionActionKind ActionKind { get; }

    public string ActionId { get; }
}

public partial class WorkspaceDecisionQueueControl : UserControl
{
    public static readonly DependencyProperty ItemsSourceProperty =
        DependencyProperty.Register(
            nameof(ItemsSource),
            typeof(IEnumerable),
            typeof(WorkspaceDecisionQueueControl),
            new PropertyMetadata(Array.Empty<WorkspaceQueueItem>()));

    public static readonly DependencyProperty QueueAutomationIdProperty =
        DependencyProperty.Register(
            nameof(QueueAutomationId),
            typeof(string),
            typeof(WorkspaceDecisionQueueControl),
            new PropertyMetadata("WorkspaceDecisionQueue"));

    public static readonly DependencyProperty ColumnsProperty =
        DependencyProperty.Register(
            nameof(Columns),
            typeof(int),
            typeof(WorkspaceDecisionQueueControl),
            new PropertyMetadata(2));

    public WorkspaceDecisionQueueControl()
    {
        InitializeComponent();
    }

    public event EventHandler<WorkspaceDecisionInvokedEventArgs>? DecisionInvoked;

    public IEnumerable ItemsSource
    {
        get => (IEnumerable)GetValue(ItemsSourceProperty);
        set => SetValue(ItemsSourceProperty, value);
    }

    public string QueueAutomationId
    {
        get => (string)GetValue(QueueAutomationIdProperty);
        set => SetValue(QueueAutomationIdProperty, value);
    }

    public int Columns
    {
        get => (int)GetValue(ColumnsProperty);
        set => SetValue(ColumnsProperty, value);
    }

    private void OnPrimaryActionClick(object sender, RoutedEventArgs e)
        => RaiseDecisionInvoked(sender, WorkspaceDecisionActionKind.Primary);

    private void OnSecondaryActionClick(object sender, RoutedEventArgs e)
        => RaiseDecisionInvoked(sender, WorkspaceDecisionActionKind.Secondary);

    private void RaiseDecisionInvoked(object sender, WorkspaceDecisionActionKind actionKind)
    {
        if (sender is FrameworkElement { Tag: WorkspaceQueueItem decision })
        {
            var args = new WorkspaceDecisionInvokedEventArgs(decision, actionKind);
            if (!string.IsNullOrWhiteSpace(args.ActionId))
            {
                DecisionInvoked?.Invoke(this, args);
            }
        }
    }
}
