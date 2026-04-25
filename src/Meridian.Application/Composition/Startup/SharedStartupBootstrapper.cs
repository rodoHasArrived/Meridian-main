using System.Text.Json;
using Meridian.Application.Backfill;
using Meridian.Application.Commands;
using Meridian.Application.Composition.Startup.StartupModels;
using Meridian.Application.Config;
using Meridian.Application.Logging;
using Meridian.Application.Monitoring;
using Meridian.Application.ResultTypes;
using Meridian.Application.Services;
using Meridian.Application.Subscriptions.Services;
using Meridian.Application.UI;
using Meridian.Contracts.Configuration;
using Meridian.Infrastructure.Adapters.Core;
using Meridian.Storage;
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

        if (!string.IsNullOrWhiteSpace(deploymentContext.ModeResolutionError))
        {
            Console.Error.WriteLine(deploymentContext.ModeResolutionError);
            log.Error("Invalid deployment mode request: {ModeResolutionError}", deploymentContext.ModeResolutionError);
            LoggingSetup.CloseAndFlush();
            return Task.FromResult(1);
        }

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
                Console.Error.WriteLine("Using default configuration. Copy config/appsettings.sample.json to config/appsettings.json to customize.");
                return new AppConfig(DataRoot: MeridianPathDefaults.ResolveDataRoot(path, null));
            }

            var json = File.ReadAllText(path);
            var cfg = JsonSerializer.Deserialize<AppConfig>(json, AppConfigJsonOptions.Read);
            var configuredDataRoot = MeridianPathDefaults.ResolveConfiguredDataRootFromJson(json, cfg?.DataRoot);
            var resolvedDataRoot = MeridianPathDefaults.ResolveDataRoot(path, configuredDataRoot);
            return (cfg ?? new AppConfig()) with { DataRoot = resolvedDataRoot };
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
            return new AppConfig(DataRoot: MeridianPathDefaults.ResolveDataRoot(path, null));
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
            return new AppConfig(DataRoot: MeridianPathDefaults.ResolveDataRoot(path, null));
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
        var granularity = string.IsNullOrWhiteSpace(cliArgs.BackfillGranularity)
            ? baseRequest.Granularity
            : DataGranularityExtensions.TryParseValue(cliArgs.BackfillGranularity, out var parsedGranularity)
                ? parsedGranularity
                : throw new InvalidOperationException(
                    $"Unsupported backfill granularity '{cliArgs.BackfillGranularity}'. " +
                    "Use one of: Daily, Hourly, 1Min, 5Min, 15Min, 30Min, 4Hour.");

        return new BackfillRequest(provider, symbols.ToArray(), from, to, granularity);
    }

    private static DateOnly? ParseDate(string? value)
        => DateOnly.TryParse(value, out var date) ? date : null;

    private static string ResolveDefaultConfigPath()
        => DefaultConfigPathResolver.Resolve();
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
        var store = new ConfigStore(cfgPath);
        var dataRoot = store.GetDataRoot(cfg);
        var symbolService = new SymbolManagementService(store, dataRoot, log);
        var storageSearchService = new StorageSearchService(
            cfg.Storage?.ToStorageOptions(dataRoot, cfg.Compress ?? false)
            ?? new StorageOptions { RootPath = dataRoot });

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
            new EtlCommands(cfgPath, log),
            new ConfigPresetCommand(new AutoConfigurationService(), log),
            new QueryCommand(new HistoricalDataQueryService(dataRoot), log),
            new CatalogCommand(storageSearchService, log),
            new GenerateLoaderCommand(dataRoot, log),
            new WalRepairCommand(cfg, log),
            new ProviderCalibrationCommand(dataRoot, log),
            // Security Master ingest: importService is null until the full host configures Postgres.
            new SecurityMasterCommands(importService: null, log)));
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
/// <remarks>
/// Delegates to <see cref="StartupOrchestrator"/> which sequences phases and selects a mode runner.
/// This class is retained for backward compatibility with existing call sites and tests.
/// </remarks>
public sealed class HostModeOrchestrator
{
    private readonly ILogger _log;
    private readonly DashboardServerFactory _dashboardServerFactory;

    public HostModeOrchestrator(ILogger log, DashboardServerFactory dashboardServerFactory)
    {
        _log = log;
        _dashboardServerFactory = dashboardServerFactory;
    }

    public Task<int> RunAsync(
        CliArguments cliArgs,
        AppConfig cfg,
        string cfgPath,
        ConfigurationService configService,
        DeploymentContext deployment,
        CancellationToken ct = default)
    {
        var ctx = new StartupContext
        {
            CliArgs = cliArgs,
            Config = cfg,
            ConfigPath = cfgPath,
            Deployment = deployment,
            ConfigurationService = configService,
            DashboardServerFactory = _dashboardServerFactory,
            Log = _log,
            CancellationToken = ct
        };

        var orchestrator = new StartupOrchestrator(_log, _dashboardServerFactory);
        return orchestrator.RunAsync(ctx);
    }
}
