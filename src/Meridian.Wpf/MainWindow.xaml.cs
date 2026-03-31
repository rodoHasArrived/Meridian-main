using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Threading;
using System.Runtime.InteropServices;
using Microsoft.Extensions.DependencyInjection;
using Meridian.Wpf.Contracts;
using Meridian.Wpf.Services;
using Meridian.Wpf.ViewModels;
using WpfServices = Meridian.Wpf.Services;
using Meridian.Wpf.Views;
using Meridian.Ui.Services;
using Meridian.Ui.Services.Contracts;
using Meridian.Ui.Services.Services;
using SysNavigation = System.Windows.Navigation;

namespace Meridian.Wpf;

/// <summary>
/// Main application window containing the navigation frame.
/// Handles global keyboard shortcuts, command palette, and window state persistence.
/// </summary>
public partial class MainWindow : Window
{
    private readonly IConnectionService _connectionService;
    private readonly WpfServices.NavigationService _navigationService;
    private readonly WpfServices.KeyboardShortcutService _keyboardShortcutService;
    private readonly WpfServices.NotificationService _notificationService;
    private readonly WpfServices.MessagingService _messagingService;
    private readonly WpfServices.ThemeService _themeService;
    private readonly OnboardingTourService _tourService;
    private readonly AlertService _alertService;
    private readonly WpfServices.WorkspaceService _workspaceService;
    private readonly FixtureModeDetector _fixtureModeDetector;

    // Clipboard watcher state
    private DispatcherTimer? _clipboardBannerTimer;
    private IReadOnlyList<string> _pendingClipboardSymbols = [];

    // Status bar view model (lifetime tied to window)
    private StatusBarViewModel? _statusBarVM;

    private static readonly string WindowStateFilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Meridian",
        "window-state.json");

    public MainWindow(
        WpfServices.NavigationService navigationService,
        WpfServices.ConnectionService connectionService,
        WpfServices.KeyboardShortcutService keyboardShortcutService,
        WpfServices.NotificationService notificationService,
        WpfServices.MessagingService messagingService,
        WpfServices.ThemeService themeService)
    {
        InitializeComponent();

        _navigationService = navigationService;
        _connectionService = connectionService;
        _keyboardShortcutService = keyboardShortcutService;
        _notificationService = notificationService;
        _messagingService = messagingService;
        _themeService = themeService;
        _tourService = OnboardingTourService.Instance;
        _alertService = AlertService.Instance;
        _workspaceService = WpfServices.WorkspaceService.Instance;
        _fixtureModeDetector = FixtureModeDetector.Instance;

        // Create status bar view model with injected services
        var statusService = App.Services.GetRequiredService<IStatusService>();
        _statusBarVM = new StatusBarViewModel(statusService, _notificationService);

        // Subscribe to fixture/offline mode changes
        _fixtureModeDetector.ModeChanged += OnFixtureModeChanged;
        UpdateFixtureModeBanner();

        // Subscribe to keyboard shortcuts
        _keyboardShortcutService.ShortcutInvoked += OnShortcutInvoked;

        // Subscribe to notifications for in-app display
        _notificationService.NotificationReceived += OnNotificationReceived;

        // Subscribe to onboarding tour events
        _tourService.StepChanged += OnTourStepChanged;
        _tourService.TourCompleted += OnTourCompleted;

        // Subscribe to alert events for guided remediation
        _alertService.AlertRaised += OnAlertRaised;

        // Subscribe to launch args forwarded from secondary instances (jump-list re-launches).
        WpfServices.SingleInstanceService.Instance.LaunchArgsReceived += OnLaunchArgsReceived;

        SourceInitialized += (_, _) =>
        {
            EnsureShellVisibleOnStartup();
            _ = Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(EnsureShellVisibleOnStartup));
            _ = RecoverShellVisibilityAsync();
        };

        // Restore window state from previous session
        RestoreWindowState();
    }

    private void OnWindowLoaded(object sender, RoutedEventArgs e)
    {
        EnsureShellVisibleOnStartup();

        // Capture the HWND for taskbar progress updates (must run after Loaded).
        WpfServices.TaskbarProgressService.Instance.Initialize(this);

        // Initialize navigation service with the frame
        _navigationService.Initialize(RootFrame);

        // Initialize keyboard shortcuts
        _keyboardShortcutService.Initialize(this);

        // Set up window DataContext to expose StatusBar property
        DataContext = new MainWindowContext { StatusBar = _statusBarVM };

        // Start status bar update loop
        _ = _statusBarVM?.StartAsync();

        // Register clipboard watcher using this window's HWND (must be called after Loaded)
        var hwnd = new WindowInteropHelper(this).Handle;
        ClipboardWatcherService.Instance.Initialize(hwnd);
        ClipboardWatcherService.Instance.SymbolsDetected += OnSymbolsDetected;

        // Register global (system-wide) hotkeys via WndProc hook.
        var hwndSource = HwndSource.FromHwnd(hwnd);
        hwndSource?.AddHook(WndProc);
        GlobalHotkeyService.Instance.GlobalHotkeyFired += OnGlobalHotkeyFired;
        GlobalHotkeyService.Instance.Initialize(hwnd);

        // Load the shell first; it owns the inner content frame and restores page state there.
        RootFrame.Navigate(App.Services.GetRequiredService<MainPage>());

        // A few services can raise transient state changes during startup.
        // Re-assert the shell as visible once the initial load work has been queued.
        _ = Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, new Action(EnsureShellVisibleOnStartup));
    }

    private void OnWindowClosing(object? sender, CancelEventArgs e)
    {
        // Save window state before closing (fire-and-forget; exceptions handled inside)
        _ = SaveWindowStateAsync();

        // Save workspace session state for next launch
        SaveWorkspaceSession();

        // Dispose status bar view model
        _statusBarVM?.Dispose();

        // Unsubscribe from all events to prevent memory leaks
        _keyboardShortcutService.ShortcutInvoked -= OnShortcutInvoked;
        _notificationService.NotificationReceived -= OnNotificationReceived;
        _tourService.StepChanged -= OnTourStepChanged;
        _tourService.TourCompleted -= OnTourCompleted;
        _alertService.AlertRaised -= OnAlertRaised;
        _fixtureModeDetector.ModeChanged -= OnFixtureModeChanged;
        WpfServices.SingleInstanceService.Instance.LaunchArgsReceived -= OnLaunchArgsReceived;

        // Clipboard watcher cleanup
        ClipboardWatcherService.Instance.SymbolsDetected -= OnSymbolsDetected;
        _clipboardBannerTimer?.Stop();
        ClipboardWatcherService.Instance.Dispose();

        // Shutdown global hotkeys to free Win32 registrations immediately.
        GlobalHotkeyService.Instance.GlobalHotkeyFired -= OnGlobalHotkeyFired;
        GlobalHotkeyService.Instance.Shutdown();
    }

    private void OnRootFrameNavigated(object sender, SysNavigation.NavigationEventArgs e)
    {
        // In WPF, get content from the Frame (sender), not from event args
        if (sender is System.Windows.Controls.Frame frame && frame.Content is FrameworkElement element)
        {
            _keyboardShortcutService.Initialize(element);
        }
    }

    /// <summary>
    /// WndProc hook that forwards WM_HOTKEY messages to <see cref="GlobalHotkeyService"/>.
    /// </summary>
    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        const int WM_HOTKEY = 0x0312;
        if (msg == WM_HOTKEY)
        {
            GlobalHotkeyService.Instance.HandleHotkeyMessage(wParam.ToInt32());
            handled = true;
        }
        return IntPtr.Zero;
    }

    private void OnGlobalHotkeyFired(object? sender, GlobalHotkeyFiredEventArgs e)
    {
        // WM_HOTKEY arrives on the UI thread in standard WPF, but guard for safety.
        if (!Dispatcher.CheckAccess())
        {
            _ = Dispatcher.InvokeAsync(() => OnGlobalHotkeyFired(sender, e));
            return;
        }

        switch (e.ActionId)
        {
            case "BringToFront":
                Show();
                Activate();
                WindowState = WindowState.Normal;
                break;

            case "PauseResumeCollector":
                _messagingService.Send("PauseResumeCollector");
                break;

            case "ToggleTickerStrip":
                _messagingService.Send("ToggleTickerStrip");
                break;
        }
    }

    private void OnWindowKeyDown(object sender, KeyEventArgs e)
    {
        // Route key events to keyboard shortcut service
        _keyboardShortcutService.HandleKeyDown(e);
    }

    private void OnShortcutInvoked(object? sender, ShortcutInvokedEventArgs e)
    {
        // Handle global shortcuts using NavigationService for consistent routing
        switch (e.ActionId)
        {
            // Navigation shortcuts
            case "NavigateDashboard":
                _navigationService.NavigateTo("Dashboard");
                break;
            case "NavigateSymbols":
                _navigationService.NavigateTo("Symbols");
                break;
            case "NavigateBackfill":
                _navigationService.NavigateTo("Backfill");
                break;
            case "NavigateSettings":
                _navigationService.NavigateTo("Settings");
                break;

            // Collector control shortcuts
            case "StartCollector":
                _ = StartCollectorAsync();
                break;
            case "StopCollector":
                _ = StopCollectorAsync();
                break;

            // Backfill shortcuts
            case "RunBackfill":
                _navigationService.NavigateTo("Backfill");
                break;
            case "PauseBackfill":
                // Send message to BackfillPage when active
                _messagingService.Send("PauseBackfill");
                break;
            case "CancelBackfill":
                // Send message to BackfillPage when active
                _messagingService.Send("CancelBackfill");
                break;

            // Symbol shortcuts
            case "AddSymbol":
                _navigationService.NavigateTo("Symbols");
                _messagingService.Send("AddSymbol");
                break;
            case "SearchSymbols":
                // Focus search box in current page
                _messagingService.Send("FocusSearch");
                break;
            case "DeleteSelected":
                // Send delete message to current page
                _messagingService.Send("DeleteSelected");
                break;
            case "SelectAll":
                // Send select all message to current page
                _messagingService.Send("SelectAll");
                break;

            // View shortcuts
            case "ToggleTheme":
                _themeService.ToggleTheme();
                break;
            case "ViewLogs":
                _navigationService.NavigateTo("ServiceManager");
                break;
            case "RefreshStatus":
                // Send refresh message to current page
                _messagingService.Send("RefreshStatus");
                break;
            case "ZoomIn":
                _messagingService.Send("ZoomIn");
                break;
            case "ZoomOut":
                _messagingService.Send("ZoomOut");
                break;

            // General shortcuts
            case "Save":
                // Send save message to current page
                _messagingService.Send("Save");
                break;
            case "Help":
                _navigationService.NavigateTo("Help");
                break;
            case "QuickCommand":
                ShowCommandPalette();
                break;
        }
    }

    /// <summary>
    /// Opens the command palette dialog (Ctrl+K).
    /// </summary>
    private void ShowCommandPalette()
    {
        var paletteService = CommandPaletteService.Instance;
        var palette = new CommandPaletteWindow(paletteService)
        {
            Owner = this
        };

        // Subscribe to command execution
        paletteService.CommandExecuted += OnPaletteCommandExecuted;

        try
        {
            palette.ShowDialog();
        }
        finally
        {
            paletteService.CommandExecuted -= OnPaletteCommandExecuted;
        }
    }

    private void OnPaletteCommandExecuted(object? sender, PaletteCommandEventArgs e)
    {
        switch (e.Category)
        {
            case PaletteCommandCategory.Navigation:
                _navigationService.NavigateTo(e.ActionId);
                break;

            case PaletteCommandCategory.Action:
                HandlePaletteAction(e.ActionId);
                break;
        }
    }

    private void HandlePaletteAction(string actionId)
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
                _navigationService.NavigateTo("Backfill");
                break;
            case "RefreshStatus":
                _messagingService.Send("RefreshStatus");
                break;
            case "AddSymbol":
                _navigationService.NavigateTo("Symbols");
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

    /// <summary>
    /// Starts the data collector service via keyboard shortcut.
    /// </summary>
    private async Task StartCollectorAsync(CancellationToken ct = default)
    {
        try
        {
            var provider = _connectionService.CurrentProvider;
            if (string.IsNullOrEmpty(provider))
            {
                provider = "default";
            }

            var success = await _connectionService.ConnectAsync(provider);
            if (success)
            {
                _notificationService.ShowNotification(
                    "Collector Started",
                    "Data collection has started successfully.",
                    NotificationType.Success,
                    5000);
            }
            else
            {
                _notificationService.ShowNotification(
                    "Start Failed",
                    "Failed to start the data collector. Check service connection.",
                    NotificationType.Error,
                    0);
            }
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

    /// <summary>
    /// Stops the data collector service via keyboard shortcut.
    /// </summary>
    private async Task StopCollectorAsync(CancellationToken ct = default)
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

    private void OnLaunchArgsReceived(object? sender, string[] args) => HandleLaunchArgs(args);

    /// <summary>
    /// Handles launch arguments forwarded from a secondary instance via the single-instance
    /// named pipe (e.g. when the user clicks a taskbar jump list item while the app is
    /// already running). Always called on the UI thread.
    /// </summary>
    private void HandleLaunchArgs(string[] args)
    {
        if (args.Length == 0) return;

        foreach (var arg in args)
        {
            if (arg.StartsWith("--page=", StringComparison.OrdinalIgnoreCase))
            {
                var pageTag = arg["--page=".Length..];
                if (!string.IsNullOrWhiteSpace(pageTag))
                    _ = _navigationService.NavigateTo(pageTag);
            }
            else if (arg.Equals("--start-collector", StringComparison.OrdinalIgnoreCase))
            {
                _ = StartCollectorAsync();
            }
        }
    }

    private void OnNotificationReceived(object? sender, NotificationEventArgs e)
    {
        // In-app notification handling can be added here
        // For now, notifications are handled by the NotificationService
    }


    private void OnSymbolsDetected(object? sender, SymbolsDetectedEventArgs e)
    {
        // Only surface the banner when Meridian is the active window to avoid
        // interrupting the user's work in other applications.
        if (!IsActive) return;

        _pendingClipboardSymbols = e.Symbols;

        var symbolList = string.Join(", ", e.Symbols);
        var count = e.Symbols.Count;
        ClipboardBannerText.Text = count == 1
            ? $"Symbol detected in clipboard: {symbolList} — Add to Watchlist?"
            : $"{count} symbols detected in clipboard: {symbolList} — Add to Watchlist?";

        ClipboardSymbolBanner.Visibility = Visibility.Visible;

        // Restart the auto-dismiss timer on every new detection
        if (_clipboardBannerTimer is null)
        {
            _clipboardBannerTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(8) };
            _clipboardBannerTimer.Tick += (_, _) => HideClipboardBanner();
        }

        _clipboardBannerTimer.Stop();
        _clipboardBannerTimer.Start();
    }

    private void ClipboardAdd_Click(object sender, RoutedEventArgs e)
    {
        var symbols = _pendingClipboardSymbols;
        HideClipboardBanner();

        if (symbols.Count == 0) return;
        _ = AddSymbolsToWatchlistAsync(symbols);
    }

    private void ClipboardDismiss_Click(object sender, RoutedEventArgs e)
    {
        HideClipboardBanner();
    }

    private void HideClipboardBanner()
    {
        _clipboardBannerTimer?.Stop();
        ClipboardSymbolBanner.Visibility = Visibility.Collapsed;
        _pendingClipboardSymbols = [];
    }

    private async Task AddSymbolsToWatchlistAsync(IReadOnlyList<string> symbols)
    {
        try
        {
            var watchlistService = WpfServices.WatchlistService.Instance;
            var watchlists = await watchlistService.GetAllWatchlistsAsync();

            int added;
            string targetName;

            if (watchlists.Count > 0)
            {
                // Add to the first (highest-priority) watchlist
                var target = watchlists[0];
                added = await watchlistService.AddSymbolsAsync(target.Id, symbols);
                targetName = target.Name;
            }
            else
            {
                // No watchlist yet — create a default one containing these symbols
                var created = await watchlistService.CreateWatchlistAsync("My Watchlist", symbols);
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

            // Navigate to the watchlist so the user sees the result
            _navigationService.NavigateTo("Watchlist");
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



    private void OnFixtureModeChanged(object? sender, EventArgs e)
    {
        if (!Dispatcher.CheckAccess())
        {
            // InvokeAsync is non-blocking — background thread is not stalled waiting for UI [P2]
            _ = Dispatcher.InvokeAsync(() =>
            {
                try
                {
                    OnFixtureModeChanged(sender, e);
                }
                catch (Exception ex)
                {
                    _notificationService.ShowNotification("UI Error", $"Failed to update fixture mode UI: {ex.Message}", NotificationType.Error);
                }
            });
            return;
        }

        UpdateFixtureModeBanner();
    }

    private void UpdateFixtureModeBanner()
    {
        if (_fixtureModeDetector.IsNonLiveMode)
        {
            FixtureModeBanner.Visibility = Visibility.Visible;
            FixtureModeText.Text = _fixtureModeDetector.ModeLabel;

            var color = _fixtureModeDetector.IsFixtureMode
                ? System.Windows.Media.Color.FromRgb(0xFF, 0xB3, 0x00) // Amber for fixture
                : System.Windows.Media.Color.FromRgb(0xF4, 0x43, 0x36); // Red for offline

            FixtureModeBanner.Background = new System.Windows.Media.SolidColorBrush(color);
        }
        else
        {
            FixtureModeBanner.Visibility = Visibility.Collapsed;
        }
    }



    private void OnTourStepChanged(object? sender, TourStepEventArgs e)
    {
        if (!Dispatcher.CheckAccess())
        {
            _ = Dispatcher.InvokeAsync(() =>
            {
                try
                {
                    OnTourStepChanged(sender, e);
                }
                catch (Exception ex)
                {
                    _notificationService.ShowNotification("UI Error", $"Failed to update tour step UI: {ex.Message}", NotificationType.Error);
                }
            });
            return;
        }

        ShowTourStepNotification(e);
    }

    private void OnTourCompleted(object? sender, TourCompletedEventArgs e)
    {
        if (!Dispatcher.CheckAccess())
        {
            _ = Dispatcher.InvokeAsync(() =>
            {
                try
                {
                    OnTourCompleted(sender, e);
                }
                catch (Exception ex)
                {
                    _notificationService.ShowNotification("UI Error", $"Failed to update tour completed UI: {ex.Message}", NotificationType.Error);
                }
            });
            return;
        }

        _notificationService.ShowNotification(
            "Tour Complete",
            $"You've completed the '{e.TourId}' tour! ({e.StepsCompleted} steps in {e.Duration.TotalSeconds:F0}s)",
            NotificationType.Success,
            5000);
    }

    private void ShowTourStepNotification(TourStepEventArgs e)
    {
        var stepInfo = $"Step {e.StepIndex + 1}/{e.TotalSteps}";
        var message = $"{stepInfo}: {e.Step.Content}";

        if (e.IsLast)
        {
            message += "\n(Last step - press Next to finish)";
        }

        _notificationService.ShowNotification(
            $"Tour: {e.Step.Title}",
            message,
            NotificationType.Info,
            0); // Persistent until user advances

        // Auto-advance via messaging: pages can listen for "TourNext" / "TourDismiss"
    }



    private void OnAlertRaised(object? sender, AlertEventArgs e)
    {
        if (e.IsUpdate) return; // Only show notification for new alerts

        if (!Dispatcher.CheckAccess())
        {
            _ = Dispatcher.InvokeAsync(() =>
            {
                try
                {
                    OnAlertRaised(sender, e);
                }
                catch (Exception ex)
                {
                    _notificationService.ShowNotification("UI Error", $"Failed to display alert notification: {ex.Message}", NotificationType.Error);
                }
            });
            return;
        }

        var alert = e.Alert;
        if (alert.IsSuppressed || alert.IsSnoozed) return;

        var notificationType = alert.Severity switch
        {
            AlertSeverity.Critical or AlertSeverity.Emergency => NotificationType.Error,
            AlertSeverity.Error => NotificationType.Error,
            AlertSeverity.Warning => NotificationType.Warning,
            _ => NotificationType.Info
        };

        if (alert.Playbook != null)
        {
            var firstStep = alert.Playbook.RemediationSteps.Length > 0
                ? alert.Playbook.RemediationSteps[0]
                : null;

            var stepHint = firstStep != null
                ? $"\nTry: {firstStep.Description}"
                : "";

            _notificationService.ShowNotification(
                alert.Title,
                $"{alert.Description}{stepHint}",
                notificationType,
                alert.Severity >= AlertSeverity.Error ? 0 : 8000);

            // Auto-execute the first remediation step if it has a navigation target
            if (firstStep?.NavigationTarget != null)
            {
                ExecuteRemediationStep(firstStep);
            }
        }
        else
        {
            _notificationService.ShowNotification(
                alert.Title,
                alert.Description,
                notificationType,
                8000);
        }
    }

    /// <summary>
    /// Executes a remediation step by navigating to the target page and/or
    /// dispatching an action command via the messaging service.
    /// </summary>
    private void ExecuteRemediationStep(RemediationStep step)
    {
        // Navigate to the relevant page if specified
        if (!string.IsNullOrEmpty(step.NavigationTarget))
        {
            _navigationService.NavigateTo(step.NavigationTarget);
        }

        // Dispatch the action via messaging if specified
        if (!string.IsNullOrEmpty(step.ActionId))
        {
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
                    // Forward unknown actions as messaging commands
                    _messagingService.Send(step.ActionId);
                    break;
            }
        }
    }

    /// <summary>
    /// Tests the current provider connection as a remediation action.
    /// </summary>
    private async Task TestConnectionAsync(CancellationToken ct = default)
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

            if (success)
            {
                // Resolve connection alerts when reconnection succeeds
                foreach (var alert in _alertService.GetActiveAlerts()
                    .Where(a => a.Category is "Connection" or "Provider"))
                {
                    _alertService.ResolveAlert(alert.Id);
                }
            }
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



    /// <summary>
    /// Restores the last workspace session state (active workspace, last page, etc.)
    /// </summary>
    private async Task RestoreWorkspaceSessionAsync(CancellationToken ct = default)
    {
        try
        {
            await _workspaceService.LoadWorkspacesAsync();

            var session = _workspaceService.GetLastSessionState();
            if (session != null)
            {
                // Restore active workspace
                if (!string.IsNullOrEmpty(session.ActiveWorkspaceId))
                {
                    await _workspaceService.ActivateWorkspaceAsync(session.ActiveWorkspaceId);
                }

                // Restore last active page after MainPage loads
                if (!string.IsNullOrEmpty(session.ActivePageTag) && session.ActivePageTag != "Dashboard")
                {
                    // Defer navigation until MainPage is fully loaded
                    _ = Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Loaded, () =>
                    {
                        _ = _navigationService.NavigateTo(session.ActivePageTag);
                    });
                }
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[MainWindow] Failed to restore workspace session: {ex.Message}");
        }
    }

    /// <summary>
    /// Saves the current workspace session state for next launch.
    /// Preserves per-page filter state and open pages accumulated during the session.
    /// </summary>
    private void SaveWorkspaceSession()
    {
        try
        {
            var currentPage = _navigationService.GetCurrentPageTag();
            var activeWorkspace = _workspaceService.ActiveWorkspace;

            // Preserve per-page filter state and open-pages list that were accumulated
            // during the session by the individual pages via UpdatePageFilterState().
            var existing = _workspaceService.GetLastSessionState();

            var session = new Ui.Services.SessionState
            {
                ActivePageTag = currentPage ?? "Dashboard",
                ActiveWorkspaceId = activeWorkspace?.Id,
                ActiveFilters = existing?.ActiveFilters ?? new System.Collections.Generic.Dictionary<string, string>(),
                OpenPages = existing?.OpenPages ?? new System.Collections.Generic.List<Ui.Services.WorkspacePage>(),
                WindowBounds = new Ui.Services.WindowBounds
                {
                    X = RestoreBounds.Left,
                    Y = RestoreBounds.Top,
                    Width = RestoreBounds.Width,
                    Height = RestoreBounds.Height,
                    IsMaximized = WindowState == WindowState.Maximized
                }
            };

            // Fire-and-forget since we're closing
            _ = _workspaceService.SaveSessionStateAsync(session);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[MainWindow] Failed to save workspace session: {ex.Message}");
        }
    }



    /// <summary>
    /// Saves the current window position, size, and state to disk.
    /// Uses source-generated serialization and async file I/O to avoid
    /// blocking the UI thread during window close. Exceptions are handled
    /// internally so callers do not need to observe the returned task.
    /// </summary>
    private async Task SaveWindowStateAsync(CancellationToken ct = default)
    {
        try
        {
            var state = new PersistedWindowState
            {
                Left = RestoreBounds.Left,
                Top = RestoreBounds.Top,
                Width = RestoreBounds.Width,
                Height = RestoreBounds.Height,
                IsMaximized = WindowState == WindowState.Maximized,
                SavedAt = DateTime.UtcNow
            };

            var dir = Path.GetDirectoryName(WindowStateFilePath);
            if (!string.IsNullOrEmpty(dir))
            {
                Directory.CreateDirectory(dir);
            }

            var json = JsonSerializer.Serialize(state, WindowStateJsonContext.Default.PersistedWindowState);

            await File.WriteAllTextAsync(WindowStateFilePath, json).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[MainWindow] Failed to save window state: {ex.Message}");
        }
    }

    /// <summary>
    /// Restores window position, size, and state from disk.
    /// Validates that the restored position is visible on a connected monitor.
    /// </summary>
    private void RestoreWindowState()
    {
        try
        {
            if (!File.Exists(WindowStateFilePath)) return;

            var json = File.ReadAllText(WindowStateFilePath);
            var state = JsonSerializer.Deserialize(json, WindowStateJsonContext.Default.PersistedWindowState);
            if (state == null) return;

            // Validate dimensions are reasonable
            if (state.Width < MinWidth || state.Height < MinHeight) return;
            if (state.Width > 10000 || state.Height > 10000) return;

            // Validate position is on a visible monitor
            if (!IsPositionOnScreen(state.Left, state.Top, state.Width, state.Height))
            {
                // Position is off-screen (monitor may have been disconnected)
                // Keep default CenterScreen position but restore size
                Width = state.Width;
                Height = state.Height;
            }
            else
            {
                WindowStartupLocation = WindowStartupLocation.Manual;
                Left = state.Left;
                Top = state.Top;
                Width = state.Width;
                Height = state.Height;
            }

            if (state.IsMaximized)
            {
                WindowState = WindowState.Maximized;
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[MainWindow] Failed to restore window state: {ex.Message}");
        }
    }

    private void EnsureShellVisibleOnStartup()
    {
        if (WindowState == WindowState.Minimized)
        {
            WindowState = WindowState.Normal;
        }

        if (!ShowInTaskbar)
        {
            ShowInTaskbar = true;
        }

        if (!IsVisible)
        {
            Show();
        }

        var hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd != IntPtr.Zero)
        {
            NativeMethods.ShowWindow(hwnd, NativeMethods.SW_RESTORE);
            NativeMethods.SetForegroundWindow(hwnd);
        }

        Activate();
    }

    private async Task RecoverShellVisibilityAsync()
    {
        var delays = new[]
        {
            TimeSpan.FromMilliseconds(250),
            TimeSpan.FromSeconds(1),
            TimeSpan.FromSeconds(3)
        };

        foreach (var delay in delays)
        {
            await Task.Delay(delay).ConfigureAwait(true);
            EnsureShellVisibleOnStartup();
        }
    }

    private static class NativeMethods
    {
        internal const int SW_RESTORE = 9;

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool SetForegroundWindow(IntPtr hWnd);
    }

    /// <summary>
    /// Checks if a position is visible on the virtual screen area.
    /// Uses WPF SystemParameters to avoid a WinForms dependency.
    /// </summary>
    private static bool IsPositionOnScreen(double left, double top, double width, double height)
    {
        // Use the virtual screen bounds (spans all monitors)
        var virtualLeft = SystemParameters.VirtualScreenLeft;
        var virtualTop = SystemParameters.VirtualScreenTop;
        var virtualWidth = SystemParameters.VirtualScreenWidth;
        var virtualHeight = SystemParameters.VirtualScreenHeight;

        var virtualRect = new Rect(virtualLeft, virtualTop, virtualWidth, virtualHeight);
        var windowRect = new Rect(left, top, width, height);

        var intersection = Rect.Intersect(windowRect, virtualRect);

        // At least 100x100 pixels must be visible
        const double minVisibleSize = 100;
        return !intersection.IsEmpty &&
               intersection.Width >= minVisibleSize &&
               intersection.Height >= minVisibleSize;
    }

    /// <summary>
    /// Persisted window state for save/restore across sessions.
    /// </summary>
    private sealed class PersistedWindowState
    {
        public double Left { get; set; }
        public double Top { get; set; }
        public double Width { get; set; }
        public double Height { get; set; }
        public bool IsMaximized { get; set; }
        public DateTime SavedAt { get; set; }
    }

    /// <summary>
    /// Source-generated JSON context for window state persistence (ADR-014).
    /// Avoids reflection-based serialization overhead.
    /// </summary>
    [JsonSourceGenerationOptions(WriteIndented = true)]
    [JsonSerializable(typeof(PersistedWindowState))]
    private sealed partial class WindowStateJsonContext : JsonSerializerContext;



    private void OnRootFrameDragEnter(object sender, DragEventArgs e)
    {
        if (!e.Data.GetDataPresent(DataFormats.FileDrop))
        {
            e.Effects = DragDropEffects.None;
            e.Handled = true;
            return;
        }

        e.Effects = DragDropEffects.Copy;
        e.Handled = true;

        UpdateDropOverlay(e);
        DropOverlay.Visibility = Visibility.Visible;
    }

    private void OnRootFrameDragOver(object sender, DragEventArgs e)
    {
        if (!e.Data.GetDataPresent(DataFormats.FileDrop))
        {
            e.Effects = DragDropEffects.None;
            e.Handled = true;
            return;
        }

        e.Effects = DragDropEffects.Copy;
        e.Handled = true;
    }

    private void OnRootFrameDragLeave(object sender, DragEventArgs e)
    {
        DropOverlay.Visibility = Visibility.Collapsed;
        e.Handled = true;
    }

    private void OnRootFrameDrop(object sender, DragEventArgs e)
    {
        DropOverlay.Visibility = Visibility.Collapsed;
        e.Handled = true;

        if (e.Data.GetData(DataFormats.FileDrop) is not string[] files || files.Length == 0)
            return;

        RouteDroppedFile(files[0]);
    }

    /// <summary>
    /// Updates the overlay subtitle with the detected file type of the first dragged file.
    /// </summary>
    private void UpdateDropOverlay(DragEventArgs e)
    {
        if (e.Data.GetData(DataFormats.FileDrop) is not string[] files || files.Length == 0)
            return;

        var fileType = DropImportService.Instance.DetectFileType(files[0]);
        var label = DropImportService.Instance.GetTypeFriendlyName(fileType);
        DropOverlaySubtitle.Text = label;
    }

    /// <summary>
    /// Detects the file type and navigates to the appropriate import page,
    /// passing the file path as a parameter so the destination can pre-populate its UI.
    /// </summary>
    private void RouteDroppedFile(string filePath)
    {
        try
        {
            var fileType = DropImportService.Instance.DetectFileType(filePath);
            var pageKey = DropImportService.Instance.GetTargetPageKey(fileType);

            _navigationService.NavigateTo(pageKey, filePath);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[MainWindow] Drop routing failed for '{filePath}': {ex.Message}");
        }
    }

}

/// <summary>
/// Data context for MainWindow, exposing the StatusBar view model.
/// </summary>
internal sealed class MainWindowContext
{
    public StatusBarViewModel? StatusBar { get; set; }
}

