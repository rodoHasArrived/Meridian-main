using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Microsoft.Win32;
using Meridian.Wpf.Contracts;
using Meridian.Wpf.Services;
using WpfServices = Meridian.Wpf.Services;

namespace Meridian.Wpf.Views;

/// <summary>
/// WpfServices.Watchlist management page for creating, editing, and organizing symbol watchlists.
/// </summary>
public partial class WatchlistPage : Page
{
    private readonly WpfServices.WatchlistService _watchlistService;
    private readonly WpfServices.LoggingService _loggingService;
    private readonly WpfServices.NotificationService _notificationService;
    private readonly WpfServices.NavigationService _navigationService;
    private readonly ObservableCollection<WatchlistDisplayModel> _watchlists = new();
    private readonly ObservableCollection<WatchlistDisplayModel> _filteredWatchlists = new();
    private CancellationTokenSource? _loadCts;

    public WatchlistPage(
        WpfServices.WatchlistService watchlistService,
        WpfServices.LoggingService loggingService,
        WpfServices.NotificationService notificationService,
        WpfServices.NavigationService navigationService)
    {
        InitializeComponent();
        _watchlistService = watchlistService;
        _loggingService = loggingService;
        _notificationService = notificationService;
        _navigationService = navigationService;
        WatchlistsContainer.ItemsSource = _filteredWatchlists;

        _watchlistService.WatchlistsChanged += OnWatchlistsChanged;
        Unloaded += OnPageUnloaded;
    }

    private void OnPageUnloaded(object sender, RoutedEventArgs e)
    {
        _watchlistService.WatchlistsChanged -= OnWatchlistsChanged;
        _loadCts?.Cancel();
        _loadCts?.Dispose();
    }

    private void OnWatchlistsChanged(object? sender, WpfServices.WatchlistsChangedEventArgs e)
    {
        // InvokeAsync + explicit await: errors are captured rather than silently discarded [P2, E1]
        _ = Dispatcher.InvokeAsync(async () =>
        {
            try
            {
                await LoadWatchlistsAsync().ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _loggingService.LogError("Failed to reload watchlists on change notification", ex);
            }
        });
    }

    private async void OnPageLoaded(object sender, RoutedEventArgs e)
    {
        await LoadWatchlistsAsync();
    }

    private async Task LoadWatchlistsAsync(CancellationToken ct = default)
    {
        _loadCts?.Cancel();
        _loadCts = new CancellationTokenSource();

        try
        {
            var watchlists = await _watchlistService.GetAllWatchlistsAsync(_loadCts.Token);
            _watchlists.Clear();

            foreach (var wl in watchlists)
            {
                _watchlists.Add(new WatchlistDisplayModel
                {
                    Id = wl.Id,
                    Name = wl.Name,
                    SymbolCount = $"{wl.Symbols.Count} symbols",
                    Color = wl.Color,
                    ColorValue = ParseColor(wl.Color),
                    IsPinned = wl.IsPinned,
                    SymbolsPreview = wl.Symbols.Take(10).ToList(),
                    ModifiedText = FormatModifiedDate(wl.ModifiedAt)
                });
            }

            ApplyFilter();
            UpdateEmptyState();
        }
        catch (OperationCanceledException)
        {
            // Cancelled - ignore
        }
        catch (Exception ex)
        {
            _loggingService.LogError("Failed to load watchlists", ex);
        }
    }

    private void ApplyFilter()
    {
        var searchText = SearchBox?.Text?.ToLower() ?? "";
        _filteredWatchlists.Clear();

        foreach (var wl in _watchlists)
        {
            if (string.IsNullOrEmpty(searchText) ||
                wl.Name.ToLower().Contains(searchText) ||
                wl.SymbolsPreview.Any(s => s.ToLower().Contains(searchText)))
            {
                _filteredWatchlists.Add(wl);
            }
        }
    }

    private void UpdateEmptyState()
    {
        EmptyState.Visibility = _filteredWatchlists.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        WatchlistsContainer.Visibility = _filteredWatchlists.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        ApplyFilter();
        UpdateEmptyState();
    }

    private async void CreateWatchlist_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new CreateWatchlistDialog();
        if (dialog.ShowDialog() != true) return;

        try
        {
            var symbols = dialog.InitialSymbols
                .Split(new[] { ',', ' ', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(s => s.Trim().ToUpperInvariant())
                .Where(s => !string.IsNullOrEmpty(s))
                .Distinct()
                .ToArray();

            var watchlist = await _watchlistService.CreateWatchlistAsync(
                dialog.WatchlistName,
                symbols,
                dialog.SelectedColor);

            _notificationService.ShowNotification(
                "Watchlist Created",
                $"Created watchlist '{watchlist.Name}' with {symbols.Length} symbols.",
                NotificationType.Success);

            await LoadWatchlistsAsync();
        }
        catch (Exception ex)
        {
            _loggingService.LogError("Failed to create watchlist", ex);
            _notificationService.ShowNotification(
                "Error",
                "Failed to create watchlist. Please try again.",
                NotificationType.Error);
        }
    }

    private async void ImportWatchlist_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Filter = "JSON Files|*.json|All Files|*.*",
            Title = "Import Watchlist"
        };

        if (dialog.ShowDialog() != true) return;

        try
        {
            var json = await System.IO.File.ReadAllTextAsync(dialog.FileName);
            var watchlist = await _watchlistService.ImportWatchlistAsync(json);

            if (watchlist != null)
            {
                _notificationService.ShowNotification(
                    "Watchlist Imported",
                    $"Imported watchlist '{watchlist.Name}'.",
                    NotificationType.Success);

                await LoadWatchlistsAsync();
            }
            else
            {
                _notificationService.ShowNotification(
                    "Import Failed",
                    "The file does not contain a valid watchlist.",
                    NotificationType.Error);
            }
        }
        catch (Exception ex)
        {
            _loggingService.LogError("Failed to import watchlist", ex);
            _notificationService.ShowNotification(
                "Error",
                "Failed to import watchlist. Please try again.",
                NotificationType.Error);
        }
    }

    private async void LoadWatchlist_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button btn || btn.Tag is not string watchlistId) return;

        try
        {
            var watchlist = await _watchlistService.GetWatchlistAsync(watchlistId);
            if (watchlist == null)
            {
                _notificationService.ShowNotification(
                    "Watchlist Not Found",
                    "The selected watchlist could not be found.",
                    NotificationType.Error);
                return;
            }

            // Navigate to symbols page with this watchlist loaded
            _navigationService.NavigateTo(typeof(SymbolsPage), watchlist);

            _notificationService.ShowNotification(
                "Watchlist Loaded",
                $"Loaded '{watchlist.Name}' with {watchlist.Symbols.Count} symbols.",
                NotificationType.Success);
        }
        catch (Exception ex)
        {
            _loggingService.LogError("Failed to load watchlist", ex);
            _notificationService.ShowNotification(
                "Error",
                "Failed to load watchlist. Please try again.",
                NotificationType.Error);
        }
    }

    private async void EditWatchlist_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button btn || btn.Tag is not string watchlistId) return;

        try
        {
            var watchlist = await _watchlistService.GetWatchlistAsync(watchlistId);
            if (watchlist == null) return;

            var dialog = new EditWatchlistDialog(watchlist);
            if (dialog.ShowDialog() != true) return;

            if (dialog.ShouldDelete)
            {
                var result = MessageBox.Show(
                    $"Are you sure you want to delete '{watchlist.Name}'?",
                    "Delete Watchlist",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Question);

                if (result == MessageBoxResult.Yes)
                {
                    await _watchlistService.DeleteWatchlistAsync(watchlistId);
                    _notificationService.ShowNotification(
                        "Watchlist Deleted",
                        $"Deleted watchlist '{watchlist.Name}'.",
                        NotificationType.Success);
                }
            }
            else
            {
                // Update the watchlist
                await _watchlistService.UpdateWatchlistAsync(
                    watchlistId,
                    dialog.WatchlistName,
                    dialog.SelectedColor);

                // Update symbols if changed
                var currentSymbols = new HashSet<string>(watchlist.Symbols, StringComparer.OrdinalIgnoreCase);
                var newSymbols = dialog.Symbols
                    .Split(new[] { ',', ' ', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries)
                    .Select(s => s.Trim().ToUpperInvariant())
                    .Where(s => !string.IsNullOrEmpty(s))
                    .Distinct()
                    .ToList();

                var toAdd = newSymbols.Where(s => !currentSymbols.Contains(s)).ToArray();
                var toRemove = currentSymbols.Where(s => !newSymbols.Contains(s, StringComparer.OrdinalIgnoreCase)).ToArray();

                if (toRemove.Length > 0)
                    await _watchlistService.RemoveSymbolsAsync(watchlistId, toRemove);
                if (toAdd.Length > 0)
                    await _watchlistService.AddSymbolsAsync(watchlistId, toAdd);

                _notificationService.ShowNotification(
                    "Watchlist Updated",
                    $"Updated watchlist '{dialog.WatchlistName}'.",
                    NotificationType.Success);
            }

            await LoadWatchlistsAsync();
        }
        catch (Exception ex)
        {
            _loggingService.LogError("Failed to edit watchlist", ex);
            _notificationService.ShowNotification(
                "Error",
                "Failed to edit watchlist. Please try again.",
                NotificationType.Error);
        }
    }

    private async void PinWatchlist_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button btn || btn.Tag is not string watchlistId) return;

        try
        {
            var watchlist = await _watchlistService.GetWatchlistAsync(watchlistId);
            if (watchlist == null) return;

            await _watchlistService.UpdateWatchlistAsync(watchlistId, isPinned: !watchlist.IsPinned);

            _notificationService.ShowNotification(
                watchlist.IsPinned ? "Unpinned" : "Pinned",
                $"Watchlist '{watchlist.Name}' {(watchlist.IsPinned ? "unpinned" : "pinned")}.",
                NotificationType.Info);

            await LoadWatchlistsAsync();
        }
        catch (Exception ex)
        {
            _loggingService.LogError("Failed to toggle pin", ex);
        }
    }

    private async void WatchlistMenu_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button btn || btn.Tag is not string watchlistId) return;

        var contextMenu = new ContextMenu();

        var exportItem = new MenuItem { Header = "Export to JSON" };
        exportItem.Click += async (_, _) => await ExportWatchlistAsync(watchlistId);
        contextMenu.Items.Add(exportItem);

        var duplicateItem = new MenuItem { Header = "Duplicate" };
        duplicateItem.Click += async (_, _) => await DuplicateWatchlistAsync(watchlistId);
        contextMenu.Items.Add(duplicateItem);

        contextMenu.Items.Add(new Separator());

        var deleteItem = new MenuItem { Header = "Delete", Foreground = Brushes.Red };
        deleteItem.Click += async (_, _) => await DeleteWatchlistAsync(watchlistId);
        contextMenu.Items.Add(deleteItem);

        contextMenu.IsOpen = true;
        await Task.CompletedTask;
    }

    private async Task ExportWatchlistAsync(string watchlistId, CancellationToken ct = default)
    {
        try
        {
            var watchlist = await _watchlistService.GetWatchlistAsync(watchlistId);
            if (watchlist == null) return;

            var json = await _watchlistService.ExportWatchlistAsync(watchlistId);
            if (json == null) return;

            var dialog = new SaveFileDialog
            {
                Filter = "JSON Files|*.json",
                FileName = $"{watchlist.Name.Replace(" ", "_")}.json",
                Title = "Export Watchlist"
            };

            if (dialog.ShowDialog() == true)
            {
                await System.IO.File.WriteAllTextAsync(dialog.FileName, json);
                _notificationService.ShowNotification(
                    "Watchlist Exported",
                    $"Exported '{watchlist.Name}' to {System.IO.Path.GetFileName(dialog.FileName)}.",
                    NotificationType.Success);
            }
        }
        catch (Exception ex)
        {
            _loggingService.LogError("Failed to export watchlist", ex);
            _notificationService.ShowNotification(
                "Error",
                "Failed to export watchlist.",
                NotificationType.Error);
        }
    }

    private async Task DuplicateWatchlistAsync(string watchlistId, CancellationToken ct = default)
    {
        try
        {
            var watchlist = await _watchlistService.GetWatchlistAsync(watchlistId);
            if (watchlist == null) return;

            await _watchlistService.CreateWatchlistAsync(
                $"{watchlist.Name} (Copy)",
                watchlist.Symbols,
                watchlist.Color);

            _notificationService.ShowNotification(
                "Watchlist Duplicated",
                $"Created copy of '{watchlist.Name}'.",
                NotificationType.Success);

            await LoadWatchlistsAsync();
        }
        catch (Exception ex)
        {
            _loggingService.LogError("Failed to duplicate watchlist", ex);
            _notificationService.ShowNotification(
                "Error",
                "Failed to duplicate watchlist.",
                NotificationType.Error);
        }
    }

    private async Task DeleteWatchlistAsync(string watchlistId, CancellationToken ct = default)
    {
        try
        {
            var watchlist = await _watchlistService.GetWatchlistAsync(watchlistId);
            if (watchlist == null) return;

            var result = MessageBox.Show(
                $"Are you sure you want to delete '{watchlist.Name}'?",
                "Delete Watchlist",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (result == MessageBoxResult.Yes)
            {
                await _watchlistService.DeleteWatchlistAsync(watchlistId);
                _notificationService.ShowNotification(
                    "Watchlist Deleted",
                    $"Deleted '{watchlist.Name}'.",
                    NotificationType.Success);

                await LoadWatchlistsAsync();
            }
        }
        catch (Exception ex)
        {
            _loggingService.LogError("Failed to delete watchlist", ex);
            _notificationService.ShowNotification(
                "Error",
                "Failed to delete watchlist.",
                NotificationType.Error);
        }
    }

    private static Color ParseColor(string? color)
    {
        if (string.IsNullOrEmpty(color))
            return (Color)ColorConverter.ConvertFromString("#3A3A4E");

        try
        {
            return (Color)ColorConverter.ConvertFromString(color);
        }
        catch
        {
            return (Color)ColorConverter.ConvertFromString("#3A3A4E");
        }
    }

    private static string FormatModifiedDate(DateTimeOffset date)
    {
        var diff = DateTimeOffset.UtcNow - date;
        if (diff.TotalMinutes < 1) return "Just now";
        if (diff.TotalMinutes < 60) return $"{(int)diff.TotalMinutes}m ago";
        if (diff.TotalHours < 24) return $"{(int)diff.TotalHours}h ago";
        if (diff.TotalDays < 7) return $"{(int)diff.TotalDays}d ago";
        return date.ToString("MMM d, yyyy");
    }
}

/// <summary>
/// Display model for watchlist cards.
/// </summary>
public sealed class WatchlistDisplayModel
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string SymbolCount { get; set; } = string.Empty;
    public string? Color { get; set; }
    public Color ColorValue { get; set; }
    public bool IsPinned { get; set; }
    public List<string> SymbolsPreview { get; set; } = new();
    public string ModifiedText { get; set; } = string.Empty;
}

/// <summary>
/// Dialog for creating a new watchlist.
/// </summary>
public sealed class CreateWatchlistDialog : Window
{
    private readonly TextBox _nameBox;
    private readonly TextBox _symbolsBox;
    private readonly ComboBox _colorCombo;

    public string WatchlistName => _nameBox.Text;
    public string InitialSymbols => _symbolsBox.Text;
    public string? SelectedColor => (_colorCombo.SelectedItem as ComboBoxItem)?.Tag?.ToString();

    private static readonly (string Name, string Hex)[] Colors = new[]
    {
        ("Green", "#4CAF50"),
        ("Blue", "#2196F3"),
        ("Purple", "#9C27B0"),
        ("Orange", "#FF9800"),
        ("Red", "#F44336"),
        ("Teal", "#009688"),
        ("Pink", "#E91E63"),
        ("Gray", "#607D8B")
    };

    public CreateWatchlistDialog()
    {
        Title = "Create Watchlist";
        Width = 450;
        Height = 350;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ResizeMode = ResizeMode.NoResize;
        Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#1E1E2E"));

        var grid = new Grid { Margin = new Thickness(20) };
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        // Name
        AddLabel(grid, "Watchlist Name:", 0);
        _nameBox = CreateTextBox();
        Grid.SetRow(_nameBox, 1);
        grid.Children.Add(_nameBox);

        // Color
        AddLabel(grid, "Color:", 2);
        _colorCombo = new ComboBox
        {
            Margin = new Thickness(0, 0, 0, 16),
            Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#2A2A3E")),
            Foreground = Brushes.White
        };
        foreach (var (name, hex) in Colors)
        {
            var item = new ComboBoxItem
            {
                Content = name,
                Tag = hex,
                Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex))
            };
            _colorCombo.Items.Add(item);
        }
        _colorCombo.SelectedIndex = 0;
        Grid.SetRow(_colorCombo, 3);
        grid.Children.Add(_colorCombo);

        // Symbols
        AddLabel(grid, "Initial Symbols (comma-separated):", 4);
        _symbolsBox = new TextBox
        {
            Margin = new Thickness(0, 4, 0, 16),
            Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#2A2A3E")),
            Foreground = Brushes.White,
            BorderBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#3A3A4E")),
            Padding = new Thickness(8),
            AcceptsReturn = true,
            TextWrapping = TextWrapping.Wrap,
            Height = 80,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto
        };
        Grid.SetRow(_symbolsBox, 5);
        grid.Children.Add(_symbolsBox);

        // Buttons
        var buttonPanel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 8, 0, 0)
        };
        Grid.SetRow(buttonPanel, 6);

        var cancelButton = CreateButton("Cancel", "#3A3A4E");
        cancelButton.Click += (_, _) => { DialogResult = false; Close(); };
        buttonPanel.Children.Add(cancelButton);

        var createButton = CreateButton("Create", "#4CAF50");
        createButton.Margin = new Thickness(8, 0, 0, 0);
        createButton.Click += OnCreateClick;
        buttonPanel.Children.Add(createButton);

        grid.Children.Add(buttonPanel);
        Content = grid;
    }

    private void AddLabel(Grid grid, string text, int row)
    {
        var label = new TextBlock
        {
            Text = text,
            Foreground = Brushes.White,
            Margin = new Thickness(0, 0, 0, 4)
        };
        Grid.SetRow(label, row);
        grid.Children.Add(label);
    }

    private static TextBox CreateTextBox() => new()
    {
        Margin = new Thickness(0, 4, 0, 16),
        Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#2A2A3E")),
        Foreground = Brushes.White,
        BorderBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#3A3A4E")),
        Padding = new Thickness(8, 6, 8, 6)
    };

    private static Button CreateButton(string content, string bgColor) => new()
    {
        Content = content,
        Width = 100,
        Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString(bgColor)),
        Foreground = Brushes.White,
        BorderThickness = new Thickness(0),
        Padding = new Thickness(12, 8, 12, 8)
    };

    private void OnCreateClick(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(_nameBox.Text))
        {
            MessageBox.Show("Please enter a watchlist name.", "Validation Error",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        DialogResult = true;
        Close();
    }
}

/// <summary>
/// Dialog for editing an existing watchlist.
/// </summary>
public sealed class EditWatchlistDialog : Window
{
    private readonly TextBox _nameBox;
    private readonly TextBox _symbolsBox;
    private readonly ComboBox _colorCombo;

    public string WatchlistName => _nameBox.Text;
    public string Symbols => _symbolsBox.Text;
    public string? SelectedColor => (_colorCombo.SelectedItem as ComboBoxItem)?.Tag?.ToString();
    public bool ShouldDelete { get; private set; }

    private static readonly (string Name, string Hex)[] Colors = new[]
    {
        ("Green", "#4CAF50"),
        ("Blue", "#2196F3"),
        ("Purple", "#9C27B0"),
        ("Orange", "#FF9800"),
        ("Red", "#F44336"),
        ("Teal", "#009688"),
        ("Pink", "#E91E63"),
        ("Gray", "#607D8B")
    };

    public EditWatchlistDialog(Watchlist watchlist)
    {
        Title = "Edit Watchlist";
        Width = 450;
        Height = 400;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ResizeMode = ResizeMode.NoResize;
        Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#1E1E2E"));

        var grid = new Grid { Margin = new Thickness(20) };
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        // Name
        AddLabel(grid, "Watchlist Name:", 0);
        _nameBox = CreateTextBox();
        _nameBox.Text = watchlist.Name;
        Grid.SetRow(_nameBox, 1);
        grid.Children.Add(_nameBox);

        // Color
        AddLabel(grid, "Color:", 2);
        _colorCombo = new ComboBox
        {
            Margin = new Thickness(0, 0, 0, 16),
            Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#2A2A3E")),
            Foreground = Brushes.White
        };
        var selectedIndex = 0;
        for (var i = 0; i < Colors.Length; i++)
        {
            var (name, hex) = Colors[i];
            var item = new ComboBoxItem
            {
                Content = name,
                Tag = hex,
                Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex))
            };
            _colorCombo.Items.Add(item);
            if (hex.Equals(watchlist.Color, StringComparison.OrdinalIgnoreCase))
                selectedIndex = i;
        }
        _colorCombo.SelectedIndex = selectedIndex;
        Grid.SetRow(_colorCombo, 3);
        grid.Children.Add(_colorCombo);

        // Symbols
        AddLabel(grid, "Symbols (comma-separated):", 4);
        _symbolsBox = new TextBox
        {
            Margin = new Thickness(0, 4, 0, 16),
            Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#2A2A3E")),
            Foreground = Brushes.White,
            BorderBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#3A3A4E")),
            Padding = new Thickness(8),
            AcceptsReturn = true,
            TextWrapping = TextWrapping.Wrap,
            Height = 100,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Text = string.Join(", ", watchlist.Symbols)
        };
        Grid.SetRow(_symbolsBox, 5);
        grid.Children.Add(_symbolsBox);

        // Buttons
        var buttonPanel = new Grid { Margin = new Thickness(0, 8, 0, 0) };
        buttonPanel.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        buttonPanel.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        buttonPanel.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        buttonPanel.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        Grid.SetRow(buttonPanel, 6);

        var deleteButton = CreateButton("Delete", "#F44336");
        deleteButton.Click += (_, _) => { ShouldDelete = true; DialogResult = true; Close(); };
        Grid.SetColumn(deleteButton, 0);
        buttonPanel.Children.Add(deleteButton);

        var cancelButton = CreateButton("Cancel", "#3A3A4E");
        cancelButton.Click += (_, _) => { DialogResult = false; Close(); };
        Grid.SetColumn(cancelButton, 2);
        buttonPanel.Children.Add(cancelButton);

        var saveButton = CreateButton("Save", "#4CAF50");
        saveButton.Margin = new Thickness(8, 0, 0, 0);
        saveButton.Click += OnSaveClick;
        Grid.SetColumn(saveButton, 3);
        buttonPanel.Children.Add(saveButton);

        grid.Children.Add(buttonPanel);
        Content = grid;
    }

    private void AddLabel(Grid grid, string text, int row)
    {
        var label = new TextBlock
        {
            Text = text,
            Foreground = Brushes.White,
            Margin = new Thickness(0, 0, 0, 4)
        };
        Grid.SetRow(label, row);
        grid.Children.Add(label);
    }

    private static TextBox CreateTextBox() => new()
    {
        Margin = new Thickness(0, 4, 0, 16),
        Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#2A2A3E")),
        Foreground = Brushes.White,
        BorderBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#3A3A4E")),
        Padding = new Thickness(8, 6, 8, 6)
    };

    private static Button CreateButton(string content, string bgColor) => new()
    {
        Content = content,
        Width = 100,
        Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString(bgColor)),
        Foreground = Brushes.White,
        BorderThickness = new Thickness(0),
        Padding = new Thickness(12, 8, 12, 8)
    };

    private void OnSaveClick(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(_nameBox.Text))
        {
            MessageBox.Show("Please enter a watchlist name.", "Validation Error",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        DialogResult = true;
        Close();
    }
}
