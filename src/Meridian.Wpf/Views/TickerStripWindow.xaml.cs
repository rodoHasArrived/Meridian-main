using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Meridian.Wpf.Services;
using Meridian.Wpf.ViewModels;

namespace Meridian.Wpf.Views;

/// <summary>
/// Always-on-top borderless ticker strip docked to the bottom of the primary screen.
/// Renders bid/ask/last for watchlist symbols in a slim bar over all other applications.
/// Auto-collapses to a 4px hairline when the mouse leaves; expands to 28px on hover.
/// </summary>
public partial class TickerStripWindow : Window
{
    private readonly TickerStripViewModel _viewModel;

    public TickerStripWindow(TickerStripViewModel viewModel)
    {
        _viewModel = viewModel;
        DataContext = _viewModel;
        InitializeComponent();
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        PositionAtScreenBottom(expanded: true);
        _viewModel.Start();
    }

    internal void OnMouseEnter(object sender, MouseEventArgs e) => Expand();
    internal void OnMouseLeave(object sender, MouseEventArgs e) => Collapse();

    private void OnRightClick(object sender, MouseButtonEventArgs e)
    {
        var menu = new ContextMenu();

        var closeItem = new MenuItem { Header = "Close Ticker Strip" };
        closeItem.Click += (_, _) => TickerStripService.Close();
        menu.Items.Add(closeItem);

        menu.Items.Add(new Separator());

        var settingsItem = new MenuItem { Header = "Settings" };
        settingsItem.Click += (_, _) => MessagingService.Instance.SendNamed(
            MessageTypes.NavigationRequested, "Settings");
        menu.Items.Add(settingsItem);

        menu.IsOpen = true;
    }

    private void Expand()
    {
        Height = 28;
        Top = SystemParameters.WorkArea.Bottom - 28;
        FullStrip.Visibility = Visibility.Visible;
        HairlineStrip.Visibility = Visibility.Collapsed;
    }

    private void Collapse()
    {
        FullStrip.Visibility = Visibility.Collapsed;
        HairlineStrip.Visibility = Visibility.Visible;
        Height = 4;
        Top = SystemParameters.WorkArea.Bottom - 4;
    }

    private void PositionAtScreenBottom(bool expanded)
    {
        Width = SystemParameters.PrimaryScreenWidth;
        Left = 0;

        if (expanded)
        {
            Height = 28;
            Top = SystemParameters.WorkArea.Bottom - 28;
        }
        else
        {
            Height = 4;
            Top = SystemParameters.WorkArea.Bottom - 4;
        }
    }

    /// <summary>Stops polling when the window is closed.</summary>
    protected override void OnClosed(System.EventArgs e)
    {
        _viewModel.Dispose();
        base.OnClosed(e);
    }
}
