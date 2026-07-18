using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Meridian.Ui.Services;
using Meridian.Ui.Services.Contracts;
using Meridian.Ui.Services.Services;
using Meridian.Wpf.Contracts;
using Meridian.Wpf.Models;
using Meridian.Wpf.Shell.Root;
using Meridian.Wpf.Shell.Session;
using Meridian.Wpf.Services;
using Meridian.Wpf.ViewModels;
using Meridian.Wpf.Views;
using Microsoft.Extensions.DependencyInjection;
using SysNavigation = System.Windows.Navigation;
using WpfServices = Meridian.Wpf.Services;

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
    private readonly WpfServices.FundContextService _fundContextService;
    private readonly WpfServices.WorkstationOperatingContextService _operatingContextService;
    private readonly DesktopShellCoordinator _shellCoordinator;
    private readonly DesktopShellSessionService _sessionService;
    private readonly DesktopLaunchRouter _launchRouter;
    private readonly FileDropRouter _fileDropRouter;
    private readonly IWindowStateStore _windowStateStore;
    private readonly DispatcherTimer _startupRecoveryTimer;

    private Rect? _restoredWindowBounds;
    private int _startupRecoveryAttempts;
    private bool _globalHotkeysAttached;

    private const double DefaultWindowWidth = 1400;
    private const double DefaultWindowHeight = 900;
    private const int StartupRecoveryMaxAttempts = 20;

    public MainWindow(
        MainWindowViewModel viewModel,
        WpfServices.NavigationService navigationService,
        WpfServices.KeyboardShortcutService keyboardShortcutService,
        WpfServices.NotificationService notificationService,
        WpfServices.FundContextService fundContextService,
        WpfServices.WorkstationOperatingContextService operatingContextService,
        DesktopShellCoordinator shellCoordinator,
        DesktopShellSessionService sessionService,
        DesktopLaunchRouter launchRouter,
        FileDropRouter fileDropRouter,
        IWindowStateStore windowStateStore)
    {
        InitializeComponent();

        _viewModel = viewModel;
        _navigationService = navigationService;
        _keyboardShortcutService = keyboardShortcutService;
        _notificationService = notificationService;
        _tourService = OnboardingTourService.Instance;
        _alertService = AlertService.Instance;
        _fundContextService = fundContextService ?? throw new ArgumentNullException(nameof(fundContextService));
        _operatingContextService = operatingContextService ?? throw new ArgumentNullException(nameof(operatingContextService));
        _shellCoordinator = shellCoordinator ?? throw new ArgumentNullException(nameof(shellCoordinator));
        _sessionService = sessionService ?? throw new ArgumentNullException(nameof(sessionService));
        _launchRouter = launchRouter ?? throw new ArgumentNullException(nameof(launchRouter));
        _fileDropRouter = fileDropRouter ?? throw new ArgumentNullException(nameof(fileDropRouter));
        _windowStateStore = windowStateStore ?? throw new ArgumentNullException(nameof(windowStateStore));
        DataContext = _viewModel;
        _viewModel.LogoutRequested += OnLogoutRequested;

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

        _startupRecoveryTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(250)
        };
        _startupRecoveryTimer.Tick += OnStartupRecoveryTick;

        SourceInitialized += (_, _) =>
        {
            EnsureShellVisibleOnStartup();
            StartShellRecoveryLoop();
        };

        // Restore window state from previous session
        RestoreWindowState();
    }

    private async void OnWindowLoaded(object sender, RoutedEventArgs e)
    {
        EnsureShellVisibleOnStartup();
        if (RootFrame.Content is null)
        {
            RootFrame.Navigate(App.Services.GetRequiredService<FundProfileSelectionPage>());
        }

        // Capture the HWND for taskbar progress updates (must run after Loaded).
        WpfServices.TaskbarProgressService.Instance.Initialize(this);

        // Initialize navigation service with the frame
        _navigationService.Initialize(RootFrame);

        // Initialize keyboard shortcuts
        _keyboardShortcutService.Initialize(this);

        // Start status bar update loop
        _ = _viewModel.StartAsync();
        _viewModel.ShowStartupExperience();

        // Register clipboard watcher using this window's HWND (must be called after Loaded)
        var hwnd = new WindowInteropHelper(this).Handle;
        ClipboardWatcherService.Instance.Initialize(hwnd);
        ClipboardWatcherService.Instance.SymbolsDetected += OnSymbolsDetected;

        // Register global (system-wide) hotkeys via WndProc hook.
        var hwndSource = HwndSource.FromHwnd(hwnd);
        hwndSource?.AddHook(WndProc);
        AttachGlobalHotkeys(hwnd);

        var launchArgs = App.GetLaunchArgs();
        var launchRequest = _launchRouter.Parse(launchArgs);

        await _sessionService.LoadWorkspacesAsync();
        await _fundContextService.LoadAsync();
        await _operatingContextService.LoadAsync();
        await _sessionService.SynchronizeLastSelectedFundAsync();

        if (_operatingContextService.CurrentContext is not null)
        {
            await EnterOperatingContextAsync(_operatingContextService.CurrentContext);
            if (launchRequest.HasActions)
            {
                await HandleLaunchArgsAsync(launchArgs);
            }

            return;
        }

        if (launchRequest.HasActions)
        {
            await HandleLaunchArgsAsync(launchArgs);
            return;
        }

        if (RootFrame.Content is not FundProfileSelectionPage)
        {
            RootFrame.Navigate(App.Services.GetRequiredService<FundProfileSelectionPage>());
        }

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
        _viewModel.LogoutRequested -= OnLogoutRequested;
        WpfServices.SingleInstanceService.Instance.LaunchArgsReceived -= OnLaunchArgsReceived;
        _startupRecoveryTimer.Stop();
        _startupRecoveryTimer.Tick -= OnStartupRecoveryTick;

        // Clipboard watcher cleanup
        ClipboardWatcherService.Instance.SymbolsDetected -= OnSymbolsDetected;
        ClipboardWatcherService.Instance.Dispose();

        // Shutdown global hotkeys to free Win32 registrations immediately.
        DetachGlobalHotkeys();

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

    private void AttachGlobalHotkeys(IntPtr hwnd)
    {
        if (!_globalHotkeysAttached)
        {
            GlobalHotkeyService.Instance.GlobalHotkeyFired += OnGlobalHotkeyFired;
            _globalHotkeysAttached = true;
        }

        GlobalHotkeyService.Instance.Initialize(hwnd);
    }

    private void DetachGlobalHotkeys()
    {
        if (_globalHotkeysAttached)
        {
            GlobalHotkeyService.Instance.GlobalHotkeyFired -= OnGlobalHotkeyFired;
            _globalHotkeysAttached = false;
        }

        GlobalHotkeyService.Instance.Shutdown();
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

    private void OnLogoutRequested(object? sender, EventArgs e)
    {
        if (!Dispatcher.CheckAccess())
        {
            _ = Dispatcher.InvokeAsync(() => OnLogoutRequested(sender, e));
            return;
        }

        ShowStartupWindowAfterLogout();
    }

    private void ShowStartupWindowAfterLogout()
    {
        SaveWorkspaceSession();
        DetachGlobalHotkeys();

        try
        {
            Hide();
            System.Windows.Application.Current.ShutdownMode = ShutdownMode.OnExplicitShutdown;

            var startupWindow = App.Services.GetRequiredService<StartupWindow>();
            startupWindow.WindowStartupLocation = WindowStartupLocation.CenterScreen;
            System.Windows.Application.Current.MainWindow = startupWindow;
            WpfServices.LoggingService.Instance.LogInfo("WPF startup window shown after logout");

            if (startupWindow.ShowDialog() != true)
            {
                WpfServices.LoggingService.Instance.LogWarning("WPF startup cancelled after logout");
                System.Windows.Application.Current.Shutdown();
                return;
            }

            System.Windows.Application.Current.MainWindow = this;
            System.Windows.Application.Current.ShutdownMode = ShutdownMode.OnMainWindowClose;
            _viewModel.RefreshAuthenticationState();
            RootFrame.Navigate(App.Services.GetRequiredService<FundProfileSelectionPage>());

            Show();
            if (WindowState == WindowState.Minimized)
            {
                WindowState = WindowState.Normal;
            }

            AttachGlobalHotkeys(new WindowInteropHelper(this).Handle);
            EnsureShellVisibleOnStartup();
            Activate();
        }
        catch (Exception ex)
        {
            WpfServices.LoggingService.Instance.LogError("[MainWindow] Failed to show startup window after logout", ex);
            MessageBox.Show(
                "Meridian could not reopen the startup login screen after logout. The desktop application will close.",
                "Meridian logout",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            System.Windows.Application.Current.Shutdown();
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
                _viewModel.NavigateCommand.Execute(e.ActionId);
                break;

            case PaletteCommandCategory.Action:
                _viewModel.HandlePaletteAction(e.ActionId);
                break;
        }
    }

    private async void OnLaunchArgsReceived(object? sender, string[] args)
    {
        if (!Dispatcher.CheckAccess())
        {
            _ = Dispatcher.InvokeAsync(() => OnLaunchArgsReceived(sender, args));
            return;
        }

        try
        {
            await HandleLaunchArgsAsync(args);
        }
        catch (Exception ex)
        {
            Meridian.Wpf.Services.LoggingService.Instance.LogError("[MainWindow] Failed to handle forwarded launch args", ex);
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
        if (!IsActive)
            return;

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
        if (e.IsUpdate)
            return; // Only show notification for new alerts

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
        if (alert.IsSuppressed || alert.IsSnoozed)
            return;

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
        var restorePlan = await _sessionService
            .BuildRestorePlanForContextAsync(context, ResolveDefaultPageTag, ct)
            .ConfigureAwait(true);

        ApplyRestorePlan(restorePlan);
    }

    private void ApplyRestorePlan(DesktopShellSessionRestorePlan? restorePlan)
    {
        if (restorePlan is null)
        {
            return;
        }

        if (!string.IsNullOrWhiteSpace(restorePlan.DeferredPageTag))
        {
            _ = Dispatcher.BeginInvoke(DispatcherPriority.Loaded, () =>
            {
                _ = _navigationService.NavigateTo(restorePlan.DeferredPageTag);
            });
        }

        if (!string.IsNullOrWhiteSpace(restorePlan.TargetPageTag))
        {
            _navigationService.NavigateTo(restorePlan.TargetPageTag);
        }
    }

    /// <summary>
    /// Saves the current workspace session state for next launch.
    /// Preserves per-page filter state and open pages accumulated during the session.
    /// </summary>
    private void SaveWorkspaceSession()
    {
        _sessionService.SaveWorkspaceSession(CaptureDesktopWindowBounds(), RootFrame.Content is MainPage);
    }

    private DesktopWindowBounds CaptureDesktopWindowBounds()
        => new(
            RestoreBounds.Left,
            RestoreBounds.Top,
            RestoreBounds.Width,
            RestoreBounds.Height,
            WindowState == WindowState.Maximized);

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
        var restorePlan = await _shellCoordinator
            .PrepareOperatingContextAsync(context, ResolveDefaultPageTag, ct)
            .ConfigureAwait(true);
        await EnsureMainPageShellReadyAsync(ct);
        ApplyRestorePlan(restorePlan);
        EnsureShellVisibleOnStartup();
    }

    private async Task HandleLaunchArgsAsync(string[] args, CancellationToken ct = default)
    {
        var request = _launchRouter.Parse(args);
        if (!request.HasActions)
        {
            return;
        }

        if (request.RequiresShell)
        {
            await EnsureMainPageShellReadyAsync(ct);
        }

        _launchRouter.Apply(args, _viewModel);
        if (request.HasPageNavigation && RootFrame.Content is MainPage mainPage)
        {
            mainPage.NavigateToLaunchPage(request.PageTag!);
        }

        EnsureShellVisibleOnStartup();

        if (request.HasScreenshotRequest)
        {
            await CaptureMainWindowScreenshotAsync(request.ScreenshotPath!, ct);
        }
    }

    private async Task CaptureMainWindowScreenshotAsync(string path, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        await EnsureMainPageShellReadyAsync(ct);
        await Dispatcher.InvokeAsync(UpdateLayout, DispatcherPriority.Render).Task;
        await Task.Delay(250, ct);
        await Dispatcher.InvokeAsync(() => SaveMainWindowScreenshot(path), DispatcherPriority.ApplicationIdle).Task;
    }

    private void SaveMainWindowScreenshot(string path)
    {
        var targetPath = Path.GetFullPath(path);
        var directory = Path.GetDirectoryName(targetPath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        EnsureShellVisibleOnStartup();
        UpdateLayout();

        var visualWidth = ActualWidth;
        var visualHeight = ActualHeight;
        if (visualWidth < 200 || visualHeight < 200)
        {
            throw new InvalidOperationException(
                $"Main window bounds are too small for automation screenshot capture ({visualWidth:N0}x{visualHeight:N0}).");
        }

        var dpi = VisualTreeHelper.GetDpi(this);
        var pixelWidth = Math.Max(1, (int)Math.Ceiling(visualWidth * dpi.DpiScaleX));
        var pixelHeight = Math.Max(1, (int)Math.Ceiling(visualHeight * dpi.DpiScaleY));
        var renderTarget = new RenderTargetBitmap(
            pixelWidth,
            pixelHeight,
            96 * dpi.DpiScaleX,
            96 * dpi.DpiScaleY,
            PixelFormats.Pbgra32);

        var visual = new DrawingVisual();
        using (var context = visual.RenderOpen())
        {
            context.DrawRectangle(
                new VisualBrush(this),
                null,
                new Rect(new Point(0, 0), new Size(visualWidth, visualHeight)));
        }

        renderTarget.Render(visual);

        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(renderTarget));
        using var stream = File.Create(targetPath);
        encoder.Save(stream);

        Meridian.Wpf.Services.LoggingService.Instance.LogInfo($"Saved automation screenshot to '{targetPath}'.");
    }

    private async Task EnsureMainPageShellReadyAsync(CancellationToken ct = default)
    {
        if (RootFrame.Content is not MainPage)
        {
            RootFrame.Navigate(App.Services.GetRequiredService<MainPage>());
        }

        EnsureShellVisibleOnStartup();

        if (RootFrame.Content is MainPage mainPage)
        {
            await mainPage.WaitForShellReadyAsync();
        }

        await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.Loaded, ct);
    }

    private async Task ShowContextSelectionAsync(bool saveCurrentSession, CancellationToken ct = default)
    {
        if (saveCurrentSession)
        {
            SaveWorkspaceSession();
        }

        await _sessionService.LoadWorkspacesAsync(ct);
        RootFrame.Navigate(App.Services.GetRequiredService<FundProfileSelectionPage>());
        EnsureShellVisibleOnStartup();
    }

    private static string ResolveDefaultPageTag(string? workspaceId)
        => ShellNavigationCatalog.GetWorkspace(WorkstationNavigationDefaults.NormalizeWorkspaceId(workspaceId))?.HomePageTag
           ?? ShellNavigationCatalog.GetDefaultWorkspace().HomePageTag;

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
            var state = new DesktopWindowState(
                RestoreBounds.Left,
                RestoreBounds.Top,
                RestoreBounds.Width,
                RestoreBounds.Height,
                WindowState == WindowState.Maximized,
                DateTime.UtcNow);
            await _windowStateStore.SaveAsync(state, ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            // Persisting window bounds is best-effort; failures must not disrupt shutdown.
            WpfServices.LoggingService.Instance.LogDebug(
                "Failed to persist window state.",
                ("exception", ex.GetType().Name),
                ("message", ex.Message));
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
            var state = _windowStateStore.Load();
            if (state == null)
                return;

            // Validate dimensions are reasonable
            if (state.Width < MinWidth || state.Height < MinHeight)
                return;
            if (state.Width > 10000 || state.Height > 10000)
                return;

            // Validate position is on a visible monitor
            if (!IsPositionOnScreen(state.Left, state.Top, state.Width, state.Height))
            {
                // Position is off-screen (monitor may have been disconnected)
                // Keep default CenterScreen position but restore size
                Width = state.Width;
                Height = state.Height;
                _restoredWindowBounds = null;
            }
            else
            {
                WindowStartupLocation = WindowStartupLocation.Manual;
                Left = state.Left;
                Top = state.Top;
                Width = state.Width;
                Height = state.Height;
                _restoredWindowBounds = new Rect(state.Left, state.Top, state.Width, state.Height);
            }

            if (state.IsMaximized)
            {
                WindowState = WindowState.Maximized;
            }
        }
        catch (Exception ex)
        {
            // Restoring saved bounds is best-effort; fall back to default placement on failure.
            WpfServices.LoggingService.Instance.LogDebug(
                "Failed to restore window state.",
                ("exception", ex.GetType().Name),
                ("message", ex.Message));
        }
    }

    private void EnsureShellVisibleOnStartup()
    {
        var nativeBounds = CaptureNativeBounds();
        if (!WindowStartupRecovery.NeedsRecovery(
                IsVisible,
                ShowInTaskbar,
                WindowState,
                nativeBounds.Width,
                nativeBounds.Height,
                MinWidth,
                MinHeight))
        {
            if (_startupRecoveryTimer.IsEnabled)
            {
                _startupRecoveryTimer.Stop();
            }

            return;
        }

        var targetBounds = WindowStartupRecovery.ResolveBounds(
            _restoredWindowBounds,
            nativeBounds,
            MinWidth,
            MinHeight,
            DefaultWindowWidth,
            DefaultWindowHeight);

        WindowStartupLocation = WindowStartupLocation.Manual;
        Left = targetBounds.Left;
        Top = targetBounds.Top;
        Width = targetBounds.Width;
        Height = targetBounds.Height;

        if (WindowState == WindowState.Minimized)
        {
            WindowState = WindowState.Normal;
        }

        if (!ShowInTaskbar)
        {
            ShowInTaskbar = true;
        }

        if (Visibility != Visibility.Visible)
        {
            Visibility = Visibility.Visible;
        }

        if (!IsVisible)
        {
            Show();
        }

        UpdateLayout();

        var hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd != IntPtr.Zero)
        {
            NativeMethods.ShowWindow(hwnd, NativeMethods.SW_SHOWNORMAL);
            NativeMethods.SetWindowPos(
                hwnd,
                IntPtr.Zero,
                (int)Math.Round(targetBounds.Left),
                (int)Math.Round(targetBounds.Top),
                (int)Math.Round(targetBounds.Width),
                (int)Math.Round(targetBounds.Height),
                NativeMethods.SWP_NOZORDER | NativeMethods.SWP_SHOWWINDOW);
        }

        var restoreTopmost = Topmost;
        Topmost = true;
        Topmost = restoreTopmost;
        Activate();
        Focus();
    }

    public void ForceStartupWindowRecovery()
    {
        EnsureShellVisibleOnStartup();
        StartShellRecoveryLoop();
    }

    private static class NativeMethods
    {
        internal const int SW_SHOWNORMAL = 1;
        internal const uint SWP_NOZORDER = 0x0004;
        internal const uint SWP_SHOWWINDOW = 0x0040;

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool SetWindowPos(
            IntPtr hWnd,
            IntPtr hWndInsertAfter,
            int X,
            int Y,
            int cx,
            int cy,
            uint uFlags);
    }

    private void StartShellRecoveryLoop()
    {
        _startupRecoveryAttempts = 0;
        if (!_startupRecoveryTimer.IsEnabled)
        {
            _startupRecoveryTimer.Start();
        }
    }

    private void OnStartupRecoveryTick(object? sender, EventArgs e)
    {
        _startupRecoveryAttempts++;
        EnsureShellVisibleOnStartup();

        if (_startupRecoveryAttempts >= StartupRecoveryMaxAttempts)
        {
            _startupRecoveryTimer.Stop();
        }
    }

    private Rect CaptureNativeBounds()
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd == IntPtr.Zero || !NativeBounds.TryGetWindowRect(hwnd, out var rect))
        {
            return new Rect(Left, Top, Width, Height);
        }

        return rect;
    }

    private static class NativeBounds
    {
        [StructLayout(LayoutKind.Sequential)]
        private struct RECT
        {
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;
        }

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

        internal static bool TryGetWindowRect(IntPtr hWnd, out Rect rect)
        {
            if (GetWindowRect(hWnd, out var nativeRect))
            {
                rect = new Rect(
                    nativeRect.Left,
                    nativeRect.Top,
                    nativeRect.Right - nativeRect.Left,
                    nativeRect.Bottom - nativeRect.Top);
                return true;
            }

            rect = Rect.Empty;
            return false;
        }
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

        DropOverlaySubtitle.Text = _fileDropRouter.GetFriendlyFileType(files[0]);
    }

    /// <summary>
    /// Detects the file type and navigates to the appropriate import page,
    /// passing the file path as a parameter so the destination can pre-populate its UI.
    /// </summary>
    private void RouteDroppedFile(string filePath)
    {
        try
        {
            _fileDropRouter.RouteFile(filePath);
        }
        catch (Exception ex)
        {
            // A malformed or unsupported drop must not crash the shell; surface it at debug level.
            WpfServices.LoggingService.Instance.LogDebug(
                "Failed to route dropped file.",
                ("exception", ex.GetType().Name),
                ("message", ex.Message));
        }
    }

}
