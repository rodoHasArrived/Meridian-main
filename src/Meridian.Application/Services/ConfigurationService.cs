using Meridian.Application.Config;
using Meridian.Application.Config.Credentials;
using Meridian.Application.Logging;
using Meridian.Application.ProviderRouting;
using Meridian.Application.UI;
using Meridian.Contracts.Configuration;
using Meridian.Infrastructure.Adapters.Alpaca;
using Meridian.Infrastructure.Adapters.AlphaVantage;
using Meridian.Infrastructure.Adapters.Finnhub;
using Meridian.Infrastructure.Adapters.Fred;
using Meridian.Infrastructure.Adapters.NasdaqDataLink;
using Meridian.Infrastructure.Adapters.Polygon;
using Meridian.Infrastructure.Adapters.Tiingo;
using Meridian.Infrastructure.Contracts;
using Meridian.ProviderSdk;
using Serilog;
using static Meridian.Application.Services.AutoConfigurationService;

namespace Meridian.Application.Services;

/// <summary>
/// Unified configuration service that consolidates all configuration-related operations.
/// This is the single entry point for wizard, auto-config, validation, provider detection,
/// credential resolution, self-healing fixes, and hot reload.
/// </summary>
/// <remarks>
/// <para><b>Consolidation Goal:</b> All CLI paths and UI workflows should route through this
/// service to eliminate duplicate validation and environment detection logic.</para>
/// <para><b>Key Responsibilities:</b></para>
/// <list type="bullet">
/// <item><description>Provider detection and enumeration</description></item>
/// <item><description>Credential resolution from environment variables and config</description></item>
/// <item><description>Configuration validation with detailed error reporting</description></item>
/// <item><description>Self-healing fixes for common configuration issues</description></item>
/// <item><description>Configuration hot reload monitoring</description></item>
/// <item><description>Unified pipeline producing ValidatedConfig from any entry point</description></item>
/// </list>
/// </remarks>
public sealed class ConfigurationService : IAsyncDisposable
{
    private readonly ILogger _log;
    private readonly ConfigurationWizard _wizard;
    private readonly AutoConfigurationService _autoConfig;
    private readonly ProviderCredentialResolver _credentialResolver;
    private readonly ConfigurationPipeline _pipeline;
    private readonly IBestOfBreedProviderSelector? _providerSelector;
    private ConfigWatcher? _watcher;

    public ConfigurationService(
        ILogger? log = null,
        ConfigurationWizard? wizard = null,
        AutoConfigurationService? autoConfig = null,
        ProviderCredentialResolver? credentialResolver = null,
        IBestOfBreedProviderSelector? providerSelector = null)
    {
        _log = log ?? LoggingSetup.ForContext<ConfigurationService>();
        _wizard = wizard ?? new ConfigurationWizard();
        _autoConfig = autoConfig ?? new AutoConfigurationService();
        _credentialResolver = credentialResolver ?? new ProviderCredentialResolver(_log);
        _pipeline = new ConfigurationPipeline(_log, _credentialResolver);
        _providerSelector = providerSelector;
    }

    /// <summary>
    /// Gets the underlying configuration pipeline for advanced scenarios.
    /// </summary>
    public ConfigurationPipeline Pipeline => _pipeline;


    /// <summary>
    /// Runs the interactive configuration wizard.
    /// </summary>
    public Task<WizardResult> RunWizardAsync(CancellationToken ct = default) => _wizard.RunAsync(ct);

    /// <summary>
    /// Runs quick auto-configuration based on environment variables.
    /// </summary>
    public WizardResult RunAutoConfig() => _wizard.RunQuickSetup();

    /// <summary>
    /// Runs quickstart: auto-configures, validates credentials, and prepares for launch.
    /// </summary>
    public Task<WizardResult> RunQuickstartAsync(CancellationToken ct = default) => _wizard.RunQuickstartAsync(ct);

    /// <summary>
    /// Auto-configures the application based on detected providers and environment.
    /// Returns a comprehensive result with detected providers, applied fixes, and recommendations.
    /// </summary>
    public AutoConfigurationService.AutoConfigResult AutoConfigure(AppConfig? existingConfig = null)
        => _autoConfig.AutoConfigure(existingConfig);

    /// <summary>
    /// Generates configuration for first-time users with interactive defaults.
    /// </summary>
    public AppConfig GenerateFirstTimeConfig(FirstTimeConfigOptions options)
        => _autoConfig.GenerateFirstTimeConfig(options);



    /// <summary>
    /// Detects all available providers based on environment variables and configuration.
    /// This is the single source of truth for provider detection.
    /// </summary>
    public IReadOnlyList<DetectedProvider> DetectProviders()
        => _autoConfig.DetectAvailableProviders();

    /// <summary>
    /// Gets a quick summary of which providers have credentials configured.
    /// </summary>
    public IReadOnlyDictionary<string, bool> GetCredentialStatus()
        => _credentialResolver.GetCredentialStatus();

    /// <summary>
    /// Gets detected providers filtered by capability.
    /// </summary>
    public IReadOnlyList<DetectedProvider> GetProvidersByCapability(string capability)
    {
        return DetectProviders()
            .Where(p => p.Capabilities.Contains(capability, StringComparer.OrdinalIgnoreCase))
            .ToList();
    }

    /// <summary>
    /// Gets the best available provider for real-time streaming.
    /// </summary>
    public DetectedProvider? GetBestRealTimeProvider()
    {
        var eligibleProviders = GetProvidersByCapability("RealTime")
            .Where(p => p.HasCredentials)
            .ToList();

        if (eligibleProviders.Count == 0)
            return null;

        if (_providerSelector is null)
            return eligibleProviders.OrderBy(static p => p.SuggestedPriority).FirstOrDefault();

        var selection = _providerSelector.SelectAsync(
                new ProviderRouteContext(ProviderCapabilityKind.RealtimeMarketData))
            .ConfigureAwait(false)
            .GetAwaiter()
            .GetResult();

        var selected = selection.SelectedDecision;
        if (selected is null)
            return eligibleProviders.OrderBy(static p => p.SuggestedPriority).FirstOrDefault();

        return eligibleProviders.FirstOrDefault(provider =>
                   string.Equals(provider.Name, selected.ProviderFamilyId, StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(provider.Name, selected.ConnectionId, StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(provider.DisplayName, selected.ConnectionId, StringComparison.OrdinalIgnoreCase))
               ?? eligibleProviders.OrderBy(static p => p.SuggestedPriority).FirstOrDefault();
    }

    /// <summary>
    /// Gets available providers for historical data backfill.
    /// </summary>
    public IReadOnlyList<DetectedProvider> GetHistoricalProviders()
    {
        return GetProvidersByCapability("Historical")
            .Where(p => p.HasCredentials)
            .OrderBy(p => p.SuggestedPriority)
            .ToList();
    }

    /// <summary>
    /// Prints provider detection results to console (for CLI commands).
    /// </summary>
    public void PrintProviderDetection(IReadOnlyList<DetectedProvider>? providers = null)
    {
        providers ??= DetectProviders();

        Console.WriteLine();
        Console.WriteLine("Detected Data Providers:");
        Console.WriteLine("-".PadRight(60, '-'));

        foreach (var provider in providers)
        {
            var status = provider.HasCredentials ? "[OK]" : "[--]";
            Console.WriteLine($"  {status} {provider.DisplayName,-25} Priority: {provider.SuggestedPriority}");
            Console.WriteLine($"        Capabilities: {string.Join(", ", provider.Capabilities)}");

            if (!provider.HasCredentials && provider.MissingCredentials.Length > 0)
            {
                Console.WriteLine($"        Missing: {string.Join(", ", provider.MissingCredentials)}");
            }
        }

        var configured = providers.Count(p => p.HasCredentials);
        Console.WriteLine();
        Console.WriteLine($"  {configured}/{providers.Count} providers configured");

        if (configured == 0)
        {
            Console.WriteLine();
            Console.WriteLine("  To configure providers, set environment variables:");
            Console.WriteLine("    export ALPACA_KEY_ID=your-key-id");
            Console.WriteLine("    export ALPACA_SECRET_KEY=your-secret-key");
            Console.WriteLine();
            Console.WriteLine("  Or run the configuration wizard:");
            Console.WriteLine("    Meridian --wizard");
        }
        Console.WriteLine();
    }



    /// <summary>
    /// Creates a provider-scoped credential context from attribute metadata and config fallbacks.
    /// </summary>
    public ICredentialContext CreateCredentialContext(
        Type providerType,
        IReadOnlyDictionary<string, string?>? configuredValues = null)
        => _credentialResolver.CreateContext(providerType, configuredValues);

    /// <summary>
    /// Resolves all credentials for a given configuration and returns a config with resolved values.
    /// </summary>
    public AppConfig ResolveAllCredentials(AppConfig config)
    {
        // Resolve Alpaca credentials
        if (config.DataSource == DataSourceKind.Alpaca || config.Alpaca != null)
        {
            var credentials = CreateCredentialContext(
                typeof(AlpacaHistoricalDataProvider),
                new Dictionary<string, string?>(StringComparer.Ordinal)
                {
                    ["ALPACA_KEY_ID"] = config.Alpaca?.KeyId,
                    ["ALPACA_SECRET_KEY"] = config.Alpaca?.SecretKey
                });
            var keyId = credentials.Get("ALPACA_KEY_ID");
            var secretKey = credentials.Get("ALPACA_SECRET_KEY");
            if (!string.IsNullOrEmpty(keyId) && !string.IsNullOrEmpty(secretKey))
            {
                config = config with
                {
                    Alpaca = (config.Alpaca ?? new AlpacaOptions(keyId, secretKey)) with
                    {
                        KeyId = keyId,
                        SecretKey = secretKey
                    }
                };
            }
        }

        // Resolve Polygon credentials
        if (config.DataSource == DataSourceKind.Polygon || config.Polygon != null)
        {
            var apiKey = CreateCredentialContext(
                typeof(PolygonHistoricalDataProvider),
                new Dictionary<string, string?>(StringComparer.Ordinal)
                {
                    ["POLYGON_API_KEY"] = config.Polygon?.ApiKey
                }).Get("POLYGON_API_KEY");
            if (!string.IsNullOrEmpty(apiKey) && config.Polygon != null)
            {
                config = config with
                {
                    Polygon = config.Polygon with { ApiKey = apiKey }
                };
            }
        }

        // Resolve backfill provider credentials
        if (config.Backfill?.Providers != null)
        {
            var providers = config.Backfill.Providers;
            var updated = false;

            if (providers.Tiingo != null)
            {
                var token = CreateCredentialContext(
                    typeof(TiingoHistoricalDataProvider),
                    new Dictionary<string, string?>(StringComparer.Ordinal)
                    {
                        ["TIINGO_API_TOKEN"] = providers.Tiingo.ApiToken
                    }).Get("TIINGO_API_TOKEN");
                if (!string.IsNullOrEmpty(token))
                {
                    providers = providers with { Tiingo = providers.Tiingo with { ApiToken = token } };
                    updated = true;
                }
            }

            if (providers.Finnhub != null)
            {
                var key = CreateCredentialContext(
                    typeof(FinnhubHistoricalDataProvider),
                    new Dictionary<string, string?>(StringComparer.Ordinal)
                    {
                        ["FINNHUB_API_KEY"] = providers.Finnhub.ApiKey
                    }).Get("FINNHUB_API_KEY");
                if (!string.IsNullOrEmpty(key))
                {
                    providers = providers with { Finnhub = providers.Finnhub with { ApiKey = key } };
                    updated = true;
                }
            }

            if (providers.Polygon != null)
            {
                var key = CreateCredentialContext(
                    typeof(PolygonHistoricalDataProvider),
                    new Dictionary<string, string?>(StringComparer.Ordinal)
                    {
                        ["POLYGON_API_KEY"] = providers.Polygon.ApiKey
                    }).Get("POLYGON_API_KEY");
                if (!string.IsNullOrEmpty(key))
                {
                    providers = providers with { Polygon = providers.Polygon with { ApiKey = key } };
                    updated = true;
                }
            }

            if (providers.AlphaVantage != null)
            {
                var key = CreateCredentialContext(
                    typeof(AlphaVantageHistoricalDataProvider),
                    new Dictionary<string, string?>(StringComparer.Ordinal)
                    {
                        ["ALPHA_VANTAGE_API_KEY"] = providers.AlphaVantage.ApiKey
                    }).Get("ALPHA_VANTAGE_API_KEY");
                if (!string.IsNullOrEmpty(key))
                {
                    providers = providers with { AlphaVantage = providers.AlphaVantage with { ApiKey = key } };
                    updated = true;
                }
            }

            if (providers.Nasdaq != null)
            {
                var key = CreateCredentialContext(
                    typeof(NasdaqDataLinkHistoricalDataProvider),
                    new Dictionary<string, string?>(StringComparer.Ordinal)
                    {
                        ["NASDAQ_DATA_LINK_API_KEY"] = providers.Nasdaq.ApiKey
                    }).Get("NASDAQ_DATA_LINK_API_KEY");
                if (!string.IsNullOrEmpty(key))
                {
                    providers = providers with { Nasdaq = providers.Nasdaq with { ApiKey = key } };
                    updated = true;
                }
            }

            if (providers.Fred != null)
            {
                var key = CreateCredentialContext(
                    typeof(FredHistoricalDataProvider),
                    new Dictionary<string, string?>(StringComparer.Ordinal)
                    {
                        ["FRED_API_KEY"] = providers.Fred.ApiKey
                    }).Get("FRED_API_KEY");
                if (!string.IsNullOrEmpty(key))
                {
                    providers = providers with { Fred = providers.Fred with { ApiKey = key } };
                    updated = true;
                }
            }

            if (updated)
            {
                config = config with { Backfill = config.Backfill with { Providers = providers } };
            }
        }

        return config;
    }



    /// <summary>
    /// Validates a configuration file and returns an exit code (0 = valid, 1 = invalid).
    /// This is the single validation entry point for CLI commands.
    /// </summary>
    public int ValidateConfig(string configPath)
    {
        var validator = new ConfigValidatorCli(_log);
        return validator.Validate(configPath);
    }

    /// <summary>
    /// Validates an AppConfig object using the unified validation pipeline.
    /// Returns true if valid, false otherwise.
    /// </summary>
    public bool ValidateConfig(AppConfig config, out IReadOnlyList<string> errors)
    {
        var validator = ConfigValidationPipeline.CreateDefault();
        var results = validator.Validate(config);

        var errorList = new List<string>();
        foreach (var result in results)
        {
            if (result.IsError)
            {
                errorList.Add($"{result.Property}: {result.Message}");
                _log.Error("Configuration validation error: {Property}: {Message}", result.Property, result.Message);
            }
        }

        errors = errorList;
        return !results.Any(r => r.IsError);
    }

    /// <summary>
    /// Performs comprehensive validation without starting the collector (dry-run).
    /// </summary>
    public async Task<DryRunResult> DryRunValidationAsync(
        AppConfig config,
        DryRunOptions? options = null,
        CancellationToken ct = default)
    {
        options ??= new DryRunOptions(
            ValidateConfiguration: true,
            ValidateFileSystem: true,
            ValidateConnectivity: true,
            ValidateProviders: true,
            ValidateSymbols: true,
            ValidateResources: true
        );

        var dryRunService = new DryRunService();
        return await dryRunService.ValidateAsync(config, options, ct);
    }

    /// <summary>
    /// Performs a quick configuration health check.
    /// </summary>
    public QuickCheckResult PerformQuickCheck(AppConfig config)
    {
        var summary = new StartupSummary();
        return summary.PerformQuickCheck(config);
    }

    /// <summary>
    /// Tests connectivity to all configured providers.
    /// </summary>
    public async Task<ConnectivityTestService.ConnectivitySummary> TestConnectivityAsync(
        AppConfig config,
        CancellationToken ct = default)
    {
        await using var tester = new ConnectivityTestService();
        return await tester.TestAllAsync(config, ct);
    }

    /// <summary>
    /// Validates API credentials for all configured providers.
    /// </summary>
    public async Task<CredentialValidationService.ValidationSummary> ValidateCredentialsAsync(
        AppConfig config,
        CancellationToken ct = default)
    {
        await using var validator = new CredentialValidationService();
        return await validator.ValidateAllAsync(config, ct);
    }



    /// <summary>
    /// Applies self-healing fixes to a configuration and returns the fixed config
    /// along with lists of applied fixes and advisory warnings.
    /// </summary>
    /// <param name="config">The configuration to fix.</param>
    /// <param name="strictness">
    /// Controls which fixes are applied.
    /// <see cref="SelfHealingStrictness.Development"/> (default) applies all fixes and logs
    /// significant (<see cref="SelfHealingSeverity.Warn"/>) ones as warnings.
    /// <see cref="SelfHealingStrictness.Production"/> applies only safe
    /// (<see cref="SelfHealingSeverity.AutoFix"/>) changes and leaves
    /// <see cref="SelfHealingSeverity.Warn"/>-level fixes unapplied.
    /// </param>
    public (AppConfig Config, IReadOnlyList<string> AppliedFixes, IReadOnlyList<string> Warnings) ApplySelfHealingFixes(
        AppConfig config,
        SelfHealingStrictness strictness = SelfHealingStrictness.Development)
    {
        var appliedFixes = new List<string>();
        var warnings = new List<string>();

        // Helper that applies a fix only when severity + strictness permit it.
        void TryFix(SelfHealingSeverity severity, string description, Func<AppConfig, AppConfig> fn)
        {
            bool apply = severity == SelfHealingSeverity.AutoFix
                         || strictness == SelfHealingStrictness.Development;
            if (apply)
            {
                config = fn(config);
                appliedFixes.Add(description);
            }
        }

        // Fix: Resolve credentials from environment
        config = ResolveAllCredentials(config);

        // Fix: Alpaca selected but no credentials (AutoFix — safe credential injection)
        if (config.DataSource == DataSourceKind.Alpaca)
        {
            if (config.Alpaca == null || string.IsNullOrEmpty(config.Alpaca.KeyId))
            {
                var credentials = CreateCredentialContext(typeof(AlpacaHistoricalDataProvider));
                var keyId = credentials.Get("ALPACA_KEY_ID");
                var secretKey = credentials.Get("ALPACA_SECRET_KEY");
                if (!string.IsNullOrEmpty(keyId) && !string.IsNullOrEmpty(secretKey))
                {
                    TryFix(
                        SelfHealingSeverity.AutoFix,
                        "Added Alpaca credentials from environment variables",
                        c => c with { Alpaca = new AlpacaOptions(keyId, secretKey) });
                }
                else
                {
                    warnings.Add(
                        "Alpaca selected but no credentials found. " +
                        "Set ALPACA_KEY_ID and ALPACA_SECRET_KEY environment variables.");
                }
            }
        }

        // Fix: IB selected but gateway not available (Warn — switches active provider)
        if (config.DataSource == DataSourceKind.IB && !IsIBGatewayAvailable())
        {
            var bestProvider = GetBestRealTimeProvider();
            if (bestProvider != null && Enum.TryParse<DataSourceKind>(bestProvider.Name, out var kind))
            {
                TryFix(
                    SelfHealingSeverity.Warn,
                    $"Switched active data provider from IB to {bestProvider.DisplayName} (IB Gateway not detected)",
                    c => c with { DataSource = kind });
            }
            else
            {
                warnings.Add("IB Gateway not detected and no alternative real-time providers configured.");
            }
        }

        // Fix: Invalid storage naming convention (AutoFix — normalises format string)
        if (config.Storage != null)
        {
            var validConventions = new[]
            {
                "flat", "bysymbol", "bydate", "bytype",
                "bysource", "byassetclass", "hierarchical", "canonical"
            };
            if (!validConventions.Contains(config.Storage.NamingConvention.ToLowerInvariant()))
            {
                var oldValue = config.Storage.NamingConvention;
                TryFix(
                    SelfHealingSeverity.AutoFix,
                    $"Invalid naming convention '{oldValue}' changed to 'BySymbol'",
                    c => c with { Storage = c.Storage! with { NamingConvention = "BySymbol" } });
            }

            // Fix: Invalid date partition (AutoFix — normalises format string)
            var validPartitions = new[] { "none", "daily", "hourly", "monthly" };
            if (!validPartitions.Contains(config.Storage.DatePartition.ToLowerInvariant()))
            {
                var oldValue = config.Storage.DatePartition;
                TryFix(
                    SelfHealingSeverity.AutoFix,
                    $"Invalid date partition '{oldValue}' changed to 'Daily'",
                    c => c with { Storage = c.Storage! with { DatePartition = "Daily" } });
            }
        }

        // Fix: Empty symbols list (Warn — adds a default symbol the operator didn't configure)
        if (config.Symbols == null || config.Symbols.Length == 0)
        {
            TryFix(
                SelfHealingSeverity.Warn,
                "Added default symbol (SPY) since none were configured",
                c => c with
                {
                    Symbols = new[]
                    {
                        new SymbolConfig("SPY", SubscribeTrades: true, SubscribeDepth: true, DepthLevels: 10)
                    }
                });
        }

        // Fix: Invalid depth levels (AutoFix — clamps numeric range)
        if (config.Symbols != null)
        {
            var fixedSymbols = config.Symbols.Select(s =>
                s.SubscribeDepth && (s.DepthLevels < 1 || s.DepthLevels > 50)
                    ? s with { DepthLevels = Math.Clamp(s.DepthLevels, 1, 50) }
                    : s).ToArray();

            if (!config.Symbols.SequenceEqual(fixedSymbols))
            {
                TryFix(
                    SelfHealingSeverity.AutoFix,
                    "Adjusted depth levels to valid range (1-50)",
                    c => c with { Symbols = fixedSymbols });
            }
        }

        // Fix: Backfill date range issues
        if (config.Backfill != null)
        {
            var backfill = config.Backfill;

            // AutoFix: From date after To date (cosmetic swap)
            if (backfill.From.HasValue && backfill.To.HasValue && backfill.From > backfill.To)
            {
                var (swappedFrom, swappedTo) = (backfill.To, backfill.From);
                TryFix(
                    SelfHealingSeverity.AutoFix,
                    "Swapped backfill From/To dates (From was after To)",
                    c => c with { Backfill = c.Backfill! with { From = swappedFrom, To = swappedTo } });
                backfill = config.Backfill with { From = swappedFrom, To = swappedTo };
            }

            // AutoFix: Future end date (safe adjustment)
            var today = DateOnly.FromDateTime(DateTime.UtcNow);
            if (backfill.To.HasValue && backfill.To > today)
            {
                TryFix(
                    SelfHealingSeverity.AutoFix,
                    "Adjusted backfill To date to today (was in the future)",
                    c => c with { Backfill = c.Backfill! with { To = today } });
            }
        }

        _log.Information("Self-healing applied {FixCount} fixes, {WarningCount} warnings",
            appliedFixes.Count, warnings.Count);

        return (config, appliedFixes, warnings);
    }

    /// <summary>
    /// Checks if IB Gateway/TWS is available on default ports.
    /// </summary>
    public bool IsIBGatewayAvailable()
    {
        try
        {
            var ports = new[] { 7496, 7497, 4001, 4002 };
            foreach (var port in ports)
            {
                using var client = new System.Net.Sockets.TcpClient();
                var result = client.BeginConnect("127.0.0.1", port, null, null);
                var success = result.AsyncWaitHandle.WaitOne(TimeSpan.FromMilliseconds(100));
                if (success && client.Connected)
                {
                    client.EndConnect(result);
                    return true;
                }
            }
        }
        catch
        {
            // Ignore connection errors
        }
        return false;
    }



    /// <summary>
    /// Applies environment variable overrides to configuration.
    /// </summary>
    public AppConfig ApplyEnvironmentOverrides(AppConfig config)
    {
        var envOverride = new ConfigEnvironmentOverride();
        return envOverride.ApplyOverrides(config);
    }

    /// <summary>
    /// Loads configuration with environment overlays and self-healing fixes applied.
    /// This is the recommended way to load configuration for all entry points.
    /// </summary>
    public AppConfig LoadAndPrepareConfig(string configPath, bool applySelfHealing = true)
    {
        // Load base config
        var config = ConfigStore.LoadConfig(configPath);

        // Apply environment-specific overlay
        config = ApplyEnvironmentOverlay(config, configPath);

        // Apply environment variable overrides
        config = ApplyEnvironmentOverrides(config);

        // Resolve credentials from environment
        config = ResolveAllCredentials(config);

        // Apply self-healing fixes if enabled
        if (applySelfHealing)
        {
            var (fixedConfig, fixes, warnings) = ApplySelfHealingFixes(config);
            config = fixedConfig;

            if (fixes.Count > 0)
            {
                _log.Information("Applied {FixCount} self-healing configuration fixes", fixes.Count);
            }
            if (warnings.Count > 0)
            {
                foreach (var warning in warnings)
                {
                    _log.Warning("Configuration warning: {Warning}", warning);
                }
            }
        }

        config = config with
        {
            DataRoot = MeridianPathDefaults.ResolveDataRoot(configPath, config.DataRoot)
        };

        return config;
    }

    /// <summary>
    /// Loads and validates configuration through the unified pipeline.
    /// Returns a ValidatedConfig with full metadata about validation, fixes, and warnings.
    /// </summary>
    /// <remarks>
    /// This is the preferred method for new code. It provides richer metadata than LoadAndPrepareConfig.
    /// </remarks>
    public ValidatedConfig LoadValidatedConfig(string configPath, PipelineOptions? options = null)
    {
        var validated = _pipeline.LoadFromFile(configPath, options);

        // Log warnings for visibility
        foreach (var warning in validated.Warnings)
        {
            _log.Warning("Configuration warning: {Warning}", warning);
        }

        return validated;
    }

    /// <summary>
    /// Processes an existing AppConfig through the unified pipeline.
    /// </summary>
    public ValidatedConfig ProcessConfig(AppConfig config, PipelineOptions? options = null)
    {
        return _pipeline.Process(config, options);
    }

    /// <summary>
    /// Runs the wizard and processes the result through the pipeline.
    /// </summary>
    public async Task<ValidatedConfig> RunWizardAndValidateAsync(CancellationToken ct = default)
    {
        var result = await _wizard.RunAsync(ct);
        return _pipeline.FromWizardResult(result);
    }

    /// <summary>
    /// Runs auto-configuration and processes the result through the pipeline.
    /// </summary>
    public ValidatedConfig RunAutoConfigAndValidate(AppConfig? existingConfig = null)
    {
        var result = _autoConfig.AutoConfigure(existingConfig);
        return _pipeline.FromAutoConfigResult(result);
    }

    /// <summary>
    /// Applies environment-specific configuration overlay (e.g., appsettings.Production.json).
    /// </summary>
    private AppConfig ApplyEnvironmentOverlay(AppConfig baseConfig, string basePath)
    {
        var envName = GetEnvironmentName();
        if (string.IsNullOrWhiteSpace(envName))
            return baseConfig;

        var directory = Path.GetDirectoryName(basePath) ?? ".";
        var fileName = Path.GetFileNameWithoutExtension(basePath);
        var extension = Path.GetExtension(basePath);
        var envPath = Path.Combine(directory, $"{fileName}.{envName}{extension}");

        if (!File.Exists(envPath))
            return baseConfig;

        try
        {
            _log.Information("Loading environment-specific configuration: {EnvPath}", envPath);
            var envConfig = ConfigStore.LoadConfig(envPath);
            return MergeConfigs(baseConfig, envConfig);
        }
        catch (Exception ex)
        {
            _log.Warning(ex, "Failed to load environment config {EnvPath}", envPath);
            return baseConfig;
        }
    }

    /// <summary>
    /// Gets the current environment name from MDC_ENVIRONMENT or DOTNET_ENVIRONMENT.
    /// </summary>
    public static string? GetEnvironmentName()
    {
        var env = Environment.GetEnvironmentVariable("MDC_ENVIRONMENT");
        if (!string.IsNullOrWhiteSpace(env))
            return env;

        return Environment.GetEnvironmentVariable("DOTNET_ENVIRONMENT");
    }

    /// <summary>
    /// Merges two configurations, with overlay values taking precedence.
    /// </summary>
    private static AppConfig MergeConfigs(AppConfig baseConfig, AppConfig overlay)
    {
        return baseConfig with
        {
            DataSource = overlay.DataSource != default ? overlay.DataSource : baseConfig.DataSource,
            DataRoot = !string.IsNullOrWhiteSpace(overlay.DataRoot) ? overlay.DataRoot : baseConfig.DataRoot,
            Compress = overlay.Compress ?? baseConfig.Compress,
            Symbols = overlay.Symbols?.Length > 0 ? overlay.Symbols : baseConfig.Symbols,
            Alpaca = overlay.Alpaca ?? baseConfig.Alpaca,
            IB = overlay.IB ?? baseConfig.IB,
            IBClientPortal = overlay.IBClientPortal ?? baseConfig.IBClientPortal,
            Polygon = overlay.Polygon ?? baseConfig.Polygon,
            Storage = overlay.Storage ?? baseConfig.Storage,
            Backfill = overlay.Backfill ?? baseConfig.Backfill
        };
    }



    /// <summary>
    /// Starts hot reload monitoring for configuration file changes.
    /// </summary>
    public ConfigWatcher StartHotReload(
        string configPath,
        Action<AppConfig> onConfigChanged,
        Action<Exception>? onError = null)
    {
        StopHotReload();

        _watcher = new ConfigWatcher(configPath);
        _watcher.ConfigChanged += onConfigChanged;
        if (onError != null)
        {
            _watcher.Error += onError;
        }

        _watcher.Start();
        _log.Information("Configuration hot reload enabled for {ConfigPath}", configPath);
        return _watcher;
    }

    /// <summary>
    /// Starts hot reload monitoring with validated configuration updates.
    /// The callback receives a ValidatedConfig that has been processed through the full pipeline.
    /// </summary>
    public ConfigWatcher StartValidatedHotReload(
        string configPath,
        Action<ValidatedConfig> onConfigChanged,
        Action<Exception>? onError = null,
        PipelineOptions? options = null)
    {
        StopHotReload();

        _watcher = _pipeline.StartHotReload(configPath, onConfigChanged, onError, options);
        return _watcher;
    }

    /// <summary>
    /// Stops hot reload monitoring.
    /// </summary>
    public void StopHotReload()
    {
        _watcher?.Dispose();
        _watcher = null;
    }



    /// <summary>
    /// Displays configuration summary to console.
    /// </summary>
    public void DisplayConfigSummary(AppConfig config, string configPath, string[] args)
    {
        var summary = new StartupSummary();
        summary.Display(config, configPath, args);
    }

    /// <summary>
    /// Generates a configuration template.
    /// </summary>
    public ConfigTemplate? GetConfigTemplate(string templateName)
    {
        var generator = new ConfigTemplateGenerator();
        return generator.GetTemplate(templateName);
    }

    /// <summary>
    /// Returns the current on-disk config via ConfigStore.
    /// </summary>
    public AppConfig GetConfig(string? configPath = null)
    {
        var store = new ConfigStore(configPath);
        return store.Load();
    }

    /// <summary>
    /// Saves a modified config back to disk.
    /// </summary>
    public async Task SaveConfigAsync(AppConfig config, string? configPath = null, CancellationToken ct = default)
    {
        var store = new ConfigStore(configPath);
        await store.SaveAsync(config);
        _log.Information("Configuration saved to {Path}", store.ConfigPath);
    }


    public async ValueTask DisposeAsync()
    {
        StopHotReload();
        await _pipeline.DisposeAsync();
    }
}
