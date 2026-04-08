using System.Text.Json.Serialization;

namespace Meridian.Contracts.Api;

/// <summary>
/// Centralized catalog of provider metadata for UI consumption.
/// Eliminates per-provider conditionals by providing standardized template output
/// that both Web and desktop can consume without provider-specific logic.
/// </summary>
/// <remarks>
/// This class provides static fallback data for scenarios where the ProviderRegistry
/// is not available (e.g., Contracts project doesn't reference Infrastructure).
/// When using the full application stack, prefer using ProviderRegistry.GetProviderCatalog()
/// which derives catalog entries from actual registered provider metadata via
/// ProviderTemplateFactory.ToCatalogEntry().
/// </remarks>
public static class ProviderCatalog
{
    /// <summary>
    /// Optional delegate for retrieving provider catalog from registered providers.
    /// Set by the application host to enable runtime-derived catalog data.
    /// </summary>
    public static Func<IReadOnlyList<ProviderCatalogEntry>>? RuntimeCatalogProvider { get; set; }

    /// <summary>
    /// Optional delegate for retrieving a single provider catalog entry by ID.
    /// Set by the application host to enable runtime-derived catalog data.
    /// </summary>
    public static Func<string, ProviderCatalogEntry?>? RuntimeCatalogEntryProvider { get; set; }

    private static readonly Dictionary<string, ProviderCatalogEntry> _entries = new(StringComparer.OrdinalIgnoreCase)
    {
        ["stooq"] = new ProviderCatalogEntry
        {
            ProviderId = "stooq",
            DisplayName = "Stooq",
            Description = "Free historical daily OHLCV data provider",
            ProviderType = ProviderTypeKind.Backfill,
            RequiresCredentials = false,
            CredentialFields = Array.Empty<CredentialFieldInfo>(),
            RateLimit = new RateLimitInfo
            {
                MaxRequestsPerWindow = 30,
                WindowSeconds = 60,
                MinDelayMs = 2000,
                Description = "Low rate limits apply"
            },
            Notes = new[]
            {
                "Stooq provides daily OHLCV data for free.",
                "Rate limits apply. Large date ranges may take several minutes.",
                "Data coverage includes US, European, and select global markets."
            },
            Warnings = Array.Empty<string>(),
            SupportedMarkets = new[] { "US", "UK", "DE", "PL" },
            DataTypes = new[] { "DailyBars" },
            Capabilities = new CapabilityInfo
            {
                SupportsAdjustedPrices = true,
                SupportsDividends = true,
                SupportsSplits = true,
                SupportsIntraday = false,
                SupportsTrades = false,
                SupportsQuotes = false
            }
        },

        ["yahoo"] = new ProviderCatalogEntry
        {
            ProviderId = "yahoo",
            DisplayName = "Yahoo Finance",
            Description = "Unofficial free historical data from Yahoo Finance",
            ProviderType = ProviderTypeKind.Backfill,
            RequiresCredentials = false,
            CredentialFields = Array.Empty<CredentialFieldInfo>(),
            RateLimit = new RateLimitInfo
            {
                MaxRequestsPerWindow = 100,
                WindowSeconds = 60,
                MinDelayMs = 500,
                Description = "Unofficial API, rate limits may vary"
            },
            Notes = new[]
            {
                "Yahoo Finance data is unofficial and may have gaps.",
                "Good for basic daily OHLCV data and dividend/split information."
            },
            Warnings = new[]
            {
                "Unofficial API - may break without notice.",
                "Data quality may vary for less liquid securities."
            },
            SupportedMarkets = new[] { "US", "UK", "EU", "APAC" },
            DataTypes = new[] { "DailyBars", "Dividends", "Splits" },
            Capabilities = new CapabilityInfo
            {
                SupportsAdjustedPrices = true,
                SupportsDividends = true,
                SupportsSplits = true,
                SupportsIntraday = false,
                SupportsTrades = false,
                SupportsQuotes = false
            }
        },

        ["alpaca"] = new ProviderCatalogEntry
        {
            ProviderId = "alpaca",
            DisplayName = "Alpaca Markets",
            Description = "Commission-free trading API with market data",
            ProviderType = ProviderTypeKind.Hybrid,
            RequiresCredentials = true,
            CredentialFields = new[]
            {
                new CredentialFieldInfo("KeyId", "ALPACA__KEYID", "API Key ID", true),
                new CredentialFieldInfo("SecretKey", "ALPACA__SECRETKEY", "API Secret Key", true)
            },
            RateLimit = new RateLimitInfo
            {
                MaxRequestsPerWindow = 200,
                WindowSeconds = 60,
                MinDelayMs = 300,
                Description = "200 requests/minute"
            },
            Notes = new[]
            {
                "Alpaca requires API credentials (free account available).",
                "Rate limit: 200 requests/minute.",
                "IEX feed is free; SIP feed requires subscription."
            },
            Warnings = Array.Empty<string>(),
            SupportedMarkets = new[] { "US" },
            DataTypes = new[] { "DailyBars", "IntradayBars", "Trades", "Quotes" },
            Capabilities = new CapabilityInfo
            {
                SupportsStreaming = true,
                SupportsAdjustedPrices = true,
                SupportsDividends = true,
                SupportsSplits = true,
                SupportsIntraday = true,
                SupportsTrades = true,
                SupportsQuotes = true
            }
        },

        ["polygon"] = new ProviderCatalogEntry
        {
            ProviderId = "polygon",
            DisplayName = "Polygon.io",
            Description = "Real-time and historical market data APIs",
            ProviderType = ProviderTypeKind.Hybrid,
            RequiresCredentials = true,
            CredentialFields = new[]
            {
                new CredentialFieldInfo("ApiKey", "POLYGON__APIKEY", "Polygon API Key", true)
            },
            RateLimit = new RateLimitInfo
            {
                MaxRequestsPerWindow = 5,
                WindowSeconds = 60,
                MinDelayMs = 12000,
                Description = "Free tier: 5 requests/minute"
            },
            Notes = new[]
            {
                "Polygon provides comprehensive market data.",
                "Free tier has limited rate limits; paid plans offer more.",
                "Supports stocks, options, forex, and crypto."
            },
            Warnings = new[]
            {
                "Free tier has 15-minute delayed data for most feeds."
            },
            SupportedMarkets = new[] { "US" },
            DataTypes = new[] { "DailyBars", "IntradayBars", "Trades", "Quotes", "Aggregates" },
            Capabilities = new CapabilityInfo
            {
                SupportsStreaming = true,
                SupportsAdjustedPrices = true,
                SupportsDividends = true,
                SupportsSplits = true,
                SupportsIntraday = true,
                SupportsTrades = true,
                SupportsQuotes = true
            }
        },

        ["robinhood"] = new ProviderCatalogEntry
        {
            ProviderId = "robinhood",
            DisplayName = "Robinhood",
            Description = "Broker-backed Robinhood integration for quotes, options chains, and live positions/orders",
            ProviderType = ProviderTypeKind.Streaming,
            RequiresCredentials = true,
            CredentialFields = new[]
            {
                new CredentialFieldInfo("AccessToken", "ROBINHOOD_ACCESS_TOKEN", "Robinhood Access Token", true)
            },
            RateLimit = new RateLimitInfo
            {
                MaxRequestsPerWindow = 60,
                WindowSeconds = 60,
                MinDelayMs = 1000,
                Description = "Broker-session rate limits vary"
            },
            Notes = new[]
            {
                "Requires a valid Robinhood access token.",
                "Supports Robinhood symbol lookup, brokerage reads, and option-chain retrieval.",
                "Live option orders require broker instrument metadata supplied by the app."
            },
            Warnings = new[]
            {
                "Uses the unofficial Robinhood API.",
                "Options support is limited to US equity options."
            },
            SupportedMarkets = new[] { "US" },
            DataTypes = new[] { "Quotes", "OptionsChain", "Brokerage", "SymbolSearch" },
            Capabilities = new CapabilityInfo
            {
                SupportsQuotes = true,
                SupportsOptionsChain = true,
                SupportsBrokerage = true
            }
        },

        ["tiingo"] = new ProviderCatalogEntry
        {
            ProviderId = "tiingo",
            DisplayName = "Tiingo",
            Description = "Financial data and analytics platform",
            ProviderType = ProviderTypeKind.Backfill,
            RequiresCredentials = true,
            CredentialFields = new[]
            {
                new CredentialFieldInfo("Token", "TIINGO__TOKEN", "Tiingo API Token", true)
            },
            RateLimit = new RateLimitInfo
            {
                MaxRequestsPerWindow = 500,
                WindowSeconds = 3600,
                MinDelayMs = 100,
                Description = "500 requests/hour (free tier)"
            },
            Notes = new[]
            {
                "Tiingo offers high-quality historical data.",
                "Free tier allows 500 requests/hour.",
                "Good data quality with corporate action adjustments."
            },
            Warnings = Array.Empty<string>(),
            SupportedMarkets = new[] { "US" },
            DataTypes = new[] { "DailyBars", "Dividends", "Splits" },
            Capabilities = new CapabilityInfo
            {
                SupportsAdjustedPrices = true,
                SupportsDividends = true,
                SupportsSplits = true,
                SupportsIntraday = false,
                SupportsTrades = false,
                SupportsQuotes = false
            }
        },

        ["finnhub"] = new ProviderCatalogEntry
        {
            ProviderId = "finnhub",
            DisplayName = "Finnhub",
            Description = "Real-time market data and alternative data",
            ProviderType = ProviderTypeKind.Backfill,
            RequiresCredentials = true,
            CredentialFields = new[]
            {
                new CredentialFieldInfo("ApiKey", "FINNHUB__APIKEY", "Finnhub API Key", true)
            },
            RateLimit = new RateLimitInfo
            {
                MaxRequestsPerWindow = 60,
                WindowSeconds = 60,
                MinDelayMs = 1000,
                Description = "60 requests/minute (free tier)"
            },
            Notes = new[]
            {
                "Finnhub provides fundamental and alternative data.",
                "Free tier: 60 API calls/minute.",
                "Good for company fundamentals and news."
            },
            Warnings = Array.Empty<string>(),
            SupportedMarkets = new[] { "US" },
            DataTypes = new[] { "DailyBars" },
            Capabilities = new CapabilityInfo
            {
                SupportsAdjustedPrices = true,
                SupportsDividends = false,
                SupportsSplits = false,
                SupportsIntraday = false,
                SupportsTrades = false,
                SupportsQuotes = false
            }
        },

        ["alphavantage"] = new ProviderCatalogEntry
        {
            ProviderId = "alphavantage",
            DisplayName = "Alpha Vantage",
            Description = "Free stock APIs with technical indicators",
            ProviderType = ProviderTypeKind.Backfill,
            RequiresCredentials = true,
            CredentialFields = new[]
            {
                new CredentialFieldInfo("ApiKey", "ALPHAVANTAGE__APIKEY", "Alpha Vantage API Key", true)
            },
            RateLimit = new RateLimitInfo
            {
                MaxRequestsPerWindow = 5,
                WindowSeconds = 60,
                MinDelayMs = 12000,
                Description = "5 requests/minute (free tier)"
            },
            Notes = new[]
            {
                "Alpha Vantage offers free API keys.",
                "Rate limit: 5 requests/minute (free tier).",
                "Supports stocks, forex, and crypto."
            },
            Warnings = new[]
            {
                "Very slow due to strict rate limits on free tier."
            },
            SupportedMarkets = new[] { "US" },
            DataTypes = new[] { "DailyBars", "IntradayBars" },
            Capabilities = new CapabilityInfo
            {
                SupportsAdjustedPrices = true,
                SupportsDividends = true,
                SupportsSplits = true,
                SupportsIntraday = true,
                SupportsTrades = false,
                SupportsQuotes = false
            }
        },

        ["nasdaqdatalink"] = new ProviderCatalogEntry
        {
            ProviderId = "nasdaqdatalink",
            DisplayName = "Nasdaq Data Link",
            Description = "Formerly Quandl - premium financial data",
            ProviderType = ProviderTypeKind.Backfill,
            RequiresCredentials = true,
            CredentialFields = new[]
            {
                new CredentialFieldInfo("ApiKey", "NASDAQDATALINK__APIKEY", "Nasdaq Data Link API Key", true)
            },
            RateLimit = new RateLimitInfo
            {
                MaxRequestsPerWindow = 300,
                WindowSeconds = 10,
                MinDelayMs = 50,
                Description = "300 requests/10 seconds"
            },
            Notes = new[]
            {
                "Premium data source (formerly Quandl).",
                "Requires paid subscription for most datasets.",
                "High-quality institutional-grade data."
            },
            Warnings = new[]
            {
                "Most datasets require paid subscription."
            },
            SupportedMarkets = new[] { "US", "Global" },
            DataTypes = new[] { "DailyBars", "Fundamentals" },
            Capabilities = new CapabilityInfo
            {
                SupportsAdjustedPrices = true,
                SupportsDividends = true,
                SupportsSplits = true,
                SupportsIntraday = false,
                SupportsTrades = false,
                SupportsQuotes = false
            }
        },

        ["ib"] = new ProviderCatalogEntry
        {
            ProviderId = "ib",
            DisplayName = "Interactive Brokers",
            Description = "Professional-grade trading and data platform",
            ProviderType = ProviderTypeKind.Streaming,
            RequiresCredentials = true,
            CredentialFields = new[]
            {
                new CredentialFieldInfo("Host", null, "TWS/Gateway Host", false, "127.0.0.1"),
                new CredentialFieldInfo("Port", null, "TWS/Gateway Port", false, "7496"),
                new CredentialFieldInfo("ClientId", null, "Client ID", false, "0")
            },
            RateLimit = new RateLimitInfo
            {
                MaxRequestsPerWindow = 50,
                WindowSeconds = 1,
                MinDelayMs = 20,
                Description = "50 messages/second"
            },
            Notes = new[]
            {
                "Requires TWS or IB Gateway running locally.",
                "Account required; paper trading available.",
                "Professional-grade L2 market depth."
            },
            Warnings = new[]
            {
                "Requires Interactive Brokers account.",
                "TWS/Gateway must be running and configured."
            },
            SupportedMarkets = new[] { "US", "EU", "APAC", "Global" },
            DataTypes = new[] { "Trades", "Quotes", "MarketDepth", "DailyBars" },
            Capabilities = new CapabilityInfo
            {
                SupportsStreaming = true,
                SupportsMarketDepth = true,
                MaxDepthLevels = 10,
                SupportsAdjustedPrices = true,
                SupportsDividends = true,
                SupportsSplits = true,
                SupportsIntraday = true,
                SupportsTrades = true,
                SupportsQuotes = true
            }
        },

        ["nyse"] = new ProviderCatalogEntry
        {
            ProviderId = "nyse",
            DisplayName = "NYSE",
            Description = "Direct NYSE market data feed",
            ProviderType = ProviderTypeKind.Streaming,
            RequiresCredentials = true,
            CredentialFields = new[]
            {
                new CredentialFieldInfo("ApiKey", "NYSE__APIKEY", "NYSE API Key", true)
            },
            RateLimit = new RateLimitInfo
            {
                MaxRequestsPerWindow = 1000,
                WindowSeconds = 60,
                MinDelayMs = 10,
                Description = "1000 requests/minute"
            },
            Notes = new[]
            {
                "Direct NYSE market data.",
                "Requires NYSE data subscription agreement.",
                "Low-latency institutional feed."
            },
            Warnings = new[]
            {
                "Requires NYSE data licensing agreement."
            },
            SupportedMarkets = new[] { "US" },
            DataTypes = new[] { "Trades", "Quotes", "MarketDepth" },
            Capabilities = new CapabilityInfo
            {
                SupportsStreaming = true,
                SupportsMarketDepth = true,
                MaxDepthLevels = 10,
                SupportsTrades = true,
                SupportsQuotes = true
            }
        },

        ["stocksharp"] = new ProviderCatalogEntry
        {
            ProviderId = "stocksharp",
            DisplayName = "StockSharp",
            Description = "Multi-connector trading framework",
            ProviderType = ProviderTypeKind.Streaming,
            RequiresCredentials = true,
            CredentialFields = new[]
            {
                new CredentialFieldInfo("ConnectorType", null, "Connector Type", true, "Rithmic")
            },
            RateLimit = null,
            Notes = new[]
            {
                "Supports multiple underlying connectors.",
                "Configure specific connector settings in StockSharp section.",
                "Supports Rithmic, IQFeed, CQG, and more."
            },
            Warnings = new[]
            {
                "Requires StockSharp connector-specific credentials."
            },
            SupportedMarkets = new[] { "US", "Futures" },
            DataTypes = new[] { "Trades", "Quotes", "MarketDepth" },
            Capabilities = new CapabilityInfo
            {
                SupportsStreaming = true,
                SupportsMarketDepth = true,
                SupportsTrades = true,
                SupportsQuotes = true
            }
        }
    };

    /// <summary>
    /// Gets all registered providers.
    /// Uses runtime catalog provider if available, otherwise falls back to static data.
    /// </summary>
    public static IReadOnlyList<ProviderCatalogEntry> GetAll()
    {
        if (RuntimeCatalogProvider != null)
        {
            var runtimeEntries = RuntimeCatalogProvider();
            if (runtimeEntries.Count > 0)
                return runtimeEntries;
        }
        return _entries.Values.ToList();
    }

    /// <summary>
    /// Gets providers of a specific type.
    /// </summary>
    public static IReadOnlyList<ProviderCatalogEntry> GetByType(ProviderTypeKind type)
    {
        var all = GetAll();
        return all.Where(e => e.ProviderType == type || e.ProviderType == ProviderTypeKind.Hybrid).ToList();
    }

    /// <summary>
    /// Gets backfill-capable providers.
    /// </summary>
    public static IReadOnlyList<ProviderCatalogEntry> GetBackfillProviders()
    {
        var all = GetAll();
        return all.Where(e =>
            e.ProviderType == ProviderTypeKind.Backfill ||
            e.ProviderType == ProviderTypeKind.Hybrid).ToList();
    }

    /// <summary>
    /// Gets streaming-capable providers.
    /// </summary>
    public static IReadOnlyList<ProviderCatalogEntry> GetStreamingProviders()
    {
        var all = GetAll();
        return all.Where(e =>
            e.ProviderType == ProviderTypeKind.Streaming ||
            e.ProviderType == ProviderTypeKind.Hybrid).ToList();
    }

    /// <summary>
    /// Tries to get a provider entry by ID.
    /// Uses runtime catalog provider if available, otherwise falls back to static data.
    /// </summary>
    public static bool TryGet(string providerId, out ProviderCatalogEntry? entry)
    {
        entry = Get(providerId);
        return entry != null;
    }

    /// <summary>
    /// Gets a provider entry by ID, or null if not found.
    /// Uses runtime catalog provider if available, otherwise falls back to static data.
    /// </summary>
    public static ProviderCatalogEntry? Get(string providerId)
    {
        if (RuntimeCatalogEntryProvider != null)
        {
            var runtimeEntry = RuntimeCatalogEntryProvider(providerId);
            if (runtimeEntry != null)
                return runtimeEntry;
        }
        return _entries.TryGetValue(providerId, out var entry) ? entry : null;
    }

    /// <summary>
    /// Gets provider notes for UI display.
    /// Returns empty array if provider not found.
    /// </summary>
    public static string[] GetProviderNotes(string providerId) =>
        Get(providerId)?.Notes ?? Array.Empty<string>();

    /// <summary>
    /// Gets provider warnings for UI display.
    /// Returns empty array if provider not found.
    /// </summary>
    public static string[] GetProviderWarnings(string providerId) =>
        Get(providerId)?.Warnings ?? Array.Empty<string>();

    /// <summary>
    /// Checks if a provider requires credentials.
    /// </summary>
    public static bool RequiresCredentials(string providerId) =>
        Get(providerId)?.RequiresCredentials ?? false;

    /// <summary>
    /// Gets required credential fields for a provider.
    /// </summary>
    public static CredentialFieldInfo[] GetCredentialFields(string providerId) =>
        Get(providerId)?.CredentialFields ?? Array.Empty<CredentialFieldInfo>();

    /// <summary>
    /// Initializes the runtime catalog provider from a ProviderRegistry instance.
    /// Call this during application startup to enable runtime-derived catalog data.
    /// </summary>
    /// <param name="getCatalog">Function to get all catalog entries from the registry.</param>
    /// <param name="getEntry">Function to get a single catalog entry by ID.</param>
    public static void InitializeFromRegistry(
        Func<IReadOnlyList<ProviderCatalogEntry>> getCatalog,
        Func<string, ProviderCatalogEntry?> getEntry)
    {
        RuntimeCatalogProvider = getCatalog;
        RuntimeCatalogEntryProvider = getEntry;
    }
}

/// <summary>
/// Provider type classification for UI routing.
/// </summary>
public enum ProviderTypeKind : byte
{
    /// <summary>Real-time streaming data provider.</summary>
    Streaming,

    /// <summary>Historical backfill data provider.</summary>
    Backfill,

    /// <summary>Symbol search/lookup provider.</summary>
    SymbolSearch,

    /// <summary>Supports both streaming and backfill.</summary>
    Hybrid
}

/// <summary>
/// Centralized provider metadata entry for standardized UI consumption.
/// </summary>
public sealed class ProviderCatalogEntry
{
    /// <summary>
    /// Gets the unique provider identifier.
    /// </summary>
    [JsonPropertyName("providerId")]
    public string ProviderId { get; init; } = string.Empty;

    /// <summary>
    /// Gets the unique provider identifier (alias for ProviderId).
    /// Provided for backward compatibility with APIs expecting "id" field.
    /// </summary>
    [JsonPropertyName("id")]
    public string Id => ProviderId;

    /// <summary>
    /// Gets the human-friendly display name for the provider.
    /// </summary>
    [JsonPropertyName("displayName")]
    public string DisplayName { get; init; } = string.Empty;

    /// <summary>
    /// Gets the human-friendly display name (alias for DisplayName).
    /// Provided for backward compatibility with APIs expecting "name" field.
    /// </summary>
    [JsonPropertyName("name")]
    public string Name => DisplayName;

    /// <summary>
    /// Gets a short description of the provider.
    /// </summary>
    [JsonPropertyName("description")]
    public string Description { get; init; } = string.Empty;

    /// <summary>
    /// Gets the provider type classification.
    /// </summary>
    [JsonPropertyName("providerType")]
    public ProviderTypeKind ProviderType { get; init; }

    /// <summary>
    /// Gets a value indicating whether credentials are required.
    /// </summary>
    [JsonPropertyName("requiresCredentials")]
    public bool RequiresCredentials { get; init; }

    /// <summary>
    /// Gets the credential fields required by the provider.
    /// </summary>
    [JsonPropertyName("credentialFields")]
    public CredentialFieldInfo[] CredentialFields { get; init; } = Array.Empty<CredentialFieldInfo>();

    /// <summary>
    /// Gets rate limit information for the provider, if available.
    /// </summary>
    [JsonPropertyName("rateLimit")]
    public RateLimitInfo? RateLimit { get; init; }

    /// <summary>
    /// Gets informational notes for the provider.
    /// </summary>
    [JsonPropertyName("notes")]
    public string[] Notes { get; init; } = Array.Empty<string>();

    /// <summary>
    /// Gets warning messages for the provider.
    /// </summary>
    [JsonPropertyName("warnings")]
    public string[] Warnings { get; init; } = Array.Empty<string>();

    /// <summary>
    /// Gets the supported market identifiers.
    /// </summary>
    [JsonPropertyName("supportedMarkets")]
    public string[] SupportedMarkets { get; init; } = new[] { "US" };

    /// <summary>
    /// Gets the data types offered by the provider.
    /// </summary>
    [JsonPropertyName("dataTypes")]
    public string[] DataTypes { get; init; } = Array.Empty<string>();

    /// <summary>
    /// Gets the provider capability metadata.
    /// </summary>
    [JsonPropertyName("capabilities")]
    public CapabilityInfo Capabilities { get; init; } = new();
}

/// <summary>
/// Credential field metadata for UI form generation.
/// </summary>
/// <param name="Name">The machine-readable credential field name.</param>
/// <param name="EnvironmentVariable">The environment variable name, if applicable.</param>
/// <param name="DisplayName">The display label for the credential field.</param>
/// <param name="Required">Whether the credential field is required.</param>
/// <param name="DefaultValue">The default value to use when none is provided.</param>
/// <param name="EnvironmentVariableAliases">Additional environment variable aliases that should also be treated as valid for this credential.</param>
public sealed record CredentialFieldInfo(
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("envVar")] string? EnvironmentVariable,
    [property: JsonPropertyName("displayName")] string DisplayName,
    [property: JsonPropertyName("required")] bool Required,
    [property: JsonPropertyName("defaultValue")] string? DefaultValue = null,
    [property: JsonPropertyName("envVarAliases")] string[]? EnvironmentVariableAliases = null)
{
    public string[] AllEnvironmentVariables =>
        (new[] { EnvironmentVariable }
            .Concat(EnvironmentVariableAliases ?? Array.Empty<string>()))
        .Where(envVar => !string.IsNullOrWhiteSpace(envVar))
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .ToArray()!;
}

/// <summary>
/// Rate limit information for UI display and validation.
/// </summary>
public sealed class RateLimitInfo
{
    /// <summary>
    /// Gets the maximum requests allowed per time window.
    /// </summary>
    [JsonPropertyName("maxRequestsPerWindow")]
    public int MaxRequestsPerWindow { get; init; }

    /// <summary>
    /// Gets the length of the rate-limit window in seconds.
    /// </summary>
    [JsonPropertyName("windowSeconds")]
    public int WindowSeconds { get; init; }

    /// <summary>
    /// Gets the minimum delay between requests in milliseconds.
    /// </summary>
    [JsonPropertyName("minDelayMs")]
    public int MinDelayMs { get; init; }

    /// <summary>
    /// Gets the rate-limit description for display purposes.
    /// </summary>
    [JsonPropertyName("description")]
    public string Description { get; init; } = string.Empty;
}

/// <summary>
/// Capability flags for standardized UI consumption.
/// Maps to the core ProviderCapabilities but optimized for JSON serialization.
/// </summary>
public sealed class CapabilityInfo
{
    /// <summary>
    /// Gets a value indicating whether streaming is supported.
    /// </summary>
    [JsonPropertyName("supportsStreaming")]
    public bool SupportsStreaming { get; init; }

    /// <summary>
    /// Gets a value indicating whether market depth is supported.
    /// </summary>
    [JsonPropertyName("supportsMarketDepth")]
    public bool SupportsMarketDepth { get; init; }

    /// <summary>
    /// Gets the maximum number of depth levels supported, if any.
    /// </summary>
    [JsonPropertyName("maxDepthLevels")]
    public int? MaxDepthLevels { get; init; }

    /// <summary>
    /// Gets a value indicating whether adjusted prices are supported.
    /// </summary>
    [JsonPropertyName("supportsAdjustedPrices")]
    public bool SupportsAdjustedPrices { get; init; }

    /// <summary>
    /// Gets a value indicating whether dividend data is supported.
    /// </summary>
    [JsonPropertyName("supportsDividends")]
    public bool SupportsDividends { get; init; }

    /// <summary>
    /// Gets a value indicating whether split data is supported.
    /// </summary>
    [JsonPropertyName("supportsSplits")]
    public bool SupportsSplits { get; init; }

    /// <summary>
    /// Gets a value indicating whether intraday bars are supported.
    /// </summary>
    [JsonPropertyName("supportsIntraday")]
    public bool SupportsIntraday { get; init; }

    /// <summary>
    /// Gets a value indicating whether trade data is supported.
    /// </summary>
    [JsonPropertyName("supportsTrades")]
    public bool SupportsTrades { get; init; }

    /// <summary>
    /// Gets a value indicating whether quote data is supported.
    /// </summary>
    [JsonPropertyName("supportsQuotes")]
    public bool SupportsQuotes { get; init; }

    /// <summary>
    /// Gets a value indicating whether options chain data is supported.
    /// </summary>
    [JsonPropertyName("supportsOptionsChain")]
    public bool SupportsOptionsChain { get; init; }

    /// <summary>
    /// Gets a value indicating whether brokerage execution is supported.
    /// </summary>
    [JsonPropertyName("supportsBrokerage")]
    public bool SupportsBrokerage { get; init; }

    /// <summary>
    /// Gets a value indicating whether auction data is supported.
    /// </summary>
    [JsonPropertyName("supportsAuctions")]
    public bool SupportsAuctions { get; init; }

    /// <summary>
    /// Converts to dictionary for JSON serialization (only includes true values).
    /// </summary>
    public IReadOnlyDictionary<string, object> ToDictionary()
    {
        var dict = new Dictionary<string, object>();

        if (SupportsStreaming)
            dict["SupportsStreaming"] = true;
        if (SupportsMarketDepth)
            dict["SupportsMarketDepth"] = true;
        if (MaxDepthLevels.HasValue)
            dict["MaxDepthLevels"] = MaxDepthLevels.Value;
        if (SupportsAdjustedPrices)
            dict["SupportsAdjustedPrices"] = true;
        if (SupportsDividends)
            dict["SupportsDividends"] = true;
        if (SupportsSplits)
            dict["SupportsSplits"] = true;
        if (SupportsIntraday)
            dict["SupportsIntraday"] = true;
        if (SupportsTrades)
            dict["SupportsTrades"] = true;
        if (SupportsQuotes)
            dict["SupportsQuotes"] = true;
        if (SupportsOptionsChain)
            dict["SupportsOptionsChain"] = true;
        if (SupportsBrokerage)
            dict["SupportsBrokerage"] = true;
        if (SupportsAuctions)
            dict["SupportsAuctions"] = true;

        return dict;
    }
}
