using System.Threading;
using Meridian.Contracts.Domain.Models;

namespace Meridian.Infrastructure.DataSources;

/// <summary>
/// Interface for historical data sources providing bar data, dividends,
/// splits, and other historical market information.
/// </summary>
public interface IHistoricalDataSource : IDataSource
{
    #region Daily Bars

    /// <summary>
    /// Gets historical daily OHLCV bars for a symbol.
    /// </summary>
    /// <param name="symbol">The symbol to fetch.</param>
    /// <param name="from">Start date (inclusive). If null, uses earliest available.</param>
    /// <param name="to">End date (inclusive). If null, uses latest available.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>List of historical bars ordered by date.</returns>
    Task<IReadOnlyList<HistoricalBar>> GetDailyBarsAsync(
        string symbol,
        DateOnly? from = null,
        DateOnly? to = null,
        CancellationToken ct = default);

    /// <summary>
    /// Gets historical daily bars with adjustment information.
    /// </summary>
    Task<IReadOnlyList<AdjustedHistoricalBar>> GetAdjustedDailyBarsAsync(
        string symbol,
        DateOnly? from = null,
        DateOnly? to = null,
        CancellationToken ct = default);

    #endregion

    #region Intraday Bars

    /// <summary>
    /// Whether this source supports intraday bar data.
    /// </summary>
    bool SupportsIntraday { get; }

    /// <summary>
    /// Supported bar intervals (e.g., "1Min", "5Min", "15Min", "1Hour").
    /// </summary>
    IReadOnlyList<string> SupportedBarIntervals { get; }

    /// <summary>
    /// Gets historical intraday bars for a symbol.
    /// </summary>
    /// <param name="symbol">The symbol to fetch.</param>
    /// <param name="interval">Bar interval (e.g., "1Min", "5Min").</param>
    /// <param name="from">Start datetime (inclusive).</param>
    /// <param name="to">End datetime (inclusive).</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>List of intraday bars ordered by time.</returns>
    Task<IReadOnlyList<IntradayBar>> GetIntradayBarsAsync(
        string symbol,
        string interval,
        DateTimeOffset? from = null,
        DateTimeOffset? to = null,
        CancellationToken ct = default);

    #endregion

    #region Corporate Actions

    /// <summary>
    /// Whether this source supports dividend data.
    /// </summary>
    bool SupportsDividends { get; }

    /// <summary>
    /// Whether this source supports split data.
    /// </summary>
    bool SupportsSplits { get; }

    /// <summary>
    /// Gets dividend history for a symbol.
    /// </summary>
    Task<IReadOnlyList<DividendInfo>> GetDividendsAsync(
        string symbol,
        DateOnly? from = null,
        DateOnly? to = null,
        CancellationToken ct = default);

    /// <summary>
    /// Gets stock split history for a symbol.
    /// </summary>
    Task<IReadOnlyList<SplitInfo>> GetSplitsAsync(
        string symbol,
        DateOnly? from = null,
        DateOnly? to = null,
        CancellationToken ct = default);

    #endregion
}

/// <summary>
/// Interface for sources that provide daily bar data.
/// </summary>
public interface IDailyBarSource
{
    /// <summary>
    /// Gets historical daily OHLCV bars for a symbol.
    /// </summary>
    Task<IReadOnlyList<HistoricalBar>> GetDailyBarsAsync(
        string symbol,
        DateOnly? from = null,
        DateOnly? to = null,
        CancellationToken ct = default);
}

/// <summary>
/// Interface for sources that provide intraday bar data.
/// </summary>
public interface IIntradayBarSource
{
    /// <summary>
    /// Supported bar intervals (e.g., "1Min", "5Min", "15Min", "1Hour").
    /// </summary>
    IReadOnlyList<string> SupportedBarIntervals { get; }

    /// <summary>
    /// Gets historical intraday bars for a symbol.
    /// </summary>
    Task<IReadOnlyList<IntradayBar>> GetIntradayBarsAsync(
        string symbol,
        string interval,
        DateTimeOffset? from = null,
        DateTimeOffset? to = null,
        CancellationToken ct = default);
}

/// <summary>
/// Interface for sources that provide corporate action data.
/// </summary>
public interface ICorporateActionSource
{
    /// <summary>
    /// Gets dividend history for a symbol.
    /// </summary>
    Task<IReadOnlyList<DividendInfo>> GetDividendsAsync(
        string symbol,
        DateOnly? from = null,
        DateOnly? to = null,
        CancellationToken ct = default);

    /// <summary>
    /// Gets stock split history for a symbol.
    /// </summary>
    Task<IReadOnlyList<SplitInfo>> GetSplitsAsync(
        string symbol,
        DateOnly? from = null,
        DateOnly? to = null,
        CancellationToken ct = default);
}

#region Historical Data Types

/// <summary>
/// Intraday bar (OHLCV) for a specific time interval.
/// </summary>
public sealed record IntradayBar(
    string Symbol,
    DateTimeOffset Timestamp,
    string Interval,
    decimal Open,
    decimal High,
    decimal Low,
    decimal Close,
    long Volume,
    string Source = "unknown",
    long? TradeCount = null,
    decimal? VWAP = null
);

/// <summary>
/// Dividend information for a stock.
/// </summary>
public sealed record DividendInfo(
    string Symbol,
    DateOnly ExDate,
    DateOnly? PaymentDate,
    DateOnly? RecordDate,
    decimal Amount,
    string Currency = "USD",
    DividendType Type = DividendType.Regular,
    string Source = "unknown"
);

/// <summary>
/// Stock split information.
/// </summary>
public sealed record SplitInfo(
    string Symbol,
    DateOnly ExDate,
    decimal SplitFrom,
    decimal SplitTo,
    string Source = "unknown"
)
{
    /// <summary>
    /// Split ratio as a decimal (e.g., 4:1 split = 4.0).
    /// </summary>
    public decimal SplitRatio => SplitTo / SplitFrom;
}

/// <summary>
/// Type of dividend.
/// </summary>
public enum DividendType : byte
{
    Regular,
    Special,
    Return,
    Liquidation
}

#endregion
