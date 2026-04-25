using System.Windows;
using System.Windows.Controls;
using Meridian.Wpf.ViewModels;

namespace Meridian.Wpf.Views;

/// <summary>
/// Watchlist management page — thin code-behind.
/// All state, business logic, and commands live in <see cref="WatchlistViewModel"/>.
/// </summary>
public partial class WatchlistPage : Page
{
    private readonly WatchlistViewModel _viewModel;

    public WatchlistPage(WatchlistViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        DataContext = _viewModel;
    }

    private async void OnPageLoaded(object sender, RoutedEventArgs e) =>
        await _viewModel.StartAsync();

    private void OnPageUnloaded(object sender, RoutedEventArgs e) =>
        _viewModel.Stop();

    // Tag-based button delegates — pass watchlistId from Button.Tag to ViewModel commands.

    private void LoadWatchlist_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string id })
            _ = _viewModel.LoadWatchlistCommand.ExecuteAsync(id);
    }

    private void EditWatchlist_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string id })
            _ = _viewModel.EditWatchlistCommand.ExecuteAsync(id);
    }

    private void PinWatchlist_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string id })
            _ = _viewModel.PinWatchlistCommand.ExecuteAsync(id);
    }

    private void WatchlistMenu_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string watchlistId })
            return;

        var contextMenu = new ContextMenu();

        var exportItem = new MenuItem { Header = "Export to JSON" };
        exportItem.Click += (_, _) => _ = _viewModel.ExportWatchlistCommand.ExecuteAsync(watchlistId);
        contextMenu.Items.Add(exportItem);

        var duplicateItem = new MenuItem { Header = "Duplicate" };
        duplicateItem.Click += (_, _) => _ = _viewModel.DuplicateWatchlistCommand.ExecuteAsync(watchlistId);
        contextMenu.Items.Add(duplicateItem);

        contextMenu.Items.Add(new Separator());

        var deleteItem = new MenuItem { Header = "Delete", Foreground = System.Windows.Media.Brushes.Red };
        deleteItem.Click += (_, _) => _ = _viewModel.DeleteWatchlistCommand.ExecuteAsync(watchlistId);
        contextMenu.Items.Add(deleteItem);

        contextMenu.IsOpen = true;
    }
}
