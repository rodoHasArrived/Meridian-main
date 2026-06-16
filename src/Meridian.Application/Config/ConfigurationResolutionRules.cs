using Meridian.Application.Config.Credentials;
using Meridian.Application.Services;
using Meridian.Application.UI;
using Meridian.Core.Config;
using Meridian.Infrastructure.Adapters.Alpaca;
using Meridian.Infrastructure.Adapters.AlphaVantage;
using Meridian.Infrastructure.Adapters.Finnhub;
using Meridian.Infrastructure.Adapters.Fred;
using Meridian.Infrastructure.Adapters.NasdaqDataLink;
using Meridian.Infrastructure.Adapters.Polygon;
using Meridian.Infrastructure.Adapters.Tiingo;
using Serilog;

namespace Meridian.Application.Config;

/// <summary>
/// Shared, side-effect-light configuration resolution rules used by the pipeline and
/// application-facing configuration service.
/// </summary>
internal static class ConfigurationResolutionRules
{
    internal static AppConfig ResolveAllCredentials(
        AppConfig config,
        ProviderCredentialResolver credentialResolver)
    {
        ArgumentNullException.ThrowIfNull(credentialResolver);

        if (config.DataSource == DataSourceKind.Alpaca || config.Alpaca != null)
        {
            var credentials = credentialResolver.CreateContext(
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

        if (config.DataSource == DataSourceKind.Polygon || config.Polygon != null)
        {
            var apiKey = credentialResolver.CreateContext(
                typeof(PolygonHistoricalDataProvider),
                new Dictionary<string, string?>(StringComparer.Ordinal)
                {
                    ["POLYGON_API_KEY"] = config.Polygon?.ApiKey
                }).Get("POLYGON_API_KEY");
            if (!string.IsNullOrEmpty(apiKey) && config.Polygon != null)
                config = config with { Polygon = config.Polygon with { ApiKey = apiKey } };
        }

        if (config.Backfill?.Providers is { } providers)
        {
            var updated = false;

            if (providers.Alpaca != null)
            {
                var credentials = credentialResolver.CreateContext(
                    typeof(AlpacaHistoricalDataProvider),
                    new Dictionary<string, string?>(StringComparer.Ordinal)
                    {
                        ["ALPACA_KEY_ID"] = providers.Alpaca.KeyId,
                        ["ALPACA_SECRET_KEY"] = providers.Alpaca.SecretKey
                    });
                var keyId = credentials.Get("ALPACA_KEY_ID");
                var secretKey = credentials.Get("ALPACA_SECRET_KEY");
                if (!string.IsNullOrEmpty(keyId) && !string.IsNullOrEmpty(secretKey))
                {
                    providers = providers with { Alpaca = providers.Alpaca with { KeyId = keyId, SecretKey = secretKey } };
                    updated = true;
                }
            }

            if (providers.Tiingo != null)
                (providers, updated) = ResolveProviderToken(providers, updated, credentialResolver, typeof(TiingoHistoricalDataProvider), "TIINGO_API_TOKEN", providers.Tiingo.ApiToken, token => providers with { Tiingo = providers.Tiingo! with { ApiToken = token } });
            if (providers.Finnhub != null)
                (providers, updated) = ResolveProviderToken(providers, updated, credentialResolver, typeof(FinnhubHistoricalDataProvider), "FINNHUB_API_KEY", providers.Finnhub.ApiKey, key => providers with { Finnhub = providers.Finnhub! with { ApiKey = key } });
            if (providers.Polygon != null)
                (providers, updated) = ResolveProviderToken(providers, updated, credentialResolver, typeof(PolygonHistoricalDataProvider), "POLYGON_API_KEY", providers.Polygon.ApiKey, key => providers with { Polygon = providers.Polygon! with { ApiKey = key } });
            if (providers.AlphaVantage != null)
                (providers, updated) = ResolveProviderToken(providers, updated, credentialResolver, typeof(AlphaVantageHistoricalDataProvider), "ALPHA_VANTAGE_API_KEY", providers.AlphaVantage.ApiKey, key => providers with { AlphaVantage = providers.AlphaVantage! with { ApiKey = key } });
            if (providers.Nasdaq != null)
                (providers, updated) = ResolveProviderToken(providers, updated, credentialResolver, typeof(NasdaqDataLinkHistoricalDataProvider), "NASDAQ_DATA_LINK_API_KEY", providers.Nasdaq.ApiKey, key => providers with { Nasdaq = providers.Nasdaq! with { ApiKey = key } });
            if (providers.Fred != null)
                (providers, updated) = ResolveProviderToken(providers, updated, credentialResolver, typeof(FredHistoricalDataProvider), "FRED_API_KEY", providers.Fred.ApiKey, key => providers with { Fred = providers.Fred! with { ApiKey = key } });

            if (updated)
                config = config with { Backfill = config.Backfill with { Providers = providers } };
        }

        return config;
    }

    private static (BackfillProvidersConfig Providers, bool Updated) ResolveProviderToken(
        BackfillProvidersConfig providers,
        bool updated,
        ProviderCredentialResolver credentialResolver,
        Type providerType,
        string key,
        string? configuredValue,
        Func<string, BackfillProvidersConfig> apply)
    {
        var value = credentialResolver.CreateContext(providerType, new Dictionary<string, string?>(StringComparer.Ordinal) { [key] = configuredValue }).Get(key);
        return string.IsNullOrEmpty(value) ? (providers, updated) : (apply(value), true);
    }

    internal static AppConfig ApplyEnvironmentOverlay(AppConfig baseConfig, string basePath, ILogger log)
    {
        var envPath = ResolveEnvironmentOverlayPath(basePath);
        if (envPath is null)
            return baseConfig;

        try
        {
            log.Information("Loading environment-specific configuration: {EnvPath}", envPath);
            return MergeConfigs(baseConfig, ConfigStore.LoadConfig(envPath));
        }
        catch (Exception ex)
        {
            log.Warning(ex, "Failed to load environment config {EnvPath}", envPath);
            return baseConfig;
        }
    }

    internal static string? ResolveEnvironmentOverlayPath(string basePath)
    {
        var envName = GetEnvironmentName();
        if (string.IsNullOrWhiteSpace(envName))
            return null;
        var directory = Path.GetDirectoryName(basePath) ?? ".";
        var fileName = Path.GetFileNameWithoutExtension(basePath);
        var extension = Path.GetExtension(basePath);
        var envPath = Path.Combine(directory, $"{fileName}.{envName}{extension}");
        return File.Exists(envPath) ? envPath : null;
    }

    internal static string? GetEnvironmentName()
    {
        var env = Environment.GetEnvironmentVariable("MDC_ENVIRONMENT");
        return string.IsNullOrWhiteSpace(env) ? Environment.GetEnvironmentVariable("DOTNET_ENVIRONMENT") : env;
    }

    internal static AppConfig MergeConfigs(AppConfig baseConfig, AppConfig overlay) => baseConfig with
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

    internal static SelfHealingResolution ApplySelfHealingFixes(
        AppConfig config,
        SelfHealingStrictness strictness,
        ProviderCredentialResolver credentialResolver,
        Func<bool> isIBGatewayAvailable,
        Func<AutoConfigurationService.DetectedProvider?>? getBestRealTimeProvider = null)
    {
        var applied = new List<SelfHealingFix>();
        var refused = new List<SelfHealingFix>();
        var warnings = new List<string>();

        void TryFix(SelfHealingSeverity severity, string description, Func<AppConfig, AppConfig> applyFn)
        {
            var fix = new SelfHealingFix(description, severity);
            if (severity == SelfHealingSeverity.AutoFix || strictness == SelfHealingStrictness.Development)
            {
                config = applyFn(config);
                applied.Add(fix);
            }
            else
            {
                refused.Add(fix);
            }
        }

        config = ResolveAllCredentials(config, credentialResolver);

        if (config.DataSource == DataSourceKind.Alpaca && (config.Alpaca == null || string.IsNullOrEmpty(config.Alpaca.KeyId)))
            warnings.Add("Alpaca selected but no credentials found. Set ALPACA_KEY_ID and ALPACA_SECRET_KEY environment variables.");

        if (config.DataSource == DataSourceKind.IB && !isIBGatewayAvailable())
        {
            var bestProvider = getBestRealTimeProvider?.Invoke();
            if (bestProvider != null && Enum.TryParse<DataSourceKind>(bestProvider.Name, out var kind))
            {
                TryFix(SelfHealingSeverity.Warn, $"Switched active data provider from IB to {bestProvider.DisplayName} (IB Gateway not detected)", c => c with { DataSource = kind });
            }
            else
            {
                var credentials = credentialResolver.CreateContext(typeof(AlpacaHistoricalDataProvider));
                var keyId = credentials.Get("ALPACA_KEY_ID");
                var secretKey = credentials.Get("ALPACA_SECRET_KEY");
                if (!string.IsNullOrEmpty(keyId) && !string.IsNullOrEmpty(secretKey))
                    TryFix(SelfHealingSeverity.Warn, "Switched active data provider from IB to Alpaca (IB Gateway not detected)", c => c with { DataSource = DataSourceKind.Alpaca, Alpaca = new AlpacaOptions(keyId, secretKey) });
                else
                    warnings.Add("IB Gateway not detected and no alternative real-time providers configured.");
            }
        }

        RepairStorage(config, TryFix);
        RepairSymbols(config, TryFix);
        RepairBackfill(config, TryFix);

        return new SelfHealingResolution(config, applied, refused, warnings);
    }

    private static void RepairStorage(AppConfig config, Action<SelfHealingSeverity, string, Func<AppConfig, AppConfig>> tryFix)
    {
        if (config.Storage == null)
            return;
        var validConventions = new[] { "flat", "bysymbol", "bydate", "bytype", "bysource", "byassetclass", "hierarchical", "canonical" };
        if (!validConventions.Contains(config.Storage.NamingConvention.ToLowerInvariant()))
        {
            var oldValue = config.Storage.NamingConvention;
            tryFix(SelfHealingSeverity.AutoFix, $"Invalid naming convention '{oldValue}' changed to 'BySymbol'", c => c with { Storage = c.Storage! with { NamingConvention = "BySymbol" } });
        }

        var validPartitions = new[] { "none", "daily", "hourly", "monthly" };
        if (!validPartitions.Contains(config.Storage.DatePartition.ToLowerInvariant()))
        {
            var oldValue = config.Storage.DatePartition;
            tryFix(SelfHealingSeverity.AutoFix, $"Invalid date partition '{oldValue}' changed to 'Daily'", c => c with { Storage = c.Storage! with { DatePartition = "Daily" } });
        }
    }

    private static void RepairSymbols(AppConfig config, Action<SelfHealingSeverity, string, Func<AppConfig, AppConfig>> tryFix)
    {
        if (config.Symbols == null || config.Symbols.Length == 0)
        {
            tryFix(SelfHealingSeverity.Warn, "Added default symbol (SPY) since none were configured", c => c with { Symbols = new[] { new SymbolConfig("SPY", SubscribeTrades: true, SubscribeDepth: true, DepthLevels: 10) } });
            return;
        }

        var fixedSymbols = config.Symbols.Select(s => s.SubscribeDepth && (s.DepthLevels < 1 || s.DepthLevels > 50) ? s with { DepthLevels = Math.Clamp(s.DepthLevels, 1, 50) } : s).ToArray();
        if (!config.Symbols.SequenceEqual(fixedSymbols))
            tryFix(SelfHealingSeverity.AutoFix, "Adjusted depth levels to valid range (1-50)", c => c with { Symbols = fixedSymbols });
    }

    private static void RepairBackfill(AppConfig config, Action<SelfHealingSeverity, string, Func<AppConfig, AppConfig>> tryFix)
    {
        if (config.Backfill == null)
            return;
        var backfill = config.Backfill;
        if (backfill.From.HasValue && backfill.To.HasValue && backfill.From > backfill.To)
        {
            var (swappedFrom, swappedTo) = (backfill.To, backfill.From);
            tryFix(SelfHealingSeverity.AutoFix, "Swapped backfill From/To dates (From was after To)", c => c with { Backfill = c.Backfill! with { From = swappedFrom, To = swappedTo } });
            backfill = backfill with { From = swappedFrom, To = swappedTo };
        }

        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        if (backfill.To.HasValue && backfill.To > today)
            tryFix(SelfHealingSeverity.AutoFix, $"Adjusted backfill To date to today ({today:yyyy-MM-dd}) - was in the future", c => c with { Backfill = c.Backfill! with { To = today } });
    }
}

internal sealed record SelfHealingResolution(
    AppConfig Config,
    IReadOnlyList<SelfHealingFix> Applied,
    IReadOnlyList<SelfHealingFix> Refused,
    IReadOnlyList<string> Warnings);
