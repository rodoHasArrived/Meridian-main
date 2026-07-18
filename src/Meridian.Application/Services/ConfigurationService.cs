using Meridian.Application.Config;
using Meridian.Core.Config;
using Meridian.Application.Config.Credentials;
using Meridian.DataIntegration.Credentials;
using Meridian.Core.Logging;
using Meridian.Application.ProviderRouting;
using Meridian.Application.UI;
using Meridian.Contracts.Configuration;
using Meridian.Platform.Runtime;
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
    private readonly Func<IBestOfBreedProviderSelector?> _providerSelectorAccessor;
    private readonly Func<bool> _ibGatewayAvailabilityProbe;
    private ConfigWatcher? _watcher;

    public ConfigurationService(
        ILogger? log = null,
        ConfigurationWizard? wizard = null,
        AutoConfigurationService? autoConfig = null,
        ProviderCredentialResolver? credentialResolver = null,
        IBestOfBreedProviderSelector? providerSelector = null,
        Func<bool>? ibGatewayAvailabilityProbe = null,
        Func<IBestOfBreedProviderSelector?>? providerSelectorAccessor = null)
    {
        _log = log ?? LoggingSetup.ForContext<ConfigurationService>();
        _wizard = wizard ?? new ConfigurationWizard();
        _autoConfig = autoConfig ?? new AutoConfigurationService();
        _credentialResolver = credentialResolver ?? new ProviderCredentialResolver(_log);
        _ibGatewayAvailabilityProbe = ibGatewayAvailabilityProbe ?? IsIBGatewayAvailable;
        _pipeline = new ConfigurationPipeline(_log, _credentialResolver, _ibGatewayAvailabilityProbe);
        _providerSelectorAccessor = providerSelectorAccessor ?? (() => providerSelector);
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

        var providerSelector = _providerSelectorAccessor();
        if (providerSelector is null)
            return eligibleProviders.OrderBy(static p => p.SuggestedPriority).FirstOrDefault();

        var selection = providerSelector.SelectAsync(
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
        => ConfigurationResolutionRules.ResolveAllCredentials(config, _credentialResolver);



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
        var result = ConfigurationResolutionRules.ApplySelfHealingFixes(
            config,
            strictness,
            _credentialResolver,
            _ibGatewayAvailabilityProbe,
            GetBestRealTimeProvider);

        _log.Information("Self-healing applied {FixCount} fixes, {WarningCount} warnings",
            result.Applied.Count, result.Warnings.Count);

        return (result.Config, result.Applied.Select(f => f.Description).ToList(), result.Warnings);
    }

    /// <summary>
    /// Checks if IB Gateway/TWS is available on default ports.
    /// </summary>
    public bool IsIBGatewayAvailable() => IBGatewayProbe.IsAvailable();



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
        config = ConfigurationResolutionRules.ApplyEnvironmentOverlay(config, configPath, _log);

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
        => ConfigurationResolutionRules.ApplyEnvironmentOverlay(baseConfig, basePath, _log);

    /// <summary>
    /// Gets the current environment name from MDC_ENVIRONMENT or DOTNET_ENVIRONMENT.
    /// </summary>
    public static string? GetEnvironmentName()
        => ConfigurationResolutionRules.GetEnvironmentName();




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
