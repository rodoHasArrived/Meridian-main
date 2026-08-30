using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Forms;
using System.Windows.Threading;
using Meridian.PortfolioRecords.FundAccounts;
using Meridian.Application.FundStructure;
using Meridian.Application.SecurityMaster;
using Meridian.Application.Services;
using Meridian.Backtesting;
using Meridian.Backtesting.Engine;
using Meridian.Contracts.Domain.Enums;
using Meridian.Contracts.Lifecycle;
using Meridian.Contracts.SecurityMaster;
using Meridian.Contracts.Services;
using Meridian.Execution.Sdk;
using Meridian.Infrastructure.Adapters.Polygon;
using Meridian.Identity;
using Meridian.Reporting;
using Meridian.QuantScript;
using Meridian.QuantScript.Api;
using Meridian.QuantScript.Compilation;
using Meridian.QuantScript.Plotting;
using Meridian.Storage;
using Meridian.Storage.SecurityMaster;
using Meridian.Storage.Services;
using Meridian.Storage.Store;
using Meridian.Strategies.Interfaces;
using Meridian.Strategies.Services;
using Meridian.Strategies.Storage;
using Meridian.Ui.Services;
using Meridian.Ui.Services.DataQuality;
using Meridian.Ui.Services.Services;
using Meridian.Ui.Shared.Services;
using Meridian.Ui.Shared.Workflows;
using Meridian.Wpf.Contracts;
using Meridian.Wpf.Features;
using Meridian.Wpf.Services;
using Meridian.Wpf.ViewModels;
using Meridian.Wpf.Views;
using Meridian.Workflow.EnvironmentDesign;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using WpfServices = Meridian.Wpf.Services;

namespace Meridian.Wpf;

/// <summary>
/// Meridian WPF Application
/// Provides maximum stability through WPF (.NET 9) for Windows-only deployment.
/// </summary>
public partial class App : System.Windows.Application
{
    private static bool _isFirstRun;
    private static bool _isFixtureMode;

    /// <summary>Set when a startup guard refused this composition and shutdown has begun.</summary>
    private static bool _startupRefused;
    private static string[] _launchArgs = [];
    private IHost? _host;
    private ApiClientService? _apiClientService;

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
        WpfServices.LoggingService.Instance.LogInfo("WPF application startup beginning");
        try
        {
            _launchArgs = await DesktopLaunchTicketClient.ResolveAsync(e.Args);
        }
        catch (Exception ex)
        {
            WpfServices.LoggingService.Instance.LogError(
                $"Desktop launch ticket redemption failed: {ex.Message}");
            _launchArgs = e.Args
                .Where(arg => !arg.StartsWith("--launch-ticket=", StringComparison.OrdinalIgnoreCase) &&
                              !arg.StartsWith("--host=", StringComparison.OrdinalIgnoreCase))
                .ToArray();
        }

        var launchRequest = DesktopLaunchArguments.Parse(_launchArgs);
        if (launchRequest.HasActions && WpfServices.SingleInstanceService.TrySendArgsToPrimary(_launchArgs, timeoutMs: 5000))
        {
            Shutdown();
            return;
        }

        // Register the AppUserModelID before any window is shown so that the
        // Windows shell (taskbar, JumpList, toast activations) maps all
        // notifications back to this process identity.
        WpfServices.ToastNotificationService.SetAppUserModelId();
        Meridian.Ui.Services.ConfigService.DefaultPathResolver =
            () => WpfServices.FirstRunService.Instance.ConfigFilePath;
        Meridian.Application.UI.ConfigStore.DefaultPathResolver =
            () => WpfServices.FirstRunService.Instance.ConfigFilePath;

        // Enforce single instance: if another Meridian window is already running,
        // forward the launch args to it via named pipe and exit cleanly.
        if (!WpfServices.SingleInstanceService.Instance.TryAcquire())
        {
            WpfServices.SingleInstanceService.SendArgsToPrimary(_launchArgs);
            Shutdown();
            return;
        }

        // Detect fixture mode from --fixture arg or MDC_FIXTURE_MODE env var
        _isFixtureMode = DetectFixtureMode(_launchArgs);
        ApplyRenderModeOverrides();

        var includeDesktopConfiguration = true;
        try
        {
            var recoveryOutcome = await WpfServices.FirstRunService.Instance.EnsureConfigurationExistsAsync();
            WpfServices.LoggingService.Instance.LogInfo(
                "Desktop configuration preflight completed before host construction",
                ("Outcome", recoveryOutcome.ToString()));
        }
        catch (Exception ex) when (IsConfigurationStartupFailure(ex))
        {
            includeDesktopConfiguration = false;
            WpfServices.LoggingService.Instance.LogError(
                "Desktop configuration could not be recovered; starting with host defaults so configuration can be repaired in-app",
                ex);
        }

        // Configure the host with dependency injection
        try
        {
            _host = BuildDesktopHost(includeDesktopConfiguration);
        }
        catch (Exception ex) when (includeDesktopConfiguration && IsConfigurationStartupFailure(ex))
        {
            WpfServices.LoggingService.Instance.LogError(
                "Desktop configuration failed during host construction; retrying startup with host defaults",
                ex);
            _host = BuildDesktopHost(includeDesktopConfiguration: false);
        }
        WpfServices.LoggingService.Instance.LogInfo("WPF application host built");

        Services = _host.Services;
        var apiClientService = Services.InitializeDesktopApiClient();
        _apiClientService = apiClientService;
        Services.GetRequiredService<WpfServices.StrategyRunWorkspaceService>();

        // Desktop sign-out must also end the shared workstation API session so no request
        // can ride the old server cookies (mirrors LifecycleControlClient's subscription).
        Services.GetRequiredService<WpfServices.DesktopAuthenticationSession>().SignedOut +=
            (_, _) => _ = apiClientService.SignOutAsync();

        // Provide the DI container to NavigationService so it can resolve pages
        WpfServices.NavigationService.Instance.SetServiceProvider(Services);
        Services.GetRequiredService<WpfServices.ViewModelViewResolver>()
            .LogMissingViewModels(Services.GetRequiredService<Meridian.Wpf.Shell.Services.IShellPageRegistry>());

        // Handle unhandled exceptions gracefully
        DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += OnDomainUnhandledException;
        TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;

        ShutdownMode = ShutdownMode.OnExplicitShutdown;
        if (!ShowStartupWindow())
        {
            WpfServices.LoggingService.Instance.LogWarning("WPF startup cancelled before authentication completed");
            Shutdown();
            return;
        }

        // Create and show MainWindow from DI (replaces StartupUri)
        var mainWindow = Services.GetRequiredService<MainWindow>();
        Current.MainWindow = mainWindow;
        ShutdownMode = ShutdownMode.OnMainWindowClose;
        mainWindow.Show();
        WpfServices.LoggingService.Instance.LogInfo("WPF main window shown");
        mainWindow.ForceStartupWindowRecovery();

        // Begin listening for args forwarded from secondary instances as soon as
        // the main window exists so automation deep links do not race startup work.
        WpfServices.SingleInstanceService.Instance.StartListening();

        // Register taskbar jump list tasks.
        WpfServices.JumpListService.Instance.Register();

        // Fire-and-forget async initialization with proper exception handling
        await SafeOnStartupAsync();
        if (_startupRefused)
        {
            // Shutdown is already in flight; re-showing the window would put a usable shell in
            // front of the operator for however long teardown takes.
            return;
        }

        WpfServices.LoggingService.Instance.LogInfo("WPF async startup completed");
        EnsureMainWindowVisible(mainWindow);
        _ = RestoreMainWindowVisibilityAsync(mainWindow);
    }

    private static IHost BuildDesktopHost(bool includeDesktopConfiguration)
    {
        return Host.CreateDefaultBuilder()
            .ConfigureAppConfiguration(config =>
            {
                // The operator's desktop settings file is the source for host-level
                // sections (e.g. Connectivity:Probes); layer it over the process defaults.
                if (includeDesktopConfiguration)
                {
                    config.AddJsonFile(
                        Meridian.Contracts.Configuration.MeridianPathDefaults.GetDesktopConfigPath(),
                        optional: true,
                        reloadOnChange: false);
                }
            })
            .ConfigureServices((context, services) =>
            {
                ConfigureServices(services, context.Configuration);
            })
            .Build();
    }

    private static bool IsConfigurationStartupFailure(Exception exception)
    {
        for (var current = exception; current is not null; current = current.InnerException)
        {
            if (current is JsonException or FormatException or InvalidDataException or IOException or UnauthorizedAccessException)
                return true;
        }

        return false;
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

    private static void ApplyRenderModeOverrides()
    {
        var softwareRendering = Environment.GetEnvironmentVariable("MDC_WPF_SOFTWARE_RENDERING");
        if (!string.Equals(softwareRendering, "1", StringComparison.Ordinal) &&
            !string.Equals(softwareRendering, "true", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        System.Windows.Media.RenderOptions.ProcessRenderMode =
            System.Windows.Interop.RenderMode.SoftwareOnly;
        WpfServices.LoggingService.Instance.LogInfo("WPF software rendering enabled for automation capture");
    }

    private static bool ShowStartupWindow()
    {
        var startupWindow = Services.GetRequiredService<StartupWindow>();
        Current.MainWindow = startupWindow;
        WpfServices.LoggingService.Instance.LogInfo("WPF startup window shown");
        return startupWindow.ShowDialog() == true;
    }

    /// <summary>
    /// Configures services for dependency injection.
    /// C1: DI-first registration — services registered by interface where possible.
    /// Pages registered as transient for constructor injection via NavigationService.
    /// </summary>
    private static void ConfigureServices(IServiceCollection services, Microsoft.Extensions.Configuration.IConfiguration configuration)
    {
        // Unified persistence config must resolve before feature modules read the
        // per-domain connection-string variables.
        Meridian.Storage.MeridianDatabaseEnvironment.ApplyUnifiedDatabaseUrl();

        AddHostEnvironmentFallback(services);

        // Register shared desktop HttpClient configurations
        services.AddDesktopHttpClients();
        services.AddDesktopApiClient();

        // ILogger<T> infrastructure — must be first so all services can resolve loggers
        services.AddLogging();

        // Operator-tunable connectivity probe timing (bound from "Connectivity:Probes"),
        // consumed by SetupWizardService TCP reachability checks.
        services.AddSingleton(
            Microsoft.Extensions.Configuration.ConfigurationBinder.Get<Meridian.Contracts.Configuration.ConnectivityProbeOptions>(
                configuration.GetSection(Meridian.Contracts.Configuration.ConnectivityProbeOptions.SectionKey))
            ?? new Meridian.Contracts.Configuration.ConnectivityProbeOptions());

        // Shared API infrastructure
        services.AddSingleton<WpfServices.WpfRemoteWorkstationClient>();
        services.AddSingleton<IRemoteWorkstationClient>(sp => sp.GetRequiredService<WpfServices.WpfRemoteWorkstationClient>());

        // ── Fixture mode service (offline mock data) ────────────────────────
        services.AddSingleton<Meridian.Ui.Services.Services.FixtureDataService>(_ => Meridian.Ui.Services.Services.FixtureDataService.Instance);
        services.AddSingleton<Meridian.Ui.Services.Services.FixtureModeDetector>(_ => Meridian.Ui.Services.Services.FixtureModeDetector.Instance);

        // ── Core services (by interface + concrete type) ────────────────────
        services.AddSingleton<IConnectionService>(_ => WpfServices.ConnectionService.Instance);
        services.AddSingleton<WpfServices.ConnectionService>(_ => WpfServices.ConnectionService.Instance);

        services.AddSingleton<INavigationService>(_ => WpfServices.NavigationService.Instance);
        services.AddSingleton<WpfServices.NavigationService>(_ => WpfServices.NavigationService.Instance);

        services.AddSingleton<Meridian.Ui.Services.Contracts.ILoggingService>(_ => WpfServices.LoggingService.Instance);
        services.AddSingleton<WpfServices.LoggingService>(_ => WpfServices.LoggingService.Instance);

        services.AddSingleton<WpfServices.ConfigService>(_ => WpfServices.ConfigService.Instance);
        services.AddSingleton<WpfServices.ThemeService>(_ => WpfServices.ThemeService.Instance);
        services.AddSingleton<WpfServices.NotificationService>(_ => WpfServices.NotificationService.Instance);
        services.AddSingleton<WpfServices.KeyboardShortcutService>(_ => WpfServices.KeyboardShortcutService.Instance);
        services.AddSingleton<WpfServices.MessagingService>(_ => WpfServices.MessagingService.Instance);
        services.AddSingleton<WpfServices.ApiStatusService>();
        services.AddSingleton<IStatusService>(sp => sp.GetRequiredService<WpfServices.ApiStatusService>());
        services.AddSingleton<WpfServices.StatusService>(_ => WpfServices.StatusService.Instance);
        services.AddSingleton<WpfServices.FirstRunService>(_ => WpfServices.FirstRunService.Instance);
        services.AddSingleton<WpfServices.DemoTourService>(_ => WpfServices.DemoTourService.Instance);
        var identityDataRoot = Environment.GetEnvironmentVariable("MDC_DATA_ROOT") ??
                               ResolveLifecycleManifestDataRoot(AppContext.BaseDirectory) ??
                               configuration["DataRoot"] ??
                               Path.Combine(
                                   Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                                   "Meridian",
                                   "Data");
        services.AddSingleton<IUserAccountStore>(sp => new FileUserAccountStore(
            new Meridian.Storage.StorageOptions { RootPath = Path.GetFullPath(identityDataRoot) },
            sp.GetService<Microsoft.Extensions.Logging.ILogger<FileUserAccountStore>>()));
        services.AddSingleton<UserProfileRegistry>(sp => new UserProfileRegistry(
            roleProfileStore: null,
            accountStore: sp.GetRequiredService<IUserAccountStore>()));

        // W9-GOV-008 criterion 2. AccountingFeatureModule registers the unpartitioned in-memory
        // fund structure, and this desktop never calls AddWorkstationSharedServices, so the
        // multi-company refusal that guard exists for ran only for the browser workstation: the
        // desktop started and served one shared graph to operators across companies.
        //
        // Registered in the composition root rather than in that feature module, because the guard
        // reads IUserAccountStore -- registered here, a few lines up -- and the module is
        // constructed standalone by its own tests. Putting it there made resolving IHostedService
        // throw for want of an account store the module never owned.
        services.AddHostedService<Meridian.Ui.Shared.Services.InMemoryFundStructureTenancyGuard>();
        services.AddSingleton<LoginSessionService>();
        services.AddSingleton<WpfServices.DesktopAuthenticationSession>();
        services.AddTransient<StartupWindowViewModel>();
        services.AddTransient<StartupWindow>();

        // ── Onboarding / workspace services ──────────────────────────────────
        services.AddSingleton<Meridian.Ui.Services.OnboardingTourService>(_ => Meridian.Ui.Services.OnboardingTourService.Instance);
        services.AddSingleton(sp =>
        {
            var workspaceService = WpfServices.WorkspaceService.Instance;
            workspaceService.SetServiceScopeFactory(sp.GetRequiredService<IServiceScopeFactory>());
            return workspaceService;
        });
        services.AddSingleton<Meridian.Ui.Services.AlertService>(_ => Meridian.Ui.Services.AlertService.Instance);
        services.AddSingleton<WpfServices.FundContextService>(_ => WpfServices.FundContextService.Instance);
        services.AddSingleton<WpfServices.IFundProfileCatalog>(sp => sp.GetRequiredService<WpfServices.FundContextService>());
        services.AddSingleton<WpfServices.WorkstationOperatingContextService>();
        services.AddSingleton<WpfServices.WorkspaceShellContextService>();
        services.AddSingleton<Meridian.Application.UI.ConfigStore>();

        // ── Domain / feature services ───────────────────────────────────────
        services.AddSingleton<WpfServices.BackendServiceManager>(_ => WpfServices.BackendServiceManager.Instance);
        services.AddSingleton<WpfServices.WatchlistService>(_ => WpfServices.WatchlistService.Instance);
        services.AddSingleton<WpfServices.IWatchlistReader>(sp => sp.GetRequiredService<WpfServices.WatchlistService>());
        services.AddSingleton<WpfServices.ArchiveHealthService>(_ => WpfServices.ArchiveHealthService.Instance);
        services.AddSingleton<WpfServices.SchemaService>(_ => WpfServices.SchemaService.Instance);
        // Strategy workspace services register through StrategyFeatureModule.

        // ── Background / infrastructure services ────────────────────────────
        services.AddSingleton<WpfServices.BackgroundTaskSchedulerService>(_ => WpfServices.BackgroundTaskSchedulerService.Instance);
        services.AddSingleton<WpfServices.OfflineTrackingPersistenceService>(_ => WpfServices.OfflineTrackingPersistenceService.Instance);
        services.AddSingleton<WpfServices.PendingOperationsQueueService>(_ => WpfServices.PendingOperationsQueueService.Instance);
        services.AddSingleton<WpfServices.ToastNotificationService>(_ => WpfServices.ToastNotificationService.Instance);
        services.AddSingleton<WpfServices.TaskbarProgressService>(_ => WpfServices.TaskbarProgressService.Instance);
        services.AddSingleton<WpfServices.TearOffPanelService>(_ => WpfServices.TearOffPanelService.Instance);
        // C1 fix: register a single SystemTrayService instance under the interface contract;
        //         the concrete type is resolved via the same singleton.
        services.AddSingleton<WpfServices.SystemTrayService>();
        services.AddSingleton<WpfServices.ISystemTrayService>(sp => sp.GetRequiredService<WpfServices.SystemTrayService>());

        // ── MainWindow ──────────────────────────────────────────────────────
        services.AddSingleton<Meridian.Wpf.ViewModels.MainWindowViewModel>();
        services.AddSingleton<MainWindow>();

        // ── Catalog-driven WPF shell pages and shell services ───────────────
        services.AddMeridianWpfShell(configuration);

        // ── Temporary legacy registrations not yet feature-owned ─────────────
        services.AddTransient<FundProfileSelectionPage>();
        services.AddTransient<Meridian.Wpf.ViewModels.FundProfileSelectionViewModel>();

        // ── Plugin loader service ────────────────────────────────────────────
        services.AddSingleton<Meridian.Infrastructure.DataSources.DataSourceRegistry>();
        services.AddSingleton<Meridian.ProviderSdk.IPluginLoaderService,
                              Meridian.ProviderSdk.PluginLoaderService>();
    }

    internal static string? ResolveLifecycleManifestDataRoot(string appBaseDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(appBaseDirectory);
        var baseDirectory = Path.GetFullPath(appBaseDirectory);
        var installRoots = new[]
        {
            baseDirectory,
            Directory.GetParent(baseDirectory)?.FullName
        }.Where(path => !string.IsNullOrWhiteSpace(path)).Distinct(StringComparer.OrdinalIgnoreCase);

        foreach (var installRoot in installRoots)
        {
            var manifestPath = Path.Combine(installRoot!, "service", "lifecycle-supervisor.json");
            if (!File.Exists(manifestPath))
                continue;
            try
            {
                var manifest = JsonSerializer.Deserialize(
                    File.ReadAllText(manifestPath),
                    LifecycleContractsJsonContext.Default.LifecycleSupervisorManifestDto);
                if (string.IsNullOrWhiteSpace(manifest?.DataRoot))
                    continue;
                var expanded = Environment.ExpandEnvironmentVariables(manifest.DataRoot);
                return Path.GetFullPath(
                    Path.IsPathRooted(expanded)
                        ? expanded
                        : Path.Combine(installRoot!, expanded));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or ArgumentException)
            {
                WpfServices.LoggingService.Instance.LogWarning(
                    "Could not resolve lifecycle supervisor data root for desktop authentication",
                    ("ManifestPath", manifestPath),
                    ("Error", ex.GetType().Name));
            }
        }

        return null;
    }

    private static void AddHostEnvironmentFallback(IServiceCollection services)
    {
        if (services.Any(static descriptor => descriptor.ServiceType == typeof(IHostEnvironment)))
        {
            return;
        }

        services.AddSingleton<IHostEnvironment>(_ => new DesktopFallbackHostEnvironment());
    }

    private sealed class DesktopFallbackHostEnvironment : IHostEnvironment
    {
        public string EnvironmentName { get; set; } =
            Environment.GetEnvironmentVariable("DOTNET_ENVIRONMENT") ??
            Environments.Production;

        public string ApplicationName { get; set; } = "Meridian.Desktop";

        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;

        public IFileProvider ContentRootFileProvider { get; set; } =
            new PhysicalFileProvider(AppContext.BaseDirectory);
    }

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

            // Start hosted services registered through shared composition, including
            // database-backed projection, outbox, and worker services.
            await StartHostServicesAsync(ct);

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

            // Notify if running in fixture/demo mode
            if (_isFixtureMode)
            {
                WpfServices.LoggingService.Instance.LogInfo("Running in demo data mode using offline sample data");
                await WpfServices.NotificationService.Instance.NotifyAsync(
                    "Demo Data Active",
                    "Application is using sample data for offline review",
                    NotificationType.Info);
            }

            // Log successful startup
            WpfServices.LoggingService.Instance.LogInfo("Application started successfully");
        }
        catch (Exception ex) when (Meridian.Ui.Shared.Services.HostStartupEscalation.IsRefusal(ex))
        {
            // A guard refused this composition. Every other startup fault below is recoverable
            // enough to carry on with a degraded shell; this one is not, because carrying on is
            // precisely what the guard forbade. Reported through a modal dialog rather than the
            // notification service: a toast on an application that is closing is not seen, and the
            // operator needs the remediation text.
            _startupRefused = true;
            WpfServices.LoggingService.Instance.LogError(
                "Application startup refused by a startup guard; shutting down", ex);

            // The guard's own message, not the wrapper's. A refusal reaching here inside an
            // AggregateException would otherwise put "One or more errors occurred" in front of the
            // operator instead of the remediation text, which is the one thing the dialog is for.
            // The outer exception is still what gets logged, because it carries the context.
            var refusal = Meridian.Ui.Shared.Services.HostStartupEscalation.TryFindRefusal(ex);
            System.Windows.MessageBox.Show(
                refusal?.Message ?? ex.Message,
                "Meridian cannot start",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            Shutdown();
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

    private async Task StartHostServicesAsync(CancellationToken ct = default)
    {
        if (_host is null)
        {
            return;
        }

        try
        {
            await _host.StartAsync(ct).ConfigureAwait(false);
            WpfServices.LoggingService.Instance.LogInfo("WPF hosted services started");
        }
        catch (Exception ex) when (Meridian.Ui.Shared.Services.HostStartupEscalation.IsRefusal(ex))
        {
            // Deliberately ahead of the tolerant catch below, and deliberately not swallowed. The
            // catch that follows exists for a worker that could not reach its database — degrading
            // to reduced processing beats taking the desktop down over it. A startup guard is the
            // opposite: it has decided this composition must not serve anything. Continuing past
            // one runs exactly the posture the guard was written to reject, which is how the
            // W9-GOV-008 multi-company refusal came to have no effect on this lane at all.
            throw;
        }
        catch (Exception ex)
        {
            WpfServices.LoggingService.Instance.LogWarning(
                "WPF hosted services failed to start; continuing with reduced database-backed worker processing",
                ("Error", ex.Message));
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
        catch (Exception)
        {
            WpfServices.LoggingService.Instance.LogWarning(
                "Offline tracking persistence failed to initialize; continuing without crash recovery persistence");
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
        catch (Exception)
        {
            WpfServices.LoggingService.Instance.LogWarning(
                "Background services failed to initialize; continuing with reduced background processing");
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
        catch (Exception)
        {
            WpfServices.LoggingService.Instance.LogWarning(
                "Workspace session restore failed; continuing with default workspace");
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

            await workspaceService.SaveSessionStateAsync(session, ct: ct).ConfigureAwait(false);
        }
        catch (Exception)
        {
            WpfServices.LoggingService.Instance.LogWarning(
                "Workspace session save failed during shutdown");
        }
    }

    private static void EnsureMainWindowVisible(Window window)
    {
        if (window is MainWindow mainWindow)
        {
            mainWindow.ForceStartupWindowRecovery();
            return;
        }

        if (window.WindowState == WindowState.Minimized)
        {
            window.WindowState = WindowState.Normal;
        }

        if (!window.ShowInTaskbar)
        {
            window.ShowInTaskbar = true;
        }

        if (!window.IsVisible)
        {
            window.Show();
        }

        window.Activate();
    }

    private static async Task RestoreMainWindowVisibilityAsync(Window window)
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

            if (Current is null || Current.Dispatcher.HasShutdownStarted)
            {
                return;
            }

            await Current.Dispatcher.InvokeAsync(() => EnsureMainWindowVisible(window), DispatcherPriority.ApplicationIdle);
        }
    }

    /// <summary>
    /// Handles app exit for clean shutdown of background services with timeout.
    /// </summary>
    private void OnExit(object sender, ExitEventArgs e)
    {
        WpfServices.LoggingService.Instance.LogInfo("WPF application shutdown requested");
        SafeOnExitAsync().GetAwaiter().GetResult();
        StopHostSafely();
        _apiClientService?.Dispose();
        _apiClientService = null;
        WpfServices.SingleInstanceService.Instance.Dispose();
        WpfServices.LoggingService.Instance.LogInfo("WPF application shutdown complete");
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
            await SaveWorkspaceSessionAsync(cts.Token).ConfigureAwait(false);

            // Shutdown services in parallel with timeout for better performance
            var shutdownTasks = new[]
            {
                ShutdownServiceAsync(() => WpfServices.BackgroundTaskSchedulerService.Instance.StopAsync(cts.Token), "BackgroundTaskScheduler", cts.Token),
                ShutdownServiceAsync(() => WpfServices.PendingOperationsQueueService.Instance.ShutdownAsync(), "PendingOperationsQueue", cts.Token),
                ShutdownServiceAsync(() => WpfServices.OfflineTrackingPersistenceService.Instance.ShutdownAsync(), "OfflineTrackingPersistence", cts.Token),
                ShutdownServiceAsync(() => WpfServices.ConnectionService.Instance.StopMonitoring(), "ConnectionService", cts.Token)
            };

            await Task.WhenAll(shutdownTasks).ConfigureAwait(false);

            // Dispose the NotifyIcon so the system-tray icon is removed cleanly.
            try
            {
                WpfServices.ToastNotificationService.Instance.Dispose();
            }
            catch (Exception ex)
            {
                WpfServices.LoggingService.Instance.LogWarning(
                    "Failed to dispose toast notification service during shutdown",
                    ("Error", ex.Message));
            }

        }
        catch (OperationCanceledException ex)
        {
            WpfServices.LoggingService.Instance.LogWarning(
                "WPF shutdown timed out",
                ("Error", ex.Message));
        }
        catch (Exception ex)
        {
            WpfServices.LoggingService.Instance.LogError("WPF shutdown failed", ex);
        }
    }

    private void StopHostSafely()
    {
        var host = _host;
        _host = null;
        if (host is null)
        {
            return;
        }

        const int HostShutdownTimeoutMs = 5000;
        try
        {
            using var cts = new CancellationTokenSource(HostShutdownTimeoutMs);
            host.StopAsync(cts.Token).ConfigureAwait(false).GetAwaiter().GetResult();
        }
        catch (OperationCanceledException)
        {
            WpfServices.LoggingService.Instance.LogWarning(
                "WPF host stop timed out",
                ("TimeoutMs", HostShutdownTimeoutMs.ToString()));
        }
        catch (Exception ex)
        {
            WpfServices.LoggingService.Instance.LogError("WPF host stop failed", ex);
        }

        try
        {
            if (host is IAsyncDisposable asyncHost)
            {
                asyncHost.DisposeAsync().AsTask().ConfigureAwait(false).GetAwaiter().GetResult();
            }
            else
            {
                host.Dispose();
            }
            WpfServices.LoggingService.Instance.LogInfo("WPF host disposed");
        }
        catch (Exception ex)
        {
            WpfServices.LoggingService.Instance.LogError("WPF host dispose failed", ex);
        }
    }

    /// <summary>
    /// Helper method to shutdown a service with proper error handling.
    /// </summary>
    private static async Task ShutdownServiceAsync(Func<Task> shutdownAction, string serviceName, CancellationToken ct)
    {
        try
        {
            await shutdownAction().WaitAsync(ct).ConfigureAwait(false);
            WpfServices.LoggingService.Instance.LogInfo(
                "WPF shutdown service completed",
                ("Service", serviceName));
        }
        catch (OperationCanceledException ex)
        {
            WpfServices.LoggingService.Instance.LogWarning(
                "WPF shutdown service timed out",
                ("Service", serviceName),
                ("Error", ex.Message));
        }
        catch (Exception ex)
        {
            WpfServices.LoggingService.Instance.LogError(
                $"WPF shutdown service failed: {serviceName}",
                ex);
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
                WpfServices.LoggingService.Instance.LogWarning(
                    "WPF shutdown service timed out",
                    ("Service", serviceName));
            }
            catch (Exception ex)
            {
                WpfServices.LoggingService.Instance.LogError(
                    $"WPF shutdown service failed: {serviceName}",
                    ex);
            }
        }, ct).ConfigureAwait(false);
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
            var provisioningResult = await WpfServices.FirstRunService.Instance.EnsureConfigurationExistsAsync();
            WpfServices.LoggingService.Instance.LogInfo(
                "Configuration presence verification finished before validation",
                ("Outcome", provisioningResult.ToString()));

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
                 System.IO.IOException or
                 UnauthorizedAccessException or
                 JsonException;

        // Always log with structured logging so the error is visible in the log file.
        WpfServices.LoggingService.Instance.LogError("Dispatcher unhandled exception", ex);

        if (isRecoverable)
        {
            e.Handled = true;
            try
            {
                _ = WpfServices.NotificationService.Instance.NotifyErrorAsync(
                    "Workstation needs attention",
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
