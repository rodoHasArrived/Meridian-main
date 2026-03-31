using System;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using System.Windows.Forms;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Meridian.Application.Services;
using Meridian.Application.SecurityMaster;
using Meridian.Backtesting;
using Meridian.Contracts.Domain.Enums;
using Meridian.Contracts.SecurityMaster;
using Meridian.Storage.SecurityMaster;
using Meridian.Strategies.Interfaces;
using Meridian.Strategies.Services;
using Meridian.Strategies.Storage;
using Meridian.Ui.Shared.Services;
using Meridian.Wpf.Contracts;
using WpfServices = Meridian.Wpf.Services;
using Meridian.Wpf.Views;
using Meridian.Ui.Services;
using Meridian.Ui.Services.DataQuality;

namespace Meridian.Wpf;

/// <summary>
/// Meridian WPF Application
/// Provides maximum stability through WPF (.NET 9) for Windows-only deployment.
/// </summary>
public partial class App : System.Windows.Application
{
    private static bool     _isFirstRun;
    private static bool     _isFixtureMode;
    private static string[] _launchArgs = [];
    private IHost? _host;

    /// <summary>
    /// Gets the service provider for dependency injection.
    /// </summary>
    public static IServiceProvider Services { get; private set; } = null!;

    /// <summary>
    /// Gets whether the application is running in fixture mode (offline mock data).
    /// Activated via --fixture command-line argument or MDC_FIXTURE_MODE=1 environment variable.
    /// </summary>
    public static bool IsFixtureMode => _isFixtureMode;

    /// <summary>
    /// Gets the main application window.
    /// </summary>
    public static new MainWindow? MainWindow => Current.MainWindow as MainWindow;

    /// <summary>
    /// Gets whether this is the first run of the application.
    /// </summary>
    public static bool IsFirstRun => _isFirstRun;

    /// <summary>
    /// Gets the command-line arguments passed at launch.
    /// Used by <see cref="MainWindow"/> to handle jump-list and deep-link args.
    /// </summary>
    public static string[] GetLaunchArgs() => _launchArgs;

    /// <summary>
    /// Gets the notification service instance.
    /// </summary>
    public static WpfServices.NotificationService Notifications => WpfServices.NotificationService.Instance;

    /// <summary>
    /// Gets the connection service instance.
    /// </summary>
    public static WpfServices.ConnectionService Connection => WpfServices.ConnectionService.Instance;

    /// <summary>
    /// Gets the theme service instance.
    /// </summary>
    public static WpfServices.ThemeService Theme => WpfServices.ThemeService.Instance;

    /// <summary>
    /// Gets the offline tracking persistence service instance.
    /// </summary>
    public static WpfServices.OfflineTrackingPersistenceService OfflineTracking => WpfServices.OfflineTrackingPersistenceService.Instance;

    /// <summary>
    /// Gets the background task scheduler service instance.
    /// </summary>
    public static WpfServices.BackgroundTaskSchedulerService Scheduler => WpfServices.BackgroundTaskSchedulerService.Instance;

    /// <summary>
    /// Gets the pending operations queue service instance.
    /// </summary>
    public static WpfServices.PendingOperationsQueueService OperationsQueue => WpfServices.PendingOperationsQueueService.Instance;

    private async void OnStartup(object sender, StartupEventArgs e)
    {
        // Register the AppUserModelID before any window is shown so that the
        // Windows shell (taskbar, JumpList, toast activations) maps all
        // notifications back to this process identity.
        WpfServices.ToastNotificationService.SetAppUserModelId();

        // Enforce single instance: if another Meridian window is already running,
        // forward the launch args to it via named pipe and exit cleanly.
        _launchArgs = e.Args;
        if (!WpfServices.SingleInstanceService.Instance.TryAcquire())
        {
            WpfServices.SingleInstanceService.SendArgsToPrimary(e.Args);
            Shutdown();
            return;
        }

        // Detect fixture mode from --fixture arg or MDC_FIXTURE_MODE env var
        _isFixtureMode = DetectFixtureMode(e.Args);

        // Parse any deep-link navigation tag from launch args (e.g. --navigate Backfill).
        // Toast balloon-tip clicks raise BalloonTipClicked in-process, but external
        // activations (future WinRT toasts, shortcuts) can pass this argument.
        var deepLinkTag = ParseDeepLinkTag(e.Args);

        // Configure the host with dependency injection
        _host = Host.CreateDefaultBuilder()
            .ConfigureServices((context, services) =>
            {
                ConfigureServices(services);
            })
            .Build();

        Services = _host.Services;
        Services.GetRequiredService<WpfServices.StrategyRunWorkspaceService>();

        // Provide the DI container to NavigationService so it can resolve pages
        WpfServices.NavigationService.Instance.SetServiceProvider(Services);

        // Handle unhandled exceptions gracefully
        DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += OnDomainUnhandledException;
        TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;

        // Create and show MainWindow from DI (replaces StartupUri)
        var mainWindow = Services.GetRequiredService<MainWindow>();
        Current.MainWindow = mainWindow;
        mainWindow.Show();

        // Register taskbar jump list tasks (Start Collector, Open Dashboard, etc.).
        WpfServices.JumpListService.Instance.Register();

        // Begin listening for args forwarded from secondary instances (jump list re-launch).
        WpfServices.SingleInstanceService.Instance.StartListening();

        // If a deep-link page was requested, navigate immediately after the window opens.
        if (!string.IsNullOrEmpty(deepLinkTag))
            WpfServices.NavigationService.Instance.NavigateTo(deepLinkTag);

        // Fire-and-forget async initialization with proper exception handling
        await SafeOnStartupAsync();
    }

    /// <summary>
    /// Parses a deep-link navigation tag from command-line args.
    /// Supports <c>--navigate &lt;PageTag&gt;</c> (e.g. <c>--navigate Backfill</c>)
    /// and <c>--page=&lt;PageTag&gt;</c> (e.g. <c>--page=Dashboard</c>, used by jump list tasks).
    /// </summary>
    private static string? ParseDeepLinkTag(string[] args)
    {
        for (var i = 0; i < args.Length - 1; i++)
        {
            if (string.Equals(args[i], "--navigate", StringComparison.OrdinalIgnoreCase))
                return args[i + 1];
        }

        // Also support --page=PageTag format used by taskbar jump list tasks.
        foreach (var arg in args)
        {
            if (arg.StartsWith("--page=", StringComparison.OrdinalIgnoreCase))
            {
                var tag = arg["--page=".Length..];
                if (!string.IsNullOrWhiteSpace(tag)) return tag;
            }
        }

        return null;
    }

    /// <summary>
    /// Detects whether fixture mode should be activated.
    /// Checks for --fixture command-line argument or MDC_FIXTURE_MODE=1 environment variable.
    /// </summary>
    private static bool DetectFixtureMode(string[] args)
    {
        // Check command-line argument
        if (Array.Exists(args, arg => string.Equals(arg, "--fixture", StringComparison.OrdinalIgnoreCase)))
            return true;

        // Check environment variable
        var envValue = Environment.GetEnvironmentVariable("MDC_FIXTURE_MODE");
        return string.Equals(envValue, "1", StringComparison.Ordinal)
            || string.Equals(envValue, "true", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Configures services for dependency injection.
    /// C1: DI-first registration — services registered by interface where possible.
    /// Pages registered as transient for constructor injection via NavigationService.
    /// </summary>
    private static void ConfigureServices(IServiceCollection services)
    {
        // Register shared desktop HttpClient configurations
        services.AddDesktopHttpClients();

        // Shared API infrastructure
        services.AddSingleton(_ => ApiClientService.Instance);

        // ── Fixture mode service (offline mock data) ────────────────────────
        services.AddSingleton(_ => Meridian.Ui.Services.Services.FixtureDataService.Instance);

        // ── Core services (by interface + concrete type) ────────────────────
        services.AddSingleton<IConnectionService>(_ => WpfServices.ConnectionService.Instance);
        services.AddSingleton(_ => WpfServices.ConnectionService.Instance);

        services.AddSingleton<INavigationService>(_ => WpfServices.NavigationService.Instance);
        services.AddSingleton(_ => WpfServices.NavigationService.Instance);

        services.AddSingleton<Meridian.Ui.Services.Contracts.ILoggingService>(_ => WpfServices.LoggingService.Instance);
        services.AddSingleton(_ => WpfServices.LoggingService.Instance);

        services.AddSingleton(_ => WpfServices.ConfigService.Instance);
        services.AddSingleton(_ => WpfServices.ThemeService.Instance);
        services.AddSingleton(_ => WpfServices.NotificationService.Instance);
        services.AddSingleton(_ => WpfServices.KeyboardShortcutService.Instance);
        services.AddSingleton(_ => WpfServices.MessagingService.Instance);
        services.AddSingleton(_ => WpfServices.StatusService.Instance);
        services.AddSingleton(_ => WpfServices.FirstRunService.Instance);

        // ── Onboarding / workspace services ──────────────────────────────────
        services.AddSingleton(_ => Meridian.Ui.Services.OnboardingTourService.Instance);
        services.AddSingleton(_ => WpfServices.WorkspaceService.Instance);
        services.AddSingleton(_ => Meridian.Ui.Services.AlertService.Instance);

        // ── Domain / feature services ───────────────────────────────────────
        services.AddSingleton(_ => WpfServices.BackendServiceManager.Instance);
        services.AddSingleton(_ => WpfServices.WatchlistService.Instance);
        services.AddSingleton(_ => WpfServices.ArchiveHealthService.Instance);
        services.AddSingleton(_ => WpfServices.SchemaService.Instance);
        services.AddSingleton(_ => WpfServices.RunMatService.Instance);
        services.AddSingleton<AdminMaintenanceServiceBase>(_ => AdminMaintenanceServiceBase.Instance);
        services.AddSingleton<AdvancedAnalyticsServiceBase>(_ => new AdvancedAnalyticsServiceBase());
        services.AddSingleton(_ => SearchService.Instance);

        // ── AI Agent service (local Ollama) ──────────────────────────────────
        services.AddSingleton<WpfServices.IAgentLoopService, WpfServices.AgentLoopService>();

        // ── Data quality shared services ─────────────────────────────────────
        services.AddSingleton<IDataQualityApiClient, DataQualityApiClient>();
        services.AddSingleton<IDataQualityPresentationService, DataQualityPresentationService>();
        services.AddTransient<IDataQualityRefreshService, DataQualityRefreshService>();
        RegisterStrategyWorkspaceServices(services);

        // ── Background / infrastructure services ────────────────────────────
        services.AddSingleton(_ => WpfServices.BackgroundTaskSchedulerService.Instance);
        services.AddSingleton(_ => WpfServices.OfflineTrackingPersistenceService.Instance);
        services.AddSingleton(_ => WpfServices.PendingOperationsQueueService.Instance);
        services.AddSingleton(_ => WpfServices.ToastNotificationService.Instance);
        // C1 fix: register a single SystemTrayService instance under the interface contract;
        //         the concrete type is resolved via the same singleton.
        services.AddSingleton<WpfServices.SystemTrayService>();
        services.AddSingleton<WpfServices.ISystemTrayService>(sp => sp.GetRequiredService<WpfServices.SystemTrayService>());

        // ── MainWindow ──────────────────────────────────────────────────────
        services.AddSingleton<MainWindow>();

        // ── Pages (transient — created per navigation) ──────────────────────
        services.AddTransient<MainPage>();
        services.AddTransient<DashboardPage>();
        services.AddTransient<WatchlistPage>();
        services.AddTransient<ProviderPage>();
        services.AddTransient<ProviderHealthPage>();
        services.AddTransient<DataSourcesPage>();
        services.AddTransient<LiveDataViewerPage>();
        services.AddTransient<SymbolsPage>();
        services.AddTransient<SymbolMappingPage>();
        services.AddTransient<SymbolStoragePage>();
        services.AddTransient<StoragePage>();
        services.AddTransient<BackfillPage>();
        services.AddTransient<PortfolioImportPage>();
        services.AddTransient<IndexSubscriptionPage>();
        services.AddTransient<ScheduleManagerPage>();
        services.AddTransient<DataQualityPage>();
        services.AddTransient<CollectionSessionPage>();
        services.AddTransient<ArchiveHealthPage>();
        services.AddTransient<ServiceManagerPage>();
        services.AddTransient<SystemHealthPage>();
        services.AddTransient<DiagnosticsPage>();
        services.AddTransient<DataExportPage>();
        services.AddTransient<DataSamplingPage>();
        services.AddTransient<TimeSeriesAlignmentPage>();
        services.AddTransient<ExportPresetsPage>();
        services.AddTransient<AnalysisExportPage>();
        services.AddTransient<AnalysisExportWizardPage>();
        services.AddTransient<EventReplayPage>();
        services.AddTransient<PackageManagerPage>();
        services.AddTransient<TradingHoursPage>();
        services.AddTransient<AdvancedAnalyticsPage>();
        services.AddTransient<ChartingPage>();
        services.AddTransient<OrderBookPage>();
        services.AddTransient<Meridian.Ui.Services.DataCalendarService>();
        services.AddTransient<DataCalendarPage>();
        services.AddTransient<StorageOptimizationPage>();
        services.AddTransient<RetentionAssurancePage>();
        services.AddTransient<AdminMaintenancePage>();
        services.AddTransient<LeanIntegrationPage>();
        services.AddTransient<MessagingHubPage>();
        services.AddTransient<Meridian.Wpf.Views.WorkspacePage>();
        services.AddTransient<NotificationCenterPage>();
        services.AddTransient<HelpPage>();
        services.AddTransient<WelcomePage>();
        services.AddTransient<SettingsPage>();
        services.AddTransient<KeyboardShortcutsPage>();
        services.AddTransient<SetupWizardPage>();
        services.AddTransient<AddProviderWizardPage>();
        services.AddTransient<ActivityLogPage>();
        services.AddTransient<DataBrowserPage>();
        services.AddTransient<BacktestPage>();
        services.AddTransient<RunMatPage>();
        services.AddTransient<StrategyRunsPage>();
        services.AddTransient<RunDetailPage>();
        services.AddTransient<RunPortfolioPage>();
        services.AddTransient<RunLedgerPage>();
        services.AddTransient<SecurityMasterPage>();
        services.AddTransient<Meridian.Wpf.ViewModels.SecurityMasterViewModel>();
        services.AddTransient<PluginManagementPage>();
        services.AddTransient<AgentPage>();

        // ── Backtesting service ──────────────────────────────────────────────
        // Registered in RegisterStrategyWorkspaceServices so optional Security Master
        // collaborators can be attached when that feature is enabled.

        // ── Ui.Services singletons accessed via DI (no static .Instance in pages) ──
        services.AddSingleton(_ => BackfillProviderConfigService.Instance);
        services.AddSingleton(_ => BackfillCheckpointService.Instance);
        services.AddSingleton(_ => Meridian.Ui.Services.ActivityFeedService.Instance);
        services.AddSingleton(_ => Meridian.Ui.Services.CommandPaletteService.Instance);
        services.AddSingleton(_ => Meridian.Ui.Services.SymbolManagementService.Instance);
        services.AddSingleton(_ => Meridian.Ui.Services.BackfillService.Instance);
        services.AddSingleton(_ => WpfServices.TaskbarProgressService.Instance);
        services.AddSingleton(_ => WpfServices.TearOffPanelService.Instance);
        services.AddSingleton(_ => WpfServices.StrategyRunWorkspaceService.Instance);

        // ── ViewModels (transient — new instance per page navigation) ────────
        services.AddTransient<Meridian.Wpf.ViewModels.BackfillViewModel>();
        services.AddTransient<Meridian.Wpf.ViewModels.ProviderViewModel>();
        services.AddTransient<Meridian.Wpf.ViewModels.DataQualityViewModel>();
        services.AddTransient<Meridian.Wpf.ViewModels.RunMatViewModel>();
        services.AddTransient<Meridian.Wpf.ViewModels.StrategyRunBrowserViewModel>();
        services.AddTransient<Meridian.Wpf.ViewModels.StrategyRunDetailViewModel>();
        services.AddTransient<Meridian.Wpf.ViewModels.StrategyRunPortfolioViewModel>();
        services.AddTransient<Meridian.Wpf.ViewModels.StrategyRunLedgerViewModel>();
        services.AddTransient<Meridian.Wpf.ViewModels.PluginManagementViewModel>();
        services.AddTransient<Meridian.Wpf.ViewModels.AgentViewModel>();
        services.AddTransient<Meridian.Wpf.ViewModels.BacktestViewModel>();
        services.AddTransient<Meridian.Wpf.ViewModels.ChartingPageViewModel>();
        services.AddTransient<Meridian.Wpf.ViewModels.TickerStripViewModel>();
        services.AddTransient<Meridian.Wpf.ViewModels.WatchlistViewModel>();
        services.AddTransient<Meridian.Wpf.ViewModels.SettingsViewModel>();

        // ── Plugin loader service ────────────────────────────────────────────
        services.AddSingleton<Meridian.Infrastructure.DataSources.DataSourceRegistry>();
        services.AddSingleton<Meridian.Application.Services.IPluginLoaderService,
                              Meridian.Application.Services.PluginLoaderService>();
    }

    private static void RegisterStrategyWorkspaceServices(IServiceCollection services)
    {
        var securityMasterConnectionString = Environment.GetEnvironmentVariable("MERIDIAN_SECURITY_MASTER_CONNECTION_STRING");
        if (!string.IsNullOrWhiteSpace(securityMasterConnectionString))
        {
            services.AddSingleton(sp => new SecurityMasterOptions
            {
                ConnectionString = securityMasterConnectionString,
                Schema = Environment.GetEnvironmentVariable("MERIDIAN_SECURITY_MASTER_SCHEMA") ?? "security_master",
                SnapshotIntervalVersions = ParseInt("MERIDIAN_SECURITY_MASTER_SNAPSHOT_INTERVAL", 50),
                ProjectionReplayBatchSize = ParseInt("MERIDIAN_SECURITY_MASTER_REPLAY_BATCH_SIZE", 500),
                PreloadProjectionCache = ParseBool("MERIDIAN_SECURITY_MASTER_PRELOAD_CACHE", true),
                ResolveInactiveByDefault = ParseBool("MERIDIAN_SECURITY_MASTER_RESOLVE_INACTIVE", true)
            });
            services.AddSingleton<ISecurityMasterEventStore, PostgresSecurityMasterEventStore>();
            services.AddSingleton<ISecurityMasterSnapshotStore, PostgresSecurityMasterSnapshotStore>();
            services.AddSingleton<ISecurityMasterStore, PostgresSecurityMasterStore>();
            services.AddSingleton<SecurityMasterAggregateRebuilder>();
            services.AddSingleton<SecurityMasterQueryService>();
            services.AddSingleton<Meridian.Application.SecurityMaster.ISecurityMasterQueryService>(sp => sp.GetRequiredService<SecurityMasterQueryService>());
            services.AddSingleton<Meridian.Contracts.SecurityMaster.ISecurityMasterQueryService>(sp => sp.GetRequiredService<SecurityMasterQueryService>());
            services.AddSingleton<ISecurityReferenceLookup, SecurityMasterSecurityReferenceLookup>();

            // Security Master bulk import services
            services.AddSingleton<SecurityMasterCsvParser>();
            services.AddSingleton<ISecurityMasterImportService, SecurityMasterImportService>();

            // Corporate action adjustment for backtesting
            services.AddSingleton<ISecurityResolver, SecurityResolver>();
            services.AddSingleton<Meridian.Backtesting.ICorporateActionAdjustmentService, Meridian.Backtesting.CorporateActionAdjustmentService>();
        }

        // Wire optional Security Master collaborators into the BacktestService singleton when available.
        services.AddSingleton(sp =>
        {
            var svc = WpfServices.BacktestService.Instance;
            svc.SecurityMasterQueryService = sp.GetService<Meridian.Contracts.SecurityMaster.ISecurityMasterQueryService>();
            svc.CorporateActionAdjustmentService = sp.GetService<Meridian.Backtesting.ICorporateActionAdjustmentService>();
            return svc;
        });

        services.AddSingleton<IStrategyRepository, StrategyRunStore>();
        services.AddSingleton<PortfolioReadService>();
        services.AddSingleton<LedgerReadService>();
        services.AddSingleton<StrategyRunReadService>();
        services.AddSingleton(sp =>
        {
            var service = new WpfServices.StrategyRunWorkspaceService(
                sp.GetRequiredService<IStrategyRepository>(),
                sp.GetRequiredService<StrategyRunReadService>());
            WpfServices.StrategyRunWorkspaceService.SetInstance(service);
            return service;
        });
    }

    private static int ParseInt(string name, int defaultValue)
        => int.TryParse(Environment.GetEnvironmentVariable(name), out var value) ? value : defaultValue;

    private static bool ParseBool(string name, bool defaultValue)
        => bool.TryParse(Environment.GetEnvironmentVariable(name), out var value) ? value : defaultValue;

    /// <summary>
    /// Performs async initialization with proper exception handling.
    /// </summary>
    private async Task SafeOnStartupAsync(CancellationToken ct = default)
    {
        try
        {
            // Run first-time setup before showing window
            await InitializeFirstRunAsync();

            // Initialize and validate configuration
            await InitializeConfigurationAsync();

            // Initialize theme service
            if (Current.MainWindow is MainWindow mainWindow)
            {
                WpfServices.ThemeService.Instance.Initialize(mainWindow);

                // Initialize system tray integration
                var systemTrayService = Services.GetRequiredService<WpfServices.ISystemTrayService>();
                systemTrayService.Initialize(mainWindow);

                // Wire notifications to system tray balloons
                WireNotificationsTray(systemTrayService);

                // Wire connection status to tray icon
                WireConnectionStatusTray(systemTrayService);
            }

            // Start connection monitoring
            WpfServices.ConnectionService.Instance.StartMonitoring();

            // Initialize offline tracking persistence (handles recovery from crashes/restarts)
            await InitializeOfflineTrackingAsync();

            // Start background task scheduler
            await InitializeBackgroundServicesAsync();

            // Handle --start-collector launch arg now that connection monitoring is active.
            if (Array.Exists(_launchArgs, a => string.Equals(a, "--start-collector", StringComparison.OrdinalIgnoreCase)))
                await StartCollectorFromLaunchArgAsync();

            // Notify if running in fixture mode
            if (_isFixtureMode)
            {
                WpfServices.LoggingService.Instance.LogWarning("Running in FIXTURE MODE — using offline mock data");
                await WpfServices.NotificationService.Instance.NotifyWarningAsync(
                    "Fixture Mode Active",
                    "Application is using mock data for offline development");
            }

            // Log successful startup
            WpfServices.LoggingService.Instance.LogInfo("Application started successfully");
        }
        catch (Exception ex)
        {
            WpfServices.LoggingService.Instance.LogError("Error during application startup", ex);

            try
            {
                await WpfServices.NotificationService.Instance.NotifyErrorAsync(
                    "Startup Error",
                    ex.Message);
            }
            catch (Exception notificationEx)
            {
                WpfServices.LoggingService.Instance.LogError(
                    "Failed to display startup error notification",
                    notificationEx);
            }
        }
    }

    /// <summary>
    /// Initializes offline tracking persistence and performs recovery if needed.
    /// </summary>
    private static async Task InitializeOfflineTrackingAsync(CancellationToken ct = default)
    {
        try
        {
            await WpfServices.OfflineTrackingPersistenceService.Instance.InitializeAsync();
        }
        catch (Exception ex)
        {
            // Continue - app should still work without persistence
        }
    }

    /// <summary>
    /// Initializes background services for scheduled tasks and offline queue processing.
    /// </summary>
    private static async Task InitializeBackgroundServicesAsync(CancellationToken ct = default)
    {
        try
        {
            // Initialize pending operations queue
            await WpfServices.PendingOperationsQueueService.Instance.InitializeAsync();

            // Start background task scheduler
            await WpfServices.BackgroundTaskSchedulerService.Instance.StartAsync();
        }
        catch (Exception ex)
        {
            // Continue - app should still work without background services
        }
    }

    /// <summary>
    /// Starts the data collector as requested by the <c>--start-collector</c> launch arg.
    /// Runs after all services are initialised so connection monitoring is active.
    /// </summary>
    private static async Task StartCollectorFromLaunchArgAsync(CancellationToken ct = default)
    {
        try
        {
            var provider = WpfServices.ConnectionService.Instance.CurrentProvider ?? "default";
            var success  = await WpfServices.ConnectionService.Instance.ConnectAsync(provider, ct);

            WpfServices.NotificationService.Instance.ShowNotification(
                success ? "Collector Started" : "Start Failed",
                success
                    ? "Data collection started via taskbar jump list."
                    : "Failed to start collector — check provider settings.",
                success
                    ? NotificationType.Success
                    : NotificationType.Error,
                success ? 5000 : 0);
        }
        catch (Exception ex)
        {
        }
    }

    /// <summary>
    /// Restores the last workspace session state (active page, window bounds).
    /// </summary>
    private static async Task RestoreWorkspaceSessionAsync(CancellationToken ct = default)
    {
        try
        {
            var workspaceService = WpfServices.WorkspaceService.Instance;
            await workspaceService.LoadWorkspacesAsync();

            var session = workspaceService.GetLastSessionState();
            if (session != null && !string.IsNullOrEmpty(session.ActivePageTag))
            {
                // Navigate to the last active page
                WpfServices.NavigationService.Instance.NavigateTo(session.ActivePageTag);
            }
        }
        catch (Exception ex)
        {
        }
    }

    /// <summary>
    /// Saves the current workspace session state before shutdown.
    /// </summary>
    private static async Task SaveWorkspaceSessionAsync(CancellationToken ct = default)
    {
        try
        {
            var workspaceService = WpfServices.WorkspaceService.Instance;
            var navService = WpfServices.NavigationService.Instance;

            var currentPageTag = navService.GetCurrentPageTag() ?? "Dashboard";
            var session = new Meridian.Ui.Services.SessionState
            {
                ActivePageTag = currentPageTag,
                SavedAt = DateTime.UtcNow
            };

            await workspaceService.SaveSessionStateAsync(session);
        }
        catch (Exception ex)
        {
        }
    }

    /// <summary>
    /// Handles app exit for clean shutdown of background services with timeout.
    /// </summary>
    private async void OnExit(object sender, ExitEventArgs e)
    {
        await SafeOnExitAsync();
        _host?.Dispose();
        WpfServices.SingleInstanceService.Instance.Dispose();
    }

    /// <summary>
    /// Performs async shutdown with proper exception handling.
    /// </summary>
    private static async Task SafeOnExitAsync(CancellationToken ct = default)
    {
        const int ShutdownTimeoutMs = 5000; // 5 second timeout for graceful shutdown

        try
        {

            // Close any floating tear-off quote panels before service shutdown
            WpfServices.TearOffPanelService.Instance.CloseAll();

            using var cts = new CancellationTokenSource(ShutdownTimeoutMs);

            // Save workspace session before shutting down services
            await SaveWorkspaceSessionAsync();

            // Shutdown services in parallel with timeout for better performance
            var shutdownTasks = new[]
            {
                ShutdownServiceAsync(() => WpfServices.BackgroundTaskSchedulerService.Instance.StopAsync(), "BackgroundTaskScheduler", cts.Token),
                ShutdownServiceAsync(() => WpfServices.PendingOperationsQueueService.Instance.ShutdownAsync(), "PendingOperationsQueue", cts.Token),
                ShutdownServiceAsync(() => WpfServices.OfflineTrackingPersistenceService.Instance.ShutdownAsync(), "OfflineTrackingPersistence", cts.Token),
                ShutdownServiceAsync(() => WpfServices.ConnectionService.Instance.StopMonitoring(), "ConnectionService", cts.Token)
            };

            await Task.WhenAll(shutdownTasks);

            // Dispose the NotifyIcon so the system-tray icon is removed cleanly.
            try { WpfServices.ToastNotificationService.Instance.Dispose(); }

        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
        }
    }

    /// <summary>
    /// Helper method to shutdown a service with proper error handling.
    /// </summary>
    private static async Task ShutdownServiceAsync(Func<Task> shutdownAction, string serviceName, CancellationToken ct)
    {
        try
        {
            await shutdownAction().WaitAsync(ct);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
        }
    }

    /// <summary>
    /// Helper method to shutdown a synchronous service.
    /// </summary>
    private static async Task ShutdownServiceAsync(Action shutdownAction, string serviceName, CancellationToken ct)
    {
        await Task.Run(() =>
        {
            try
            {
                ct.ThrowIfCancellationRequested();
                shutdownAction();
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception ex)
            {
            }
        }, ct);
    }

    /// <summary>
    /// Performs first-run initialization including config setup.
    /// </summary>
    private static async Task InitializeFirstRunAsync(CancellationToken ct = default)
    {
        try
        {
            var firstRunService = WpfServices.FirstRunService.Instance;
            _isFirstRun = await firstRunService.IsFirstRunAsync();

            if (_isFirstRun)
            {
                await firstRunService.InitializeAsync();
            }
        }
        catch (Exception ex)
        {
            // Q2: Log first-run setup failures instead of silently swallowing
            WpfServices.LoggingService.Instance.LogError("First-run setup failed", ex);
        }
    }

    /// <summary>
    /// Initializes and validates the application configuration.
    /// </summary>
    private static async Task InitializeConfigurationAsync(CancellationToken ct = default)
    {
        try
        {
            // Initialize the config service
            await WpfServices.ConfigService.Instance.InitializeAsync();

            // Validate configuration
            var validationResult = await WpfServices.ConfigService.Instance.ValidateConfigAsync();

            if (!validationResult.IsValid)
            {
                foreach (var error in validationResult.Errors)
                {
                    WpfServices.LoggingService.Instance.LogError("Configuration error: " + error);
                }
            }

            foreach (var warning in validationResult.Warnings)
            {
                WpfServices.LoggingService.Instance.LogWarning("Configuration warning: " + warning);
            }

            WpfServices.LoggingService.Instance.LogInfo("Configuration initialized",
                ("isValid", validationResult.IsValid.ToString()),
                ("errors", validationResult.Errors.Length.ToString()),
                ("warnings", validationResult.Warnings.Length.ToString()));
        }
        catch (Exception ex)
        {
            WpfServices.LoggingService.Instance.LogError("Failed to initialize configuration", ex);
            // Continue - app should still work with defaults
        }
    }

    /// <summary>
    /// Handles unhandled exceptions on the UI thread.
    /// E3 fix: only suppress transient/recoverable exceptions; fatal ones are logged and re-raised
    /// so the process can terminate cleanly instead of limping forward in a broken state.
    /// </summary>
    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        var ex = e.Exception;

        // Determine whether the exception is likely recoverable (transient UI or I/O issues).
        var isRecoverable =
            ex is InvalidOperationException or
                 System.Net.Http.HttpRequestException or
                 TimeoutException or
                 OperationCanceledException or
                 System.IO.IOException;

        // Always log with structured logging so the error is visible in the log file.
        WpfServices.LoggingService.Instance.LogError("Dispatcher unhandled exception", ex);

        if (isRecoverable)
        {
            e.Handled = true;
            try
            {
                _ = WpfServices.NotificationService.Instance.NotifyErrorAsync(
                    "Application Error",
                    ex.Message);
            }
            catch (Exception notifyEx)
            {
                WpfServices.LoggingService.Instance.LogWarning(
                    "Notification failure during error handling",
                    ("Error", notifyEx.Message));
            }
        }
        // Non-recoverable exceptions are not marked Handled so WPF can tear down cleanly.
    }

    /// <summary>
    /// Handles unhandled exceptions from non-UI threads.
    /// </summary>
    private void OnDomainUnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        if (e.ExceptionObject is Exception ex)
        {
            WpfServices.LoggingService.Instance.LogError("Domain unhandled exception", ex);
        }
    }

    /// <summary>
    /// Handles unobserved task exceptions.
    /// </summary>
    private void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        WpfServices.LoggingService.Instance.LogError("Unobserved task exception", e.Exception);
        e.SetObserved(); // Prevent the process from terminating
    }

    /// <summary>
    /// Wires the NotificationService to the system tray to display important notifications as balloon tips.
    /// </summary>
    private static void WireNotificationsTray(WpfServices.ISystemTrayService systemTrayService)
    {
        var notificationService = WpfServices.NotificationService.Instance;
        notificationService.NotificationReceived += (sender, args) =>
        {
            // Only show high-priority notifications in tray to avoid spam
            if (args.Type == NotificationType.Error || args.Type == NotificationType.Success)
            {
                var icon = args.Type switch
                {
                    NotificationType.Error => System.Windows.Forms.ToolTipIcon.Error,
                    NotificationType.Warning => System.Windows.Forms.ToolTipIcon.Warning,
                    NotificationType.Success => System.Windows.Forms.ToolTipIcon.Info,
                    _ => System.Windows.Forms.ToolTipIcon.Info
                };

                systemTrayService.ShowBalloonTip(args.Title, args.Message, icon, args.DurationMs);
            }
        };
    }

    /// <summary>
    /// Wires the ConnectionService status changes to the system tray icon color and tooltip.
    /// Updates the tray icon to reflect connection health: green (connected), amber (reconnecting), red (disconnected).
    /// </summary>
    private static void WireConnectionStatusTray(WpfServices.ISystemTrayService systemTrayService)
    {
        var connectionService = WpfServices.ConnectionService.Instance;
        connectionService.StateChanged += (sender, args) =>
        {
            // Map ConnectionState to ConnectionStatus
            var status = args.NewState switch
            {
                ConnectionState.Connected => ConnectionStatus.Connected,
                ConnectionState.Reconnecting => ConnectionStatus.Reconnecting,
                ConnectionState.Disconnected => ConnectionStatus.Disconnected,
                _ => ConnectionStatus.Faulted
            };

            systemTrayService.UpdateHealthStatus(status);
        };

        // Set initial status
        var initialStatus = connectionService.State switch
        {
            ConnectionState.Connected => ConnectionStatus.Connected,
            ConnectionState.Reconnecting => ConnectionStatus.Reconnecting,
            ConnectionState.Disconnected => ConnectionStatus.Disconnected,
            _ => ConnectionStatus.Faulted
        };
        systemTrayService.UpdateHealthStatus(initialStatus);
    }
}
