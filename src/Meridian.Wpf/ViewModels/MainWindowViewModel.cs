using System.Windows.Media;
using System.Windows.Threading;
using System.Linq;
using CommunityToolkit.Mvvm.Input;
using Meridian.Ui.Services;
using Meridian.Ui.Services.Services;
using Meridian.Wpf.Contracts;
using Meridian.Wpf.Services;
using WpfServices = Meridian.Wpf.Services;

namespace Meridian.Wpf.ViewModels;

/// <summary>
/// ViewModel for <see cref="MainWindow"/>. Owns shell-level commands plus
/// banner state so the window code-behind only handles WPF-specific hooks.
/// </summary>
public sealed class MainWindowViewModel : BindableBase, IDisposable
{
    private static readonly Brush FixtureBrush = CreateBrush(0xFF, 0xB3, 0x00);
    private static readonly Brush OfflineBrush = CreateBrush(0xF4, 0x43, 0x36);

    private readonly IConnectionService _connectionService;
    private readonly NavigationService _navigationService;
    private readonly WpfServices.NotificationService _notificationService;
    private readonly MessagingService _messagingService;
    private readonly ThemeService _themeService;
    private readonly WpfServices.WatchlistService _watchlistService;
    private readonly FixtureModeDetector _fixtureModeDetector;
    private readonly DispatcherTimer _clipboardBannerTimer;

    private IReadOnlyList<string> _pendingClipboardSymbols = [];
    private Visibility _fixtureModeBannerVisibility = Visibility.Collapsed;
    private string _fixtureModeText = string.Empty;
    private Brush _fixtureModeBannerBackground = FixtureBrush;
    private Visibility _clipboardBannerVisibility = Visibility.Collapsed;
    private string _clipboardBannerText = string.Empty;

    public MainWindowViewModel(
        IConnectionService connectionService,
        NavigationService navigationService,
        WpfServices.NotificationService notificationService,
        MessagingService messagingService,
        ThemeService themeService,
        WpfServices.WatchlistService watchlistService,
        FixtureModeDetector fixtureModeDetector,
        IStatusService statusService)
    {
        _connectionService = connectionService ?? throw new ArgumentNullException(nameof(connectionService));
        _navigationService = navigationService ?? throw new ArgumentNullException(nameof(navigationService));
        _notificationService = notificationService ?? throw new ArgumentNullException(nameof(notificationService));
        _messagingService = messagingService ?? throw new ArgumentNullException(nameof(messagingService));
        _themeService = themeService ?? throw new ArgumentNullException(nameof(themeService));
        _watchlistService = watchlistService ?? throw new ArgumentNullException(nameof(watchlistService));
        _fixtureModeDetector = fixtureModeDetector ?? throw new ArgumentNullException(nameof(fixtureModeDetector));

        StatusBar = new StatusBarViewModel(statusService, notificationService);

        NavigateCommand = new RelayCommand<string>(Navigate);
        StartCollectorCommand = new AsyncRelayCommand(StartCollectorAsync);
        StopCollectorCommand = new AsyncRelayCommand(StopCollectorAsync);
        RefreshCommand = new RelayCommand(() => _messagingService.Send("RefreshStatus"));
        AddClipboardSymbolsCommand = new AsyncRelayCommand(AddPendingSymbolsToWatchlistAsync, () => _pendingClipboardSymbols.Count > 0);
        DismissClipboardBannerCommand = new RelayCommand(HideClipboardBanner);

        _clipboardBannerTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(8) };
        _clipboardBannerTimer.Tick += OnClipboardBannerTimerTick;

        _fixtureModeDetector.ModeChanged += OnFixtureModeChanged;
        UpdateFixtureModeBanner();
    }

    public StatusBarViewModel StatusBar { get; }

    public IRelayCommand<string> NavigateCommand { get; }

    public IAsyncRelayCommand StartCollectorCommand { get; }

    public IAsyncRelayCommand StopCollectorCommand { get; }

    public IRelayCommand RefreshCommand { get; }

    public IAsyncRelayCommand AddClipboardSymbolsCommand { get; }

    public IRelayCommand DismissClipboardBannerCommand { get; }

    public Visibility FixtureModeBannerVisibility
    {
        get => _fixtureModeBannerVisibility;
        private set => SetProperty(ref _fixtureModeBannerVisibility, value);
    }

    public string FixtureModeText
    {
        get => _fixtureModeText;
        private set => SetProperty(ref _fixtureModeText, value);
    }

    public Brush FixtureModeBannerBackground
    {
        get => _fixtureModeBannerBackground;
        private set => SetProperty(ref _fixtureModeBannerBackground, value);
    }

    public Visibility ClipboardBannerVisibility
    {
        get => _clipboardBannerVisibility;
        private set => SetProperty(ref _clipboardBannerVisibility, value);
    }

    public string ClipboardBannerText
    {
        get => _clipboardBannerText;
        private set => SetProperty(ref _clipboardBannerText, value);
    }

    public async Task StartAsync(CancellationToken ct = default)
    {
        await StatusBar.StartAsync(ct);
    }

    public void ShowClipboardSymbols(IReadOnlyList<string> symbols)
    {
        _pendingClipboardSymbols = symbols ?? [];
        ClipboardBannerText = FormatClipboardBannerText(_pendingClipboardSymbols);
        ClipboardBannerVisibility = _pendingClipboardSymbols.Count > 0
            ? Visibility.Visible
            : Visibility.Collapsed;

        AddClipboardSymbolsCommand.NotifyCanExecuteChanged();

        _clipboardBannerTimer.Stop();
        if (_pendingClipboardSymbols.Count > 0)
        {
            _clipboardBannerTimer.Start();
        }
    }

    public void HideClipboardBanner()
    {
        _clipboardBannerTimer.Stop();
        _pendingClipboardSymbols = [];
        ClipboardBannerText = string.Empty;
        ClipboardBannerVisibility = Visibility.Collapsed;
        AddClipboardSymbolsCommand.NotifyCanExecuteChanged();
    }

    public void HandleShortcut(string actionId)
    {
        switch (actionId)
        {
            case "NavigateDashboard":
                Navigate("Dashboard");
                break;
            case "NavigateSymbols":
                Navigate("Symbols");
                break;
            case "NavigateBackfill":
                Navigate("Backfill");
                break;
            case "NavigateSettings":
                Navigate("Settings");
                break;
            case "StartCollector":
                _ = StartCollectorAsync();
                break;
            case "StopCollector":
                _ = StopCollectorAsync();
                break;
            case "RunBackfill":
                Navigate("Backfill");
                break;
            case "PauseBackfill":
                _messagingService.Send("PauseBackfill");
                break;
            case "CancelBackfill":
                _messagingService.Send("CancelBackfill");
                break;
            case "AddSymbol":
                Navigate("Symbols");
                _messagingService.Send("AddSymbol");
                break;
            case "SearchSymbols":
                _messagingService.Send("FocusSearch");
                break;
            case "DeleteSelected":
                _messagingService.Send("DeleteSelected");
                break;
            case "SelectAll":
                _messagingService.Send("SelectAll");
                break;
            case "ToggleTheme":
                _themeService.ToggleTheme();
                break;
            case "ViewLogs":
                Navigate("ServiceManager");
                break;
            case "RefreshStatus":
                _messagingService.Send("RefreshStatus");
                break;
            case "ZoomIn":
                _messagingService.Send("ZoomIn");
                break;
            case "ZoomOut":
                _messagingService.Send("ZoomOut");
                break;
            case "Save":
                _messagingService.Send("Save");
                break;
            case "Help":
                Navigate("Help");
                break;
        }
    }

    public void HandlePaletteAction(string actionId)
    {
        switch (actionId)
        {
            case "StartCollector":
                _ = StartCollectorAsync();
                break;
            case "StopCollector":
                _ = StopCollectorAsync();
                break;
            case "RunBackfill":
                Navigate("Backfill");
                break;
            case "RefreshStatus":
                _messagingService.Send("RefreshStatus");
                break;
            case "AddSymbol":
                Navigate("Symbols");
                _messagingService.Send("AddSymbol");
                break;
            case "ToggleTheme":
                _themeService.ToggleTheme();
                break;
            case "Save":
                _messagingService.Send("Save");
                break;
            case "SearchSymbols":
                _messagingService.Send("FocusSearch");
                break;
        }
    }

    public void HandleLaunchArgs(string[] args)
    {
        if (args.Length == 0)
        {
            return;
        }

        foreach (var arg in args)
        {
            if (arg.StartsWith("--page=", StringComparison.OrdinalIgnoreCase))
            {
                var pageTag = arg["--page=".Length..];
                if (!string.IsNullOrWhiteSpace(pageTag))
                {
                    Navigate(pageTag);
                }
            }
            else if (arg.Equals("--start-collector", StringComparison.OrdinalIgnoreCase))
            {
                _ = StartCollectorAsync();
            }
        }
    }

    public void ExecuteRemediationStep(RemediationStep step)
    {
        if (!string.IsNullOrEmpty(step.NavigationTarget))
        {
            Navigate(step.NavigationTarget);
        }

        if (string.IsNullOrEmpty(step.ActionId))
        {
            return;
        }

        switch (step.ActionId)
        {
            case "TestConnectivity":
                _messagingService.Send("TestConnectivity");
                break;
            case "TestConnection":
                _ = TestConnectionAsync();
                break;
            case "RunBackfill":
                _messagingService.Send("RunBackfill");
                break;
            case "RunMigration":
                _messagingService.Send("RunMigration");
                break;
            case "ValidateData":
                _messagingService.Send("ValidateData");
                break;
            default:
                _messagingService.Send(step.ActionId);
                break;
        }
    }

    public void Dispose()
    {
        _clipboardBannerTimer.Stop();
        _clipboardBannerTimer.Tick -= OnClipboardBannerTimerTick;
        _fixtureModeDetector.ModeChanged -= OnFixtureModeChanged;
        StatusBar.Dispose();
    }

    private static Brush CreateBrush(byte r, byte g, byte b)
    {
        var brush = new SolidColorBrush(Color.FromRgb(r, g, b));
        brush.Freeze();
        return brush;
    }

    private void OnFixtureModeChanged(object? sender, EventArgs e)
    {
        UpdateFixtureModeBanner();
    }

    private void OnClipboardBannerTimerTick(object? sender, EventArgs e)
    {
        HideClipboardBanner();
    }

    private void UpdateFixtureModeBanner()
    {
        FixtureModeBannerVisibility = _fixtureModeDetector.IsNonLiveMode
            ? Visibility.Visible
            : Visibility.Collapsed;
        FixtureModeText = _fixtureModeDetector.ModeLabel;
        FixtureModeBannerBackground = _fixtureModeDetector.IsFixtureMode
            ? FixtureBrush
            : OfflineBrush;
    }

    private void Navigate(string? pageTag)
    {
        if (string.IsNullOrWhiteSpace(pageTag))
        {
            return;
        }

        _navigationService.NavigateTo(pageTag);
    }

    private async Task StartCollectorAsync()
    {
        try
        {
            var provider = _connectionService.CurrentProvider;
            if (string.IsNullOrEmpty(provider))
            {
                provider = "default";
            }

            var success = await _connectionService.ConnectAsync(provider);
            _notificationService.ShowNotification(
                success ? "Collector Started" : "Start Failed",
                success
                    ? "Data collection has started successfully."
                    : "Failed to start the data collector. Check service connection.",
                success ? NotificationType.Success : NotificationType.Error,
                success ? 5000 : 0);
        }
        catch (Exception ex)
        {
            _notificationService.ShowNotification(
                "Start Error",
                $"Error starting collector: {ex.Message}",
                NotificationType.Error,
                0);
        }
    }

    private async Task StopCollectorAsync()
    {
        try
        {
            await _connectionService.DisconnectAsync();
            _notificationService.ShowNotification(
                "Collector Stopped",
                "Data collection has been stopped.",
                NotificationType.Warning,
                5000);
        }
        catch (Exception ex)
        {
            _notificationService.ShowNotification(
                "Stop Error",
                $"Error stopping collector: {ex.Message}",
                NotificationType.Error,
                0);
        }
    }

    private async Task TestConnectionAsync()
    {
        try
        {
            var provider = _connectionService.CurrentProvider ?? "default";
            var success = await _connectionService.ConnectAsync(provider);

            _notificationService.ShowNotification(
                success ? "Connection Restored" : "Connection Failed",
                success
                    ? $"Successfully reconnected to {provider}."
                    : $"Could not reconnect to {provider}. Check credentials in Settings.",
                success ? NotificationType.Success : NotificationType.Error,
                5000);
        }
        catch (Exception ex)
        {
            _notificationService.ShowNotification(
                "Connection Test Error",
                $"Error testing connection: {ex.Message}",
                NotificationType.Error,
                0);
        }
    }

    private async Task AddPendingSymbolsToWatchlistAsync()
    {
        var symbols = _pendingClipboardSymbols;
        HideClipboardBanner();

        if (symbols.Count == 0)
        {
            return;
        }

        try
        {
            var watchlists = await _watchlistService.GetAllWatchlistsAsync();

            int added;
            string targetName;

            if (watchlists.Count > 0)
            {
                var target = watchlists[0];
                added = await _watchlistService.AddSymbolsAsync(target.Id, symbols);
                targetName = target.Name;
            }
            else
            {
                var created = await _watchlistService.CreateWatchlistAsync("My Watchlist", symbols);
                added = symbols.Count;
                targetName = created.Name;
            }

            var symbolList = string.Join(", ", symbols);
            _notificationService.ShowNotification(
                "Watchlist Updated",
                added > 0
                    ? $"Added {added} symbol(s) to \"{targetName}\": {symbolList}"
                    : $"All symbols already in \"{targetName}\".",
                added > 0 ? NotificationType.Success : NotificationType.Info,
                5000);

            Navigate("Watchlist");
        }
        catch (Exception ex)
        {
            _notificationService.ShowNotification(
                "Watchlist Error",
                $"Could not add symbols: {ex.Message}",
                NotificationType.Error,
                0);
        }
    }

    private static string FormatClipboardBannerText(IReadOnlyList<string> symbols)
    {
        if (symbols.Count == 0)
        {
            return string.Empty;
        }

        const int previewLimit = 4;
        var previewSymbols = symbols.Take(previewLimit).ToArray();
        var symbolList = string.Join(", ", previewSymbols);
        if (symbols.Count > previewSymbols.Length)
        {
            symbolList = $"{symbolList} +{symbols.Count - previewSymbols.Length} more";
        }

        return symbols.Count == 1
            ? $"Symbol detected in clipboard: {symbolList} - Add to Watchlist?"
            : $"{symbols.Count} symbols detected in clipboard: {symbolList} - Add to Watchlist?";
    }
}
