using Meridian.Domain.Events;

namespace Meridian.Domain.Models;

/// <summary>
/// Represents a real-time OHLCV aggregate bar from streaming data providers.
/// Unlike <see cref="HistoricalBar"/> which uses DateOnly for daily bars,
/// this model uses DateTimeOffset to support intraday timeframes (second, minute, etc.).
/// </summary>
/// <remarks>
/// Polygon.io provides two aggregate event types:
/// - "A" (per-second aggregates)
/// - "AM" (per-minute aggregates)
/// </remarks>
public sealed record AggregateBar : MarketEventPayload
{
    /// <summary>
    /// The ticker symbol for this aggregate.
    /// </summary>
    public string Symbol { get; }

    /// <summary>
    /// The start timestamp of the aggregate window (UTC).
    /// </summary>
    public DateTimeOffset StartTime { get; }

    /// <summary>
    /// The end timestamp of the aggregate window (UTC).
    /// </summary>
    public DateTimeOffset EndTime { get; }

    /// <summary>
    /// Opening price of the aggregate window.
    /// </summary>
    public decimal Open { get; }

    /// <summary>
    /// Highest price during the aggregate window.
    /// </summary>
    public decimal High { get; }

    /// <summary>
    /// Lowest price during the aggregate window.
    /// </summary>
    public decimal Low { get; }

    /// <summary>
    /// Closing price of the aggregate window.
    /// </summary>
    public decimal Close { get; }

    /// <summary>
    /// Total volume during the aggregate window.
    /// </summary>
    public long Volume { get; }

    /// <summary>
    /// Volume-weighted average price for the aggregate window.
    /// </summary>
    public decimal Vwap { get; }

    /// <summary>
    /// Number of trades in the aggregate window.
    /// </summary>
    public int TradeCount { get; }

    /// <summary>
    /// The timeframe of this aggregate (e.g., "second", "minute").
    /// </summary>
    public AggregateTimeframe Timeframe { get; }

    /// <summary>
    /// Data source that provided this aggregate.
    /// </summary>
    public string Source { get; }

    /// <summary>
    /// Sequence number for ordering.
    /// </summary>
    public long SequenceNumber { get; }

    public AggregateBar(
        string Symbol,
        DateTimeOffset StartTime,
        DateTimeOffset EndTime,
        decimal Open,
        decimal High,
        decimal Low,
        decimal Close,
        long Volume,
        decimal Vwap = 0m,
        int TradeCount = 0,
        AggregateTimeframe Timeframe = AggregateTimeframe.Minute,
        string Source = "Polygon",
        long SequenceNumber = 0)
    {
        if (string.IsNullOrWhiteSpace(Symbol))
            throw new ArgumentException("Symbol is required", nameof(Symbol));

        if (Open <= 0 || High <= 0 || Low <= 0 || Close <= 0)
            throw new ArgumentOutOfRangeException(nameof(Open), "OHLC values must be greater than zero.");

        if (Low > High)
            throw new ArgumentOutOfRangeException(nameof(Low), "Low cannot exceed high.");

        if (Open > High || Close > High)
            throw new ArgumentOutOfRangeException(nameof(High), "Open/Close cannot exceed high.");

        if (Open < Low || Close < Low)
            throw new ArgumentOutOfRangeException(nameof(Low), "Open/Close cannot be below low.");

        if (Volume < 0)
            throw new ArgumentOutOfRangeException(nameof(Volume), "Volume cannot be negative.");

        if (EndTime < StartTime)
            throw new ArgumentOutOfRangeException(nameof(EndTime), "End time cannot be before start time.");

        this.Symbol = Symbol;
        this.StartTime = StartTime;
        this.EndTime = EndTime;
        this.Open = Open;
        this.High = High;
        this.Low = Low;
        this.Close = Close;
        this.Volume = Volume;
        this.Vwap = Vwap;
        this.TradeCount = TradeCount;
        this.Timeframe = Timeframe;
        this.Source = Source;
        this.SequenceNumber = SequenceNumber;
    }
}

/// <summary>
/// Timeframe for aggregate bars.
/// </summary>
public enum AggregateTimeframe : byte
{
    /// <summary>
    /// Per-second aggregate (Polygon "A" event).
    /// </summary>
    Second = 0,

    /// <summary>
    /// Per-minute aggregate (Polygon "AM" event).
    /// </summary>
    Minute = 1,

    /// <summary>
    /// Hourly aggregate.
    /// </summary>
    Hour = 2,

    /// <summary>
    /// Daily aggregate.
    /// </summary>
    Day = 3
}
