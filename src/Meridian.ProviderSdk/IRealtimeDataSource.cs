using System.Threading;
using Meridian.Contracts.Configuration;
using Meridian.Contracts.Domain.Enums;

namespace Meridian.Infrastructure.DataSources;

/// <summary>
/// Interface for real-time data sources providing streaming market data.
/// Extends IDataSource with real-time specific functionality for trades,
/// quotes, and market depth.
/// </summary>
public interface IRealtimeDataSource : IDataSource
{
    #region Connection

    /// <summary>
    /// Connects to the real-time data stream.
    /// </summary>
    Task ConnectAsync(CancellationToken ct = default);

    /// <summary>
    /// Disconnects from the real-time data stream.
    /// </summary>
    Task DisconnectAsync(CancellationToken ct = default);

    /// <summary>
    /// Whether the source is currently connected.
    /// </summary>
    bool IsConnected { get; }

    #endregion

    #region Trade Subscriptions

    /// <summary>
    /// Subscribes to real-time trade prints for the specified symbol.
    /// </summary>
    /// <param name="config">Symbol configuration.</param>
    /// <returns>Subscription ID for unsubscription.</returns>
    int SubscribeTrades(SymbolConfig config);

    /// <summary>
    /// Unsubscribes from a trade subscription.
    /// </summary>
    void UnsubscribeTrades(int subscriptionId);

    /// <summary>
    /// Observable stream of trade events.
    /// </summary>
    IObservable<RealtimeTrade> Trades { get; }

    #endregion

    #region Quote Subscriptions

    /// <summary>
    /// Subscribes to real-time BBO quotes for the specified symbol.
    /// </summary>
    /// <param name="config">Symbol configuration.</param>
    /// <returns>Subscription ID for unsubscription.</returns>
    int SubscribeQuotes(SymbolConfig config);

    /// <summary>
    /// Unsubscribes from a quote subscription.
    /// </summary>
    void UnsubscribeQuotes(int subscriptionId);

    /// <summary>
    /// Observable stream of quote events.
    /// </summary>
    IObservable<RealtimeQuote> Quotes { get; }

    #endregion

    #region Depth Subscriptions

    /// <summary>
    /// Subscribes to market depth for the specified symbol.
    /// </summary>
    /// <param name="config">Symbol configuration.</param>
    /// <returns>Subscription ID for unsubscription.</returns>
    int SubscribeMarketDepth(SymbolConfig config);

    /// <summary>
    /// Unsubscribes from a depth subscription.
    /// </summary>
    void UnsubscribeMarketDepth(int subscriptionId);

    /// <summary>
    /// Observable stream of market depth events.
    /// </summary>
    IObservable<RealtimeDepthUpdate> DepthUpdates { get; }

    #endregion

    #region Active Subscriptions

    /// <summary>
    /// Gets all active subscription IDs.
    /// </summary>
    IReadOnlySet<int> ActiveSubscriptions { get; }

    /// <summary>
    /// Gets the symbols currently subscribed.
    /// </summary>
    IReadOnlySet<string> SubscribedSymbols { get; }

    /// <summary>
    /// Unsubscribes from all active subscriptions.
    /// </summary>
    void UnsubscribeAll();

    #endregion
}

/// <summary>
/// Interface for sources that provide trade data.
/// </summary>
public interface ITradeSource
{
    /// <summary>
    /// Subscribes to trade prints for the given symbol.
    /// </summary>
    int SubscribeTrades(SymbolConfig config);

    /// <summary>
    /// Unsubscribes from a trade subscription.
    /// </summary>
    void UnsubscribeTrades(int subscriptionId);

    /// <summary>
    /// Observable stream of trade events.
    /// </summary>
    IObservable<RealtimeTrade> Trades { get; }
}

/// <summary>
/// Interface for sources that provide quote data.
/// </summary>
public interface IQuoteSource
{
    /// <summary>
    /// Subscribes to BBO quotes for the given symbol.
    /// </summary>
    int SubscribeQuotes(SymbolConfig config);

    /// <summary>
    /// Unsubscribes from a quote subscription.
    /// </summary>
    void UnsubscribeQuotes(int subscriptionId);

    /// <summary>
    /// Observable stream of quote events.
    /// </summary>
    IObservable<RealtimeQuote> Quotes { get; }
}

/// <summary>
/// Interface for sources that provide market depth data.
/// </summary>
public interface IDepthSource
{
    /// <summary>
    /// Subscribes to market depth for the given symbol.
    /// </summary>
    int SubscribeMarketDepth(SymbolConfig config);

    /// <summary>
    /// Unsubscribes from a depth subscription.
    /// </summary>
    void UnsubscribeMarketDepth(int subscriptionId);

    /// <summary>
    /// Observable stream of market depth updates.
    /// </summary>
    IObservable<RealtimeDepthUpdate> DepthUpdates { get; }

    /// <summary>
    /// Maximum number of depth levels supported.
    /// </summary>
    int MaxDepthLevels { get; }
}

#region Real-time Event Types

/// <summary>
/// Real-time trade event from a data source.
/// </summary>
public sealed record RealtimeTrade(
    string Symbol,
    decimal Price,
    long Size,
    DateTimeOffset Timestamp,
    string SourceId,
    string? Exchange = null,
    string? Conditions = null,
    long? SequenceNumber = null,
    AggressorSide Side = AggressorSide.Unknown
);

/// <summary>
/// Real-time BBO quote event from a data source.
/// </summary>
public sealed record RealtimeQuote(
    string Symbol,
    decimal BidPrice,
    long BidSize,
    decimal AskPrice,
    long AskSize,
    DateTimeOffset Timestamp,
    string SourceId,
    string? BidExchange = null,
    string? AskExchange = null,
    long? SequenceNumber = null
);

/// <summary>
/// Real-time market depth update from a data source.
/// </summary>
public sealed record RealtimeDepthUpdate(
    string Symbol,
    DepthOperation Operation,
    OrderBookSide Side,
    int Level,
    decimal Price,
    long Size,
    DateTimeOffset Timestamp,
    string SourceId,
    string? MarketMaker = null,
    long? SequenceNumber = null
);

#endregion
