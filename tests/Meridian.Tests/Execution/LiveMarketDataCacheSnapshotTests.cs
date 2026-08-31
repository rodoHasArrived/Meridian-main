using FluentAssertions;
using Meridian.Contracts.Domain.Enums;
using Meridian.Contracts.Domain.Models;
using Meridian.Execution.Adapters;
using Meridian.Execution.Interfaces;
using Xunit;

namespace Meridian.Tests.Execution;

/// <summary>
/// Regression coverage for the torn-read defect behind #2676: reading the last quote and the
/// last trade as two independent cache lookups let a reader observe the quote from before a
/// market update paired with the trade from after it, so a paper fill could price against an
/// envelope that was never in effect. <see cref="LiveMarketDataCache.GetSnapshot"/> must hand
/// out one coherent per-symbol state instead.
/// </summary>
public sealed class LiveMarketDataCacheSnapshotTests
{
    private static BboQuotePayload Quote(long sequence, decimal bid = 500m, decimal ask = 500.5m) =>
        new(DateTimeOffset.UtcNow, "AAPL", bid, 10, ask, 10,
            MidPrice: null, Spread: null, SequenceNumber: sequence);

    private static Trade Print(long sequence, decimal price = 500.25m) =>
        new(DateTimeOffset.UtcNow, "AAPL", price, Size: 10,
            Aggressor: AggressorSide.Buy, SequenceNumber: sequence);

    [Fact]
    public void GetSnapshot_ReturnsLatestRecordedFields()
    {
        var cache = new LiveMarketDataCache();
        var quote = Quote(sequence: 1);
        var trade = Print(sequence: 1);
        cache.RecordQuote("AAPL", quote);
        cache.RecordTrade("AAPL", trade);

        var snapshot = cache.GetSnapshot("AAPL");

        snapshot.Quote.Should().BeSameAs(quote);
        snapshot.Trade.Should().BeSameAs(trade);
        snapshot.Bar.Should().BeNull();
        cache.GetLastQuote("AAPL").Should().BeSameAs(quote);
        cache.GetLastTrade("AAPL").Should().BeSameAs(trade);
    }

    [Fact]
    public void GetSnapshot_UnknownSymbol_ReturnsEmptySnapshot()
    {
        var snapshot = new LiveMarketDataCache().GetSnapshot("UNSEEN");

        snapshot.Quote.Should().BeNull();
        snapshot.Trade.Should().BeNull();
        snapshot.Bar.Should().BeNull();
    }

    [Fact]
    public async Task GetSnapshot_NeverPairsPreUpdateQuoteWithPostUpdateTrade()
    {
        // The exact interleaving from #2676: a writer publishes the quote for version i and
        // then the trade for version i, while a reader captures concurrently. Because every
        // write publishes quote-before-trade, a coherent observation can contain a trade
        // OLDER than its quote (the trade for version i has not landed yet) but never a
        // trade NEWER than its quote — that torn pair is precisely what priced the stop
        // fill at the stale ask. The independent-dictionary implementation fails this
        // within a few thousand iterations; the atomic state swap can never fail it.
        var cache = new LiveMarketDataCache();
        const int Versions = 150_000;

        var writer = Task.Run(() =>
        {
            for (var version = 1L; version <= Versions; version++)
            {
                cache.RecordQuote("AAPL", Quote(version));
                cache.RecordTrade("AAPL", Print(version));
            }
        });

        var tornObservations = 0;
        while (!writer.IsCompleted)
        {
            var snapshot = cache.GetSnapshot("AAPL");
            if (snapshot is { Quote: { } quote, Trade: { } trade }
                && trade.SequenceNumber > quote.SequenceNumber)
            {
                tornObservations++;
            }
        }

        await writer;
        tornObservations.Should().Be(0,
            "a snapshot must never pair a pre-update quote with a post-update trade (#2676)");
    }

    [Fact]
    public void DefaultInterfaceSnapshot_ComposesTheIndividualGetters()
    {
        // Adapters that do not override GetSnapshot still get a working (though not
        // atomically coherent) composition of their individual getters.
        var quote = Quote(sequence: 7);
        var trade = Print(sequence: 7);
        ILiveFeedAdapter adapter = new SingleValueAdapter(quote, trade);

        var snapshot = adapter.GetSnapshot("AAPL");

        snapshot.Quote.Should().BeSameAs(quote);
        snapshot.Trade.Should().BeSameAs(trade);
        snapshot.Bar.Should().BeNull("the interface default for GetLastBar returns null");
    }

    private sealed class SingleValueAdapter(BboQuotePayload quote, Trade trade) : ILiveFeedAdapter
    {
        public IReadOnlySet<string> SubscribedSymbols { get; } = new HashSet<string>();

        public Trade? GetLastTrade(string symbol) => trade;

        public BboQuotePayload? GetLastQuote(string symbol) => quote;

        public LOBSnapshot? GetLastOrderBook(string symbol) => null;
    }
}
