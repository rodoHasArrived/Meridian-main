using System.Text.Json;
using Meridian.Application.Config;
using Meridian.Application.Logging;
using Meridian.Application.Wizard.Metadata;
using Meridian.Storage;
using Serilog;

namespace Meridian.Application.Services;

/// <summary>
/// Service for automatic configuration based on available credentials and environment.
/// Implements user-friendly auto-detection and configuration of data providers.
/// </summary>
public sealed class AutoConfigurationService
{
    private readonly ILogger _log = LoggingSetup.ForContext<AutoConfigurationService>();

    /// <summary>
    /// Result of provider detection.
    /// </summary>
    public sealed record DetectedProvider(
        string Name,
        string DisplayName,
        bool HasCredentials,
        string[] MissingCredentials,
        bool IsRecommended,
        int SuggestedPriority,
        string[] Capabilities
    );

    /// <summary>
    /// Result of auto-configuration analysis.
    /// </summary>
    public sealed record AutoConfigResult(
        bool Success,
        AppConfig Config,
        IReadOnlyList<DetectedProvider> DetectedProviders,
        IReadOnlyList<string> AppliedFixes,
        IReadOnlyList<string> Recommendations,
        IReadOnlyList<string> Warnings
    );

    /// <summary>
    /// Detects all available providers based on environment variables.
    /// Uses <see cref="ProviderRegistry"/> as the single source of provider metadata.
    /// </summary>
    public IReadOnlyList<DetectedProvider> DetectAvailableProviders()
    {
        var providers = new List<DetectedProvider>();

        foreach (var descriptor in ProviderRegistry.All)
        {
            var (hasCredentials, missing) = CheckCredentials(descriptor);

            providers.Add(new DetectedProvider(
                Name: descriptor.Name,
                DisplayName: descriptor.DisplayName,
                HasCredentials: hasCredentials,
                MissingCredentials: missing,
                IsRecommended: hasCredentials && descriptor.Capabilities.Contains("RealTime"),
                SuggestedPriority: descriptor.Priority,
                Capabilities: descriptor.Capabilities
            ));
        }

        return providers.OrderBy(p => p.HasCredentials ? 0 : 1)
                        .ThenBy(p => p.SuggestedPriority)
                        .ToList();
    }

    /// <summary>
    /// Auto-configures the application based on detected providers and environment.
    /// </summary>
    public AutoConfigResult AutoConfigure(AppConfig? existingConfig = null)
    {
        var config = existingConfig ?? new AppConfig();
        var appliedFixes = new List<string>();
        var recommendations = new List<string>();
        var warnings = new List<string>();

        var detectedProviders = DetectAvailableProviders();
        var availableRealTime = detectedProviders
            .Where(p => p.HasCredentials && p.Capabilities.Contains("RealTime"))
            .ToList();
        var availableHistorical = detectedProviders
            .Where(p => p.HasCredentials && p.Capabilities.Contains("Historical"))
            .ToList();

        _log.Information("Auto-configuration: Detected {RealTimeCount} real-time providers, {HistoricalCount} historical providers",
            availableRealTime.Count, availableHistorical.Count);

        // Auto-configure real-time data source
        if (config.DataSource == DataSourceKind.IB && !IsIBGatewayAvailable())
        {
            if (availableRealTime.Any())
            {
                var bestProvider = availableRealTime.First();
                if (Enum.TryParse<DataSourceKind>(bestProvider.Name, out var dataSourceKind))
                {
                    config = config with { DataSource = dataSourceKind };
                    appliedFixes.Add($"Switched from IB to {bestProvider.DisplayName} (IB Gateway not detected)");
                }
            }
            else
            {
                warnings.Add("No real-time data providers configured. Set ALPACA_KEY_ID/ALPACA_SECRET_KEY or POLYGON_API_KEY environment variables.");
            }
        }

        // Auto-configure Alpaca credentials if available
        config = AutoConfigureAlpaca(config, appliedFixes);

        // Auto-configure Polygon credentials if available
        config = AutoConfigurePolygon(config, appliedFixes);

        // Auto-configure backfill providers
        config = AutoConfigureBackfill(config, availableHistorical, appliedFixes);

        // Auto-configure storage based on environment
        config = AutoConfigureStorage(config, appliedFixes);

        // Apply self-healing fixes
        config = ApplySelfHealingFixes(config, appliedFixes, warnings);

        // Generate recommendations
        GenerateRecommendations(config, detectedProviders, recommendations);

        return new AutoConfigResult(
            Success: true,
            Config: config,
            DetectedProviders: detectedProviders,
            AppliedFixes: appliedFixes,
            Recommendations: recommendations,
            Warnings: warnings
        );
    }

    /// <summary>
    /// Generates configuration for first-time users with interactive defaults.
    /// </summary>
    public AppConfig GenerateFirstTimeConfig(FirstTimeConfigOptions options)
    {
        var config = new AppConfig();
        var detectedProviders = DetectAvailableProviders();

        // Select data source
        if (options.UseCase == UseCase.RealTimeTrading)
        {
            var realTimeProvider = detectedProviders
                .FirstOrDefault(p => p.HasCredentials && p.Capabilities.Contains("RealTime"));

            if (realTimeProvider != null && Enum.TryParse<DataSourceKind>(realTimeProvider.Name, out var kind))
            {
                config = config with { DataSource = kind };
            }
        }

        // Configure Alpaca if credentials are available
        var alpacaKeyId = GetEnvVar("ALPACA_KEY_ID", "MDC_ALPACA_KEY_ID");
        var alpacaSecret = GetEnvVar("ALPACA_SECRET_KEY", "MDC_ALPACA_SECRET_KEY");
        if (!string.IsNullOrEmpty(alpacaKeyId) && !string.IsNullOrEmpty(alpacaSecret))
        {
            config = config with
            {
                Alpaca = new AlpacaOptions(
                    KeyId: alpacaKeyId,
                    SecretKey: alpacaSecret,
                    Feed: options.UseCase == UseCase.RealTimeTrading ? "sip" : "iex",
                    UseSandbox: options.UseCase == UseCase.Development
                )
            };
        }

        // Set up symbols based on use case
        config = config with
        {
            Symbols = GetDefaultSymbols(options.SymbolPreset)
        };

        // Configure storage using profiles based on use case
        var storageProfile = options.UseCase switch
        {
            UseCase.Development => StorageProfilePresets.DefaultProfile, // Research for dev
            UseCase.Research => StorageProfilePresets.DefaultProfile, // Research
            UseCase.RealTimeTrading => "LowLatency", // Low latency for trading
            UseCase.Production => "Archival", // Archival for production
            _ => StorageProfilePresets.DefaultProfile
        };

        config = config with
        {
            Storage = new StorageConfig(
                Profile: storageProfile,
                RetentionDays: options.UseCase == UseCase.Development ? 30 : null
            ),
            Compress = options.UseCase != UseCase.Development
        };

        // Configure backfill
        if (options.EnableBackfill)
        {
            config = config with
            {
                Backfill = GenerateBackfillConfig(detectedProviders)
            };
        }

        return config;
    }

    private AppConfig AutoConfigureAlpaca(AppConfig config, List<string> appliedFixes)
    {
        var keyId = GetEnvVar("ALPACA_KEY_ID", "MDC_ALPACA_KEY_ID");
        var secretKey = GetEnvVar("ALPACA_SECRET_KEY", "MDC_ALPACA_SECRET_KEY");

        if (string.IsNullOrEmpty(keyId) || string.IsNullOrEmpty(secretKey))
            return config;

        // Only auto-configure if not already set or using placeholder
        if (config.Alpaca == null ||
            string.IsNullOrEmpty(config.Alpaca.KeyId) ||
            config.Alpaca.KeyId.StartsWith("YOUR_") ||
            config.Alpaca.KeyId.StartsWith("${"))
        {
            var feed = GetEnvVar("ALPACA_FEED", "MDC_ALPACA_FEED") ?? "iex";
            var sandbox = GetEnvVar("ALPACA_SANDBOX", "MDC_ALPACA_SANDBOX");

            config = config with
            {
                Alpaca = new AlpacaOptions(
                    KeyId: keyId,
                    SecretKey: secretKey,
                    Feed: feed,
                    UseSandbox: sandbox?.Equals("true", StringComparison.OrdinalIgnoreCase) ?? false
                )
            };
            appliedFixes.Add("Auto-configured Alpaca credentials from environment variables");
        }

        return config;
    }

    private AppConfig AutoConfigurePolygon(AppConfig config, List<string> appliedFixes)
    {
        var apiKey = GetEnvVar("POLYGON_API_KEY", "MDC_POLYGON_API_KEY");

        if (string.IsNullOrEmpty(apiKey))
            return config;

        if (config.Polygon == null || string.IsNullOrEmpty(config.Polygon.ApiKey))
        {
            config = config with
            {
                Polygon = new PolygonOptions(
                    ApiKey: apiKey,
                    Feed: "stocks",
                    SubscribeTrades: true
                )
            };
            appliedFixes.Add("Auto-configured Polygon credentials from environment variables");
        }

        return config;
    }

    private AppConfig AutoConfigureBackfill(AppConfig config, IReadOnlyList<DetectedProvider> availableHistorical, List<string> appliedFixes)
    {
        if (config.Backfill?.Providers != null)
            return config;

        var providers = new BackfillProvidersConfig();
        var priorityOrder = new List<string>();

        // Auto-enable providers with credentials
        foreach (var provider in availableHistorical.OrderBy(p => p.SuggestedPriority))
        {
            switch (provider.Name)
            {
                case "Alpaca":
                    var alpacaKey = GetEnvVar("ALPACA_KEY_ID", "MDC_ALPACA_KEY_ID");
                    var alpacaSecret = GetEnvVar("ALPACA_SECRET_KEY", "MDC_ALPACA_SECRET_KEY");
                    providers = providers with
                    {
                        Alpaca = new AlpacaBackfillConfig(
                            Enabled: true,
                            KeyId: alpacaKey,
                            SecretKey: alpacaSecret,
                            Priority: provider.SuggestedPriority
                        )
                    };
                    priorityOrder.Add("alpaca");
                    break;

                case "Tiingo":
                    var tiingoToken = GetEnvVar("TIINGO_API_TOKEN", "TIINGO_TOKEN", "MDC_TIINGO_TOKEN");
                    providers = providers with
                    {
                        Tiingo = new TiingoConfig(
                            Enabled: true,
                            ApiToken: tiingoToken,
                            Priority: provider.SuggestedPriority
                        )
                    };
                    priorityOrder.Add("tiingo");
                    break;

                case "Polygon":
                    var polygonKey = GetEnvVar("POLYGON_API_KEY", "MDC_POLYGON_API_KEY");
                    providers = providers with
                    {
                        Polygon = new PolygonConfig(
                            Enabled: true,
                            ApiKey: polygonKey,
                            Priority: provider.SuggestedPriority
                        )
                    };
                    priorityOrder.Add("polygon");
                    break;

                case "Finnhub":
                    var finnhubKey = GetEnvVar("FINNHUB_API_KEY", "MDC_FINNHUB_API_KEY");
                    providers = providers with
                    {
                        Finnhub = new FinnhubConfig(
                            Enabled: true,
                            ApiKey: finnhubKey,
                            Priority: provider.SuggestedPriority
                        )
                    };
                    priorityOrder.Add("finnhub");
                    break;

                case "AlphaVantage":
                    var avKey = GetEnvVar("ALPHA_VANTAGE_API_KEY", "ALPHAVANTAGE_API_KEY", "MDC_ALPHA_VANTAGE_API_KEY");
                    providers = providers with
                    {
                        AlphaVantage = new AlphaVantageConfig(
                            Enabled: true,
                            ApiKey: avKey,
                            Priority: provider.SuggestedPriority
                        )
                    };
                    priorityOrder.Add("alphavantage");
                    break;

                case "FRED":
                    var fredKey = GetEnvVar("FRED_API_KEY", "FRED__APIKEY", "MDC_FRED_API_KEY");
                    providers = providers with
                    {
                        Fred = new FredConfig(
                            Enabled: true,
                            ApiKey: fredKey,
                            Priority: provider.SuggestedPriority
                        )
                    };
                    priorityOrder.Add("fred");
                    break;

                case "Yahoo":
                    providers = providers with
                    {
                        Yahoo = new YahooFinanceConfig(Enabled: true, Priority: provider.SuggestedPriority)
                    };
                    priorityOrder.Add("yahoo");
                    break;

                case "Stooq":
                    providers = providers with
                    {
                        Stooq = new StooqConfig(Enabled: true, Priority: provider.SuggestedPriority)
                    };
                    priorityOrder.Add("stooq");
                    break;

                case "NasdaqDataLink":
                    var nasdaqKey = GetEnvVar("NASDAQ_API_KEY", "MDC_NASDAQ_API_KEY", "QUANDL_API_KEY");
                    providers = providers with
                    {
                        Nasdaq = new NasdaqDataLinkConfig(
                            Enabled: true,
                            ApiKey: nasdaqKey,
                            Priority: provider.SuggestedPriority
                        )
                    };
                    priorityOrder.Add("nasdaq");
                    break;
            }
        }

        // Always enable free providers as fallback
        if (providers.Yahoo == null)
        {
            providers = providers with { Yahoo = new YahooFinanceConfig(Enabled: true, Priority: 100) };
            priorityOrder.Add("yahoo");
        }
        if (providers.Stooq == null)
        {
            providers = providers with { Stooq = new StooqConfig(Enabled: true, Priority: 110) };
            priorityOrder.Add("stooq");
        }

        var backfill = config.Backfill ?? new BackfillConfig();
        config = config with
        {
            Backfill = backfill with
            {
                Provider = "composite",
                Providers = providers,
                ProviderPriority = priorityOrder.ToArray(),
                EnableFallback = true,
                EnableRateLimitRotation = true
            }
        };

        if (availableHistorical.Any())
        {
            appliedFixes.Add($"Auto-configured {availableHistorical.Count} backfill providers based on environment credentials");
        }

        return config;
    }

    private AppConfig AutoConfigureStorage(AppConfig config, List<string> appliedFixes)
    {
        if (config.Storage != null)
            return config;

        // Detect available disk space and set sensible defaults
        var dataRoot = config.DataRoot;
        long? maxMegabytes = null;

        try
        {
            var rootPath = Path.GetFullPath(dataRoot);
            var drive = new DriveInfo(Path.GetPathRoot(rootPath) ?? "/");

            // Reserve 10% of available space, max 100GB
            var availableBytes = drive.AvailableFreeSpace;
            var reserveBytes = Math.Min(availableBytes / 10, 100L * 1024 * 1024 * 1024);
            maxMegabytes = reserveBytes / (1024 * 1024);

            _log.Debug("Auto-detected storage: {AvailableGB} GB available, reserving {ReserveGB} GB",
                availableBytes / (1024 * 1024 * 1024), reserveBytes / (1024 * 1024 * 1024));
        }
        catch (Exception ex)
        {
            _log.Debug(ex, "Could not auto-detect storage space");
        }

        // Use default profile (Research) instead of scattered individual defaults
        config = config with
        {
            Storage = new StorageConfig(
                Profile: StorageProfilePresets.DefaultProfile,
                MaxTotalMegabytes: maxMegabytes
            )
        };

        appliedFixes.Add($"Auto-configured storage with '{StorageProfilePresets.DefaultProfile}' profile");

        return config;
    }

    private AppConfig ApplySelfHealingFixes(AppConfig config, List<string> appliedFixes, List<string> warnings)
    {
        // Fix: Alpaca selected but no credentials
        if (config.DataSource == DataSourceKind.Alpaca)
        {
            if (config.Alpaca == null || string.IsNullOrEmpty(config.Alpaca.KeyId))
            {
                // Try to get from environment
                var keyId = GetEnvVar("ALPACA_KEY_ID", "MDC_ALPACA_KEY_ID");
                var secretKey = GetEnvVar("ALPACA_SECRET_KEY", "MDC_ALPACA_SECRET_KEY");

                if (!string.IsNullOrEmpty(keyId) && !string.IsNullOrEmpty(secretKey))
                {
                    config = config with
                    {
                        Alpaca = new AlpacaOptions(keyId, secretKey)
                    };
                    appliedFixes.Add("Fixed: Added Alpaca credentials from environment variables");
                }
                else
                {
                    // Fall back to free provider
                    warnings.Add("Alpaca selected but no credentials found. Consider setting ALPACA_KEY_ID and ALPACA_SECRET_KEY environment variables.");
                }
            }
        }

        // Fix: Invalid naming convention
        if (config.Storage != null)
        {
            var validConventions = new[] { "flat", "bysymbol", "bydate", "bytype", "bysource", "byassetclass", "hierarchical", "canonical" };
            if (!validConventions.Contains(config.Storage.NamingConvention.ToLowerInvariant()))
            {
                config = config with
                {
                    Storage = config.Storage with { NamingConvention = "BySymbol" }
                };
                appliedFixes.Add($"Fixed: Invalid naming convention '{config.Storage.NamingConvention}' changed to 'BySymbol'");
            }

            // Fix: Invalid date partition
            var validPartitions = new[] { "none", "daily", "hourly", "monthly" };
            if (!validPartitions.Contains(config.Storage.DatePartition.ToLowerInvariant()))
            {
                config = config with
                {
                    Storage = config.Storage with { DatePartition = "Daily" }
                };
                appliedFixes.Add($"Fixed: Invalid date partition '{config.Storage.DatePartition}' changed to 'Daily'");
            }
        }

        // Fix: Empty symbols list
        if (config.Symbols == null || config.Symbols.Length == 0)
        {
            config = config with
            {
                Symbols = new[]
                {
                    new SymbolConfig("SPY", SubscribeTrades: true, SubscribeDepth: true, DepthLevels: 10)
                }
            };
            appliedFixes.Add("Fixed: Added default symbol (SPY) since none were configured");
        }

        // Fix: Invalid depth levels
        if (config.Symbols != null)
        {
            var fixedSymbols = config.Symbols.Select(s =>
            {
                if (s.SubscribeDepth && (s.DepthLevels < 1 || s.DepthLevels > 50))
                {
                    return s with { DepthLevels = Math.Clamp(s.DepthLevels, 1, 50) };
                }
                return s;
            }).ToArray();

            if (!config.Symbols.SequenceEqual(fixedSymbols))
            {
                config = config with { Symbols = fixedSymbols };
                appliedFixes.Add("Fixed: Adjusted depth levels to valid range (1-50)");
            }
        }

        // Fix: Backfill date range issues
        if (config.Backfill != null)
        {
            var backfill = config.Backfill;
            var needsFix = false;

            // Fix: From date after To date
            if (backfill.From.HasValue && backfill.To.HasValue && backfill.From > backfill.To)
            {
                backfill = backfill with { From = backfill.To, To = backfill.From };
                needsFix = true;
                appliedFixes.Add("Fixed: Swapped backfill From/To dates (From was after To)");
            }

            // Fix: Future end date
            var today = DateOnly.FromDateTime(DateTime.UtcNow);
            if (backfill.To.HasValue && backfill.To > today)
            {
                backfill = backfill with { To = today };
                needsFix = true;
                appliedFixes.Add("Fixed: Adjusted backfill To date to today (was in the future)");
            }

            if (needsFix)
            {
                config = config with { Backfill = backfill };
            }
        }

        return config;
    }

    private void GenerateRecommendations(AppConfig config, IReadOnlyList<DetectedProvider> providers, List<string> recommendations)
    {
        var configuredProviders = providers.Where(p => p.HasCredentials).ToList();
        var unconfiguredProviders = providers.Where(p => !p.HasCredentials && p.MissingCredentials.Length > 0).ToList();

        // Recommend missing providers
        if (!configuredProviders.Any(p => p.Capabilities.Contains("RealTime")))
        {
            recommendations.Add("Consider setting up a real-time data provider (Alpaca or Polygon) for live trading data.");
        }

        if (!configuredProviders.Any(p => p.Name == "Tiingo" || p.Name == "Polygon"))
        {
            recommendations.Add("Consider adding Tiingo (TIINGO_API_TOKEN) or Polygon (POLYGON_API_KEY) for better historical data quality.");
        }

        // Recommend compression for production
        if (!(config.Compress ?? false) && configuredProviders.Count > 0)
        {
            recommendations.Add("Enable compression (Compress: true) to reduce storage usage in production.");
        }

        // Recommend retention policy
        if (config.Storage?.RetentionDays == null && config.Storage?.MaxTotalMegabytes == null)
        {
            recommendations.Add("Consider setting RetentionDays or MaxTotalMegabytes to prevent unbounded storage growth.");
        }

        // Provider-specific recommendations
        if (config.DataSource == DataSourceKind.Alpaca && config.Alpaca?.Feed == "iex")
        {
            recommendations.Add("You're using the IEX feed (free). Consider upgrading to 'sip' for consolidated market data.");
        }
    }

    private BackfillConfig GenerateBackfillConfig(IReadOnlyList<DetectedProvider> providers)
    {
        var historical = providers.Where(p => p.HasCredentials && p.Capabilities.Contains("Historical")).ToList();
        var priority = historical.Select(p => p.Name.ToLowerInvariant()).ToList();

        // Always add free providers
        if (!priority.Contains("yahoo"))
            priority.Add("yahoo");
        if (!priority.Contains("stooq"))
            priority.Add("stooq");

        return new BackfillConfig(
            Enabled: false,
            Provider: "composite",
            EnableFallback: true,
            EnableRateLimitRotation: true,
            ProviderPriority: priority.ToArray()
        );
    }

    private static SymbolConfig[] GetDefaultSymbols(SymbolPreset preset)
    {
        return preset switch
        {
            SymbolPreset.USMajorIndices => new[]
            {
                new SymbolConfig("SPY", SubscribeTrades: true, SubscribeDepth: true, DepthLevels: 10),
                new SymbolConfig("QQQ", SubscribeTrades: true, SubscribeDepth: true, DepthLevels: 10),
                new SymbolConfig("DIA", SubscribeTrades: true, SubscribeDepth: true, DepthLevels: 10),
                new SymbolConfig("IWM", SubscribeTrades: true, SubscribeDepth: true, DepthLevels: 10)
            },
            SymbolPreset.TechGiants => new[]
            {
                new SymbolConfig("AAPL", SubscribeTrades: true, SubscribeDepth: true, DepthLevels: 10),
                new SymbolConfig("MSFT", SubscribeTrades: true, SubscribeDepth: true, DepthLevels: 10),
                new SymbolConfig("GOOGL", SubscribeTrades: true, SubscribeDepth: true, DepthLevels: 10),
                new SymbolConfig("AMZN", SubscribeTrades: true, SubscribeDepth: true, DepthLevels: 10),
                new SymbolConfig("META", SubscribeTrades: true, SubscribeDepth: true, DepthLevels: 10),
                new SymbolConfig("NVDA", SubscribeTrades: true, SubscribeDepth: true, DepthLevels: 10)
            },
            SymbolPreset.SP500Top20 => new[]
            {
                new SymbolConfig("AAPL", SubscribeTrades: true, SubscribeDepth: false),
                new SymbolConfig("MSFT", SubscribeTrades: true, SubscribeDepth: false),
                new SymbolConfig("GOOGL", SubscribeTrades: true, SubscribeDepth: false),
                new SymbolConfig("AMZN", SubscribeTrades: true, SubscribeDepth: false),
                new SymbolConfig("NVDA", SubscribeTrades: true, SubscribeDepth: false),
                new SymbolConfig("META", SubscribeTrades: true, SubscribeDepth: false),
                new SymbolConfig("BRK.B", SubscribeTrades: true, SubscribeDepth: false),
                new SymbolConfig("TSLA", SubscribeTrades: true, SubscribeDepth: false),
                new SymbolConfig("UNH", SubscribeTrades: true, SubscribeDepth: false),
                new SymbolConfig("JNJ", SubscribeTrades: true, SubscribeDepth: false),
                new SymbolConfig("JPM", SubscribeTrades: true, SubscribeDepth: false),
                new SymbolConfig("V", SubscribeTrades: true, SubscribeDepth: false),
                new SymbolConfig("PG", SubscribeTrades: true, SubscribeDepth: false),
                new SymbolConfig("MA", SubscribeTrades: true, SubscribeDepth: false),
                new SymbolConfig("HD", SubscribeTrades: true, SubscribeDepth: false),
                new SymbolConfig("CVX", SubscribeTrades: true, SubscribeDepth: false),
                new SymbolConfig("MRK", SubscribeTrades: true, SubscribeDepth: false),
                new SymbolConfig("ABBV", SubscribeTrades: true, SubscribeDepth: false),
                new SymbolConfig("LLY", SubscribeTrades: true, SubscribeDepth: false),
                new SymbolConfig("PEP", SubscribeTrades: true, SubscribeDepth: false)
            },
            SymbolPreset.Crypto => new[]
            {
                new SymbolConfig("BTC/USD", SubscribeTrades: true, SubscribeDepth: true, DepthLevels: 10),
                new SymbolConfig("ETH/USD", SubscribeTrades: true, SubscribeDepth: true, DepthLevels: 10),
                new SymbolConfig("SOL/USD", SubscribeTrades: true, SubscribeDepth: false)
            },
            SymbolPreset.Custom or _ => new[]
            {
                new SymbolConfig("SPY", SubscribeTrades: true, SubscribeDepth: true, DepthLevels: 10)
            }
        };
    }

    private static (bool HasCredentials, string[] Missing) CheckCredentials(ProviderDescriptor descriptor)
    {
        if (descriptor.RequiredEnvVars.Length == 0)
            return (true, Array.Empty<string>());

        var missing = new List<string>();

        foreach (var envVar in descriptor.RequiredEnvVars)
        {
            var value = Environment.GetEnvironmentVariable(envVar);

            // Check alternatives if main not set
            if (string.IsNullOrEmpty(value))
            {
                var altFound = descriptor.AlternativeEnvVars.Any(alt =>
                    !string.IsNullOrEmpty(Environment.GetEnvironmentVariable(alt)));

                if (!altFound)
                    missing.Add(envVar);
            }
        }

        return (missing.Count == 0, missing.ToArray());
    }

    private static string? GetEnvVar(params string[] names)
    {
        foreach (var name in names)
        {
            var value = Environment.GetEnvironmentVariable(name);
            if (!string.IsNullOrEmpty(value))
                return value;
        }
        return null;
    }

    private static bool IsIBGatewayAvailable()
    {
        // Check if IB Gateway/TWS is running on default ports
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
    /// Applies a role-based configuration preset, setting ~15 config values at once.
    /// Presets: Researcher, DayTrader, OptionsTrader, Crypto.
    /// </summary>
    public AppConfig ApplyPreset(ConfigPreset preset, AppConfig? existingConfig = null)
    {
        var config = existingConfig ?? new AppConfig();
        var detectedProviders = DetectAvailableProviders();

        return preset switch
        {
            ConfigPreset.Researcher => ApplyResearcherPreset(config, detectedProviders),
            ConfigPreset.DayTrader => ApplyDayTraderPreset(config, detectedProviders),
            ConfigPreset.OptionsTrader => ApplyOptionsTraderPreset(config, detectedProviders),
            ConfigPreset.Crypto => ApplyCryptoPreset(config, detectedProviders),
            _ => config
        };
    }

    /// <summary>
    /// Lists available presets with descriptions.
    /// </summary>
    public static IReadOnlyList<ConfigPresetInfo> GetPresetDescriptions() => new[]
    {
        new ConfigPresetInfo(ConfigPreset.Researcher, "Researcher",
            "Historical analysis, daily bars. Uses free backfill providers (Stooq/Yahoo), " +
            "BySymbol storage with Parquet export, no real-time streaming.",
            new[] { "Stooq + Yahoo backfill", "BySymbol storage", "Research storage profile", "Parquet export enabled", "No real-time streaming" }),
        new ConfigPresetInfo(ConfigPreset.DayTrader, "Day Trader",
            "Real-time streaming with L2 depth data. Uses Alpaca streaming, " +
            "10 depth levels, JSONL hot storage, low-latency profile.",
            new[] { "Alpaca streaming", "10 depth levels", "LowLatency storage profile", "Trades + Quotes + Depth", "US major indices" }),
        new ConfigPresetInfo(ConfigPreset.OptionsTrader, "Options Trader",
            "Options chain + Greeks collection. Uses IB streaming, " +
            "derivatives enabled, weekly/monthly expirations.",
            new[] { "IB streaming", "Derivatives enabled", "Greeks capture", "Weekly expirations", "SPY/QQQ/IWM + SPX/NDX" }),
        new ConfigPresetInfo(ConfigPreset.Crypto, "Crypto",
            "24/7 crypto collection. Uses Alpaca crypto feed, " +
            "no market hours filter, extended retention.",
            new[] { "Alpaca crypto feed", "BTC/ETH/SOL", "No market hours filter", "365-day retention", "Archival storage profile" })
    };

    private AppConfig ApplyResearcherPreset(AppConfig config, IReadOnlyList<DetectedProvider> providers)
    {
        _log.Information("Applying 'Researcher' configuration preset");

        var backfillProviders = new List<string>();
        foreach (var p in providers.Where(p => p.HasCredentials && p.Capabilities.Contains("Historical")))
            backfillProviders.Add(p.Name.ToLowerInvariant());
        if (!backfillProviders.Contains("yahoo"))
            backfillProviders.Add("yahoo");
        if (!backfillProviders.Contains("stooq"))
            backfillProviders.Add("stooq");

        return config with
        {
            DataSource = DataSourceKind.IB, // Not used in research mode
            Symbols = new[]
            {
                new SymbolConfig("SPY", SubscribeTrades: false, SubscribeDepth: false),
                new SymbolConfig("QQQ", SubscribeTrades: false, SubscribeDepth: false),
                new SymbolConfig("AAPL", SubscribeTrades: false, SubscribeDepth: false),
                new SymbolConfig("MSFT", SubscribeTrades: false, SubscribeDepth: false),
                new SymbolConfig("GOOGL", SubscribeTrades: false, SubscribeDepth: false)
            },
            Storage = new StorageConfig(
                NamingConvention: "BySymbol",
                DatePartition: "Daily",
                Profile: "Research",
                EnableParquetSink: true
            ),
            Compress = true,
            Backfill = new BackfillConfig(
                Enabled: true,
                Provider: "composite",
                EnableFallback: true,
                EnableRateLimitRotation: true,
                ProviderPriority: backfillProviders.ToArray()
            )
        };
    }

    private AppConfig ApplyDayTraderPreset(AppConfig config, IReadOnlyList<DetectedProvider> providers)
    {
        _log.Information("Applying 'Day Trader' configuration preset");

        var hasAlpaca = providers.Any(p => p.Name == "Alpaca" && p.HasCredentials);
        var dataSource = hasAlpaca ? DataSourceKind.Alpaca : DataSourceKind.IB;

        config = AutoConfigureAlpaca(config, new List<string>());

        return config with
        {
            DataSource = dataSource,
            Symbols = new[]
            {
                new SymbolConfig("SPY", SubscribeTrades: true, SubscribeDepth: true, DepthLevels: 10),
                new SymbolConfig("QQQ", SubscribeTrades: true, SubscribeDepth: true, DepthLevels: 10),
                new SymbolConfig("AAPL", SubscribeTrades: true, SubscribeDepth: true, DepthLevels: 10),
                new SymbolConfig("MSFT", SubscribeTrades: true, SubscribeDepth: true, DepthLevels: 10)
            },
            Storage = new StorageConfig(
                NamingConvention: "BySymbol",
                DatePartition: "Daily",
                Profile: "LowLatency",
                RetentionDays: 30
            ),
            Compress = false,
            Backfill = new BackfillConfig(
                Enabled: false,
                Provider: "composite",
                EnableFallback: true
            )
        };
    }

    private AppConfig ApplyOptionsTraderPreset(AppConfig config, IReadOnlyList<DetectedProvider> providers)
    {
        _log.Information("Applying 'Options Trader' configuration preset");

        return config with
        {
            DataSource = DataSourceKind.IB,
            Symbols = new[]
            {
                new SymbolConfig("SPY", SubscribeTrades: true, SubscribeDepth: true, DepthLevels: 10),
                new SymbolConfig("QQQ", SubscribeTrades: true, SubscribeDepth: true, DepthLevels: 10),
                new SymbolConfig("IWM", SubscribeTrades: true, SubscribeDepth: true, DepthLevels: 10),
                new SymbolConfig("AAPL", SubscribeTrades: true, SubscribeDepth: false)
            },
            Storage = new StorageConfig(
                NamingConvention: "BySymbol",
                DatePartition: "Daily",
                Profile: "Research"
            ),
            Compress = true,
            Derivatives = new DerivativesConfig(
                Enabled: true,
                Underlyings: new[] { "SPY", "QQQ", "IWM", "AAPL" },
                MaxDaysToExpiration: 90,
                StrikeRange: 20,
                CaptureGreeks: true,
                CaptureChainSnapshots: true,
                CaptureOpenInterest: true,
                ExpirationFilter: new[] { "weekly", "monthly" },
                IndexOptions: new IndexOptionsConfig(
                    Enabled: true,
                    Indices: new[] { "SPX", "NDX" },
                    IncludeWeeklies: true
                )
            ),
            Backfill = new BackfillConfig(
                Enabled: true,
                Provider: "composite",
                EnableFallback: true,
                EnableRateLimitRotation: true
            )
        };
    }

    private AppConfig ApplyCryptoPreset(AppConfig config, IReadOnlyList<DetectedProvider> providers)
    {
        _log.Information("Applying 'Crypto' configuration preset");

        config = AutoConfigureAlpaca(config, new List<string>());

        return config with
        {
            DataSource = DataSourceKind.Alpaca,
            Alpaca = config.Alpaca != null
                ? config.Alpaca with { Feed = "crypto" }
                : null,
            Symbols = new[]
            {
                new SymbolConfig("BTC/USD", SubscribeTrades: true, SubscribeDepth: true, DepthLevels: 10),
                new SymbolConfig("ETH/USD", SubscribeTrades: true, SubscribeDepth: true, DepthLevels: 10),
                new SymbolConfig("SOL/USD", SubscribeTrades: true, SubscribeDepth: false),
                new SymbolConfig("AVAX/USD", SubscribeTrades: true, SubscribeDepth: false),
                new SymbolConfig("LINK/USD", SubscribeTrades: true, SubscribeDepth: false)
            },
            Storage = new StorageConfig(
                NamingConvention: "BySymbol",
                DatePartition: "Daily",
                Profile: "Archival",
                RetentionDays: 365
            ),
            Compress = true,
            Backfill = new BackfillConfig(
                Enabled: true,
                Provider: "composite",
                EnableFallback: true
            )
        };
    }

}

/// <summary>
/// Available configuration presets for different user roles.
/// </summary>
public enum ConfigPreset : byte
{
    /// <summary>Historical analysis, daily bars, Parquet export.</summary>
    Researcher,
    /// <summary>Real-time streaming, L2 depth, low-latency storage.</summary>
    DayTrader,
    /// <summary>Options chain + Greeks, IB streaming, derivatives enabled.</summary>
    OptionsTrader,
    /// <summary>24/7 crypto collection, Alpaca crypto feed, extended retention.</summary>
    Crypto
}

/// <summary>
/// Descriptive information about a configuration preset.
/// </summary>
public sealed record ConfigPresetInfo(
    ConfigPreset Preset,
    string Name,
    string Description,
    string[] KeySettings
);

/// <summary>
/// Options for first-time configuration generation.
/// </summary>
public sealed record FirstTimeConfigOptions(
    UseCase UseCase = UseCase.Development,
    SymbolPreset SymbolPreset = SymbolPreset.USMajorIndices,
    bool EnableBackfill = true,
    bool EnableCompression = true
);

/// <summary>
/// Use case for configuration.
/// </summary>
public enum UseCase : byte
{
    Development,
    Research,
    RealTimeTrading,
    BackfillOnly,
    Production
}

/// <summary>
/// Preset symbol lists.
/// </summary>
public enum SymbolPreset : byte
{
    Custom,
    USMajorIndices,
    TechGiants,
    SP500Top20,
    Crypto
}

/// <summary>
/// Role-based configuration presets that configure ~15 settings at once.
/// Reduces time-to-value from 30+ minutes of manual config to a single --preset flag.
/// </summary>
public static class ConfigurationPresets
{
    /// <summary>
    /// Available preset names.
    /// </summary>
    public static IReadOnlyList<string> AvailablePresets => new[]
    {
        "researcher", "daytrader", "options", "crypto"
    };

    /// <summary>
    /// Gets the preset description for display.
    /// </summary>
    public static IReadOnlyDictionary<string, string> PresetDescriptions => new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        ["researcher"] = "Historical analysis with daily bars, Parquet export, no real-time streaming",
        ["daytrader"] = "Real-time streaming with L2 data, low-latency JSONL storage",
        ["options"] = "Options chain + Greeks via IB, derivatives enabled, weekly/monthly expirations",
        ["crypto"] = "24/7 crypto collection via Alpaca, no market hours filter, extended retention"
    };

    /// <summary>
    /// Applies a named preset to the given config, returning a new config with preset values.
    /// </summary>
    public static AppConfig ApplyPreset(string presetName, AppConfig config)
    {
        return presetName.ToLowerInvariant() switch
        {
            "researcher" => ApplyResearcherPreset(config),
            "daytrader" => ApplyDayTraderPreset(config),
            "options" => ApplyOptionsPreset(config),
            "crypto" => ApplyCryptoPreset(config),
            _ => throw new ArgumentException($"Unknown preset: '{presetName}'. Available: {string.Join(", ", AvailablePresets)}")
        };
    }

    private static AppConfig ApplyResearcherPreset(AppConfig config)
    {
        return config with
        {
            DataSource = DataSourceKind.Alpaca,
            Symbols = new[]
            {
                new SymbolConfig("SPY", SubscribeTrades: false, SubscribeDepth: false),
                new SymbolConfig("QQQ", SubscribeTrades: false, SubscribeDepth: false),
                new SymbolConfig("AAPL", SubscribeTrades: false, SubscribeDepth: false),
                new SymbolConfig("MSFT", SubscribeTrades: false, SubscribeDepth: false)
            },
            Storage = new StorageConfig(
                Profile: StorageProfilePresets.DefaultProfile,
                NamingConvention: "BySymbol",
                DatePartition: "Daily"
            ),
            Compress = true,
            Backfill = (config.Backfill ?? new BackfillConfig()) with
            {
                Enabled = true,
                Provider = "composite",
                EnableFallback = true,
                ProviderPriority = new[] { "stooq", "yahoo", "tiingo", "alpaca" }
            }
        };
    }

    private static AppConfig ApplyDayTraderPreset(AppConfig config)
    {
        return config with
        {
            DataSource = DataSourceKind.Alpaca,
            Symbols = new[]
            {
                new SymbolConfig("SPY", SubscribeTrades: true, SubscribeDepth: true, DepthLevels: 10),
                new SymbolConfig("QQQ", SubscribeTrades: true, SubscribeDepth: true, DepthLevels: 10),
                new SymbolConfig("AAPL", SubscribeTrades: true, SubscribeDepth: true, DepthLevels: 10),
                new SymbolConfig("MSFT", SubscribeTrades: true, SubscribeDepth: true, DepthLevels: 10),
                new SymbolConfig("NVDA", SubscribeTrades: true, SubscribeDepth: true, DepthLevels: 10),
                new SymbolConfig("TSLA", SubscribeTrades: true, SubscribeDepth: true, DepthLevels: 10)
            },
            Storage = new StorageConfig(
                Profile: "LowLatency",
                NamingConvention: "BySymbol",
                DatePartition: "Daily",
                RetentionDays: 30
            ),
            Compress = false
        };
    }

    private static AppConfig ApplyOptionsPreset(AppConfig config)
    {
        return config with
        {
            DataSource = DataSourceKind.IB,
            Symbols = new[]
            {
                new SymbolConfig("SPY", SubscribeTrades: true, SubscribeDepth: true, DepthLevels: 10,
                    SecurityType: "STK", Exchange: "SMART"),
                new SymbolConfig("QQQ", SubscribeTrades: true, SubscribeDepth: true, DepthLevels: 10,
                    SecurityType: "STK", Exchange: "SMART"),
                new SymbolConfig("AAPL", SubscribeTrades: true, SubscribeDepth: true, DepthLevels: 10,
                    SecurityType: "STK", Exchange: "SMART"),
                new SymbolConfig("MSFT", SubscribeTrades: true, SubscribeDepth: true, DepthLevels: 10,
                    SecurityType: "STK", Exchange: "SMART")
            },
            Storage = new StorageConfig(
                Profile: StorageProfilePresets.DefaultProfile,
                NamingConvention: "BySymbol",
                DatePartition: "Daily"
            ),
            Compress = true,
            Derivatives = new DerivativesConfig(
                Enabled: true,
                CaptureGreeks: true,
                CaptureChainSnapshots: true,
                CaptureOpenInterest: true
            )
        };
    }

    private static AppConfig ApplyCryptoPreset(AppConfig config)
    {
        return config with
        {
            DataSource = DataSourceKind.Alpaca,
            Symbols = new[]
            {
                new SymbolConfig("BTC/USD", SubscribeTrades: true, SubscribeDepth: true, DepthLevels: 10),
                new SymbolConfig("ETH/USD", SubscribeTrades: true, SubscribeDepth: true, DepthLevels: 10),
                new SymbolConfig("SOL/USD", SubscribeTrades: true, SubscribeDepth: false),
                new SymbolConfig("AVAX/USD", SubscribeTrades: true, SubscribeDepth: false),
                new SymbolConfig("DOGE/USD", SubscribeTrades: true, SubscribeDepth: false)
            },
            Storage = new StorageConfig(
                Profile: StorageProfilePresets.DefaultProfile,
                NamingConvention: "BySymbol",
                DatePartition: "Daily",
                RetentionDays: 90
            ),
            Compress = true,
            Alpaca = (config.Alpaca ?? new AlpacaOptions()) with
            {
                Feed = "crypto"
            }
        };
    }
}
