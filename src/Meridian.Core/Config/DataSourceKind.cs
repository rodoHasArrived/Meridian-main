namespace Meridian.Application.Config;

/// <summary>
/// Supported market data providers.
/// </summary>
public enum DataSourceKind : byte
{
    /// <summary>Interactive Brokers via native TWS API.</summary>
    IB = 0,

    /// <summary>Alpaca Markets via WebSocket.</summary>
    Alpaca = 1,

    /// <summary>Polygon.io via WebSocket.</summary>
    Polygon = 2,

    /// <summary>
    /// StockSharp unified connector framework.
    /// Provides access to 90+ data sources including Rithmic, IQFeed, CQG, and more.
    /// </summary>
    StockSharp = 3,

    /// <summary>NYSE market data feed.</summary>
    NYSE = 4,

    /// <summary>Built-in synthetic/reference market data for offline development and replay.</summary>
    Synthetic = 5
}
