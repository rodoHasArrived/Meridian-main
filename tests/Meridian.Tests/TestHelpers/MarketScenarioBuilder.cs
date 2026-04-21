using Meridian.Contracts.Domain.Enums;
using Meridian.Contracts.Domain.Models;
using Meridian.Domain.Events;

namespace Meridian.Tests.TestHelpers;

/// <summary>
/// Builds deterministic market-event sequences for named production-like scenarios
/// so integration and multi-layer tests can exercise realistic event flow.
/// </summary>
internal static class MarketScenarioBuilder
{
    /// <summary>
    /// Mixed burst of trades and BBO quotes for one or more symbols at the session open.
    /// </summary>
    public static List<MarketEvent> BuildSessionOpen(
        IReadOnlyList<string> symbols,
        DateTimeOffset openTime,
        int tradesPerSymbol,
        int quotesPerSymbol,
        IReadOnlyDictionary<string, decimal>? basePrice = null)
    {
        ArgumentNullException.ThrowIfNull(symbols);

        if (tradesPerSymbol < 0)
            throw new ArgumentOutOfRangeException(nameof(tradesPerSymbol), tradesPerSymbol, "Trade count must be non-negative.");

        if (quotesPerSymbol < 0)
            throw new ArgumentOutOfRangeException(nameof(quotesPerSymbol), quotesPerSymbol, "Quote count must be non-negative.");

        var events = new List<MarketEvent>(symbols.Count * (tradesPerSymbol + quotesPerSymbol));
        var nextSequence = 1L;

        foreach (var symbol in symbols)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(symbol);

            var symbolBasePrice = basePrice is not null && basePrice.TryGetValue(symbol, out var configuredBasePrice)
                ? configuredBasePrice
                : 100m;

            events.AddRange(BuildSequentialTrades(symbol, openTime, tradesPerSymbol, nextSequence, symbolBasePrice));
            nextSequence += tradesPerSymbol;

            events.AddRange(BuildSequentialQuotes(symbol, openTime, quotesPerSymbol, nextSequence, symbolBasePrice));
            nextSequence += quotesPerSymbol;
        }

        return events;
    }

    /// <summary>
    /// Deterministic sequential trades with monotonic sequence numbers and a small upward drift.
    /// </summary>
    public static List<MarketEvent> BuildSequentialTrades(
        string symbol,
        DateTimeOffset startTime,
        int count,
        long startSequence,
        decimal startPrice,
        decimal priceStep = 0.01m)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(symbol);

        if (count < 0)
            throw new ArgumentOutOfRangeException(nameof(count), count, "Trade count must be non-negative.");

        if (startSequence < 0)
            throw new ArgumentOutOfRangeException(nameof(startSequence), startSequence, "Sequence number must be non-negative.");

        if (startPrice <= 0)
            throw new ArgumentOutOfRangeException(nameof(startPrice), startPrice, "Start price must be greater than zero.");

        var events = new List<MarketEvent>(count);

        for (var i = 0; i < count; i++)
        {
            var timestamp = startTime.AddMilliseconds(i * 20);
            var sequence = startSequence + i;
            var trade = new Trade(
                Timestamp: timestamp,
                Symbol: symbol,
                Price: startPrice + (priceStep * i),
                Size: 100,
                Aggressor: i % 2 == 0 ? AggressorSide.Buy : AggressorSide.Sell,
                SequenceNumber: sequence,
                Venue: "XNAS");

            events.Add(MarketEvent.Trade(timestamp, symbol, trade, seq: sequence, source: "XNAS"));
        }

        return events;
    }

    /// <summary>
    /// Deterministic BBO quote sequence centered around the supplied mid-price.
    /// </summary>
    public static List<MarketEvent> BuildSequentialQuotes(
        string symbol,
        DateTimeOffset startTime,
        int count,
        long startSequence,
        decimal midPrice,
        decimal halfSpread = 0.01m)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(symbol);

        if (count < 0)
            throw new ArgumentOutOfRangeException(nameof(count), count, "Quote count must be non-negative.");

        if (startSequence < 0)
            throw new ArgumentOutOfRangeException(nameof(startSequence), startSequence, "Sequence number must be non-negative.");

        if (midPrice <= 0)
            throw new ArgumentOutOfRangeException(nameof(midPrice), midPrice, "Mid-price must be greater than zero.");

        if (halfSpread < 0)
            throw new ArgumentOutOfRangeException(nameof(halfSpread), halfSpread, "Half-spread must be non-negative.");

        var events = new List<MarketEvent>(count);

        for (var i = 0; i < count; i++)
        {
            var timestamp = startTime.AddMilliseconds(i * 50);
            var sequence = startSequence + i;
            var bidPrice = midPrice - halfSpread;
            var askPrice = midPrice + halfSpread;
            var quote = new BboQuotePayload(
                Timestamp: timestamp,
                Symbol: symbol,
                BidPrice: bidPrice,
                BidSize: 200,
                AskPrice: askPrice,
                AskSize: 200,
                MidPrice: midPrice,
                Spread: askPrice - bidPrice,
                SequenceNumber: sequence,
                Venue: "XNAS");

            events.Add(MarketEvent.BboQuote(timestamp, symbol, quote, seq: sequence, source: "XNAS"));
        }

        return events;
    }

    /// <summary>
    /// Flash-crash trade burst where price falls by the specified percentage within the duration window.
    /// </summary>
    public static List<MarketEvent> BuildFlashCrash(
        string symbol,
        DateTimeOffset startTime,
        decimal preCrashPrice,
        decimal dropPct = 0.10m,
        int count = 50,
        int durationMs = 800)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(symbol);

        if (preCrashPrice <= 0)
            throw new ArgumentOutOfRangeException(nameof(preCrashPrice), preCrashPrice, "Pre-crash price must be greater than zero.");

        if (dropPct <= 0 || dropPct >= 1)
            throw new ArgumentOutOfRangeException(nameof(dropPct), dropPct, "Drop percentage must be greater than zero and less than one.");

        if (count < 0)
            throw new ArgumentOutOfRangeException(nameof(count), count, "Trade count must be non-negative.");

        if (durationMs < 0)
            throw new ArgumentOutOfRangeException(nameof(durationMs), durationMs, "Duration must be non-negative.");

        var events = new List<MarketEvent>(count);
        var dropPerTick = preCrashPrice * dropPct / Math.Max(count, 1);

        for (var i = 0; i < count; i++)
        {
            var timestamp = startTime.AddMilliseconds(i * durationMs / Math.Max(count, 1));
            var sequence = i + 1L;
            var trade = new Trade(
                Timestamp: timestamp,
                Symbol: symbol,
                Price: preCrashPrice - (dropPerTick * i),
                Size: 5_000,
                Aggressor: AggressorSide.Sell,
                SequenceNumber: sequence,
                Venue: "XNAS");

            events.Add(MarketEvent.Trade(timestamp, symbol, trade, seq: sequence, source: "XNAS"));
        }

        return events;
    }

    /// <summary>
    /// Feed interruption with trades before the disconnect and trades after a sequence gap.
    /// </summary>
    public static (List<MarketEvent> BeforeGap, List<MarketEvent> AfterGap) BuildFeedInterruption(
        string symbol,
        DateTimeOffset startTime,
        int tradesBeforeGap = 5,
        int gapSize = 10,
        int tradesAfterGap = 5)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(symbol);

        if (tradesBeforeGap < 0)
            throw new ArgumentOutOfRangeException(nameof(tradesBeforeGap), tradesBeforeGap, "Trade count must be non-negative.");

        if (gapSize < 0)
            throw new ArgumentOutOfRangeException(nameof(gapSize), gapSize, "Gap size must be non-negative.");

        if (tradesAfterGap < 0)
            throw new ArgumentOutOfRangeException(nameof(tradesAfterGap), tradesAfterGap, "Trade count must be non-negative.");

        var beforeGap = BuildSequentialTrades(
            symbol,
            startTime,
            tradesBeforeGap,
            startSequence: 1,
            startPrice: 100m);

        var resumeSequence = 1L + tradesBeforeGap + gapSize;
        var afterGap = BuildSequentialTrades(
            symbol,
            startTime.AddSeconds(1),
            tradesAfterGap,
            startSequence: resumeSequence,
            startPrice: 100m);

        return (beforeGap, afterGap);
    }
}
