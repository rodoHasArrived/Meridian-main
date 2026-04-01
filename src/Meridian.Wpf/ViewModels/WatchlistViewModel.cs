using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using CommunityToolkit.Mvvm.Input;
using Meridian.Wpf.Models;
using Meridian.Wpf.Views;
using WpfServices = Meridian.Wpf.Services;

namespace Meridian.Wpf.ViewModels;

/// <summary>
/// ViewModel for the Watchlist page.
/// Holds all state, business logic, and commands so the code-behind can be
/// thinned to lifecycle wiring only.
/// </summary>
public sealed class WatchlistViewModel : BindableBase, IDisposable
{
    private readonly WpfServices.WatchlistService _watchlistService;
    private readonly WpfServices.LoggingService _loggingService;
    private readonly WpfServices.NotificationService _notificationService;
    private readonly WpfServices.NavigationService _navigationService;

    private readonly ObservableCollection<WatchlistDisplayModel> _allWatchlists = new();
    private CancellationTokenSource? _loadCts;

    private string _searchText = string.Empty;
    private Visibility _emptyStateVisibility = Visibility.Collapsed;
    private Visibility _listVisibility = Visibility.Collapsed;

    public WatchlistViewModel(
        WpfServices.WatchlistService watchlistService,
        WpfServices.LoggingService loggingService,
        WpfServices.NotificationService notificationService,
        WpfServices.NavigationService navigationService)
    {
        _watchlistService = watchlistService;
        _loggingService = loggingService;
        _notificationService = notificationService;
        _navigationService = navigationService;

        FilteredWatchlists = new ObservableCollection<WatchlistDisplayModel>();

        CreateWatchlistCommand = new AsyncRelayCommand(CreateWatchlistAsync);
        ImportWatchlistCommand = new AsyncRelayCommand(ImportWatchlistAsync);
        LoadWatchlistCommand = new AsyncRelayCommand<string>(LoadWatchlistAsync);
        EditWatchlistCommand = new AsyncRelayCommand<string>(EditWatchlistAsync);
        PinWatchlistCommand = new AsyncRelayCommand<string>(PinWatchlistAsync);
        ExportWatchlistCommand = new AsyncRelayCommand<string>(id => ExportWatchlistAsync(id ?? string.Empty));
        DuplicateWatchlistCommand = new AsyncRelayCommand<string>(id => DuplicateWatchlistAsync(id ?? string.Empty));
        DeleteWatchlistCommand = new AsyncRelayCommand<string>(id => DeleteWatchlistAsync(id ?? string.Empty));
    }

    // ── Collections ───────────────────────────────────────────────────────────

    public ObservableCollection<WatchlistDisplayModel> FilteredWatchlists { get; }

    // ── Bindable Properties ───────────────────────────────────────────────────

    public string SearchText
    {
        get => _searchText;
        set
        {
            if (SetProperty(ref _searchText, value))
                ApplyFilter();
        }
    }

    public Visibility EmptyStateVisibility
    {
        get => _emptyStateVisibility;
        private set => SetProperty(ref _emptyStateVisibility, value);
    }

    public Visibility ListVisibility
    {
        get => _listVisibility;
        private set => SetProperty(ref _listVisibility, value);
    }

    // ── Commands ──────────────────────────────────────────────────────────────

    public IAsyncRelayCommand CreateWatchlistCommand { get; }
    public IAsyncRelayCommand ImportWatchlistCommand { get; }
    public IAsyncRelayCommand<string> LoadWatchlistCommand { get; }
    public IAsyncRelayCommand<string> EditWatchlistCommand { get; }
    public IAsyncRelayCommand<string> PinWatchlistCommand { get; }
    public IAsyncRelayCommand<string> ExportWatchlistCommand { get; }
    public IAsyncRelayCommand<string> DuplicateWatchlistCommand { get; }
    public IAsyncRelayCommand<string> DeleteWatchlistCommand { get; }

    // ── Lifecycle ─────────────────────────────────────────────────────────────

    public async Task StartAsync(CancellationToken ct = default)
    {
        _watchlistService.WatchlistsChanged += OnWatchlistsChanged;
        await LoadWatchlistsAsync(ct);
    }

    public void Stop()
    {
        _watchlistService.WatchlistsChanged -= OnWatchlistsChanged;
        _loadCts?.Cancel();
        _loadCts?.Dispose();
        _loadCts = null;
    }

    public void Dispose() => Stop();

    // ── Internal ──────────────────────────────────────────────────────────────

    private void OnWatchlistsChanged(object? sender, WpfServices.WatchlistsChangedEventArgs e)
    {
        _ = System.Windows.Application.Current?.Dispatcher.InvokeAsync(async () =>
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

    private async Task LoadWatchlistsAsync(CancellationToken ct = default)
    {
        _loadCts?.Cancel();
        _loadCts = new CancellationTokenSource();

        try
        {
            var watchlists = await _watchlistService.GetAllWatchlistsAsync(_loadCts.Token);
            _allWatchlists.Clear();

            foreach (var wl in watchlists)
            {
                _allWatchlists.Add(new WatchlistDisplayModel
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
        }
        catch (OperationCanceledException)
        {
            // Cancelled — ignore
        }
        catch (Exception ex)
        {
            _loggingService.LogError("Failed to load watchlists", ex);
        }
    }

    private void ApplyFilter()
    {
        var search = _searchText.ToLowerInvariant();
        FilteredWatchlists.Clear();

        foreach (var wl in _allWatchlists)
        {
            if (string.IsNullOrEmpty(search) ||
                wl.Name.ToLowerInvariant().Contains(search) ||
                wl.SymbolsPreview.Any(s => s.ToLowerInvariant().Contains(search)))
            {
                FilteredWatchlists.Add(wl);
            }
        }

        UpdateVisibility();
    }

    private void UpdateVisibility()
    {
        var hasItems = FilteredWatchlists.Count > 0;
        EmptyStateVisibility = hasItems ? Visibility.Collapsed : Visibility.Visible;
        ListVisibility = hasItems ? Visibility.Visible : Visibility.Collapsed;
    }

    private async Task CreateWatchlistAsync(CancellationToken ct = default)
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

            await LoadWatchlistsAsync(ct);
        }
        catch (Exception ex)
        {
            _loggingService.LogError("Failed to create watchlist", ex);
            _notificationService.ShowNotification("Error", "Failed to create watchlist. Please try again.", NotificationType.Error);
        }
    }

    private async Task ImportWatchlistAsync(CancellationToken ct = default)
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Filter = "JSON Files|*.json|All Files|*.*",
            Title = "Import Watchlist"
        };

        if (dialog.ShowDialog() != true) return;

        try
        {
            var json = await File.ReadAllTextAsync(dialog.FileName, ct);
            var watchlist = await _watchlistService.ImportWatchlistAsync(json);

            if (watchlist != null)
            {
                _notificationService.ShowNotification("Watchlist Imported", $"Imported watchlist '{watchlist.Name}'.", NotificationType.Success);
                await LoadWatchlistsAsync(ct);
            }
            else
            {
                _notificationService.ShowNotification("Import Failed", "The file does not contain a valid watchlist.", NotificationType.Error);
            }
        }
        catch (Exception ex)
        {
            _loggingService.LogError("Failed to import watchlist", ex);
            _notificationService.ShowNotification("Error", "Failed to import watchlist. Please try again.", NotificationType.Error);
        }
    }

    public async Task LoadWatchlistAsync(string? watchlistId, CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(watchlistId)) return;
        try
        {
            var watchlist = await _watchlistService.GetWatchlistAsync(watchlistId, ct);
            if (watchlist == null)
            {
                _notificationService.ShowNotification("Watchlist Not Found", "The selected watchlist could not be found.", NotificationType.Error);
                return;
            }

            _navigationService.NavigateTo(typeof(SymbolsPage), watchlist);
            _notificationService.ShowNotification("Watchlist Loaded", $"Loaded '{watchlist.Name}' with {watchlist.Symbols.Count} symbols.", NotificationType.Success);
        }
        catch (Exception ex)
        {
            _loggingService.LogError("Failed to load watchlist", ex);
            _notificationService.ShowNotification("Error", "Failed to load watchlist. Please try again.", NotificationType.Error);
        }
    }

    public async Task EditWatchlistAsync(string? watchlistId, CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(watchlistId)) return;
        try
        {
            var watchlist = await _watchlistService.GetWatchlistAsync(watchlistId, ct);
            if (watchlist == null) return;

            var dialog = new EditWatchlistDialog(watchlist);
            if (dialog.ShowDialog() != true) return;

            if (dialog.ShouldDelete)
            {
                var confirm = MessageBox.Show(
                    $"Are you sure you want to delete '{watchlist.Name}'?",
                    "Delete Watchlist",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Question);

                if (confirm == MessageBoxResult.Yes)
                {
                    await _watchlistService.DeleteWatchlistAsync(watchlistId, ct);
                    _notificationService.ShowNotification("Watchlist Deleted", $"Deleted watchlist '{watchlist.Name}'.", NotificationType.Success);
                }
            }
            else
            {
                await _watchlistService.UpdateWatchlistAsync(watchlistId, dialog.WatchlistName, dialog.SelectedColor);

                var currentSymbols = new System.Collections.Generic.HashSet<string>(watchlist.Symbols, StringComparer.OrdinalIgnoreCase);
                var newSymbols = dialog.Symbols
                    .Split(new[] { ',', ' ', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries)
                    .Select(s => s.Trim().ToUpperInvariant())
                    .Where(s => !string.IsNullOrEmpty(s))
                    .Distinct()
                    .ToList();

                var toAdd = newSymbols.Where(s => !currentSymbols.Contains(s)).ToArray();
                var toRemove = currentSymbols.Where(s => !newSymbols.Contains(s, StringComparer.OrdinalIgnoreCase)).ToArray();

                if (toRemove.Length > 0)
                    await _watchlistService.RemoveSymbolsAsync(watchlistId, toRemove, ct);
                if (toAdd.Length > 0)
                    await _watchlistService.AddSymbolsAsync(watchlistId, toAdd, ct);

                _notificationService.ShowNotification("Watchlist Updated", $"Updated watchlist '{dialog.WatchlistName}'.", NotificationType.Success);
            }

            await LoadWatchlistsAsync(ct);
        }
        catch (Exception ex)
        {
            _loggingService.LogError("Failed to edit watchlist", ex);
            _notificationService.ShowNotification("Error", "Failed to edit watchlist. Please try again.", NotificationType.Error);
        }
    }

    public async Task PinWatchlistAsync(string? watchlistId, CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(watchlistId)) return;
        try
        {
            var watchlist = await _watchlistService.GetWatchlistAsync(watchlistId, ct);
            if (watchlist == null) return;

            await _watchlistService.UpdateWatchlistAsync(watchlistId, isPinned: !watchlist.IsPinned);
            _notificationService.ShowNotification(
                watchlist.IsPinned ? "Unpinned" : "Pinned",
                $"Watchlist '{watchlist.Name}' {(watchlist.IsPinned ? "unpinned" : "pinned")}.",
                NotificationType.Info);

            await LoadWatchlistsAsync(ct);
        }
        catch (Exception ex)
        {
            _loggingService.LogError("Failed to toggle pin", ex);
        }
    }

    internal async Task ExportWatchlistAsync(string watchlistId, CancellationToken ct = default)
    {
        try
        {
            var watchlist = await _watchlistService.GetWatchlistAsync(watchlistId, ct);
            if (watchlist == null) return;

            var json = await _watchlistService.ExportWatchlistAsync(watchlistId, ct);
            if (json == null) return;

            var dialog = new Microsoft.Win32.SaveFileDialog
            {
                Filter = "JSON Files|*.json",
                FileName = $"{watchlist.Name.Replace(" ", "_")}.json",
                Title = "Export Watchlist"
            };

            if (dialog.ShowDialog() == true)
            {
                await File.WriteAllTextAsync(dialog.FileName, json, ct);
                _notificationService.ShowNotification("Watchlist Exported", $"Exported '{watchlist.Name}' to {Path.GetFileName(dialog.FileName)}.", NotificationType.Success);
            }
        }
        catch (Exception ex)
        {
            _loggingService.LogError("Failed to export watchlist", ex);
            _notificationService.ShowNotification("Error", "Failed to export watchlist.", NotificationType.Error);
        }
    }

    internal async Task DuplicateWatchlistAsync(string watchlistId, CancellationToken ct = default)
    {
        try
        {
            var watchlist = await _watchlistService.GetWatchlistAsync(watchlistId, ct);
            if (watchlist == null) return;

            await _watchlistService.CreateWatchlistAsync($"{watchlist.Name} (Copy)", watchlist.Symbols, watchlist.Color);
            _notificationService.ShowNotification("Watchlist Duplicated", $"Created copy of '{watchlist.Name}'.", NotificationType.Success);
            await LoadWatchlistsAsync(ct);
        }
        catch (Exception ex)
        {
            _loggingService.LogError("Failed to duplicate watchlist", ex);
            _notificationService.ShowNotification("Error", "Failed to duplicate watchlist.", NotificationType.Error);
        }
    }

    internal async Task DeleteWatchlistAsync(string watchlistId, CancellationToken ct = default)
    {
        try
        {
            var watchlist = await _watchlistService.GetWatchlistAsync(watchlistId, ct);
            if (watchlist == null) return;

            var result = MessageBox.Show(
                $"Are you sure you want to delete '{watchlist.Name}'?",
                "Delete Watchlist",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (result == MessageBoxResult.Yes)
            {
                await _watchlistService.DeleteWatchlistAsync(watchlistId, ct);
                _notificationService.ShowNotification("Watchlist Deleted", $"Deleted '{watchlist.Name}'.", NotificationType.Success);
                await LoadWatchlistsAsync(ct);
            }
        }
        catch (Exception ex)
        {
            _loggingService.LogError("Failed to delete watchlist", ex);
            _notificationService.ShowNotification("Error", "Failed to delete watchlist.", NotificationType.Error);
        }
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static Color ParseColor(string? color)
    {
        if (string.IsNullOrEmpty(color))
            return (Color)ColorConverter.ConvertFromString("#3A3A4E");
        try { return (Color)ColorConverter.ConvertFromString(color); }
        catch { return (Color)ColorConverter.ConvertFromString("#3A3A4E"); }
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
