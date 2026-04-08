using System.Windows;
using System.Windows.Controls;
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

    private void OnPrimaryCommandClick(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: WorkspaceCommandItem command })
        {
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
                IsEnabled = command.IsEnabled
            };

            menuItem.Click += OnSecondaryCommandClick;
            menu.Items.Add(menuItem);
        }

        menu.IsOpen = true;
    }

    private void OnSecondaryCommandClick(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: WorkspaceCommandItem command })
        {
            CommandInvoked?.Invoke(this, new WorkspaceCommandInvokedEventArgs(command));
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

        MoreButton.Visibility = CommandGroup.SecondaryCommands.Count > 0
            ? Visibility.Visible
            : Visibility.Collapsed;
    }
}
