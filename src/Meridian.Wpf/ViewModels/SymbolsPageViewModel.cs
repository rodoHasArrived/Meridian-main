using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Meridian.Ui.Services;
using Meridian.Wpf.Models;
using WpfServices = Meridian.Wpf.Services;

namespace Meridian.Wpf.ViewModels;

/// <summary>
/// ViewModel for the Symbols subscription management page.
/// All state, business logic, watchlist event handling, and backend sync live here;
/// the code-behind is thinned to lifecycle wiring and pure UI-input delegation.
/// </summary>
public sealed class SymbolsPageViewModel : BindableBase, IDisposable
{
    private readonly WpfServices.ConfigService _configService;
    private readonly WpfServices.WatchlistService _watchlistService;
    private readonly WpfServices.LoggingService _loggingService;
    private readonly WpfServices.NotificationService _notificationService;
    private readonly SymbolManagementService _symbolManagementService;

    private CancellationTokenSource? _loadCts;

    public static readonly IReadOnlyDictionary<string, string[]> SymbolTemplates =
        new Dictionary<string, string[]>
        {
            ["FAANG"] = new[] { "META", "AAPL", "AMZN", "NFLX", "GOOGL" },
            ["MagnificentSeven"] = new[] { "AAPL", "MSFT", "GOOGL", "AMZN", "NVDA", "META", "TSLA" },
            ["MajorETFs"] = new[] { "SPY", "QQQ", "IWM", "DIA", "VTI" },
            ["Semiconductors"] = new[] { "NVDA", "AMD", "INTC", "TSM", "AVGO", "QCOM" },
            ["Financials"] = new[] { "JPM", "BAC", "WFC", "GS", "MS", "C" }
        };

    // ── Public collections ──────────────────────────────────────────────────
    public ObservableCollection<SymbolViewModel> Symbols { get; } = new();
    public ObservableCollection<SymbolViewModel> FilteredSymbols { get; } = new();
    public ObservableCollection<WatchlistInfo> Watchlists { get; } = new();

    // ── Bindable properties ─────────────────────────────────────────────────
    private string _symbolCountText = "0 symbols";
    public string SymbolCountText
    {
        get => _symbolCountText;
        private set => SetProperty(ref _symbolCountText, value);
    }

    private string _selectionCountText = "0 selected";
    public string SelectionCountText
    {
        get => _selectionCountText;
        private set => SetProperty(ref _selectionCountText, value);
    }

    private bool _canBulkAction;
    public bool CanBulkAction
    {
        get => _canBulkAction;
        private set => SetProperty(ref _canBulkAction, value);
    }

    public SymbolsPageViewModel(
        WpfServices.ConfigService configService,
        WpfServices.WatchlistService watchlistService,
        WpfServices.LoggingService loggingService,
        WpfServices.NotificationService notificationService)
    {
        _configService = configService;
        _watchlistService = watchlistService;
        _loggingService = loggingService;
        _notificationService = notificationService;
        _symbolManagementService = SymbolManagementService.Instance;
    }

    // ── Lifecycle ───────────────────────────────────────────────────────────
    public async Task StartAsync(CancellationToken ct = default)
    {
        _watchlistService.WatchlistsChanged += OnWatchlistsChanged;
        await LoadSymbolsFromConfigAsync();
        await LoadWatchlistsAsync();
    }

    public void Stop()
    {
        _watchlistService.WatchlistsChanged -= OnWatchlistsChanged;
        _loadCts?.Cancel();
        _loadCts?.Dispose();
    }

    private void OnWatchlistsChanged(object? sender, WpfServices.WatchlistsChangedEventArgs e)
    {
        _ = System.Windows.Application.Current?.Dispatcher.InvokeAsync(LoadWatchlistsAsync);
    }

    // ── Data loading ────────────────────────────────────────────────────────
    public async Task LoadSymbolsFromConfigAsync(CancellationToken ct = default)
    {
        Symbols.Clear();
        try
        {
            var configuredSymbols = await _configService.GetConfiguredSymbolsAsync();
            foreach (var cfg in configuredSymbols)
            {
                Symbols.Add(new SymbolViewModel
                {
                    Symbol = cfg.Symbol,
                    SubscribeTrades = cfg.SubscribeTrades,
                    SubscribeDepth = cfg.SubscribeDepth,
                    DepthLevels = cfg.DepthLevels,
                    Exchange = cfg.Exchange,
                    LocalSymbol = cfg.LocalSymbol,
                    SecurityType = cfg.SecurityType,
                    Strike = cfg.Strike,
                    Right = cfg.Right,
                    LastTradeDateOrContractMonth = cfg.LastTradeDateOrContractMonth,
                    OptionStyle = cfg.OptionStyle,
                    Multiplier = cfg.Multiplier
                });
            }
        }
        catch (Exception ex)
        {
            _loggingService.LogError("Failed to load symbols from configuration", ex);
        }

        SymbolCountText = $"{Symbols.Count} symbols";
    }

    public async Task LoadWatchlistsAsync(CancellationToken ct = default)
    {
        _loadCts?.Cancel();
        _loadCts = new CancellationTokenSource();
        try
        {
            var watchlists = await _watchlistService.GetAllWatchlistsAsync(_loadCts.Token);
            Watchlists.Clear();
            foreach (var wl in watchlists)
            {
                Watchlists.Add(new WatchlistInfo
                {
                    Id = wl.Id,
                    Name = wl.Name,
                    SymbolCount = $"{wl.Symbols.Count} symbols",
                    Color = wl.Color,
                    IsPinned = wl.IsPinned
                });
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            _loggingService.LogError("Failed to load watchlists", ex);
        }
    }

    // ── Filtering ───────────────────────────────────────────────────────────
    public void ApplyFilters(string searchText, string filter, string exchangeFilter)
    {
        FilteredSymbols.Clear();
        foreach (var symbol in Symbols)
        {
            if (!string.IsNullOrEmpty(searchText) &&
                !symbol.Symbol.Contains(searchText, StringComparison.OrdinalIgnoreCase))
                continue;

            if (filter == "Trades" && !symbol.SubscribeTrades) continue;
            if (filter == "Depth" && !symbol.SubscribeDepth) continue;
            if (filter == "Both" && !(symbol.SubscribeTrades && symbol.SubscribeDepth)) continue;
            if (exchangeFilter != "All" && symbol.Exchange != exchangeFilter) continue;

            FilteredSymbols.Add(symbol);
        }

        UpdateSelectionCount();
    }

    public void UpdateSelectionCount()
    {
        var count = FilteredSymbols.Count(s => s.IsSelected);
        SelectionCountText = $"{count} selected";
        CanBulkAction = count > 0;
    }

    // ── Bulk operations ─────────────────────────────────────────────────────
    public void BulkEnableTrades()
    {
        var selected = FilteredSymbols.Where(s => s.IsSelected).ToList();
        foreach (var symbol in selected)
            symbol.SubscribeTrades = true;
        _notificationService.ShowNotification(
            "Bulk Update",
            $"Enabled trades for {selected.Count} symbols.",
            NotificationType.Success);
    }

    public void BulkEnableDepth()
    {
        var selected = FilteredSymbols.Where(s => s.IsSelected).ToList();
        foreach (var symbol in selected)
            symbol.SubscribeDepth = true;
        _notificationService.ShowNotification(
            "Bulk Update",
            $"Enabled depth for {selected.Count} symbols.",
            NotificationType.Success);
    }

    public async Task BulkDeleteSymbolsAsync(IEnumerable<SymbolViewModel> selectedSymbols, CancellationToken ct = default)
    {
        var toDelete = selectedSymbols.ToList();
        var symbolNames = toDelete.Select(s => s.Symbol).ToList();
        foreach (var symbol in toDelete)
            Symbols.Remove(symbol);

        SymbolCountText = $"{Symbols.Count} symbols";
        _notificationService.ShowNotification(
            "Bulk Delete",
            $"Deleted {toDelete.Count} symbols.",
            NotificationType.Success);

        await PersistSymbolsToConfigAsync();
        foreach (var name in symbolNames)
            _ = SyncRemoveSymbolFromBackendAsync(name);
    }

    // ── Templates ───────────────────────────────────────────────────────────
    public void AddTemplate(string templateName)
    {
        if (!SymbolTemplates.TryGetValue(templateName, out var symbols)) return;
        var added = 0;
        foreach (var symbol in symbols)
        {
            if (!Symbols.Any(s => s.Symbol == symbol))
            {
                Symbols.Add(new SymbolViewModel
                {
                    Symbol = symbol,
                    SubscribeTrades = true,
                    SubscribeDepth = false,
                    DepthLevels = 10,
                    Exchange = "SMART"
                });
                added++;
            }
        }

        SymbolCountText = $"{Symbols.Count} symbols";
        _notificationService.ShowNotification(
            "Template Added",
            $"Added {added} new symbols from {templateName} template.",
            NotificationType.Success);
    }

    // ── Watchlist operations ────────────────────────────────────────────────
    /// <summary>Loads symbols from a watchlist. Returns false if not found.</summary>
    public async Task<bool> LoadWatchlistSymbolsAsync(string? watchlistId, CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(watchlistId))
        {
            var first = Watchlists.FirstOrDefault();
            if (first == null)
            {
                _notificationService.ShowNotification(
                    "No Watchlists",
                    "No watchlists available. Create a watchlist first.",
                    NotificationType.Warning);
                return false;
            }
            watchlistId = first.Id;
        }

        try
        {
            var watchlist = await _watchlistService.GetWatchlistAsync(watchlistId);
            if (watchlist == null)
            {
                _notificationService.ShowNotification(
                    "Watchlist Not Found",
                    "The selected watchlist could not be found.",
                    NotificationType.Error);
                return false;
            }

            Symbols.Clear();
            foreach (var symbol in watchlist.Symbols)
            {
                Symbols.Add(new SymbolViewModel
                {
                    Symbol = symbol,
                    SubscribeTrades = true,
                    SubscribeDepth = false,
                    DepthLevels = 10,
                    Exchange = "SMART"
                });
            }

            SymbolCountText = $"{Symbols.Count} symbols";
            _notificationService.ShowNotification(
                "Watchlist Loaded",
                $"Loaded {watchlist.Symbols.Count} symbols from '{watchlist.Name}'.",
                NotificationType.Success);
            return true;
        }
        catch (Exception ex)
        {
            _loggingService.LogError("Failed to load watchlist", ex);
            _notificationService.ShowNotification(
                "Error",
                "Failed to load watchlist. Please try again.",
                NotificationType.Error);
            return false;
        }
    }

    public async Task SaveWatchlistAsync(string name, bool saveAsNew, string? existingWatchlistId, CancellationToken ct = default)
    {
        try
        {
            var symbols = Symbols.Select(s => s.Symbol).ToArray();
            if (saveAsNew || string.IsNullOrEmpty(existingWatchlistId))
            {
                var watchlist = await _watchlistService.CreateWatchlistAsync(name, symbols);
                _notificationService.ShowNotification(
                    "Watchlist Created",
                    $"Created watchlist '{watchlist.Name}' with {symbols.Length} symbols.",
                    NotificationType.Success);
            }
            else
            {
                var existing = await _watchlistService.GetWatchlistAsync(existingWatchlistId);
                if (existing != null)
                {
                    await _watchlistService.RemoveSymbolsAsync(existingWatchlistId, existing.Symbols);
                    await _watchlistService.AddSymbolsAsync(existingWatchlistId, symbols);
                    _notificationService.ShowNotification(
                        "Watchlist Updated",
                        $"Updated watchlist '{existing.Name}' with {symbols.Length} symbols.",
                        NotificationType.Success);
                }
            }

            await LoadWatchlistsAsync();
        }
        catch (Exception ex)
        {
            _loggingService.LogError("Failed to save watchlist", ex);
            _notificationService.ShowNotification(
                "Error",
                "Failed to save watchlist. Please try again.",
                NotificationType.Error);
        }
    }

    // ── CRUD ────────────────────────────────────────────────────────────────
    /// <summary>
    /// Adds or updates a symbol. Returns null on success; otherwise a user-visible error message.
    /// </summary>
    public async Task<string?> SaveSymbolAsync(
        string symbolName,
        bool subscribeTrades,
        bool subscribeDepth,
        int depthLevels,
        string exchange,
        string? localSymbol,
        string securityType,
        decimal? strike,
        string? right,
        string? lastTradeDateOrContractMonth,
        string? optionStyle,
        int? multiplier,
        SymbolViewModel? editTarget, CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(symbolName))
            return "Symbol is required.";

        if (editTarget != null)
        {
            editTarget.Symbol = symbolName;
            editTarget.SubscribeTrades = subscribeTrades;
            editTarget.SubscribeDepth = subscribeDepth;
            editTarget.DepthLevels = depthLevels;
            editTarget.Exchange = exchange;
            editTarget.LocalSymbol = localSymbol;
            editTarget.SecurityType = securityType;
            editTarget.Strike = strike;
            editTarget.Right = right;
            editTarget.LastTradeDateOrContractMonth = lastTradeDateOrContractMonth;
            editTarget.OptionStyle = optionStyle;
            editTarget.Multiplier = multiplier;
            _notificationService.ShowNotification(
                "Success",
                $"Symbol {symbolName} updated successfully.",
                NotificationType.Success);
        }
        else
        {
            if (Symbols.Any(s => s.Symbol == symbolName))
            {
                _notificationService.ShowNotification(
                    "Duplicate Symbol",
                    $"{symbolName} already exists.",
                    NotificationType.Warning);
                return $"{symbolName} already exists.";
            }

            var newVm = new SymbolViewModel
            {
                Symbol = symbolName,
                SubscribeTrades = subscribeTrades,
                SubscribeDepth = subscribeDepth,
                DepthLevels = depthLevels,
                Exchange = exchange,
                LocalSymbol = localSymbol,
                SecurityType = securityType,
                Strike = strike,
                Right = right,
                LastTradeDateOrContractMonth = lastTradeDateOrContractMonth,
                OptionStyle = optionStyle,
                Multiplier = multiplier
            };
            Symbols.Add(newVm);
            _notificationService.ShowNotification(
                "Success",
                $"Symbol {symbolName} added successfully.",
                NotificationType.Success);
            _ = SyncAddSymbolToBackendAsync(symbolName, subscribeTrades, subscribeDepth, depthLevels, exchange);
        }

        SymbolCountText = $"{Symbols.Count} symbols";
        await PersistSymbolsToConfigAsync();
        return null;
    }

    public async Task DeleteSymbolAsync(SymbolViewModel symbol, CancellationToken ct = default)
    {
        var symbolToDelete = symbol.Symbol;
        Symbols.Remove(symbol);
        SymbolCountText = $"{Symbols.Count} symbols";

        _notificationService.ShowNotification(
            "Success",
            $"Symbol {symbolToDelete} deleted.",
            NotificationType.Success);

        await PersistSymbolsToConfigAsync();
        _ = SyncRemoveSymbolFromBackendAsync(symbolToDelete);
    }

    // ── Persistence & backend sync ──────────────────────────────────────────
    public async Task PersistSymbolsToConfigAsync(CancellationToken ct = default)
    {
        try
        {
            var symbolDtos = Symbols.Select(s =>
                new Meridian.Contracts.Configuration.SymbolConfigDto
                {
                    Symbol = s.Symbol,
                    SubscribeTrades = s.SubscribeTrades,
                    SubscribeDepth = s.SubscribeDepth,
                    DepthLevels = s.DepthLevels,
                    Exchange = s.Exchange,
                    LocalSymbol = s.LocalSymbol,
                    SecurityType = s.SecurityType,
                    Strike = s.Strike,
                    Right = s.Right,
                    LastTradeDateOrContractMonth = s.LastTradeDateOrContractMonth,
                    OptionStyle = s.OptionStyle,
                    Multiplier = s.Multiplier
                }).ToArray();

            await _configService.SaveSymbolsAsync(symbolDtos);
        }
        catch (Exception ex)
        {
            _loggingService.LogError("Failed to persist symbols to config", ex);
        }
    }

    private async Task SyncAddSymbolToBackendAsync(
        string symbol, bool subscribeTrades, bool subscribeDepth, int depthLevels, string exchange, CancellationToken ct = default)
    {
        try
        {
            await _symbolManagementService.AddSymbolAsync(
                symbol, subscribeTrades, subscribeDepth, depthLevels, exchange);
        }
        catch (Exception ex)
        {
            _loggingService.LogError("Backend sync failed for add symbol", ex, ("symbol", symbol));
        }
    }

    private async Task SyncRemoveSymbolFromBackendAsync(string symbol, CancellationToken ct = default)
    {
        try
        {
            await _symbolManagementService.RemoveSymbolAsync(symbol);
        }
        catch (Exception ex)
        {
            _loggingService.LogError("Backend sync failed for remove symbol", ex, ("symbol", symbol));
        }
    }

    public void Dispose() => Stop();
}
