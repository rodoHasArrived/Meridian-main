using Meridian.Infrastructure.Contracts;

namespace Meridian.Infrastructure.Adapters.Core;

/// <summary>
/// Types of market data providers.
/// </summary>
public enum ProviderType : byte
{
    /// <summary>Real-time streaming data provider.</summary>
    Streaming,

    /// <summary>Historical backfill data provider.</summary>
    Backfill,

    /// <summary>Symbol search/lookup provider.</summary>
    SymbolSearch
}

/// <summary>
/// Unified metadata interface that all provider types implement.
/// Enables consistent discovery, routing, and UI presentation across
/// streaming, backfill, and symbol search providers.
/// </summary>
/// <remarks>
/// This interface centralizes provider identity and capabilities into a single
/// contract, eliminating special-case logic in the registry and UI layers.
/// UI-specific properties (Notes, Warnings, CredentialFields) have default
/// implementations returning empty arrays, allowing providers to optionally
/// override them for richer UI presentation.
/// </remarks>
[ImplementsAdr("ADR-001", "Unified provider metadata contract for all provider types")]
public interface IProviderMetadata
{
    /// <summary>
    /// Unique identifier for the provider (e.g., "alpaca", "polygon", "yahoo").
    /// </summary>
    string ProviderId { get; }

    /// <summary>
    /// Human-readable display name.
    /// </summary>
    string ProviderDisplayName { get; }

    /// <summary>
    /// Description of the provider and its capabilities.
    /// </summary>
    string ProviderDescription { get; }

    /// <summary>
    /// Priority for routing and failover (lower = higher priority, tried first).
    /// </summary>
    int ProviderPriority { get; }

    /// <summary>
    /// Unified capability flags and metadata for this provider.
    /// </summary>
    ProviderCapabilities ProviderCapabilities { get; }

    #region UI-Specific Metadata (Default Implementations)

    /// <summary>
    /// Whether this provider requires credentials to operate.
    /// </summary>
    bool RequiresCredentials => ProviderCredentialFields.Length > 0;

    /// <summary>
    /// Credential fields required for this provider.
    /// Override to specify API keys, tokens, or other authentication requirements.
    /// </summary>
    ProviderCredentialField[] ProviderCredentialFields => Array.Empty<ProviderCredentialField>();

    /// <summary>
    /// Informational notes about using this provider (displayed in UI).
    /// </summary>
    string[] ProviderNotes => Array.Empty<string>();

    /// <summary>
    /// Warnings about limitations or issues with this provider (displayed in UI).
    /// </summary>
    string[] ProviderWarnings => Array.Empty<string>();

    /// <summary>
    /// Data types supported by this provider (e.g., "DailyBars", "Trades", "Quotes").
    /// </summary>
    string[] SupportedDataTypes => DeriveDataTypesFromCapabilities();

    /// <summary>
    /// Derives supported data types from provider capabilities.
    /// </summary>
    private string[] DeriveDataTypesFromCapabilities()
    {
        var types = new List<string>();
        var caps = ProviderCapabilities;

        if (caps.SupportsBackfill)
        {
            types.Add("DailyBars");
            if (caps.SupportsIntraday)
                types.Add("IntradayBars");
            if (caps.SupportsDividends)
                types.Add("Dividends");
            if (caps.SupportsSplits)
                types.Add("Splits");
        }

        if (caps.SupportsRealtimeTrades || caps.SupportsHistoricalTrades)
            types.Add("Trades");
        if (caps.SupportsRealtimeQuotes || caps.SupportsHistoricalQuotes)
            types.Add("Quotes");
        if (caps.SupportsMarketDepth)
            types.Add("MarketDepth");
        if (caps.SupportsHistoricalAuctions)
            types.Add("Auctions");
        if (caps.SupportsOptionsChain)
            types.Add("OptionsChain");
        if (caps.SupportsBrokerage)
            types.Add("Brokerage");

        return types.ToArray();
    }

    #endregion
}

/// <summary>
/// Credential field metadata for UI form generation and validation.
/// </summary>
/// <param name="Name">Internal field name (e.g., "ApiKey", "SecretKey").</param>
/// <param name="EnvironmentVariable">Environment variable name (e.g., "ALPACA__KEYID").</param>
/// <param name="DisplayName">Human-readable label for UI display.</param>
/// <param name="Required">Whether this field is required.</param>
/// <param name="DefaultValue">Optional default value hint.</param>
public sealed record ProviderCredentialField(
    string Name,
    string? EnvironmentVariable,
    string DisplayName,
    bool Required,
    string? DefaultValue = null);

/// <summary>
/// Unified capability record that consolidates capabilities across all provider types.
/// Replaces HistoricalDataCapabilities and extends DataSourceCapabilities
/// for consistent capability discovery.
/// </summary>
/// <remarks>
/// This record uses a flags-based approach with additional metadata to support
/// all provider types: streaming, backfill, and symbol search.
/// </remarks>
public sealed record ProviderCapabilities
{
    #region Provider Type Flags

    /// <summary>Supports real-time streaming data.</summary>
    public bool SupportsStreaming { get; init; }

    /// <summary>Supports historical data backfill.</summary>
    public bool SupportsBackfill { get; init; }

    /// <summary>Supports symbol search/lookup.</summary>
    public bool SupportsSymbolSearch { get; init; }

    /// <summary>Supports options chain data retrieval.</summary>
    public bool SupportsOptionsChain { get; init; }

    /// <summary>Supports brokerage order execution (submit, cancel, modify orders).</summary>
    public bool SupportsBrokerage { get; init; }

    #endregion

    #region Streaming Capabilities

    /// <summary>Supports real-time trade data.</summary>
    public bool SupportsRealtimeTrades { get; init; }

    /// <summary>Supports real-time quote data.</summary>
    public bool SupportsRealtimeQuotes { get; init; }

    /// <summary>Supports market depth/order book data.</summary>
    public bool SupportsMarketDepth { get; init; }

    /// <summary>Maximum depth levels supported (null = unlimited).</summary>
    public int? MaxDepthLevels { get; init; }

    /// <summary>Maximum symbols per subscription (null = unlimited).</summary>
    public int? MaxSymbolsPerSubscription { get; init; }

    #endregion

    #region Backfill Capabilities

    /// <summary>Returns split/dividend adjusted prices.</summary>
    public bool SupportsAdjustedPrices { get; init; }

    /// <summary>Supports intraday bar data.</summary>
    public bool SupportsIntraday { get; init; }

    /// <summary>Includes dividend data.</summary>
    public bool SupportsDividends { get; init; }

    /// <summary>Includes split data.</summary>
    public bool SupportsSplits { get; init; }

    /// <summary>Supports historical quote (NBBO) data.</summary>
    public bool SupportsHistoricalQuotes { get; init; }

    /// <summary>Supports historical trade data.</summary>
    public bool SupportsHistoricalTrades { get; init; }

    /// <summary>Supports historical auction data.</summary>
    public bool SupportsHistoricalAuctions { get; init; }

    /// <summary>Minimum bar resolution supported.</summary>
    public TimeSpan? MinBarResolution { get; init; }

    /// <summary>Supported bar intervals (e.g., "1m", "5m", "1h", "1d").</summary>
    public IReadOnlyList<string>? SupportedBarIntervals { get; init; }

    #endregion

    #region Symbol Search Capabilities

    /// <summary>Supports filtering by asset type.</summary>
    public bool SupportsAssetTypeFilter { get; init; }

    /// <summary>Supports filtering by exchange.</summary>
    public bool SupportsExchangeFilter { get; init; }

    /// <summary>Supported asset types for filtering (e.g., "stock", "etf", "crypto").</summary>
    public IReadOnlyList<string>? SupportedAssetTypes { get; init; }

    /// <summary>Supported exchanges for filtering.</summary>
    public IReadOnlyList<string>? SupportedExchanges { get; init; }

    #endregion

    #region Market Coverage

    /// <summary>Supported market regions (e.g., "US", "UK", "DE").</summary>
    public IReadOnlyList<string> SupportedMarkets { get; init; } = new[] { "US" };

    #endregion

    #region Rate Limiting

    /// <summary>Maximum requests per time window.</summary>
    public int? MaxRequestsPerWindow { get; init; }

    /// <summary>Rate limit time window.</summary>
    public TimeSpan? RateLimitWindow { get; init; }

    /// <summary>Minimum delay between requests.</summary>
    public TimeSpan? MinRequestDelay { get; init; }

    #endregion

    #region Factory Methods

    /// <summary>Default empty capabilities.</summary>
    public static ProviderCapabilities None { get; } = new();

    /// <summary>Basic streaming provider capabilities.</summary>
    public static ProviderCapabilities Streaming(
        bool trades = true,
        bool quotes = true,
        bool depth = false,
        int? maxDepthLevels = null) => new()
        {
            SupportsStreaming = true,
            SupportsRealtimeTrades = trades,
            SupportsRealtimeQuotes = quotes,
            SupportsMarketDepth = depth,
            MaxDepthLevels = maxDepthLevels
        };

    /// <summary>Basic backfill provider with daily bars only.</summary>
    public static ProviderCapabilities BackfillBarsOnly { get; } = new()
    {
        SupportsBackfill = true,
        SupportsAdjustedPrices = true,
        SupportsDividends = true,
        SupportsSplits = true
    };

    /// <summary>Full-featured backfill provider.</summary>
    public static ProviderCapabilities BackfillFullFeatured { get; } = new()
    {
        SupportsBackfill = true,
        SupportsAdjustedPrices = true,
        SupportsIntraday = true,
        SupportsDividends = true,
        SupportsSplits = true,
        SupportsHistoricalQuotes = true,
        SupportsHistoricalTrades = true,
        SupportsHistoricalAuctions = true
    };

    /// <summary>Basic symbol search provider.</summary>
    public static ProviderCapabilities SymbolSearch { get; } = new()
    {
        SupportsSymbolSearch = true
    };

    /// <summary>Filterable symbol search provider.</summary>
    public static ProviderCapabilities SymbolSearchFilterable(
        IReadOnlyList<string>? assetTypes = null,
        IReadOnlyList<string>? exchanges = null) => new()
        {
            SupportsSymbolSearch = true,
            SupportsAssetTypeFilter = assetTypes is { Count: > 0 },
            SupportsExchangeFilter = exchanges is { Count: > 0 },
            SupportedAssetTypes = assetTypes,
            SupportedExchanges = exchanges
        };

    /// <summary>Options chain provider capabilities.</summary>
    public static ProviderCapabilities OptionsChain(
        bool greeks = true,
        bool openInterest = true,
        bool streaming = false) => new()
        {
            SupportsStreaming = streaming,
            SupportsOptionsChain = true,
            SupportsRealtimeQuotes = streaming
        };

    /// <summary>Brokerage provider supporting order execution.</summary>
    public static ProviderCapabilities Brokerage(
        bool streaming = false,
        bool backfill = false,
        bool trades = true,
        bool quotes = true) => new()
        {
            SupportsBrokerage = true,
            SupportsStreaming = streaming,
            SupportsBackfill = backfill,
            SupportsRealtimeTrades = trades,
            SupportsRealtimeQuotes = quotes
        };

    /// <summary>Hybrid provider supporting both streaming and backfill.</summary>
    public static ProviderCapabilities Hybrid(
        bool trades = true,
        bool quotes = true,
        bool depth = false,
        bool adjustedPrices = true,
        bool intraday = true) => new()
        {
            SupportsStreaming = true,
            SupportsBackfill = true,
            SupportsRealtimeTrades = trades,
            SupportsRealtimeQuotes = quotes,
            SupportsMarketDepth = depth,
            SupportsAdjustedPrices = adjustedPrices,
            SupportsIntraday = intraday,
            SupportsDividends = true,
            SupportsSplits = true
        };

    #endregion

    #region Conversion Helpers

    /// <summary>
    /// Creates capabilities from legacy HistoricalDataCapabilities.
    /// </summary>
    public static ProviderCapabilities FromHistoricalCapabilities(
        HistoricalDataCapabilities caps,
        int? maxRequestsPerWindow = null,
        TimeSpan? rateLimitWindow = null,
        TimeSpan? minDelay = null) => new()
        {
            SupportsBackfill = true,
            SupportsAdjustedPrices = caps.AdjustedPrices,
            SupportsIntraday = caps.Intraday,
            SupportsDividends = caps.Dividends,
            SupportsSplits = caps.Splits,
            SupportsHistoricalQuotes = caps.Quotes,
            SupportsHistoricalTrades = caps.Trades,
            SupportsHistoricalAuctions = caps.Auctions,
            SupportedMarkets = caps.SupportedMarkets,
            MaxRequestsPerWindow = maxRequestsPerWindow,
            RateLimitWindow = rateLimitWindow,
            MinRequestDelay = minDelay
        };

    /// <summary>
    /// Converts to dictionary for JSON serialization and UI consumption.
    /// </summary>
    public IReadOnlyDictionary<string, object> ToDictionary()
    {
        var dict = new Dictionary<string, object>();

        // Provider types
        if (SupportsStreaming)
            dict["SupportsStreaming"] = true;
        if (SupportsBackfill)
            dict["SupportsBackfill"] = true;
        if (SupportsSymbolSearch)
            dict["SupportsSymbolSearch"] = true;
        if (SupportsOptionsChain)
            dict["SupportsOptionsChain"] = true;
        if (SupportsBrokerage)
            dict["SupportsBrokerage"] = true;

        // Streaming
        if (SupportsRealtimeTrades)
            dict["SupportsRealtimeTrades"] = true;
        if (SupportsRealtimeQuotes)
            dict["SupportsRealtimeQuotes"] = true;
        if (SupportsMarketDepth)
            dict["SupportsMarketDepth"] = true;
        if (MaxDepthLevels.HasValue)
            dict["MaxDepthLevels"] = MaxDepthLevels.Value;
        if (MaxSymbolsPerSubscription.HasValue)
            dict["MaxSymbolsPerSubscription"] = MaxSymbolsPerSubscription.Value;

        // Backfill
        if (SupportsAdjustedPrices)
            dict["SupportsAdjustedPrices"] = true;
        if (SupportsIntraday)
            dict["SupportsIntraday"] = true;
        if (SupportsDividends)
            dict["SupportsDividends"] = true;
        if (SupportsSplits)
            dict["SupportsSplits"] = true;
        if (SupportsHistoricalQuotes)
            dict["SupportsQuotes"] = true;
        if (SupportsHistoricalTrades)
            dict["SupportsTrades"] = true;
        if (SupportsHistoricalAuctions)
            dict["SupportsAuctions"] = true;
        if (MinBarResolution.HasValue)
            dict["MinBarResolutionMs"] = MinBarResolution.Value.TotalMilliseconds;
        if (SupportedBarIntervals is { Count: > 0 })
            dict["SupportedBarIntervals"] = SupportedBarIntervals;

        // Symbol search
        if (SupportedAssetTypes is { Count: > 0 })
            dict["SupportedAssetTypes"] = SupportedAssetTypes;
        if (SupportedExchanges is { Count: > 0 })
            dict["SupportedExchanges"] = SupportedExchanges;

        // Markets
        if (SupportedMarkets is { Count: > 0 })
            dict["SupportedMarkets"] = SupportedMarkets;

        // Rate limiting
        if (MaxRequestsPerWindow.HasValue)
            dict["MaxRequestsPerWindow"] = MaxRequestsPerWindow.Value;
        if (RateLimitWindow.HasValue)
            dict["RateLimitWindowSeconds"] = RateLimitWindow.Value.TotalSeconds;
        if (MinRequestDelay.HasValue)
            dict["RateLimitMinDelayMs"] = MinRequestDelay.Value.TotalMilliseconds;

        return dict;
    }

    #endregion

    #region Computed Properties

    /// <summary>Determines the primary provider type based on capabilities.</summary>
    public ProviderType PrimaryType =>
        SupportsStreaming && SupportsBackfill ? ProviderType.Streaming :
        SupportsStreaming ? ProviderType.Streaming :
        SupportsBackfill ? ProviderType.Backfill :
        SupportsSymbolSearch ? ProviderType.SymbolSearch :
        ProviderType.Streaming;

    /// <summary>Whether the provider has any tick-level historical data.</summary>
    public bool HasTickData => SupportsHistoricalQuotes || SupportsHistoricalTrades || SupportsHistoricalAuctions;

    /// <summary>Whether the provider has corporate action data.</summary>
    public bool HasCorporateActions => SupportsDividends || SupportsSplits;

    /// <summary>Whether the provider supports a specific market.</summary>
    public bool SupportsMarket(string market) =>
        SupportedMarkets.Contains(market, StringComparer.OrdinalIgnoreCase);

    #endregion
}
