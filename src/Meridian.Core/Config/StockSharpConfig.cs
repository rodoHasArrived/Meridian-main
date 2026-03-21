namespace Meridian.Application.Config;

/// <summary>
/// Configuration for StockSharp connector integration.
/// Provides access to 90+ data sources through StockSharp's unified connector framework.
/// Supported connectors: Rithmic, IQFeed, CQG, InteractiveBrokers, Binance, Coinbase, Kraken, and more.
/// </summary>
public sealed record StockSharpConfig(
    bool Enabled = false,

    string ConnectorType = "Rithmic",

    string? AdapterType = null,

    string? AdapterAssembly = null,

    Dictionary<string, string>? ConnectionParams = null,

    bool UseBinaryStorage = false,

    string StoragePath = "data/stocksharp/{connector}",

    bool EnableRealTime = true,

    bool EnableHistorical = true,

    RithmicConfig? Rithmic = null,

    IQFeedConfig? IQFeed = null,

    CQGConfig? CQG = null,

    StockSharpIBConfig? InteractiveBrokers = null,

    BinanceConfig? Binance = null,

    CoinbaseConfig? Coinbase = null,

    KrakenConfig? Kraken = null
);

/// <summary>
/// Rithmic-specific configuration.
/// Rithmic provides low-latency futures data for CME, NYMEX, COMEX, etc.
/// </summary>
public sealed record RithmicConfig(
    string Server = "Rithmic Test",

    string UserName = "",

    string Password = "",

    string CertFile = "",

    bool UsePaperTrading = true
);

/// <summary>
/// IQFeed-specific configuration.
/// IQFeed provides tick-level equities data with historical lookups.
/// </summary>
public sealed record IQFeedConfig(
    string Host = "127.0.0.1",

    int Level1Port = 9100,

    int Level2Port = 9200,

    int LookupPort = 9300,

    string ProductId = "",

    string ProductVersion = "1.0"
);

/// <summary>
/// CQG-specific configuration.
/// CQG provides futures/options data with excellent historical coverage.
/// </summary>
public sealed record CQGConfig(
    string UserName = "",

    string Password = "",

    bool UseDemoServer = true
);

/// <summary>
/// Interactive Brokers configuration for StockSharp connector.
/// Alternative to native IB TWS API integration.
/// </summary>
public sealed record StockSharpIBConfig(
    string Host = "127.0.0.1",

    int Port = 7496,

    int ClientId = 1
);

/// <summary>
/// Binance crypto exchange configuration.
/// Supports spot and futures markets with real-time WebSocket streams.
/// </summary>
/// <remarks>
/// Requires StockSharp crowdfunding membership for crypto connectors.
/// </remarks>
public sealed record BinanceConfig(
    string ApiKey = "",

    string ApiSecret = "",

    bool UseTestnet = false,

    string MarketType = "Spot",

    bool SubscribeOrderBook = true,

    int OrderBookDepth = 20,

    bool SubscribeTrades = true
);

/// <summary>
/// Coinbase crypto exchange configuration.
/// Supports Coinbase Pro (Advanced Trade) API.
/// </summary>
public sealed record CoinbaseConfig(
    string ApiKey = "",

    string ApiSecret = "",

    string Passphrase = "",

    bool UseSandbox = false,

    bool SubscribeOrderBook = true,

    string OrderBookLevel = "level2",

    bool SubscribeTrades = true
);

/// <summary>
/// Kraken crypto exchange configuration.
/// Supports spot markets with WebSocket streams.
/// </summary>
public sealed record KrakenConfig(
    string ApiKey = "",

    string ApiSecret = "",

    bool SubscribeOrderBook = true,

    int OrderBookDepth = 25,

    bool SubscribeTrades = true,

    bool SubscribeOhlc = false,

    int OhlcInterval = 1
);
