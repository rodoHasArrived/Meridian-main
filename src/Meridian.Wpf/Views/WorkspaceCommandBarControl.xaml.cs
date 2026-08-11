using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using Meridian.Wpf.Models;

namespace Meridian.Wpf.Views;

public sealed class WorkspaceCommandInvokedEventArgs : EventArgs
{
    public WorkspaceCommandInvokedEventArgs(WorkspaceCommandItem command)
    {
        Command = command ?? throw new ArgumentNullException(nameof(command));
    }

    public WorkspaceCommandItem Command { get; }
}

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
        if (CommandGroup.SecondaryCommands.Count == 0)
        {
            return;
        }

        var menu = new ContextMenu
        {
            PlacementTarget = MoreButton
        };

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
            menu.Items.Add(menuItem);
        }

        menu.IsOpen = true;
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

        MoreButton.Visibility = CommandGroup.SecondaryCommands.Count > 0
            ? Visibility.Visible
            : Visibility.Collapsed;
    }
}
