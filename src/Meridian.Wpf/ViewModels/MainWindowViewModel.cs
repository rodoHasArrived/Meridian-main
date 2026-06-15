using System.Linq;
using System.Windows.Media;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.Input;
using Meridian.Ui.Services;
using Meridian.Ui.Services.Services;
using Meridian.Wpf.Contracts;
using Meridian.Wpf.Models;
using Meridian.Wpf.Services;
using WpfServices = Meridian.Wpf.Services;

namespace Meridian.Wpf.ViewModels;

/// <summary>
/// ViewModel for <see cref="MainWindow"/>. Owns shell-level commands plus
/// banner state so the window code-behind only handles WPF-specific hooks.
/// </summary>
public sealed class MainWindowViewModel : BindableBase, IDisposable
{
    private static readonly Brush FixtureBrush = CreateBrush(0x25, 0x63, 0xEB);
    private static readonly Brush OfflineBrush = CreateBrush(0xF4, 0x43, 0x36);

    private readonly IConnectionService _connectionService;
    private readonly NavigationService _navigationService;
    private readonly WpfServices.NotificationService _notificationService;
    private readonly MessagingService _messagingService;
    private readonly ThemeService _themeService;
    private readonly WpfServices.WatchlistService _watchlistService;
    private readonly FixtureModeDetector _fixtureModeDetector;
    private readonly DesktopAuthenticationSession _authenticationSession;
    private readonly DispatcherTimer _clipboardBannerTimer;
    private readonly DispatcherTimer _startupBannerTimer;

    private IReadOnlyList<string> _pendingClipboardSymbols = [];
    private Visibility _authenticatedSessionVisibility = Visibility.Collapsed;
    private string _authenticatedOperatorText = "Not signed in";
    private string _authenticatedRoleText = "No active session";
    private string _authenticatedSessionDetail = "Sign in from the Meridian startup screen.";
    private Visibility _fixtureModeBannerVisibility = Visibility.Collapsed;
    private string _fixtureModeText = string.Empty;
    private Brush _fixtureModeBannerBackground = FixtureBrush;
    private Visibility _startupBannerVisibility = Visibility.Collapsed;
    private string _startupBannerStatusText = "Startup briefing";
    private string _startupBannerMessage = "Review the active operating context, shell mode, and readiness posture before taking live actions.";
    private string _startupBannerDetail = "Use Welcome for the full workstation briefing, then move through Trading, Portfolio, Accounting, Reporting, Strategy, Data, or Settings from the same governed shell.";
    private Visibility _clipboardBannerVisibility = Visibility.Collapsed;
    private string _clipboardBannerText = string.Empty;
    private bool _startupExperienceShown;

    public MainWindowViewModel(
        IConnectionService connectionService,
        NavigationService navigationService,
        WpfServices.NotificationService notificationService,
        MessagingService messagingService,
        ThemeService themeService,
        WpfServices.WatchlistService watchlistService,
        FixtureModeDetector fixtureModeDetector,
        DesktopAuthenticationSession authenticationSession,
        IStatusService statusService)
    {
        _connectionService = connectionService ?? throw new ArgumentNullException(nameof(connectionService));
        _navigationService = navigationService ?? throw new ArgumentNullException(nameof(navigationService));
        _notificationService = notificationService ?? throw new ArgumentNullException(nameof(notificationService));
        _messagingService = messagingService ?? throw new ArgumentNullException(nameof(messagingService));
        _themeService = themeService ?? throw new ArgumentNullException(nameof(themeService));
        _watchlistService = watchlistService ?? throw new ArgumentNullException(nameof(watchlistService));
        _fixtureModeDetector = fixtureModeDetector ?? throw new ArgumentNullException(nameof(fixtureModeDetector));
        _authenticationSession = authenticationSession ?? throw new ArgumentNullException(nameof(authenticationSession));

        StatusBar = new StatusBarViewModel(statusService, notificationService);

        NavigateCommand = new RelayCommand<string>(Navigate);
        StartCollectorCommand = new AsyncRelayCommand(StartCollectorAsync);
        StopCollectorCommand = new AsyncRelayCommand(StopCollectorAsync);
        RefreshCommand = new RelayCommand(() => _messagingService.Send("RefreshStatus"));
        LogoutCommand = new RelayCommand(Logout, CanLogout);
        AddClipboardSymbolsCommand = new AsyncRelayCommand(AddPendingSymbolsToWatchlistAsync, () => _pendingClipboardSymbols.Count > 0);
        DismissStartupBannerCommand = new RelayCommand(HideStartupBanner);
        DismissClipboardBannerCommand = new RelayCommand(HideClipboardBanner);

        _startupBannerTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(18) };
        _startupBannerTimer.Tick += OnStartupBannerTimerTick;
        _clipboardBannerTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(8) };
        _clipboardBannerTimer.Tick += OnClipboardBannerTimerTick;

        _fixtureModeDetector.ModeChanged += OnFixtureModeChanged;
        RefreshAuthenticationState();
        UpdateFixtureModeBanner();
    }

    public event EventHandler? LogoutRequested;

    public StatusBarViewModel StatusBar { get; }

    public IReadOnlyList<ShellNavigationPanelSection> NavigationSections { get; } = ShellNavigationCatalog.BuildNavigationPanel();

    public IRelayCommand<string> NavigateCommand { get; }

    public IAsyncRelayCommand StartCollectorCommand { get; }

    public IAsyncRelayCommand StopCollectorCommand { get; }

    public IRelayCommand RefreshCommand { get; }

    public IRelayCommand LogoutCommand { get; }

    public IAsyncRelayCommand AddClipboardSymbolsCommand { get; }

    public IRelayCommand DismissStartupBannerCommand { get; }

    public IRelayCommand DismissClipboardBannerCommand { get; }

    public Visibility AuthenticatedSessionVisibility
    {
        get => _authenticatedSessionVisibility;
        private set => SetProperty(ref _authenticatedSessionVisibility, value);
    }

    public string AuthenticatedOperatorText
    {
        get => _authenticatedOperatorText;
        private set => SetProperty(ref _authenticatedOperatorText, value);
    }

    public string AuthenticatedRoleText
    {
        get => _authenticatedRoleText;
        private set => SetProperty(ref _authenticatedRoleText, value);
    }

    public string AuthenticatedSessionDetail
    {
        get => _authenticatedSessionDetail;
        private set => SetProperty(ref _authenticatedSessionDetail, value);
    }

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

    public Visibility StartupBannerVisibility
    {
        get => _startupBannerVisibility;
        private set => SetProperty(ref _startupBannerVisibility, value);
    }

    public string StartupBannerStatusText
    {
        get => _startupBannerStatusText;
        private set => SetProperty(ref _startupBannerStatusText, value);
    }

    public string StartupBannerMessage
    {
        get => _startupBannerMessage;
        private set => SetProperty(ref _startupBannerMessage, value);
    }

    public string StartupBannerDetail
    {
        get => _startupBannerDetail;
        private set => SetProperty(ref _startupBannerDetail, value);
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

    public void RefreshAuthenticationState()
    {
        var actor = _authenticationSession.CurrentActor;
        var hasActor = !string.IsNullOrWhiteSpace(actor);

        AuthenticatedSessionVisibility = hasActor
            ? Visibility.Visible
            : Visibility.Collapsed;
        AuthenticatedOperatorText = hasActor
            ? actor
            : "Not signed in";
        AuthenticatedRoleText = ResolveAuthenticatedRoleText();
        AuthenticatedSessionDetail = ResolveAuthenticatedSessionDetail();
        LogoutCommand.NotifyCanExecuteChanged();
    }

    public void ShowStartupExperience()
    {
        if (_startupExperienceShown)
        {
            return;
        }

        _startupExperienceShown = true;

        var isNonLiveMode = _fixtureModeDetector.IsNonLiveMode;
        StartupBannerStatusText = isNonLiveMode
            ? _fixtureModeDetector.ModeLabel
            : "Startup briefing";
        StartupBannerMessage = isNonLiveMode
            ? "Meridian opened in a non-live environment. Validate workflows, data posture, and operator routes here before moving to production credentials or capital."
            : "Review the active operating context, shell mode, and readiness posture before taking live actions.";
        StartupBannerDetail = isNonLiveMode
            ? "Use Welcome for the full workstation briefing, confirm the mode banner, and keep live-provider changes gated until the environment is intentionally switched."
            : "Use Welcome for the full workstation briefing, then move through Trading, Portfolio, Accounting, Reporting, Strategy, Data, or Settings from the same governed shell.";
        StartupBannerVisibility = Visibility.Visible;

        _startupBannerTimer.Stop();
        _startupBannerTimer.Start();

        _notificationService.ShowNotification(
            "Meridian ready",
            isNonLiveMode
                ? $"Meridian started in {_fixtureModeDetector.ModeLabel}. Review operating context and readiness before leaving the safe test lane."
                : "Meridian started successfully. Review operating context and readiness before live actions.",
            NotificationType.Info,
            6000);
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

    public void HideStartupBanner()
    {
        _startupBannerTimer.Stop();
        StartupBannerVisibility = Visibility.Collapsed;
    }

    public void HandleShortcut(string actionId)
    {
        switch (actionId)
        {
            case "NavigateDashboard":
                Navigate("StrategyShell");
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
        var request = DesktopLaunchArguments.Parse(args);
        if (!request.HasActions)
        {
            return;
        }

        if (request.HasPageNavigation)
        {
            Navigate(request.PageTag);
        }

        if (request.StartCollector)
        {
            _ = StartCollectorAsync();
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
        _startupBannerTimer.Stop();
        _startupBannerTimer.Tick -= OnStartupBannerTimerTick;
        _clipboardBannerTimer.Stop();
        _clipboardBannerTimer.Tick -= OnClipboardBannerTimerTick;
        _fixtureModeDetector.ModeChanged -= OnFixtureModeChanged;
        StatusBar.Dispose();
    }

    private bool CanLogout()
        => !string.IsNullOrWhiteSpace(_authenticationSession.CurrentActor);

    private void Logout()
    {
        if (!CanLogout())
        {
            return;
        }

        _authenticationSession.SignOut();
        RefreshAuthenticationState();
        LogoutRequested?.Invoke(this, EventArgs.Empty);
    }

    private string ResolveAuthenticatedRoleText()
    {
        if (_authenticationSession.IsAnonymousDevelopmentSession)
        {
            return "Development session";
        }

        return _authenticationSession.CurrentRole is { } role
            ? $"{role} access"
            : "No active session";
    }

    private string ResolveAuthenticatedSessionDetail()
    {
        if (_authenticationSession.IsAnonymousDevelopmentSession)
        {
            return "Local development session. No credentials are stored in the desktop app.";
        }

        return _authenticationSession.IsAuthenticated
            ? "In-memory desktop session. Credentials remain environment-backed."
            : "Sign in from the Meridian startup screen.";
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

    private void OnStartupBannerTimerTick(object? sender, EventArgs e)
    {
        HideStartupBanner();
    }

    private void UpdateFixtureModeBanner()
    {
        FixtureModeBannerVisibility = _fixtureModeDetector.IsNonLiveMode
            ? Visibility.Visible
            : Visibility.Collapsed;
        FixtureModeText = _fixtureModeDetector.ModeLabel;
        FixtureModeBannerBackground = _fixtureModeDetector.ModeKind == FixtureModeKind.Fixture
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
