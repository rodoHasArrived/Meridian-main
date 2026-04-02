using Bogus;
using Meridian.Contracts.Domain.Models;

namespace Meridian.Tests.TestHelpers.Builders;

/// <summary>
/// Fluent builder for <see cref="HistoricalBar"/> test instances.
/// Defaults satisfy all OHLC validation constraints; only override what the test cares about.
/// </summary>
/// <example>
/// <code>
/// var bar = new HistoricalBarBuilder().ForSymbol("AAPL").Build();
/// var bars = new HistoricalBarBuilder().ForSymbol("SPY").CreateMany(30);
/// </code>
/// </example>
public sealed class HistoricalBarBuilder
{
    // Instance-level Faker so parallel test execution does not race on a shared static.
    private readonly Faker _faker = new();

    private string _symbol = "TEST";
    private DateOnly _sessionDate = new(2024, 1, 2);
    private decimal? _open;
    private decimal? _high;
    private decimal? _low;
    private decimal? _close;
    private long? _volume;
    private string _source = "test";
    private long _sequenceNumber = 0;

    /// <summary>Sets the ticker symbol.</summary>
    public HistoricalBarBuilder ForSymbol(string symbol)
    {
        _symbol = symbol;
        return this;
    }

    /// <summary>Sets the trading session date.</summary>
    public HistoricalBarBuilder ForDate(DateOnly date)
    {
        _sessionDate = date;
        return this;
    }

    /// <summary>Sets all four OHLC prices explicitly. Must satisfy High >= Open, Close >= Low.</summary>
    public HistoricalBarBuilder WithOhlc(decimal open, decimal high, decimal low, decimal close)
    {
        _open = open;
        _high = high;
        _low = low;
        _close = close;
        return this;
    }

    /// <summary>Overrides only the closing price; other prices are derived from it.</summary>
    public HistoricalBarBuilder WithClose(decimal close)
    {
        _close = close;
        return this;
    }

    /// <summary>Sets the session volume.</summary>
    public HistoricalBarBuilder WithVolume(long volume)
    {
        _volume = volume;
        return this;
    }

    /// <summary>Sets the data-source label (e.g. "alpaca", "stooq").</summary>
    public HistoricalBarBuilder WithSource(string source)
    {
        _source = source;
        return this;
    }

    /// <summary>Sets the sequence number.</summary>
    public HistoricalBarBuilder WithSequenceNumber(long sequenceNumber)
    {
        _sequenceNumber = sequenceNumber;
        return this;
    }

    /// <summary>Builds a single <see cref="HistoricalBar"/> using the configured values.</summary>
    public HistoricalBar Build()
    {
        var (open, high, low, close) = ResolveOhlc(_open, _high, _low, _close, _faker);
        return new HistoricalBar(
            Symbol: _symbol,
            SessionDate: _sessionDate,
            Open: open,
            High: high,
            Low: low,
            Close: close,
            Volume: _volume ?? _faker.Random.Long(100_000L, 50_000_000L),
            Source: _source,
            SequenceNumber: _sequenceNumber);
    }

    /// <summary>
    /// Builds a chronological sequence of <paramref name="count"/> bars with a random-walk price.
    /// The date advances by one calendar day per bar.
    /// </summary>
    public IReadOnlyList<HistoricalBar> CreateMany(int count)
    {
        var bars = new List<HistoricalBar>(count);
        var date = _sessionDate;
        var price = _close ?? Math.Round((decimal)_faker.Random.Double(50, 300), 2);

        for (var i = 0; i < count; i++)
        {
            var (open, high, low, close) = RandomWalkOhlc(price, _faker);
            bars.Add(new HistoricalBar(
                Symbol: _symbol,
                SessionDate: date,
                Open: open,
                High: high,
                Low: low,
                Close: close,
                Volume: _volume ?? _faker.Random.Long(100_000L, 50_000_000L),
                Source: _source,
                SequenceNumber: i + 1));

            price = close;
            date = date.AddDays(1);
        }

        return bars;
    }

    // ── helpers ──────────────────────────────────────────────────────────────

    private static (decimal open, decimal high, decimal low, decimal close) ResolveOhlc(
        decimal? openOverride, decimal? highOverride, decimal? lowOverride, decimal? closeOverride,
        Faker faker)
    {
        var basePrice = closeOverride ?? Math.Round((decimal)faker.Random.Double(50, 300), 2);
        var close = closeOverride ?? basePrice;
        var open = openOverride ?? Math.Round(basePrice * (decimal)(1.0 + faker.Random.Double(-0.02, 0.02)), 2);

        // Ensure high/low strictly respect OHLC constraints after rounding.
        var rawHigh = highOverride ?? Math.Max(open, close) * (decimal)(1.0 + faker.Random.Double(0.001, 0.01));
        var rawLow = lowOverride ?? Math.Min(open, close) * (decimal)(1.0 - faker.Random.Double(0.001, 0.01));

        var high = Math.Round(rawHigh, 2);
        var low = Math.Round(rawLow, 2);

        // Fix up rounding edge-cases to preserve invariants.
        high = Math.Max(high, Math.Max(open, close));
        low = Math.Min(low, Math.Min(open, close));

        return (open, high, low, close);
    }

    private static (decimal open, decimal high, decimal low, decimal close) RandomWalkOhlc(
        decimal prevClose, Faker faker)
    {
        var close = Math.Round(prevClose * (decimal)(1.0 + faker.Random.Double(-0.02, 0.02)), 2);
        if (close <= 0)
            close = Math.Round(prevClose * 0.98m, 2);

        var open = Math.Round(prevClose * (decimal)(1.0 + faker.Random.Double(-0.005, 0.005)), 2);
        if (open <= 0)
            open = prevClose;

        var rawHigh = Math.Max(open, close) * (decimal)(1.0 + faker.Random.Double(0.001, 0.005));
        var rawLow = Math.Min(open, close) * (decimal)(1.0 - faker.Random.Double(0.001, 0.005));

        var high = Math.Max(Math.Round(rawHigh, 2), Math.Max(open, close));
        var low = Math.Min(Math.Round(rawLow, 2), Math.Min(open, close));

        return (open, high, low, close);
    }
}
