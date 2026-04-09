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
using Meridian.Wpf.Models;
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
    private readonly MainWindowViewModel _viewModel;
    private readonly WpfServices.NavigationService _navigationService;
    private readonly WpfServices.KeyboardShortcutService _keyboardShortcutService;
    private readonly WpfServices.NotificationService _notificationService;
    private readonly OnboardingTourService _tourService;
    private readonly AlertService _alertService;
    private readonly WpfServices.WorkspaceService _workspaceService;
    private readonly WpfServices.FundContextService _fundContextService;
    private readonly WpfServices.WorkstationOperatingContextService _operatingContextService;

    private static readonly string WindowStateFilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Meridian",
        "window-state.json");

    public MainWindow(
        MainWindowViewModel viewModel,
        WpfServices.NavigationService navigationService,
        WpfServices.KeyboardShortcutService keyboardShortcutService,
        WpfServices.NotificationService notificationService,
        WpfServices.FundContextService fundContextService,
        WpfServices.WorkstationOperatingContextService operatingContextService)
    {
        InitializeComponent();

        _viewModel = viewModel;
        _navigationService = navigationService;
        _keyboardShortcutService = keyboardShortcutService;
        _notificationService = notificationService;
        _tourService = OnboardingTourService.Instance;
        _alertService = AlertService.Instance;
        _workspaceService = WpfServices.WorkspaceService.Instance;
        _fundContextService = fundContextService ?? throw new ArgumentNullException(nameof(fundContextService));
        _operatingContextService = operatingContextService ?? throw new ArgumentNullException(nameof(operatingContextService));
        DataContext = _viewModel;

        // Subscribe to keyboard shortcuts
        _keyboardShortcutService.ShortcutInvoked += OnShortcutInvoked;

        // Subscribe to notifications for in-app display
        _notificationService.NotificationReceived += OnNotificationReceived;

        // Subscribe to onboarding tour events
        _tourService.StepChanged += OnTourStepChanged;
        _tourService.TourCompleted += OnTourCompleted;

        // Subscribe to alert events for guided remediation
        _alertService.AlertRaised += OnAlertRaised;
        _fundContextService.FundSwitchRequested += OnFundSwitchRequested;
        _operatingContextService.ActiveContextChanging += OnActiveContextChanging;
        _operatingContextService.ActiveContextChanged += OnActiveContextChanged;
        _operatingContextService.ContextSwitchRequested += OnContextSwitchRequested;

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

    private async void OnWindowLoaded(object sender, RoutedEventArgs e)
    {
        EnsureShellVisibleOnStartup();

        // Capture the HWND for taskbar progress updates (must run after Loaded).
        WpfServices.TaskbarProgressService.Instance.Initialize(this);

        // Initialize navigation service with the frame
        _navigationService.Initialize(RootFrame);

        // Initialize keyboard shortcuts
        _keyboardShortcutService.Initialize(this);

        // Start status bar update loop
        _ = _viewModel.StartAsync();

        // Register clipboard watcher using this window's HWND (must be called after Loaded)
        var hwnd = new WindowInteropHelper(this).Handle;
        ClipboardWatcherService.Instance.Initialize(hwnd);
        ClipboardWatcherService.Instance.SymbolsDetected += OnSymbolsDetected;

        // Register global (system-wide) hotkeys via WndProc hook.
        var hwndSource = HwndSource.FromHwnd(hwnd);
        hwndSource?.AddHook(WndProc);
        GlobalHotkeyService.Instance.GlobalHotkeyFired += OnGlobalHotkeyFired;
        GlobalHotkeyService.Instance.Initialize(hwnd);

        await _workspaceService.LoadWorkspacesAsync();
        await _fundContextService.LoadAsync();
        await _operatingContextService.LoadAsync();
        await SynchronizeLastSelectedFundAsync();

        if (_operatingContextService.CurrentContext is not null)
        {
            await EnterOperatingContextAsync(_operatingContextService.CurrentContext);
            return;
        }

        RootFrame.Navigate(App.Services.GetRequiredService<FundProfileSelectionPage>());

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

        // Unsubscribe from all events to prevent memory leaks
        _keyboardShortcutService.ShortcutInvoked -= OnShortcutInvoked;
        _notificationService.NotificationReceived -= OnNotificationReceived;
        _tourService.StepChanged -= OnTourStepChanged;
        _tourService.TourCompleted -= OnTourCompleted;
        _alertService.AlertRaised -= OnAlertRaised;
        _fundContextService.FundSwitchRequested -= OnFundSwitchRequested;
        _operatingContextService.ActiveContextChanging -= OnActiveContextChanging;
        _operatingContextService.ActiveContextChanged -= OnActiveContextChanged;
        _operatingContextService.ContextSwitchRequested -= OnContextSwitchRequested;
        WpfServices.SingleInstanceService.Instance.LaunchArgsReceived -= OnLaunchArgsReceived;

        // Clipboard watcher cleanup
        ClipboardWatcherService.Instance.SymbolsDetected -= OnSymbolsDetected;
        ClipboardWatcherService.Instance.Dispose();

        // Shutdown global hotkeys to free Win32 registrations immediately.
        GlobalHotkeyService.Instance.GlobalHotkeyFired -= OnGlobalHotkeyFired;
        GlobalHotkeyService.Instance.Shutdown();

        _viewModel.Dispose();
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
                _viewModel.HandleShortcut("PauseResumeCollector");
                break;

            case "ToggleTickerStrip":
                _viewModel.HandleShortcut("ToggleTickerStrip");
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
        if (e.ActionId == "QuickCommand")
        {
            ShowCommandPalette();
            return;
        }

        _viewModel.HandleShortcut(e.ActionId);
    }

    /// <summary>
    /// Opens the command palette (Ctrl+K).
    /// Delegates to the inline overlay inside <see cref="MainPage"/> so that the
    /// <c>CommandPaletteInput</c> UI-Automation element stays within the main-window
    /// subtree and can be found by automation scripts.
    /// Falls back to the standalone dialog when the frame does not yet hold a <see cref="MainPage"/>.
    /// </summary>
    private void ShowCommandPalette()
    {
        if (RootFrame.Content is MainPage page)
        {
            page.ShowCommandPaletteOverlay();
            return;
        }

        // Fallback: frame not yet loaded with MainPage — use the standalone dialog.
        var paletteService = CommandPaletteService.Instance;
        var palette = new CommandPaletteWindow(paletteService) { Owner = this };
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
                _viewModel.NavigateCommand.Execute(e.ActionId);
                break;

            case PaletteCommandCategory.Action:
                _viewModel.HandlePaletteAction(e.ActionId);
                break;
        }
    }

    private void OnLaunchArgsReceived(object? sender, string[] args) => _viewModel.HandleLaunchArgs(args);

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

        _viewModel.ShowClipboardSymbols(e.Symbols);
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
                _viewModel.ExecuteRemediationStep(firstStep);
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
     /// Restores the last workspace session state (active workspace, last page, etc.)
     /// </summary>
    private async Task RestoreWorkspaceSessionForContextAsync(WorkstationOperatingContext context, CancellationToken ct = default)
    {
        try
        {
            await _workspaceService.LoadWorkspacesAsync();

            var session = _workspaceService.GetLastSessionStateForContext(context.ContextKey);
            var targetWorkspaceId = !string.IsNullOrWhiteSpace(session?.ActiveWorkspaceId)
                ? session!.ActiveWorkspaceId
                : context.DefaultWorkspaceId;

            if (!string.IsNullOrWhiteSpace(targetWorkspaceId))
            {
                await _workspaceService.ActivateWorkspaceAsync(targetWorkspaceId);
            }

            var targetPageTag = !string.IsNullOrWhiteSpace(session?.ActivePageTag)
                ? session!.ActivePageTag
                : context.DefaultLandingPageTag;

            if (string.IsNullOrWhiteSpace(targetPageTag))
            {
                targetPageTag = ResolveDefaultPageTag(targetWorkspaceId);
            }

            _navigationService.NavigateTo(targetPageTag);
        }
        catch (Exception)
        {
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
            if (_operatingContextService.CurrentContext is null &&
                _fundContextService.CurrentFundProfile is null &&
                RootFrame.Content is not MainPage)
            {
                return;
            }

            var operatingContextKey = _operatingContextService.CurrentContext?.ContextKey
                ?? _fundContextService.CurrentFundProfile?.FundProfileId;
            var currentPage = _navigationService.GetCurrentPageTag();
            var activeWorkspace = _workspaceService.ActiveWorkspace;

            // Preserve per-page filter state and open-pages list that were accumulated
            // during the session by the individual pages via UpdatePageFilterState().
            var existing = _workspaceService.GetLastSessionStateForContext(operatingContextKey);

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
            _ = _workspaceService.SaveSessionStateAsync(session, operatingContextKey);
        }
        catch (Exception)
        {
        }
    }

    private async Task SynchronizeLastSelectedFundAsync(CancellationToken ct = default)
    {
        var workspaceContextKey = _workspaceService.LastSelectedOperatingContextKey;
        var compatibilityFundProfileId = _operatingContextService.CurrentContext?.CompatibilityFundProfileId;
        if (string.IsNullOrWhiteSpace(compatibilityFundProfileId) &&
            _operatingContextService.CurrentContext?.ScopeKind == OperatingContextScopeKind.Fund)
        {
            compatibilityFundProfileId = _operatingContextService.CurrentContext.ScopeId;
        }

        if (string.IsNullOrWhiteSpace(compatibilityFundProfileId) &&
            WorkstationOperatingContext.TryGetFundScopeId(workspaceContextKey, out var workspaceFundScopeId))
        {
            compatibilityFundProfileId = workspaceFundScopeId;
        }

        if (string.IsNullOrWhiteSpace(_fundContextService.LastSelectedFundProfileId) &&
            !string.IsNullOrWhiteSpace(compatibilityFundProfileId))
        {
            await _fundContextService.SetLastSelectedFundProfileIdAsync(compatibilityFundProfileId, ct);
        }

        var targetContextKey = _operatingContextService.CurrentContext?.ContextKey;
        if (string.IsNullOrWhiteSpace(targetContextKey))
        {
            if (WorkstationOperatingContext.TryParseContextKey(workspaceContextKey, out _, out _))
            {
                targetContextKey = workspaceContextKey;
            }
            else if (!string.IsNullOrWhiteSpace(_fundContextService.LastSelectedFundProfileId))
            {
                targetContextKey = WorkstationOperatingContext.CreateContextKey(
                    OperatingContextScopeKind.Fund,
                    _fundContextService.LastSelectedFundProfileId!);
            }
        }

        if (!string.IsNullOrWhiteSpace(targetContextKey) &&
            !string.Equals(_workspaceService.LastSelectedOperatingContextKey, targetContextKey, StringComparison.OrdinalIgnoreCase))
        {
            await _workspaceService.SetLastSelectedOperatingContextKeyAsync(targetContextKey, ct);
        }
    }

    private void OnActiveContextChanging(object? sender, WorkstationOperatingContextChangingEventArgs e)
    {
        if (!Dispatcher.CheckAccess())
        {
            _ = Dispatcher.InvokeAsync(() => OnActiveContextChanging(sender, e));
            return;
        }

        SaveWorkspaceSession();
    }

    private async void OnActiveContextChanged(object? sender, WorkstationOperatingContextChangedEventArgs e)
    {
        if (!Dispatcher.CheckAccess())
        {
            _ = Dispatcher.InvokeAsync(() => OnActiveContextChanged(sender, e));
            return;
        }

        await EnterOperatingContextAsync(e.Context);
    }

    private async void OnFundSwitchRequested(object? sender, EventArgs e)
    {
        if (!Dispatcher.CheckAccess())
        {
            _ = Dispatcher.InvokeAsync(() => OnFundSwitchRequested(sender, e));
            return;
        }

        await ShowContextSelectionAsync(saveCurrentSession: true);
    }

    private async void OnContextSwitchRequested(object? sender, EventArgs e)
    {
        if (!Dispatcher.CheckAccess())
        {
            _ = Dispatcher.InvokeAsync(() => OnContextSwitchRequested(sender, e));
            return;
        }

        await ShowContextSelectionAsync(saveCurrentSession: true);
    }

    private async Task EnterOperatingContextAsync(WorkstationOperatingContext context, CancellationToken ct = default)
    {
        await _workspaceService.SetLastSelectedOperatingContextKeyAsync(context.ContextKey, ct);
        if (RootFrame.Content is not MainPage)
        {
            RootFrame.Navigate(App.Services.GetRequiredService<MainPage>());
        }

        EnsureShellVisibleOnStartup();

        _ = Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(async () =>
        {
            await RestoreWorkspaceSessionForContextAsync(context, ct);
        }));
    }

    private async Task ShowContextSelectionAsync(bool saveCurrentSession, CancellationToken ct = default)
    {
        if (saveCurrentSession)
        {
            SaveWorkspaceSession();
        }

        await _workspaceService.LoadWorkspacesAsync(ct);
        RootFrame.Navigate(App.Services.GetRequiredService<FundProfileSelectionPage>());
        EnsureShellVisibleOnStartup();
    }

    private static string ResolveDefaultPageTag(string? workspaceId) => NormalizeWorkspaceId(workspaceId) switch
    {
        "trading" => "TradingShell",
        "data-operations" => "DataOperationsShell",
        "governance" => "GovernanceShell",
        _ => "ResearchShell"
    };

    private static string NormalizeWorkspaceId(string? workspaceId)
        => string.IsNullOrWhiteSpace(workspaceId) ? "research" : workspaceId.Trim();



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
        catch (Exception)
        {
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
        catch (Exception)
        {
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
        catch (Exception)
        {
        }
    }

}

