using System.Text.Json;
using Meridian.Application.Backfill;
using Meridian.Application.Commands;
using Meridian.Application.Config;
using Meridian.Application.Coordination;
using Meridian.Application.Logging;
using Meridian.Application.Monitoring;
using Meridian.Application.Pipeline;
using Meridian.Application.ResultTypes;
using Meridian.Application.Services;
using Meridian.Application.Subscriptions;
using Meridian.Application.Subscriptions.Services;
using Meridian.Application.UI;
using Meridian.Contracts.Domain.Enums;
using Meridian.Contracts.Domain.Models;
using Meridian.Domain.Collectors;
using Meridian.Domain.Events;
using Meridian.Domain.Models;
using Meridian.Infrastructure;
using Meridian.Infrastructure.Adapters.Core;
using Meridian.Infrastructure.Adapters.Failover;
using Meridian.Infrastructure.Contracts;
using Meridian.Storage;
using Meridian.Storage.Policies;
using Meridian.Storage.Replay;
using Meridian.Storage.Services;
using Serilog;
using BackfillRequest = Meridian.Application.Backfill.BackfillRequest;
using DeploymentContext = Meridian.Application.Config.DeploymentContext;
using DeploymentMode = Meridian.Application.Config.DeploymentMode;

namespace Meridian.Application.Composition.Startup;

/// <summary>
/// Factory delegate for creating the host-specific dashboard server implementation.
/// </summary>
public delegate IHostDashboardServer DashboardServerFactory(string configPath, int port);

/// <summary>
/// Host-agnostic abstraction over the dashboard server used by shared startup orchestration.
/// </summary>
public interface IHostDashboardServer : IAsyncDisposable
{
    Task StartAsync(CancellationToken ct = default);
    Task StopAsync(CancellationToken ct = default);
}

/// <summary>
/// Shared bootstrap entry point for the console host.
/// Keeps <c>Program.cs</c> thin while reusing the same startup flow as the shared composition layer.
/// </summary>
public static class SharedStartupBootstrapper
{
    public static Task<int> RunAsync(string[] args, DashboardServerFactory dashboardServerFactory, CancellationToken ct = default)
    {
        var cliArgs = CliArguments.Parse(args);
        var cfgPath = SharedStartupHelpers.ResolveConfigPath(cliArgs);
        var initialCfg = SharedStartupHelpers.LoadConfigMinimal(cfgPath);

        LoggingSetup.Initialize(dataRoot: initialCfg.DataRoot);
        var log = LoggingSetup.ForContext("Program");
        var deploymentContext = SharedStartupHelpers.ResolveDeployment(args, cfgPath);

        log.Debug("Deployment context: {Mode}, Command: {Command}, Docker: {IsDocker}",
            deploymentContext.Mode, deploymentContext.Command, deploymentContext.IsDocker);

        return RunCoreAsync(cliArgs, cfgPath, log, deploymentContext, dashboardServerFactory, ct);
    }

    private static async Task<int> RunCoreAsync(
        CliArguments cliArgs,
        string cfgPath,
        ILogger log,
        DeploymentContext deploymentContext,
        DashboardServerFactory dashboardServerFactory,
        CancellationToken ct)
    {
        await using var configService = new ConfigurationService(log);
        var cfg = configService.LoadAndPrepareConfig(cfgPath);

        try
        {
            var orchestrator = new HostModeOrchestrator(log, dashboardServerFactory);
            return await orchestrator.RunAsync(cliArgs, cfg, cfgPath, configService, deploymentContext, ct);
        }
        catch (Exception ex)
        {
            var errorCode = ErrorCodeExtensions.FromException(ex);
            var friendlyError = FriendlyErrorFormatter.Format(ex);

            FriendlyErrorFormatter.DisplayError(friendlyError);
            log.Fatal(ex, "Meridian terminated unexpectedly (ErrorCode={ErrorCode}, ExitCode={ExitCode})",
                errorCode, errorCode.ToExitCode());

            return errorCode.ToExitCode();
        }
        finally
        {
            LoggingSetup.CloseAndFlush();
        }
    }
}

/// <summary>
/// Shared startup helpers extracted from the console entry point so all hosts can reuse the same rules.
/// </summary>
public static class SharedStartupHelpers
{
    private const string DefaultConfigFileName = "appsettings.json";
    private const string ConfigDirectoryDefaultConfigFileName = "config/appsettings.json";
    private const string ConfigPathEnvVar = "MDC_CONFIG_PATH";

    /// <summary>
    /// Resolves the configuration file path from CLI arguments, environment variables, or defaults.
    /// Priority: <c>--config</c> argument &gt; <c>MDC_CONFIG_PATH</c> env var &gt; <c>appsettings.json</c>.
    /// </summary>
    public static string ResolveConfigPath(CliArguments cliArgs)
    {
        if (!string.IsNullOrWhiteSpace(cliArgs.ConfigPath))
            return cliArgs.ConfigPath;

        var envValue = Environment.GetEnvironmentVariable(ConfigPathEnvVar);
        if (!string.IsNullOrWhiteSpace(envValue))
            return envValue;

        return ResolveDefaultConfigPath();
    }

    /// <summary>
    /// Performs a minimal configuration load so logging can be initialized before the full startup path runs.
    /// </summary>
    public static AppConfig LoadConfigMinimal(string path)
    {
        try
        {
            if (!File.Exists(path))
            {
                Console.Error.WriteLine($"[Warning] Configuration file not found: {path}");
                Console.Error.WriteLine("Using default configuration. Copy appsettings.sample.json to appsettings.json to customize.");
                return new AppConfig();
            }

            var json = File.ReadAllText(path);
            var cfg = JsonSerializer.Deserialize<AppConfig>(json, AppConfigJsonOptions.Read);
            return cfg ?? new AppConfig();
        }
        catch (JsonException ex)
        {
            Console.Error.WriteLine($"[Error] Invalid JSON in configuration file: {path}");
            Console.Error.WriteLine($"  Error: {ex.Message}");
            Console.Error.WriteLine("  Troubleshooting:");
            Console.Error.WriteLine("    1. Validate JSON syntax at jsonlint.com");
            Console.Error.WriteLine("    2. Check for trailing commas or missing quotes");
            Console.Error.WriteLine("    3. Compare against appsettings.sample.json");
            Console.Error.WriteLine("    4. Run: dotnet user-secrets init (for sensitive data)");
            return new AppConfig();
        }
        catch (UnauthorizedAccessException)
        {
            throw new Application.Exceptions.ConfigurationException(
                $"Access denied reading configuration file: {path}. Check file permissions.",
                path, null);
        }
        catch (IOException ex)
        {
            throw new Application.Exceptions.ConfigurationException(
                $"I/O error reading configuration file: {path}. {ex.Message}",
                path, null);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[Error] Failed to load configuration: {ex.Message}");
            Console.Error.WriteLine("Using default configuration.");
            Console.Error.WriteLine("For detailed help, see HELP.md or run with --help");
            return new AppConfig();
        }
    }

    /// <summary>
    /// Resolves deployment and mode selection using the shared deployment context rules.
    /// </summary>
    public static DeploymentContext ResolveDeployment(string[] args, string configPath)
        => DeploymentContext.FromArgs(args, configPath);

    /// <summary>
    /// Applies a default symbol subscription when the runtime configuration omits all symbols.
    /// </summary>
    public static AppConfig EnsureDefaultSymbols(AppConfig cfg)
    {
        if (cfg.Symbols is { Length: > 0 })
            return cfg;

        var fallback = new[] { new SymbolConfig("SPY", SubscribeTrades: true, SubscribeDepth: true, DepthLevels: 10) };
        return cfg with { Symbols = fallback };
    }

    /// <summary>
    /// Builds the backfill request from configuration plus CLI overrides.
    /// </summary>
    public static BackfillRequest BuildBackfillRequest(AppConfig cfg, CliArguments cliArgs)
    {
        var baseRequest = BackfillRequest.FromConfig(cfg);
        var provider = cliArgs.BackfillProvider ?? baseRequest.Provider;
        var symbols = !string.IsNullOrWhiteSpace(cliArgs.BackfillSymbols)
            ? cliArgs.BackfillSymbols.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            : baseRequest.Symbols;
        var from = ParseDate(cliArgs.BackfillFrom) ?? baseRequest.From;
        var to = ParseDate(cliArgs.BackfillTo) ?? baseRequest.To;

        return new BackfillRequest(provider, symbols.ToArray(), from, to);
    }

    private static DateOnly? ParseDate(string? value)
        => DateOnly.TryParse(value, out var date) ? date : null;

    private static string ResolveDefaultConfigPath()
    {
        if (File.Exists(ConfigDirectoryDefaultConfigFileName))
            return ConfigDirectoryDefaultConfigFileName;

        return DefaultConfigFileName;
    }
}

internal sealed record CommandDispatchPlan(CommandDispatcher Dispatcher);

internal static class CommandDispatchPlanner
{
    public static CommandDispatchPlan Create(
        AppConfig cfg,
        string cfgPath,
        ILogger log,
        ConfigurationService configService)
    {
        var symbolService = new SymbolManagementService(new ConfigStore(cfgPath), cfg.DataRoot, log);
        var storageSearchService = new StorageSearchService(
            cfg.Storage?.ToStorageOptions(cfg.DataRoot, cfg.Compress ?? false)
            ?? new StorageOptions { RootPath = cfg.DataRoot });

        return new CommandDispatchPlan(new CommandDispatcher(
            new HelpCommand(),
            new ConfigCommands(configService, log),
            new DiagnosticsCommands(cfg, cfgPath, configService, log),
            new SchemaCheckCommand(cfg, log),
            new SymbolCommands(symbolService, log),
            new ValidateConfigCommand(configService, cfgPath, log),
            new DryRunCommand(cfg, configService, log),
            new SelfTestCommand(log),
            new PackageCommands(cfg, log),
            new ConfigPresetCommand(new AutoConfigurationService(), log),
            new QueryCommand(new HistoricalDataQueryService(cfg.DataRoot), log),
            new CatalogCommand(storageSearchService, log),
            new GenerateLoaderCommand(cfg.DataRoot, log),
            new WalRepairCommand(cfg, log)));
    }
}

internal static class StartupValidationRunner
{
    public static int? ValidateConfiguration(AppConfig cfg, ConfigurationService configService, ILogger log)
    {
        if (configService.ValidateConfig(cfg, out _))
        {
            return null;
        }

        log.Error("Exiting due to configuration errors (ExitCode={ExitCode})",
            ErrorCode.ConfigurationInvalid.ToExitCode());
        return ErrorCode.ConfigurationInvalid.ToExitCode();
    }

    public static int? EnsureDataDirectoryPermissions(AppConfig cfg, ILogger log)
    {
        var permissionsService = new FilePermissionsService(new FilePermissionsOptions
        {
            DirectoryMode = "755",
            FileMode = "644",
            ValidateOnStartup = true
        });

        var permissionsResult = permissionsService.EnsureDirectoryPermissions(cfg.DataRoot);
        if (permissionsResult.Success)
        {
            log.Information("Data directory permissions configured: {Message}", permissionsResult.Message);
            return null;
        }

        log.Error("Failed to configure data directory permissions: {Message} (ExitCode={ExitCode}). " +
            "Troubleshooting: 1) Check that the application has write access to the parent directory. " +
            "2) On Linux/macOS, ensure the user has appropriate permissions. " +
            "3) On Windows, run as administrator if needed.",
            permissionsResult.Message, ErrorCode.FileAccessDenied.ToExitCode());
        return ErrorCode.FileAccessDenied.ToExitCode();
    }

    public static async Task<int?> ValidateSchemasAsync(
        CliArguments cliArgs,
        AppConfig cfg,
        ILogger log,
        CancellationToken ct = default)
    {
        if (!cliArgs.ValidateSchemas)
        {
            return null;
        }

        log.Information("Running startup schema compatibility check...");
        await using var schemaService = new SchemaValidationService(
            new SchemaValidationOptions { EnableVersionTracking = true },
            cfg.DataRoot);

        var schemaCheckResult = await schemaService.PerformStartupCheckAsync(ct);
        if (schemaCheckResult.Success)
        {
            log.Information("Schema compatibility check passed: {Message}", schemaCheckResult.Message);
            return null;
        }

        log.Warning("Schema compatibility check found issues: {Message}", schemaCheckResult.Message);
        if (!cliArgs.StrictSchemas)
        {
            return null;
        }

        log.Error("Exiting due to schema incompatibilities (--strict-schemas enabled, ExitCode={ExitCode})",
            ErrorCode.SchemaMismatch.ToExitCode());
        return ErrorCode.SchemaMismatch.ToExitCode();
    }
}

/// <summary>
/// Shared startup orchestrator that dispatches commands and runs the appropriate host mode using <see cref="HostStartupFactory"/>.
/// </summary>
public sealed class HostModeOrchestrator
{
    private readonly ILogger _log;
    private readonly DashboardServerFactory _dashboardServerFactory;

    public HostModeOrchestrator(ILogger log, DashboardServerFactory dashboardServerFactory)
    {
        _log = log;
        _dashboardServerFactory = dashboardServerFactory;
    }

    public async Task<int> RunAsync(
        CliArguments cliArgs,
        AppConfig cfg,
        string cfgPath,
        ConfigurationService configService,
        DeploymentContext deployment,
        CancellationToken ct = default)
    {
        var commandPlan = CommandDispatchPlanner.Create(cfg, cfgPath, _log, configService);
        var (handled, cliResult) = await commandPlan.Dispatcher.TryDispatchAsync(cliArgs.Raw, ct);
        if (handled)
        {
            return cliResult.ExitCode;
        }

        if (deployment.Mode == DeploymentMode.Web)
        {
            return await RunWebDashboardAsync(cfgPath, deployment, ct);
        }

        var configValidationExitCode = StartupValidationRunner.ValidateConfiguration(cfg, configService, _log);
        if (configValidationExitCode.HasValue)
        {
            return configValidationExitCode.Value;
        }

        var permissionsExitCode = StartupValidationRunner.EnsureDataDirectoryPermissions(cfg, _log);
        if (permissionsExitCode.HasValue)
        {
            return permissionsExitCode.Value;
        }

        var schemaExitCode = await StartupValidationRunner.ValidateSchemasAsync(cliArgs, cfg, _log, ct);
        if (schemaExitCode.HasValue)
        {
            return schemaExitCode.Value;
        }

        return await RunCollectorAsync(cliArgs, cfg, cfgPath, configService, deployment, ct);
    }

    private async Task<int> RunWebDashboardAsync(
        string cfgPath,
        DeploymentContext deployment,
        CancellationToken ct)
    {
        _log.Information("Starting web dashboard ({ModeDescription})...", deployment.ModeDescription);

        await using var webServer = _dashboardServerFactory(cfgPath, deployment.HttpPort);
        await webServer.StartAsync(ct);

        _log.Information("Web dashboard started at http://localhost:{Port}", deployment.HttpPort);
        Console.WriteLine($"Web dashboard running at http://localhost:{deployment.HttpPort}");
        Console.WriteLine("Press Ctrl+C to stop...");

        var done = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        ConsoleCancelEventHandler handler = (_, e) =>
        {
            e.Cancel = true;
            _log.Information("Shutdown requested");
            done.TrySetResult();
        };

        Console.CancelKeyPress += handler;
        try
        {
            using var registration = ct.Register(() => done.TrySetCanceled(ct));
            try
            {
                await done.Task;
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                _log.Information("Web dashboard shutdown requested via cancellation token");
            }
        }
        finally
        {
            Console.CancelKeyPress -= handler;
        }

        var shutdownToken = ct.IsCancellationRequested ? CancellationToken.None : ct;
        _log.Information("Stopping web dashboard...");
        await webServer.StopAsync(shutdownToken);
        _log.Information("Web dashboard stopped");
        return 0;
    }

    private async Task<int> RunCollectorAsync(
        CliArguments cliArgs,
        AppConfig cfg,
        string cfgPath,
        ConfigurationService configService,
        DeploymentContext deployment,
        CancellationToken ct)
    {
        var statusPath = Path.Combine(cfg.DataRoot, "_status", "status.json");
        await using var statusWriter = new StatusWriter(statusPath, () => configService.LoadAndPrepareConfig(cfgPath));
        ConfigWatcher? watcher = null;
        IHostDashboardServer? uiServer = null;

        if (deployment.Mode == DeploymentMode.Desktop)
        {
            _log.Information("Desktop mode: starting UI server ({ModeDescription})...", deployment.ModeDescription);
            uiServer = _dashboardServerFactory(cfgPath, deployment.HttpPort);
            await uiServer.StartAsync(ct);
            _log.Information("Desktop mode UI server started at http://localhost:{Port}", deployment.HttpPort);
        }

        await using var hostStartup = HostStartupFactory.Create(deployment, cfgPath);
        var storageOpt = hostStartup.StorageOptions;
        var pipeline = hostStartup.Pipeline;

        await pipeline.RecoverAsync();
        _log.Information("WAL enabled for pipeline durability");

        var policy = hostStartup.GetRequiredService<JsonlStoragePolicy>();
        _log.Information("Storage path: {RootPath}", storageOpt.RootPath);
        _log.Information("Naming convention: {NamingConvention}", storageOpt.NamingConvention);
        _log.Information("Date partitioning: {DatePartition}", storageOpt.DatePartition);
        _log.Information("Compression: {CompressionEnabled}", storageOpt.Compress ? "enabled" : "disabled");
        _log.Debug("Example path: {ExamplePath}", policy.GetPathPreview());

        var backfillRequested = cliArgs.Backfill || (cfg.Backfill?.Enabled == true);
        if (backfillRequested)
        {
            return await RunBackfillAsync(cliArgs, cfg, cfgPath, pipeline, statusWriter, uiServer, ct);
        }

        var quoteCollector = hostStartup.GetRequiredService<QuoteCollector>();
        var tradeCollector = hostStartup.GetRequiredService<TradeDataCollector>();
        var depthCollector = hostStartup.GetRequiredService<MarketDepthCollector>();

        if (!string.IsNullOrWhiteSpace(cliArgs.Replay))
        {
            _log.Information("Replaying events from {ReplayPath}...", cliArgs.Replay);
            var replayer = new JsonlReplayer(cliArgs.Replay);
            await foreach (var evt in replayer.ReadEventsAsync(ct))
            {
                await pipeline.PublishAsync(evt);
            }

            await pipeline.FlushAsync();
            await statusWriter.WriteOnceAsync();
            return 0;
        }

        var providerRegistry = hostStartup.GetRequiredService<ProviderRegistry>();
        var failoverCfg = cfg.DataSources;
        var failoverRules = failoverCfg?.FailoverRules ?? Array.Empty<FailoverRuleConfig>();
        var useFailover = failoverCfg?.EnableFailover == true && failoverRules.Length > 0;

        ConnectionHealthMonitor? healthMonitor = null;
        StreamingFailoverService? failoverService = null;
        IMarketDataClient dataClient;

        if (useFailover)
        {
            _log.Information("Streaming failover enabled with {RuleCount} rules", failoverRules.Length);

            healthMonitor = new ConnectionHealthMonitor();
            failoverService = new StreamingFailoverService(healthMonitor);

            var rule = failoverRules[0];
            var providerMap = new Dictionary<string, IMarketDataClient>(StringComparer.OrdinalIgnoreCase);
            var allProviderIds = new[] { rule.PrimaryProviderId }
                .Concat(rule.BackupProviderIds)
                .Distinct(StringComparer.OrdinalIgnoreCase);

            var sources = failoverCfg!.Sources ?? Array.Empty<DataSourceConfig>();
            var providerIds = allProviderIds.ToList();
            var creationTasks = providerIds.Select(providerId =>
            {
                var source = sources.FirstOrDefault(s => string.Equals(s.Id, providerId, StringComparison.OrdinalIgnoreCase));
                var providerKind = source?.Provider ?? cfg.DataSource;
                return Task.Run(() =>
                {
                    try
                    {
                        var client = providerRegistry.CreateStreamingClient(providerKind);
                        return (providerId, client: (IMarketDataClient?)client, providerKind, error: (Exception?)null);
                    }
                    catch (Exception ex)
                    {
                        return (providerId, client: (IMarketDataClient?)null, providerKind, error: (Exception?)ex);
                    }
                }, ct);
            });

            var results = await Task.WhenAll(creationTasks);
            foreach (var (providerId, client, providerKind, error) in results)
            {
                if (client != null)
                {
                    providerMap[providerId] = client;
                    failoverService.RegisterProvider(providerId);
                    _log.Information("Created streaming client for failover provider {ProviderId} ({Kind})", providerId, providerKind);
                }
                else
                {
                    _log.Warning(error, "Failed to create streaming client for provider {ProviderId}; skipping", providerId);
                }
            }

            if (providerMap.Count == 0)
            {
                _log.Error("No streaming providers could be created for failover; falling back to single provider");
                dataClient = providerRegistry.CreateStreamingClient(cfg.DataSource);
            }
            else
            {
                var initialProvider = providerMap.ContainsKey(rule.PrimaryProviderId)
                    ? rule.PrimaryProviderId
                    : providerMap.Keys.First();

                dataClient = new FailoverAwareMarketDataClient(providerMap, failoverService, rule.Id, initialProvider);
                failoverService.Start(failoverCfg);
            }
        }
        else
        {
            dataClient = providerRegistry.CreateStreamingClient(cfg.DataSource);
        }

        await using var dataClientDisposable = dataClient;

        try
        {
            var leaseManager = hostStartup.GetService<ILeaseManager>();
            if (leaseManager is not null)
            {
                var coordinationSnapshot = await leaseManager.GetSnapshotAsync(ct);
                _log.Information(
                    "Coordination initialized: enabled={Enabled}, mode={Mode}, instance={InstanceId}, root={RootPath}",
                    coordinationSnapshot.Enabled,
                    coordinationSnapshot.Mode,
                    coordinationSnapshot.InstanceId,
                    coordinationSnapshot.RootPath);
            }

            using var connectTimeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            await dataClient.ConnectAsync(connectTimeoutCts.Token);
        }
        catch (OperationCanceledException)
        {
            _log.Error(
                "Connection to {DataSource} timed out after 30 seconds. " +
                "Check network connectivity, firewall rules, and provider credentials. " +
                "Use --dry-run to validate configuration without connecting.",
                cfg.DataSource);
            return ErrorCode.ConnectionTimeout.ToExitCode();
        }
        catch (Exception ex)
        {
            var errorCode = ErrorCodeExtensions.FromException(ex);
            if (errorCode == ErrorCode.Unknown)
            {
                errorCode = ErrorCode.ConnectionFailed;
            }

            _log.Error(ex, "Failed to connect to {DataSource} data provider (ErrorCode={ErrorCode}, ExitCode={ExitCode}). Check credentials and connectivity.",
                cfg.DataSource, errorCode, errorCode.ToExitCode());

            return errorCode.ToExitCode();
        }

        var subscriptionManager = hostStartup.CreateSubscriptionOrchestrator(dataClient, cfg.DataSource.ToString());
        var runtimeCfg = SharedStartupHelpers.EnsureDefaultSymbols(cfg);
        await subscriptionManager.ApplyAsync(runtimeCfg, ct);
        var symbols = runtimeCfg.Symbols ?? Array.Empty<SymbolConfig>();

        if (deployment.HotReloadEnabled)
        {
            watcher = configService.StartHotReload(cfgPath, newCfg =>
            {
                try
                {
                    var nextCfg = SharedStartupHelpers.EnsureDefaultSymbols(newCfg);
                    subscriptionManager.ApplyAsync(nextCfg).GetAwaiter().GetResult();
                    _ = statusWriter.WriteOnceAsync();
                    _log.Information("Applied hot-reloaded configuration: {Count} symbols", nextCfg.Symbols?.Length ?? 0);
                }
                catch (Exception ex)
                {
                    _log.Error(ex, "Failed to apply hot-reloaded configuration");
                }
            }, ex => _log.Error(ex, "Configuration watcher error"));

            _log.Information("Watching {ConfigPath} for subscription changes", cfgPath);
        }

        if (cliArgs.SimulateFeed)
        {
            var now = DateTimeOffset.UtcNow;
            var sym = symbols[0].Symbol;

            depthCollector.OnDepth(new MarketDepthUpdate(now, sym, 0, DepthOperation.Insert, OrderBookSide.Bid, 500.24m, 300m, "MM1"));
            depthCollector.OnDepth(new MarketDepthUpdate(now, sym, 0, DepthOperation.Insert, OrderBookSide.Ask, 500.26m, 250m, "MM2"));
            depthCollector.OnDepth(new MarketDepthUpdate(now, sym, 0, DepthOperation.Update, OrderBookSide.Bid, 500.24m, 350m, "MM1"));
            depthCollector.OnDepth(new MarketDepthUpdate(now, sym, 3, DepthOperation.Update, OrderBookSide.Ask, 500.30m, 100m, "MMX"));
            depthCollector.ResetSymbolStream(sym);
            depthCollector.OnDepth(new MarketDepthUpdate(now, sym, 0, DepthOperation.Insert, OrderBookSide.Bid, 500.20m, 100m, "MM3"));
            depthCollector.OnDepth(new MarketDepthUpdate(now, sym, 0, DepthOperation.Insert, OrderBookSide.Ask, 500.22m, 90m, "MM4"));

            tradeCollector.OnTrade(new MarketTradeUpdate(now, sym, 500.21m, 100, AggressorSide.Buy, SequenceNumber: 1, StreamId: "SIM", Venue: "TEST"));

            await Task.Delay(200, ct);
        }

        _log.Information("Wrote MarketEvents to {StoragePath}", storageOpt.RootPath);
        var pipelineMetrics = pipeline.EventMetrics;
        _log.Information("Metrics: published={Published}, integrity={Integrity}, dropped={Dropped}",
            pipelineMetrics.Published, pipelineMetrics.Integrity, pipelineMetrics.Dropped);

        _log.Information("Disconnecting from data provider...");
        await dataClient.DisconnectAsync();

        failoverService?.Dispose();
        healthMonitor?.Dispose();

        _log.Information("Shutdown complete");

        watcher?.Dispose();
        if (uiServer != null)
        {
            await uiServer.StopAsync(ct);
            await uiServer.DisposeAsync();
        }

        return 0;
    }

    private async Task<int> RunBackfillAsync(
        CliArguments cliArgs,
        AppConfig cfg,
        string cfgPath,
        EventPipeline pipeline,
        StatusWriter statusWriter,
        IHostDashboardServer? uiServer,
        CancellationToken ct)
    {
        var backfillRequest = SharedStartupHelpers.BuildBackfillRequest(cfg, cliArgs);

        await using var backfillHost = HostStartupFactory.CreateForBackfill(cfgPath);
        var backfillProviders = backfillHost.CreateBackfillProviders();

        IHistoricalDataProvider[] providersArray;
        var requestedProvider = backfillRequest.Provider?.Trim();
        var useCompositeProvider = (cfg.Backfill?.EnableFallback ?? true)
            && (string.IsNullOrWhiteSpace(requestedProvider)
                || string.Equals(requestedProvider, "composite", StringComparison.OrdinalIgnoreCase)
                || string.Equals(requestedProvider, "auto", StringComparison.OrdinalIgnoreCase));

        if (useCompositeProvider)
        {
            var composite = backfillHost.CreateCompositeBackfillProvider(backfillProviders);
            providersArray = [composite];
        }
        else
        {
            providersArray = backfillProviders.ToArray();
        }

        var backfill = new HistoricalBackfillService(providersArray, _log);
        var result = await backfill.RunAsync(backfillRequest, pipeline);
        var statusStore = BackfillStatusStore.FromConfig(cfg);
        await statusStore.WriteAsync(result);
        await pipeline.FlushAsync();
        await statusWriter.WriteOnceAsync();

        if (uiServer != null)
        {
            await uiServer.StopAsync(ct);
            await uiServer.DisposeAsync();
        }

        return result.Success ? 0 : ErrorCode.ProviderError.ToExitCode();
    }
}
