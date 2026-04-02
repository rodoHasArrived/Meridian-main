using Bogus;
using Meridian.Contracts.Domain.Events;
using Meridian.Contracts.Domain.Models;

namespace Meridian.Tests.TestHelpers.Builders;

/// <summary>
/// Fluent builder for <see cref="MarketEventDto"/> test instances.
/// Wraps payload builders so tests can describe the event at the right level of abstraction.
/// </summary>
/// <example>
/// <code>
/// // Wrap an explicit Trade payload
/// var evt = new MarketEventBuilder().ForSymbol("AAPL").WithTrade().Build();
///
/// // Wrap an explicit HistoricalBar payload
/// var evt = new MarketEventBuilder().ForSymbol("SPY").WithBar().Build();
///
/// // Bring-your-own payload
/// var trade = new TradeBuilder().AtPrice(150m).Build();
/// var evt = new MarketEventBuilder().ForSymbol("AAPL").WithTrade(trade).Build();
/// </code>
/// </example>
public sealed class MarketEventBuilder
{
    // Instance-level Faker so parallel test execution does not race on a shared static.
    private readonly Faker _faker = new();

    private string _symbol = "TEST";
    private DateTimeOffset? _timestamp;
    private string _source = "test";
    private long _sequence = 0;
    private MarketEventPayload? _payload;
    private BuilderPayloadKind _kind = BuilderPayloadKind.Trade;

    private enum BuilderPayloadKind { Trade, Bar }

    /// <summary>Sets the ticker symbol that appears on the event envelope.</summary>
    public MarketEventBuilder ForSymbol(string symbol)
    {
        _symbol = symbol;
        return this;
    }

    /// <summary>Sets the event timestamp.</summary>
    public MarketEventBuilder WithTimestamp(DateTimeOffset timestamp)
    {
        _timestamp = timestamp;
        return this;
    }

    /// <summary>Sets the data-source label.</summary>
    public MarketEventBuilder WithSource(string source)
    {
        _source = source;
        return this;
    }

    /// <summary>Sets the sequence number on the envelope.</summary>
    public MarketEventBuilder WithSequence(long sequence)
    {
        _sequence = sequence;
        return this;
    }

    /// <summary>
    /// Wraps a <see cref="Trade"/> payload. If <paramref name="trade"/> is <c>null</c>
    /// a default trade is generated via <see cref="TradeBuilder"/>.
    /// </summary>
    public MarketEventBuilder WithTrade(Trade? trade = null)
    {
        _kind = BuilderPayloadKind.Trade;
        _payload = trade;
        return this;
    }

    /// <summary>
    /// Wraps a <see cref="HistoricalBar"/> payload. If <paramref name="bar"/> is <c>null</c>
    /// a default bar is generated via <see cref="HistoricalBarBuilder"/>.
    /// </summary>
    public MarketEventBuilder WithBar(HistoricalBar? bar = null)
    {
        _kind = BuilderPayloadKind.Bar;
        _payload = bar;
        return this;
    }

    /// <summary>Wraps an arbitrary pre-built <see cref="MarketEventPayload"/>.</summary>
    public MarketEventBuilder WithPayload(MarketEventPayload payload)
    {
        _payload = payload;
        return this;
    }

    /// <summary>Builds a single <see cref="MarketEventDto"/> using the configured values.</summary>
    public MarketEventDto Build()
    {
        var ts = _timestamp ?? DateTimeOffset.UtcNow;

        switch (_kind)
        {
            case BuilderPayloadKind.Bar:
                {
                    var bar = (_payload as HistoricalBar)
                        ?? new HistoricalBarBuilder().ForSymbol(_symbol).Build();
                    return MarketEventDto.CreateHistoricalBar(ts, _symbol, bar, _sequence, _source);
                }

            default: // Trade
                {
                    var trade = (_payload as Trade)
                        ?? new TradeBuilder().ForSymbol(_symbol).Build();
                    return MarketEventDto.CreateTrade(ts, _symbol, trade, _sequence, _source);
                }
        }
    }

    /// <summary>
    /// Builds a sequence of <paramref name="count"/> events with the same symbol and source.
    /// Sequence numbers are auto-incremented from 1.
    /// </summary>
    public IReadOnlyList<MarketEventDto> CreateMany(int count)
    {
        var events = new List<MarketEventDto>(count);
        var ts = _timestamp ?? DateTimeOffset.UtcNow;

        for (var i = 0; i < count; i++)
        {
            var seq = _sequence > 0 ? _sequence + i : i + 1;

            MarketEventDto evt;
            if (_kind == BuilderPayloadKind.Bar)
            {
                var bar = new HistoricalBarBuilder()
                    .ForSymbol(_symbol)
                    .ForDate(DateOnly.FromDateTime(ts.AddDays(i).DateTime))
                    .WithSequenceNumber(seq)
                    .Build();
                evt = MarketEventDto.CreateHistoricalBar(ts.AddDays(i), _symbol, bar, seq, _source);
            }
            else
            {
                var trade = new TradeBuilder()
                    .ForSymbol(_symbol)
                    .WithSequenceNumber(seq)
                    .WithTimestamp(ts.AddMilliseconds(i * 100))
                    .Build();
                evt = MarketEventDto.CreateTrade(ts.AddMilliseconds(i * 100), _symbol, trade, seq, _source);
            }

            events.Add(evt);
        }

        return events;
    }
}
