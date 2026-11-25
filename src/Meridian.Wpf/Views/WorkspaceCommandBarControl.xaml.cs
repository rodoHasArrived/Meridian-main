using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using Meridian.Wpf.Models;

namespace Meridian.Wpf.Views;

/// <summary>
/// Carries the workspace command selected by the operator.
/// </summary>
public sealed class WorkspaceCommandInvokedEventArgs : EventArgs
{
    public WorkspaceCommandInvokedEventArgs(WorkspaceCommandItem command)
    {
        Command = command ?? throw new ArgumentNullException(nameof(command));
    }

    public WorkspaceCommandItem Command { get; }
}

/// <summary>
/// Displays primary and secondary workspace commands and raises command invocation events.
/// </summary>
public partial class WorkspaceCommandBarControl : UserControl
{
    public static readonly DependencyProperty CommandGroupProperty =
        DependencyProperty.Register(
            nameof(CommandGroup),
            typeof(WorkspaceCommandGroup),
            typeof(WorkspaceCommandBarControl),
            new PropertyMetadata(new WorkspaceCommandGroup(), OnCommandGroupChanged));

    public static readonly DependencyProperty IsCompactSurfaceProperty =
        DependencyProperty.Register(
            nameof(IsCompactSurface),
            typeof(bool),
            typeof(WorkspaceCommandBarControl),
            new PropertyMetadata(false));

    public static readonly DependencyProperty CommandInvokedCommandProperty =
        DependencyProperty.Register(
            nameof(CommandInvokedCommand),
            typeof(ICommand),
            typeof(WorkspaceCommandBarControl));

    /// <summary>
    /// When set, the More menu ends with "Reset layout", which runs this command. Workspace shells
    /// bind it to their pane-layout reset so every shell offers the same recovery path.
    /// </summary>
    public static readonly DependencyProperty ResetLayoutCommandProperty =
        DependencyProperty.Register(
            nameof(ResetLayoutCommand),
            typeof(ICommand),
            typeof(WorkspaceCommandBarControl),
            new PropertyMetadata(null, OnResetLayoutCommandChanged));

    internal const string ResetLayoutMenuLabel = "Reset layout";
    internal const string ResetLayoutAutomationId = "WorkspaceCommandResetLayout";

    public WorkspaceCommandBarControl()
    {
        InitializeComponent();
        Loaded += (_, _) => UpdateMoreButtonVisibility();
    }

    public event EventHandler<WorkspaceCommandInvokedEventArgs>? CommandInvoked;

    public WorkspaceCommandGroup CommandGroup
    {
        get => (WorkspaceCommandGroup)GetValue(CommandGroupProperty);
        set => SetValue(CommandGroupProperty, value);
    }

    public bool IsCompactSurface
    {
        get => (bool)GetValue(IsCompactSurfaceProperty);
        set => SetValue(IsCompactSurfaceProperty, value);
    }

    public ICommand? CommandInvokedCommand
    {
        get => (ICommand?)GetValue(CommandInvokedCommandProperty);
        set => SetValue(CommandInvokedCommandProperty, value);
    }

    public ICommand? ResetLayoutCommand
    {
        get => (ICommand?)GetValue(ResetLayoutCommandProperty);
        set => SetValue(ResetLayoutCommandProperty, value);
    }

    private void OnPrimaryCommandClick(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: WorkspaceCommandItem command })
        {
            TryInvokeCommandRouting(command);
            CommandInvoked?.Invoke(this, new WorkspaceCommandInvokedEventArgs(command));
        }
    }

    private void OnMoreButtonClick(object sender, RoutedEventArgs e)
    {
        var items = BuildOverflowMenuItems();
        if (items.Count == 0)
        {
            return;
        }

        var menu = new ContextMenu
        {
            PlacementTarget = MoreButton
        };

        foreach (var item in items)
        {
            menu.Items.Add(item);
        }

        menu.IsOpen = true;
    }

    /// <summary>The More menu's entries: the secondary commands, then "Reset layout" when bound.</summary>
    internal IReadOnlyList<Control> BuildOverflowMenuItems()
    {
        var items = new List<Control>();
        foreach (var command in CommandGroup.SecondaryCommands)
        {
            var menuItem = new MenuItem
            {
                Header = string.IsNullOrWhiteSpace(command.ShortcutHint)
                    ? command.Label
                    : $"{command.Label}  {command.ShortcutHint}",
                Tag = command,
                IsEnabled = command.IsEnabled,

                // Null rather than empty: an empty tooltip still pops an empty popup on hover.
                ToolTip = string.IsNullOrWhiteSpace(command.DisabledReason)
                    ? null
                    : command.DisabledReason
            };

            // Overflow commands render no inline disabled reason, so the tooltip is the only place
            // it surfaces — and tooltips are suppressed on disabled items unless asked otherwise.
            ToolTipService.SetShowOnDisabled(menuItem, true);
            AutomationProperties.SetAutomationId(menuItem, command.AutomationId);
            AutomationProperties.SetName(menuItem, command.Label);

            menuItem.Click += OnSecondaryCommandClick;
            items.Add(menuItem);
        }

        if (ResetLayoutCommand is { } resetLayout)
        {
            if (items.Count > 0)
            {
                items.Add(new Separator());
            }

            var resetItem = new MenuItem
            {
                Header = ResetLayoutMenuLabel,
                Command = resetLayout,
                ToolTip = "Restore this workspace's default panes. Other workspaces keep their layouts."
            };
            AutomationProperties.SetAutomationId(resetItem, ResetLayoutAutomationId);
            AutomationProperties.SetName(resetItem, ResetLayoutMenuLabel);
            items.Add(resetItem);
        }

        return items;
    }

    private void OnSecondaryCommandClick(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: WorkspaceCommandItem command })
        {
            TryInvokeCommandRouting(command);
            CommandInvoked?.Invoke(this, new WorkspaceCommandInvokedEventArgs(command));
        }
    }

    internal bool TryInvokeCommandRouting(WorkspaceCommandItem command)
    {
        ArgumentNullException.ThrowIfNull(command);

        var routedCommand = CommandInvokedCommand;
        if (routedCommand?.CanExecute(command) != true)
        {
            return false;
        }

        routedCommand.Execute(command);
        return true;
    }

    private static void OnResetLayoutCommandChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is WorkspaceCommandBarControl control)
        {
            control.UpdateMoreButtonVisibility();
        }
    }

    private static void OnCommandGroupChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is WorkspaceCommandBarControl control)
        {
            control.UpdateMoreButtonVisibility();
        }
    }

    private void UpdateMoreButtonVisibility()
    {
        if (MoreButton is null)
        {
            return;
        }

        MoreButton.Visibility = CommandGroup.SecondaryCommands.Count > 0 || ResetLayoutCommand is not null
            ? Visibility.Visible
            : Visibility.Collapsed;
    }
}
