using Meridian.Application.Config.Credentials;
using Meridian.Application.Logging;
using Meridian.Application.Services;
using Meridian.Application.UI;
using Meridian.Infrastructure.Adapters.Alpaca;
using Meridian.Infrastructure.Adapters.AlphaVantage;
using Meridian.Infrastructure.Adapters.Finnhub;
using Meridian.Infrastructure.Adapters.Fred;
using Meridian.Infrastructure.Adapters.NasdaqDataLink;
using Meridian.Infrastructure.Adapters.Polygon;
using Meridian.Infrastructure.Adapters.Tiingo;
using Meridian.Infrastructure.Contracts;
using Serilog;

namespace Meridian.Application.Config;

/// <summary>
/// Unified configuration pipeline that always produces a validated, normalized configuration.
/// This is the single entry point for all configuration loading regardless of source
/// (file, wizard, auto-config, hot reload, programmatic).
/// </summary>
/// <remarks>
/// <para><b>Pipeline Stages:</b></para>
/// <list type="number">
/// <item><description>Load base configuration from source</description></item>
/// <item><description>Apply environment-specific overlay (e.g., appsettings.Production.json)</description></item>
/// <item><description>Apply environment variable overrides</description></item>
/// <item><description>Resolve credentials from all sources</description></item>
/// <item><description>Apply self-healing fixes (optional)</description></item>
/// <item><description>Validate the final configuration</description></item>
/// <item><description>Return ValidatedConfig with full metadata</description></item>
/// </list>
/// </remarks>
public sealed class ConfigurationPipeline : IAsyncDisposable
{
    private readonly ILogger _log;
    private readonly ProviderCredentialResolver _credentialResolver;
    private readonly ConfigEnvironmentOverride _envOverride;
    private ConfigWatcher? _watcher;

    public ConfigurationPipeline(ILogger? log = null, ProviderCredentialResolver? credentialResolver = null)
    {
        _log = log ?? LoggingSetup.ForContext<ConfigurationPipeline>();
        _credentialResolver = credentialResolver ?? new ProviderCredentialResolver(_log);
        _envOverride = new ConfigEnvironmentOverride();
    }


    /// <summary>
    /// Loads and validates configuration from a file path.
    /// This is the primary entry point for file-based configuration.
    /// </summary>
    public ValidatedConfig LoadFromFile(string configPath, PipelineOptions? options = null)
    {
        options ??= PipelineOptions.Default;

        try
        {
            // Stage 1: Load base config
            var config = ConfigStore.LoadConfig(configPath);
            var environmentName = GetEnvironmentName();

            // Stage 2: Apply environment overlay
            config = ApplyEnvironmentOverlay(config, configPath);

            // Run through common pipeline
            return RunPipeline(config, configPath, environmentName, ConfigurationOrigin.File, options);
        }
        catch (Exception ex)
        {
            _log.Error(ex, "Failed to load configuration from {ConfigPath}", configPath);
            return ValidatedConfig.Failed(
                null,
                new[] { $"Failed to load configuration: {ex.Message}" },
                configPath,
                ConfigurationOrigin.File);
        }
    }

    /// <summary>
    /// Processes an existing AppConfig through the pipeline.
    /// Use this for programmatically created configurations.
    /// </summary>
    public ValidatedConfig Process(AppConfig config, PipelineOptions? options = null)
    {
        options ??= PipelineOptions.Default;
        var environmentName = GetEnvironmentName();

        return RunPipeline(config, null, environmentName, ConfigurationOrigin.Programmatic, options);
    }

    /// <summary>
    /// Processes configuration from the wizard result.
    /// </summary>
    public ValidatedConfig FromWizardResult(WizardResult result, PipelineOptions? options = null)
    {
        if (!result.Success || result.Config == null)
        {
            return ValidatedConfig.Failed(
                null,
                new[] { "Wizard did not produce a valid configuration" },
                source: ConfigurationOrigin.Wizard);
        }

        options ??= PipelineOptions.Default;
        var environmentName = GetEnvironmentName();

        return RunPipeline(result.Config, result.ConfigPath, environmentName, ConfigurationOrigin.Wizard, options);
    }

    /// <summary>
    /// Processes configuration from auto-config result.
    /// </summary>
    public ValidatedConfig FromAutoConfigResult(AutoConfigurationService.AutoConfigResult result, PipelineOptions? options = null)
    {
        options ??= PipelineOptions.Default;
        var environmentName = GetEnvironmentName();

        var warnings = new List<string>(result.Warnings);
        if (result.Recommendations.Count > 0)
        {
            warnings.AddRange(result.Recommendations.Select(r => $"Recommendation: {r}"));
        }

        var validated = RunPipeline(result.Config, null, environmentName, ConfigurationOrigin.AutoConfig, options);

        // Merge auto-config specific info
        return validated with
        {
            AppliedFixes = validated.AppliedFixes.Concat(result.AppliedFixes).ToList(),
            Warnings = validated.Warnings.Concat(warnings).ToList()
        };
    }

    /// <summary>
    /// Processes configuration from hot reload.
    /// </summary>
    public ValidatedConfig FromHotReload(AppConfig config, string configPath, PipelineOptions? options = null)
    {
        options ??= PipelineOptions.Default with { ApplySelfHealing = true };
        var environmentName = GetEnvironmentName();

        return RunPipeline(config, configPath, environmentName, ConfigurationOrigin.HotReload, options);
    }



    /// <summary>
    /// Starts watching a configuration file for changes.
    /// The callback receives a ValidatedConfig that has been processed through the full pipeline.
    /// </summary>
    public ConfigWatcher StartHotReload(
        string configPath,
        Action<ValidatedConfig> onConfigChanged,
        Action<Exception>? onError = null,
        PipelineOptions? options = null)
    {
        StopHotReload();

        options ??= PipelineOptions.Default;

        _watcher = new ConfigWatcher(configPath);
        _watcher.ConfigChanged += rawConfig =>
        {
            var validated = FromHotReload(rawConfig, configPath, options);
            onConfigChanged(validated);
        };

        if (onError != null)
        {
            _watcher.Error += onError;
        }

        _watcher.Start();
        _log.Information("Configuration hot reload enabled for {ConfigPath}", configPath);
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



    private ValidatedConfig RunPipeline(
        AppConfig config,
        string? sourcePath,
        string? environmentName,
        ConfigurationOrigin source,
        PipelineOptions options)
    {
        var appliedFixes = new List<string>();
        var blockedFixes = new List<string>();
        var warnings = new List<string>();
        var validationErrors = new List<string>();

        try
        {
            // Stage 2.5: Warn if both legacy DataSource and new DataSources are set
            if (config.DataSource != default && config.DataSources?.Sources is { Length: > 0 })
            {
                const string deprecationMessage =
                    "Both 'DataSource' and 'DataSources' are set. " +
                    "'DataSources' takes precedence for multi-provider configuration. " +
                    "Remove 'DataSource' to silence this warning.";
                _log.Warning(deprecationMessage);
                warnings.Add(deprecationMessage);
            }

            // Stage 2.6: Warn if credentials appear directly in config file
            WarnIfCredentialsInConfigFile(config, warnings);

            // Stage 2.7: Warn about provider-specific symbol fields that won't apply
            WarnAboutProviderSpecificSymbolFields(config, warnings);

            // Stage 3: Apply environment variable overrides
            config = _envOverride.ApplyOverrides(config);

            // Stage 4: Resolve credentials
            config = ResolveAllCredentials(config);

            // Stage 5: Apply self-healing fixes (if enabled)
            if (options.ApplySelfHealing)
            {
                var (healedConfig, applied, refused, healWarnings) =
                    ApplySelfHealingFixes(config, options.HealingStrictness);
                config = healedConfig;
                warnings.AddRange(healWarnings);

                foreach (var fix in applied)
                {
                    appliedFixes.Add(fix.Description);
                    if (fix.Severity == SelfHealingSeverity.Warn)
                        _log.Warning(
                            "[Config] Self-healing applied significant change: {Fix}",
                            fix.Description);
                    else
                        _log.Debug("[Config] Self-healing applied: {Fix}", fix.Description);
                }

                if (applied.Count > 0)
                    _log.Information("Applied {FixCount} self-healing configuration fixes", applied.Count);

                foreach (var fix in refused)
                {
                    var errorMsg =
                        $"Configuration requires manual correction " +
                        $"(set MDC_CONFIG_STRICTNESS=development to auto-fix): {fix.Description}";
                    blockedFixes.Add(fix.Description);
                    validationErrors.Add(errorMsg);
                    _log.Error(
                        "[Config] Self-healing refused in production mode — operator action required: {Fix}",
                        fix.Description);
                }

                if (refused.Count > 0)
                    _log.Error(
                        "Startup blocked: {RefusedCount} self-healing fix(es) require manual configuration correction",
                        refused.Count);
            }

            // Stage 6: Validate
            var isValid = validationErrors.Count == 0;

            if (options.ValidateConfig)
            {
                var validator = ConfigValidationPipeline.CreateDefault();
                var results = validator.Validate(config);

                foreach (var result in results)
                {
                    var message = $"{result.Property}: {result.Message}";
                    if (result.IsError)
                    {
                        validationErrors.Add(message);
                        _log.Error("Configuration validation error: {Message}", message);
                    }
                    else if (result.Severity == ConfigValidationSeverity.Warning)
                    {
                        warnings.Add(message);
                        _log.Warning("Configuration validation warning: {Message}", message);
                    }
                }

                isValid = validationErrors.Count == 0;

                if (!isValid)
                {
                    _log.Warning("Configuration validation failed with {ErrorCount} errors", validationErrors.Count);
                }
            }

            // Stage 7: Return validated config
            return ValidatedConfig.FromConfig(
                config,
                isValid,
                sourcePath,
                validationErrors,
                appliedFixes,
                warnings,
                environmentName,
                source,
                blockedFixes);
        }
        catch (Exception ex)
        {
            _log.Error(ex, "Configuration pipeline failed");
            return ValidatedConfig.Failed(
                config,
                new[] { $"Pipeline error: {ex.Message}" },
                sourcePath,
                source);
        }
    }



    private static readonly string[] PlaceholderValues =
        ["__SET_ME__", "your-key-here", "your-secret-here", "REPLACE_ME", "ENTER_YOUR", "INSERT_YOUR", "TODO", "xxx"];

    private static bool IsLikelyRealCredential(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return false;

        // Skip placeholder values
        return !PlaceholderValues.Any(p =>
            value.Contains(p, StringComparison.OrdinalIgnoreCase));
    }

    private void WarnIfCredentialsInConfigFile(AppConfig config, List<string> warnings)
    {
        var credentialFields = new List<(string fieldName, string? value, string envVar)>();

        // Alpaca credentials
        if (config.Alpaca != null)
        {
            credentialFields.Add(("Alpaca.KeyId", config.Alpaca.KeyId, "ALPACA__KEYID"));
            credentialFields.Add(("Alpaca.SecretKey", config.Alpaca.SecretKey, "ALPACA__SECRETKEY"));
        }

        // Polygon credentials
        if (config.Polygon != null)
        {
            credentialFields.Add(("Polygon.ApiKey", config.Polygon.ApiKey, "POLYGON__APIKEY"));
        }

        // Backfill provider credentials
        if (config.Backfill?.Providers != null)
        {
            var providers = config.Backfill.Providers;
            if (providers.Tiingo != null)
                credentialFields.Add(("Backfill.Providers.Tiingo.ApiToken", providers.Tiingo.ApiToken, "TIINGO__TOKEN"));
            if (providers.Finnhub != null)
                credentialFields.Add(("Backfill.Providers.Finnhub.ApiKey", providers.Finnhub.ApiKey, "FINNHUB__TOKEN"));
            if (providers.AlphaVantage != null)
                credentialFields.Add(("Backfill.Providers.AlphaVantage.ApiKey", providers.AlphaVantage.ApiKey, "ALPHAVANTAGE__APIKEY"));
            if (providers.Fred != null)
                credentialFields.Add(("Backfill.Providers.Fred.ApiKey", providers.Fred.ApiKey, "FRED__APIKEY"));
            if (providers.Polygon != null)
                credentialFields.Add(("Backfill.Providers.Polygon.ApiKey", providers.Polygon.ApiKey, "POLYGON__APIKEY"));
            if (providers.Nasdaq != null)
                credentialFields.Add(("Backfill.Providers.Nasdaq.ApiKey", providers.Nasdaq.ApiKey, "NASDAQ__APIKEY"));
        }

        foreach (var (fieldName, value, envVar) in credentialFields)
        {
            if (IsLikelyRealCredential(value))
            {
                var msg = $"Credential '{fieldName}' appears to be set directly in the config file. " +
                          $"Use environment variable {envVar} instead to avoid accidental commits.";
                _log.Warning(msg);
                warnings.Add(msg);
            }
        }
    }

    private void WarnAboutProviderSpecificSymbolFields(AppConfig config, List<string> warnings)
    {
        if (config.Symbols == null || config.Symbols.Length == 0)
            return;

        // IB-specific fields that have non-default values
        var isIB = config.DataSource == DataSourceKind.IB;
        if (isIB)
            return; // No warning needed when using IB

        var providerName = config.DataSource.ToString();
        foreach (var symbol in config.Symbols)
        {
            var ibFields = new List<string>();

            if (!string.IsNullOrEmpty(symbol.PrimaryExchange))
                ibFields.Add("PrimaryExchange");
            if (!string.IsNullOrEmpty(symbol.LocalSymbol))
                ibFields.Add("LocalSymbol");
            if (!string.IsNullOrEmpty(symbol.TradingClass))
                ibFields.Add("TradingClass");
            if (symbol.ConId.HasValue)
                ibFields.Add("ConId");
            if (symbol.SecurityType != "STK")
                ibFields.Add("SecurityType");
            if (symbol.Exchange != "SMART")
                ibFields.Add("Exchange");

            if (ibFields.Count > 0)
            {
                var fields = string.Join(", ", ibFields);
                var msg = $"Symbol {symbol.Symbol} has IB-specific fields ({fields}) " +
                          $"but the active provider is {providerName} -- these fields will be ignored.";
                _log.Information(msg);
                warnings.Add(msg);
            }
        }
    }



    private AppConfig ResolveAllCredentials(AppConfig config)
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
    /// Discovers and conditionally applies self-healing fixes to <paramref name="config"/>
    /// based on the requested <paramref name="strictness"/> level.
    /// </summary>
    /// <returns>
    /// A tuple of:
    /// <list type="bullet">
    /// <item><description><c>Config</c> — the (potentially modified) configuration.</description></item>
    /// <item><description><c>Applied</c> — fixes that were applied to the config.</description></item>
    /// <item><description><c>Refused</c> — <see cref="SelfHealingSeverity.Warn"/>-level fixes that
    /// were <em>not</em> applied because <paramref name="strictness"/> is
    /// <see cref="SelfHealingStrictness.Production"/>.</description></item>
    /// <item><description><c>Warnings</c> — advisory messages that are not actionable fixes.</description></item>
    /// </list>
    /// </returns>
    private (AppConfig Config,
             IReadOnlyList<SelfHealingFix> Applied,
             IReadOnlyList<SelfHealingFix> Refused,
             IReadOnlyList<string> Warnings)
        ApplySelfHealingFixes(AppConfig config, SelfHealingStrictness strictness)
    {
        var applied = new List<SelfHealingFix>();
        var refused = new List<SelfHealingFix>();
        var warnings = new List<string>();

        // Helper: attempt to apply a fix, routing to applied/refused based on severity + strictness.
        void TryFix(
            SelfHealingSeverity severity,
            string description,
            Func<AppConfig, AppConfig> applyFn)
        {
            bool apply = severity == SelfHealingSeverity.AutoFix
                         || strictness == SelfHealingStrictness.Development;

            var fix = new SelfHealingFix(description, severity);
            if (apply)
            {
                config = applyFn(config);
                applied.Add(fix);
            }
            else
            {
                refused.Add(fix);
            }
        }

        // ── Fix: Alpaca selected but no credentials (AutoFix — safe credential injection) ──
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

        // ── Fix: IB selected but gateway not available (Warn — switches active provider) ──
        if (config.DataSource == DataSourceKind.IB && !IsIBGatewayAvailable())
        {
            var credentials = CreateCredentialContext(typeof(AlpacaHistoricalDataProvider));
            var keyId = credentials.Get("ALPACA_KEY_ID");
            var secretKey = credentials.Get("ALPACA_SECRET_KEY");
            if (!string.IsNullOrEmpty(keyId) && !string.IsNullOrEmpty(secretKey))
            {
                TryFix(
                    SelfHealingSeverity.Warn,
                    "Switched active data provider from IB to Alpaca (IB Gateway not detected)",
                    c => c with
                    {
                        DataSource = DataSourceKind.Alpaca,
                        Alpaca = new AlpacaOptions(keyId, secretKey)
                    });
            }
            else
            {
                warnings.Add("IB Gateway not detected and no alternative real-time providers configured.");
            }
        }

        // ── Fix: Invalid storage naming convention (AutoFix — normalises format string) ──
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

            // ── Fix: Invalid date partition (AutoFix — normalises format string) ──
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

        // ── Fix: Empty symbols list (Warn — adds a default symbol the operator didn't configure) ──
        if (config.Symbols == null || config.Symbols.Length == 0)
        {
            TryFix(
                SelfHealingSeverity.Warn,
                "Added default symbol (SPY) because no symbols were configured — set Symbols explicitly to silence this",
                c => c with
                {
                    Symbols = new[]
                    {
                        new SymbolConfig("SPY", SubscribeTrades: true, SubscribeDepth: true, DepthLevels: 10)
                    }
                });
        }

        // ── Fix: Invalid depth levels (AutoFix — clamps numeric range) ──
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
                    "Adjusted depth levels to valid range (1–50)",
                    c => c with { Symbols = fixedSymbols });
            }
        }

        // ── Fix: Backfill date range issues ──
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
                    c => c with
                    {
                        Backfill = c.Backfill! with { From = swappedFrom, To = swappedTo }
                    });
                // Refresh local backfill snapshot so the future-date check sees the swapped values.
                backfill = config.Backfill!;
            }

            // AutoFix: Future end date (safe adjustment)
            var today = DateOnly.FromDateTime(DateTime.UtcNow);
            if (backfill.To.HasValue && backfill.To > today)
            {
                TryFix(
                    SelfHealingSeverity.AutoFix,
                    $"Adjusted backfill To date to today ({today:yyyy-MM-dd}) — was in the future",
                    c => c with { Backfill = c.Backfill! with { To = today } });
            }
        }

        _log.Debug(
            "Self-healing: {AppliedCount} fix(es) applied, {RefusedCount} refused, {WarningCount} warning(s)",
            applied.Count, refused.Count, warnings.Count);

        return (config, applied, refused, warnings);
    }

    private ICredentialContext CreateCredentialContext(
        Type providerType,
        IReadOnlyDictionary<string, string?>? configuredValues = null)
    {
        return _credentialResolver.CreateContext(providerType, configuredValues);
    }

    private static bool IsIBGatewayAvailable()
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

    private static string? GetEnvironmentName()
    {
        var env = Environment.GetEnvironmentVariable("MDC_ENVIRONMENT");
        if (!string.IsNullOrWhiteSpace(env))
            return env;

        return Environment.GetEnvironmentVariable("DOTNET_ENVIRONMENT");
    }

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


    public ValueTask DisposeAsync()
    {
        StopHotReload();
        return ValueTask.CompletedTask;
    }
}

/// <summary>
/// Options for controlling the configuration pipeline behavior.
/// </summary>
public sealed record PipelineOptions
{
    /// <summary>
    /// Whether to apply self-healing fixes to the configuration.
    /// </summary>
    public bool ApplySelfHealing { get; init; } = true;

    /// <summary>
    /// Whether to validate the configuration.
    /// </summary>
    public bool ValidateConfig { get; init; } = true;

    /// <summary>
    /// Controls how aggressively self-healing behaviours are applied.
    /// <see cref="SelfHealingStrictness.Development"/> (default) applies all fixes and logs
    /// significant ones as warnings.  <see cref="SelfHealingStrictness.Production"/> applies
    /// only cosmetic <see cref="SelfHealingSeverity.AutoFix"/> changes and refuses
    /// <see cref="SelfHealingSeverity.Warn"/>-level fixes with a startup error so that
    /// operators must correct the configuration manually.
    /// </summary>
    public SelfHealingStrictness HealingStrictness { get; init; } = SelfHealingStrictness.Development;

    /// <summary>
    /// Default options — self-healing and validation enabled, strictness from
    /// <c>MDC_CONFIG_STRICTNESS</c> environment variable (defaults to Development).
    /// </summary>
    public static PipelineOptions Default { get; } = new()
    {
        HealingStrictness = ParseStrictness(Environment.GetEnvironmentVariable("MDC_CONFIG_STRICTNESS"))
    };

    /// <summary>
    /// Options for strict validation without self-healing.
    /// </summary>
    public static PipelineOptions Strict { get; } = new()
    {
        ApplySelfHealing = false,
        ValidateConfig = true
    };

    /// <summary>
    /// Options for lenient loading (no validation, no self-healing).
    /// </summary>
    public static PipelineOptions Lenient { get; } = new()
    {
        ApplySelfHealing = false,
        ValidateConfig = false
    };

    /// <summary>
    /// Parses the <c>MDC_CONFIG_STRICTNESS</c> environment variable (or any string value)
    /// into a <see cref="SelfHealingStrictness"/> level.
    /// Recognised values: <c>production</c>, <c>prod</c>, <c>strict</c> (case-insensitive).
    /// Everything else — including null — resolves to <see cref="SelfHealingStrictness.Development"/>.
    /// </summary>
    internal static SelfHealingStrictness ParseStrictness(string? value) =>
        value?.Trim().ToLowerInvariant() switch
        {
            "production" or "prod" or "strict" => SelfHealingStrictness.Production,
            _ => SelfHealingStrictness.Development
        };
}

/// <summary>
/// Controls how self-healing reacts to significant configuration issues.
/// </summary>
public enum SelfHealingStrictness : byte
{
    /// <summary>
    /// All self-healing fixes are applied.  <see cref="SelfHealingSeverity.Warn"/>-level
    /// changes are applied but logged as <c>Warning</c>.  Suitable for local development
    /// and CI environments where operator attention is not required for every startup.
    /// </summary>
    Development = 0,

    /// <summary>
    /// Only safe cosmetic fixes (<see cref="SelfHealingSeverity.AutoFix"/>) are applied
    /// silently.  <see cref="SelfHealingSeverity.Warn"/>-level fixes are <b>refused</b>
    /// and surfaced as validation errors so that the process cannot start until a human
    /// corrects the configuration.  Use <c>MDC_CONFIG_STRICTNESS=production</c> to enable.
    /// </summary>
    Production = 1
}

/// <summary>
/// Indicates how impactful a self-healing fix is, which determines how it is handled
/// under different <see cref="SelfHealingStrictness"/> levels.
/// </summary>
public enum SelfHealingSeverity : byte
{
    /// <summary>
    /// Safe, cosmetic correction (e.g., clamping an out-of-range value, normalising a
    /// format string, resolving credentials from environment variables).  Applied silently
    /// in all environments.
    /// </summary>
    AutoFix = 0,

    /// <summary>
    /// Significant behavioural change (e.g., switching the active data provider, adding a
    /// default symbol set).  Applied with a <c>Warning</c>-level log entry in
    /// <see cref="SelfHealingStrictness.Development"/>; <b>refused</b> with a startup error
    /// in <see cref="SelfHealingStrictness.Production"/>.
    /// </summary>
    Warn = 1
}

/// <summary>
/// Describes a single self-healing change that was proposed by
/// <see cref="ConfigurationPipeline"/>.
/// </summary>
/// <param name="Description">Human-readable description of the change.</param>
/// <param name="Severity">How impactful the change is.</param>
public sealed record SelfHealingFix(string Description, SelfHealingSeverity Severity);
