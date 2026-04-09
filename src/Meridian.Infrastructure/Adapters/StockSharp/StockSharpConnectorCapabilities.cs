namespace Meridian.Infrastructure.Adapters.StockSharp;

/// <summary>
/// Documents the capabilities of each StockSharp connector type.
/// Provides programmatic access to what data types and features each connector supports.
/// </summary>
/// <remarks>
/// This class serves as a centralized reference for connector capabilities, enabling:
/// - UI display of available features per connector
/// - Validation of requested operations against connector capabilities
/// - Documentation of supported data types
///
/// Capability information is based on StockSharp connector documentation and testing.
/// </remarks>
public static class StockSharpConnectorCapabilities
{
    /// <summary>
    /// Get capabilities for a specific connector type.
    /// </summary>
    /// <param name="connectorType">The connector type (e.g., "Rithmic", "IQFeed").</param>
    /// <returns>Capabilities record for the connector, or Unknown if not recognized.</returns>
    public static ConnectorCapabilities GetCapabilities(string connectorType)
    {
        return connectorType.ToLowerInvariant() switch
        {
            "rithmic" => Rithmic,
            "iqfeed" => IQFeed,
            "cqg" => CQG,
            "interactivebrokers" or "ib" => InteractiveBrokers,
            "binance" => Binance,
            "coinbase" => Coinbase,
            "kraken" => Kraken,
            _ => Unknown
        };
    }

    /// <summary>
    /// Rithmic connector capabilities.
    /// Rithmic provides low-latency futures data for CME, NYMEX, COMEX, CBOT.
    /// </summary>
    public static ConnectorCapabilities Rithmic { get; } = new(
        ConnectorType: "Rithmic",
        DisplayName: "Rithmic",
        Description: "Low-latency futures data for CME Group exchanges",
        SupportsStreaming: true,
        SupportsHistorical: true,
        SupportsCandles: true,
        SupportsSecurityLookup: true,
        SupportsOrderLog: true,
        SupportsTrades: true,
        SupportsDepth: true,
        SupportsQuotes: true,
        MaxDepthLevels: 20,
        SupportedMarkets: new[] { "CME", "NYMEX", "COMEX", "CBOT", "ICE" },
        SupportedAssetTypes: new[] { "Future", "FuturesOption" },
        Notes: new[]
        {
            "Requires Rithmic subscription and credentials",
            "Supports paper trading environment",
            "Order log available for supported exchanges"
        },
        Warnings: new[]
        {
            "SSL certificate required for production connections"
        }
    );

    /// <summary>
    /// IQFeed connector capabilities.
    /// IQFeed provides comprehensive US equities, options, and futures data.
    /// </summary>
    public static ConnectorCapabilities IQFeed { get; } = new(
        ConnectorType: "IQFeed",
        DisplayName: "IQFeed",
        Description: "Comprehensive US equities, options, and futures data with historical lookup",
        SupportsStreaming: true,
        SupportsHistorical: true,
        SupportsCandles: true,
        SupportsSecurityLookup: true,
        SupportsOrderLog: true,
        SupportsTrades: true,
        SupportsDepth: true,
        SupportsQuotes: true,
        MaxDepthLevels: 10,
        SupportedMarkets: new[] { "NYSE", "NASDAQ", "AMEX", "CME", "NYMEX", "COMEX", "CBOT" },
        SupportedAssetTypes: new[] { "Stock", "ETF", "Option", "Future", "FuturesOption", "Index" },
        Notes: new[]
        {
            "Requires DTN IQFeed subscription",
            "IQFeed client software must be running locally",
            "Supports historical tick data lookup"
        },
        Warnings: new[]
        {
            "Windows only - requires IQFeed client application"
        }
    );

    /// <summary>
    /// CQG connector capabilities.
    /// CQG provides futures and options data with excellent historical coverage.
    /// </summary>
    public static ConnectorCapabilities CQG { get; } = new(
        ConnectorType: "CQG",
        DisplayName: "CQG",
        Description: "Futures and options data with comprehensive historical coverage",
        SupportsStreaming: true,
        SupportsHistorical: true,
        SupportsCandles: true,
        SupportsSecurityLookup: true,
        SupportsOrderLog: false,
        SupportsTrades: true,
        SupportsDepth: true,
        SupportsQuotes: true,
        MaxDepthLevels: 10,
        SupportedMarkets: new[] { "CME", "NYMEX", "COMEX", "CBOT", "ICE", "Eurex", "LME" },
        SupportedAssetTypes: new[] { "Future", "FuturesOption", "Option" },
        Notes: new[]
        {
            "Requires CQG subscription and credentials",
            "Demo server available for testing",
            "Excellent historical data coverage"
        },
        Warnings: Array.Empty<string>()
    );

    /// <summary>
    /// Interactive Brokers connector capabilities (via StockSharp).
    /// IB provides global multi-asset coverage through TWS/Gateway.
    /// </summary>
    public static ConnectorCapabilities InteractiveBrokers { get; } = new(
        ConnectorType: "InteractiveBrokers",
        DisplayName: "Interactive Brokers",
        Description: "Global multi-asset coverage through TWS/Gateway",
        SupportsStreaming: true,
        SupportsHistorical: true,
        SupportsCandles: true,
        SupportsSecurityLookup: true,
        SupportsOrderLog: false,
        SupportsTrades: true,
        SupportsDepth: true,
        SupportsQuotes: true,
        MaxDepthLevels: 10,
        SupportedMarkets: new[] { "NYSE", "NASDAQ", "AMEX", "ARCA", "BATS", "CME", "LSE", "TSE", "HKEX" },
        SupportedAssetTypes: new[] { "Stock", "ETF", "Option", "Future", "FuturesOption", "Forex", "Bond", "CFD", "Index" },
        Notes: new[]
        {
            "Requires TWS or IB Gateway running locally",
            "Supports SMART routing across multiple exchanges",
            "Paper trading available",
            "Historical data includes adjusted prices"
        },
        Warnings: new[]
        {
            "Market data subscriptions required for real-time data",
            "Historical data may have pacing violations"
        }
    );

    /// <summary>
    /// Binance connector capabilities.
    /// Binance provides crypto spot and futures data.
    /// </summary>
    public static ConnectorCapabilities Binance { get; } = new(
        ConnectorType: "Binance",
        DisplayName: "Binance",
        Description: "Crypto spot and futures data via WebSocket",
        SupportsStreaming: true,
        SupportsHistorical: true,
        SupportsCandles: true,
        SupportsSecurityLookup: true,
        SupportsOrderLog: false,
        SupportsTrades: true,
        SupportsDepth: true,
        SupportsQuotes: true,
        MaxDepthLevels: 20,
        SupportedMarkets: new[] { "Binance" },
        SupportedAssetTypes: new[] { "Crypto" },
        Notes: new[]
        {
            "Requires StockSharp crowdfunding membership",
            "Supports spot, USDT futures, and coin-margined futures",
            "Testnet available for development"
        },
        Warnings: new[]
        {
            "API rate limits apply",
            "Requires separate NuGet package: StockSharp.Binance"
        }
    );

    /// <summary>
    /// Coinbase connector capabilities.
    /// Coinbase provides crypto data via Advanced Trade API.
    /// </summary>
    public static ConnectorCapabilities Coinbase { get; } = new(
        ConnectorType: "Coinbase",
        DisplayName: "Coinbase",
        Description: "Crypto data via Coinbase Advanced Trade API",
        SupportsStreaming: true,
        SupportsHistorical: true,
        SupportsCandles: true,
        SupportsSecurityLookup: true,
        SupportsOrderLog: false,
        SupportsTrades: true,
        SupportsDepth: true,
        SupportsQuotes: true,
        MaxDepthLevels: 50,
        SupportedMarkets: new[] { "Coinbase" },
        SupportedAssetTypes: new[] { "Crypto" },
        Notes: new[]
        {
            "Requires StockSharp crowdfunding membership",
            "Supports Level 2 and Level 3 order book data",
            "Sandbox environment available"
        },
        Warnings: new[]
        {
            "Requires separate NuGet package: StockSharp.Coinbase"
        }
    );

    /// <summary>
    /// Kraken connector capabilities.
    /// Kraken provides crypto spot market data.
    /// </summary>
    public static ConnectorCapabilities Kraken { get; } = new(
        ConnectorType: "Kraken",
        DisplayName: "Kraken",
        Description: "Crypto spot market data via WebSocket",
        SupportsStreaming: true,
        SupportsHistorical: true,
        SupportsCandles: true,
        SupportsSecurityLookup: true,
        SupportsOrderLog: false,
        SupportsTrades: true,
        SupportsDepth: true,
        SupportsQuotes: true,
        MaxDepthLevels: 1000,
        SupportedMarkets: new[] { "Kraken" },
        SupportedAssetTypes: new[] { "Crypto" },
        Notes: new[]
        {
            "Requires StockSharp crowdfunding membership",
            "Configurable order book depth (10, 25, 100, 500, 1000)",
            "Supports OHLC candle subscriptions"
        },
        Warnings: new[]
        {
            "Requires separate NuGet package: StockSharp.Kraken"
        }
    );

    /// <summary>
    /// Unknown/unsupported connector.
    /// </summary>
    public static ConnectorCapabilities Unknown { get; } = new(
        ConnectorType: "Unknown",
        DisplayName: "Unknown Connector",
        Description: "Connector type not recognized",
        SupportsStreaming: false,
        SupportsHistorical: false,
        SupportsCandles: false,
        SupportsSecurityLookup: false,
        SupportsOrderLog: false,
        SupportsTrades: false,
        SupportsDepth: false,
        SupportsQuotes: false,
        MaxDepthLevels: null,
        SupportedMarkets: Array.Empty<string>(),
        SupportedAssetTypes: Array.Empty<string>(),
        Notes: Array.Empty<string>(),
        Warnings: new[] { "Connector type not recognized. Use Custom connector with AdapterType." }
    );

    /// <summary>
    /// Get all known connector types.
    /// </summary>
    public static IReadOnlyList<string> KnownConnectorTypes { get; } = new[]
    {
        "Rithmic",
        "IQFeed",
        "CQG",
        "InteractiveBrokers",
        "Binance",
        "Coinbase",
        "Kraken"
    };

    /// <summary>
    /// The StockSharp adapter set Meridian treats as Wave 1 validated.
    /// Other recognized connectors remain available as optional/example paths,
    /// but they are not part of the provider-confidence gate.
    /// </summary>
    public static IReadOnlyList<string> Wave1ValidatedConnectorTypes { get; } = new[]
    {
        "Rithmic",
        "IQFeed",
        "CQG",
        "InteractiveBrokers"
    };

    /// <summary>
    /// Get all connectors that support historical data.
    /// </summary>
    public static IReadOnlyList<ConnectorCapabilities> GetConnectorsWithHistoricalSupport()
    {
        return KnownConnectorTypes
            .Select(GetCapabilities)
            .Where(c => c.SupportsHistorical)
            .ToList();
    }

    /// <summary>
    /// Get the connector capabilities that are part of Meridian's Wave 1 provider-confidence gate.
    /// </summary>
    public static IReadOnlyList<ConnectorCapabilities> GetWave1ValidatedConnectors()
    {
        return Wave1ValidatedConnectorTypes
            .Select(GetCapabilities)
            .ToList();
    }

    /// <summary>
    /// Get recognized named connectors that remain optional/example paths outside the Wave 1 gate.
    /// </summary>
    public static IReadOnlyList<ConnectorCapabilities> GetOptionalExampleConnectors()
    {
        return KnownConnectorTypes
            .Except(Wave1ValidatedConnectorTypes, StringComparer.OrdinalIgnoreCase)
            .Select(GetCapabilities)
            .ToList();
    }

    /// <summary>
    /// Get all connectors that support order log.
    /// </summary>
    public static IReadOnlyList<ConnectorCapabilities> GetConnectorsWithOrderLogSupport()
    {
        return KnownConnectorTypes
            .Select(GetCapabilities)
            .Where(c => c.SupportsOrderLog)
            .ToList();
    }

    /// <summary>
    /// Get connectors that support a specific market.
    /// </summary>
    public static IReadOnlyList<ConnectorCapabilities> GetConnectorsForMarket(string market)
    {
        return KnownConnectorTypes
            .Select(GetCapabilities)
            .Where(c => c.SupportedMarkets.Contains(market, StringComparer.OrdinalIgnoreCase))
            .ToList();
    }
}

/// <summary>
/// Detailed capabilities record for a StockSharp connector.
/// </summary>
/// <param name="ConnectorType">The connector type identifier.</param>
/// <param name="DisplayName">Human-readable display name.</param>
/// <param name="Description">Description of the connector.</param>
/// <param name="SupportsStreaming">Whether real-time streaming is supported.</param>
/// <param name="SupportsHistorical">Whether historical data downloads are supported.</param>
/// <param name="SupportsCandles">Whether candle/OHLC subscriptions are supported.</param>
/// <param name="SupportsSecurityLookup">Whether security/symbol lookup is supported.</param>
/// <param name="SupportsOrderLog">Whether order log (tape) data is supported.</param>
/// <param name="SupportsTrades">Whether trade tick data is supported.</param>
/// <param name="SupportsDepth">Whether market depth (L2) data is supported.</param>
/// <param name="SupportsQuotes">Whether BBO/quote data is supported.</param>
/// <param name="MaxDepthLevels">Maximum depth levels supported (null = unlimited).</param>
/// <param name="SupportedMarkets">List of supported markets/exchanges.</param>
/// <param name="SupportedAssetTypes">List of supported asset types.</param>
/// <param name="Notes">Informational notes about using this connector.</param>
/// <param name="Warnings">Warnings about limitations or requirements.</param>
public sealed record ConnectorCapabilities(
    string ConnectorType,
    string DisplayName,
    string Description,
    bool SupportsStreaming,
    bool SupportsHistorical,
    bool SupportsCandles,
    bool SupportsSecurityLookup,
    bool SupportsOrderLog,
    bool SupportsTrades,
    bool SupportsDepth,
    bool SupportsQuotes,
    int? MaxDepthLevels,
    IReadOnlyList<string> SupportedMarkets,
    IReadOnlyList<string> SupportedAssetTypes,
    IReadOnlyList<string> Notes,
    IReadOnlyList<string> Warnings
)
{
    /// <summary>
    /// Check if the connector supports a specific market.
    /// </summary>
    public bool SupportsMarket(string market) =>
        SupportedMarkets.Contains(market, StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Check if the connector supports a specific asset type.
    /// </summary>
    public bool SupportsAssetType(string assetType) =>
        SupportedAssetTypes.Contains(assetType, StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Get a summary of capabilities as a dictionary for UI display.
    /// </summary>
    public IReadOnlyDictionary<string, object> ToDictionary() => new Dictionary<string, object>
    {
        ["connectorType"] = ConnectorType,
        ["displayName"] = DisplayName,
        ["description"] = Description,
        ["supportsStreaming"] = SupportsStreaming,
        ["supportsHistorical"] = SupportsHistorical,
        ["supportsCandles"] = SupportsCandles,
        ["supportsSecurityLookup"] = SupportsSecurityLookup,
        ["supportsOrderLog"] = SupportsOrderLog,
        ["supportsTrades"] = SupportsTrades,
        ["supportsDepth"] = SupportsDepth,
        ["supportsQuotes"] = SupportsQuotes,
        ["maxDepthLevels"] = MaxDepthLevels ?? 0,
        ["supportedMarkets"] = SupportedMarkets,
        ["supportedAssetTypes"] = SupportedAssetTypes,
        ["notes"] = Notes,
        ["warnings"] = Warnings
    };
}
