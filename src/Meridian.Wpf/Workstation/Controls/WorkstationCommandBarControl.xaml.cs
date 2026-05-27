using System.Windows;
using System.Windows.Controls;
using Meridian.Wpf.Workstation.Models;

namespace Meridian.Wpf.Workstation.Controls;

public sealed class WorkstationCommandInvokedEventArgs : EventArgs
{
    public WorkstationCommandInvokedEventArgs(WorkstationCommandModel command)
    {
        Command = command ?? throw new ArgumentNullException(nameof(command));
    }

    public WorkstationCommandModel Command { get; }
}

public partial class WorkstationCommandBarControl : UserControl
{
    public static readonly DependencyProperty CommandGroupProperty =
        DependencyProperty.Register(
            nameof(CommandGroup),
            typeof(WorkstationCommandGroupModel),
            typeof(WorkstationCommandBarControl),
            new PropertyMetadata(new WorkstationCommandGroupModel(), OnCommandGroupChanged));

    public WorkstationCommandBarControl()
    {
        InitializeComponent();
        Loaded += (_, _) => UpdateMoreButtonVisibility();
    }

    public event EventHandler<WorkstationCommandInvokedEventArgs>? CommandInvoked;

    public WorkstationCommandGroupModel CommandGroup
    {
        get => (WorkstationCommandGroupModel)GetValue(CommandGroupProperty);
        set => SetValue(CommandGroupProperty, value);
    }

    private void OnPrimaryCommandClick(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: WorkstationCommandModel command })
        {
            CommandInvoked?.Invoke(this, new WorkstationCommandInvokedEventArgs(command));
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
                ToolTip = command.DisabledReason
            };

            menuItem.Click += OnSecondaryCommandClick;
            menu.Items.Add(menuItem);
        }

        menu.IsOpen = true;
    }

    private void OnSecondaryCommandClick(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: WorkstationCommandModel command })
        {
            CommandInvoked?.Invoke(this, new WorkstationCommandInvokedEventArgs(command));
        }
    }

    private static void OnCommandGroupChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is WorkstationCommandBarControl control)
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
