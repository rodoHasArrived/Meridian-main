using System.Reflection;
using System.Text.Json;
using FluentAssertions;
using Meridian.Application.Config;
using Meridian.Contracts.Domain.Enums;
using Meridian.Contracts.Domain.Models;
using Meridian.Domain.Collectors;
using Meridian.Domain.Events;
using Meridian.Infrastructure.Adapters.Alpaca;
using Meridian.Tests.TestHelpers;
using Xunit;
using DomainMarketEvent = Meridian.Domain.Events.MarketEvent;

namespace Meridian.Tests.Infrastructure.Providers;

/// <summary>
/// Tests for Alpaca message parsing correctness — timestamp handling,
/// trade size precision, and content-based trade deduplication.
/// Implements fixes for issues 1.3 and 1.4 from the March-2026
/// high-impact improvement brainstorm document.
/// </summary>
public sealed class AlpacaMessageParsingTests
{
    private readonly TestMarketEventPublisher _publisher;
    private readonly TradeDataCollector _tradeCollector;
    private readonly QuoteCollector _quoteCollector;
    private readonly AlpacaMarketDataClient _client;
    private readonly MethodInfo _handleMessage;

    public AlpacaMessageParsingTests()
    {
        _publisher = new TestMarketEventPublisher();
        _tradeCollector = new TradeDataCollector(_publisher);
        _quoteCollector = new QuoteCollector(_publisher);
        _client = new AlpacaMarketDataClient(
            _tradeCollector,
            _quoteCollector,
            new AlpacaOptions { KeyId = "AKTEST12345", SecretKey = "test-secret-key" });

        // Access the private HandleMessage method for focused message-parsing tests.
        _handleMessage = typeof(AlpacaMarketDataClient)
            .GetMethod("HandleMessage", BindingFlags.NonPublic | BindingFlags.Instance)!;
        _handleMessage.Should().NotBeNull("HandleMessage must exist for these tests to be meaningful");
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private void Dispatch(string json)
    {
        using var doc = JsonDocument.Parse(json);
        _handleMessage.Invoke(_client, new object[] { doc.RootElement });
    }

    private IReadOnlyList<DomainMarketEvent> Published => _publisher.PublishedEvents;

    // ── Trade size precision (issue 1.3) ─────────────────────────────────────

    [Fact]
    public void HandleMessage_TradeMessage_ParsesSizeAsInt64()
    {
        // Arrange — a block trade size that exceeds int.MaxValue (~2.15 billion).
        const long blockTradeSize = 3_000_000_000L;
        var json = BuildTradeJson("SPY", 450.12m, blockTradeSize, "2024-06-15T14:30:00Z");

        // Act
        Dispatch(json);

        // Assert — a Trade event with the full size must be published.
        var tradeEvents = Published.Where(e => e.Type == MarketEventType.Trade).ToList();
        tradeEvents.Should().HaveCount(1);
        var trade = tradeEvents[0].Payload.Should().BeOfType<Trade>().Subject;
        trade.Size.Should().Be(blockTradeSize,
            "GetInt64 must be used so large block trade sizes are not truncated");
    }

    [Fact]
    public void HandleMessage_TradeMessage_NormalSize_IsForwarded()
    {
        // Arrange
        var json = BuildTradeJson("AAPL", 185.50m, 500L, "2024-06-15T14:30:00Z");

        // Act
        Dispatch(json);

        // Assert — a Trade event must be published with the correct size.
        var tradeEvents = Published.Where(e => e.Type == MarketEventType.Trade).ToList();
        tradeEvents.Should().HaveCount(1);
        var trade = tradeEvents[0].Payload.Should().BeOfType<Trade>().Subject;
        trade.Size.Should().Be(500L);
    }

    // ── Timestamp rejection (issue 1.3) ──────────────────────────────────────

    [Fact]
    public void HandleMessage_TradeMessage_UnparseableTimestamp_IsDropped()
    {
        // Arrange — timestamp field is present but contains garbage.
        var json = BuildTradeJson("SPY", 450m, 100L, "not-a-timestamp");

        // Act
        Dispatch(json);

        // Assert — no events at all should be published.
        Published.Should().BeEmpty(
            "a trade with an unparseable timestamp must be dropped, not silently substituted with UtcNow");
    }

    [Fact]
    public void HandleMessage_TradeMessage_MissingTimestamp_IsDropped()
    {
        // Arrange — "t" field is absent entirely.
        var json = """{"T":"t","S":"SPY","p":450.12,"s":100,"i":42}""";

        // Act
        Dispatch(json);

        // Assert
        Published.Should().BeEmpty(
            "a trade with a missing timestamp must be dropped");
    }

    [Fact]
    public void HandleMessage_TradeMessage_ValidIso8601Timestamp_IsForwarded()
    {
        // Arrange
        var json = BuildTradeJson("MSFT", 380m, 200L, "2024-06-15T09:30:00.123456789Z");

        // Act
        Dispatch(json);

        // Assert — the Trade event must carry the correct parsed timestamp.
        var tradeEvents = Published.Where(e => e.Type == MarketEventType.Trade).ToList();
        tradeEvents.Should().HaveCount(1);
        var trade = tradeEvents[0].Payload.Should().BeOfType<Trade>().Subject;
        trade.Timestamp.Should().BeCloseTo(
            DateTimeOffset.Parse("2024-06-15T09:30:00.123456789Z"),
            precision: TimeSpan.FromMilliseconds(1));
    }

    [Fact]
    public void HandleMessage_QuoteMessage_UnparseableTimestamp_IsDropped()
    {
        // Arrange
        var json = """{"T":"q","S":"AAPL","bp":185.50,"bs":500,"ap":185.55,"as":300,"t":"bad-ts"}""";

        // Act
        Dispatch(json);

        // Assert
        Published.Should().BeEmpty(
            "a quote with an unparseable timestamp must be dropped, not silently substituted with UtcNow");
    }

    [Fact]
    public void HandleMessage_QuoteMessage_ValidTimestamp_IsForwarded()
    {
        // Arrange
        var json = """{"T":"q","S":"AAPL","bp":185.50,"bs":500,"ap":185.55,"as":300,"t":"2024-06-15T14:30:00Z"}""";

        // Act
        Dispatch(json);

        // Assert
        Published.Should().HaveCount(1);
        Published[0].Type.Should().Be(MarketEventType.BboQuote);
    }

    // ── Content-based deduplication (issue 1.4) ──────────────────────────────

    [Fact]
    public void HandleMessage_DuplicateTradeMessage_SecondIsSuppressed()
    {
        // Arrange — identical trade message dispatched twice (Alpaca re-delivery scenario).
        var json = BuildTradeJson("SPY", 450.00m, 100L, "2024-06-15T14:30:00.000Z");

        // Act
        Dispatch(json);
        Dispatch(json);

        // Assert — only one Trade event must be forwarded; the duplicate is silently dropped.
        var tradeEvents = Published.Where(e => e.Type == MarketEventType.Trade).ToList();
        tradeEvents.Should().HaveCount(1,
            "duplicate trades with the same (symbol, price, size, timestamp) must be suppressed");
    }

    [Fact]
    public void HandleMessage_TradesWithDifferentSymbols_BothForwarded()
    {
        // Arrange — same price/size/timestamp but different symbols → not duplicates.
        var json1 = BuildTradeJson("SPY", 450.00m, 100L, "2024-06-15T14:30:00Z");
        var json2 = BuildTradeJson("QQQ", 450.00m, 100L, "2024-06-15T14:30:00Z");

        // Act
        Dispatch(json1);
        Dispatch(json2);

        // Assert
        var tradeEvents = Published.Where(e => e.Type == MarketEventType.Trade).ToList();
        tradeEvents.Should().HaveCount(2,
            "trades for different symbols are distinct even if price/size/timestamp match");
    }

    [Fact]
    public void HandleMessage_TradesWithDifferentPrices_BothForwarded()
    {
        // Arrange — same symbol/size but different prices and unique trade IDs → not duplicates.
        var json1 = """{"T":"t","S":"AAPL","p":185.50,"s":100,"i":101,"t":"2024-06-15T14:30:00Z"}""";
        var json2 = """{"T":"t","S":"AAPL","p":185.60,"s":100,"i":102,"t":"2024-06-15T14:30:01Z"}""";

        // Act
        Dispatch(json1);
        Dispatch(json2);

        // Assert
        var tradeEvents = Published.Where(e => e.Type == MarketEventType.Trade).ToList();
        tradeEvents.Should().HaveCount(2);
    }

    [Fact]
    public void HandleMessage_TradesWithDifferentTimestamps_BothForwarded()
    {
        // Arrange — same symbol/price/size but different timestamps and unique IDs → not duplicates.
        var json1 = """{"T":"t","S":"AAPL","p":185.50,"s":100,"i":201,"t":"2024-06-15T14:30:00Z"}""";
        var json2 = """{"T":"t","S":"AAPL","p":185.50,"s":100,"i":202,"t":"2024-06-15T14:30:01Z"}""";

        // Act
        Dispatch(json1);
        Dispatch(json2);

        // Assert
        var tradeEvents = Published.Where(e => e.Type == MarketEventType.Trade).ToList();
        tradeEvents.Should().HaveCount(2);
    }

    [Fact]
    public void HandleMessage_DedupWindow_EvictsOldEntriesWhenFull()
    {
        // Arrange — fill the dedup window past capacity so the oldest entry is evicted.
        // With a window of 2048 we need to insert 2049 unique entries so that the first
        // entry (SYM0000) is pushed out by the eviction policy.
        const int windowSize = 2048;
        for (int i = 0; i < windowSize + 1; i++)
        {
            // Use distinct symbols so the TradeDataCollector doesn't raise sequence-continuity
            // integrity events that would inflate the published-event count.
            var sym = $"SYM{i:D4}";
            Dispatch($@"{{""T"":""t"",""S"":""{sym}"",""p"":{100m + i},""s"":100,""i"":{i + 1},""t"":""2024-06-15T14:30:00Z""}}");
        }

        var tradeCountBefore = Published.Count(e => e.Type == MarketEventType.Trade);
        tradeCountBefore.Should().Be(windowSize + 1);

        // SYM0000 was the oldest entry; after windowSize+1 inserts it has been evicted.
        // Re-sending it should be accepted again.
        Dispatch("""{"T":"t","S":"SYM0000","p":100,"s":100,"i":9999,"t":"2024-06-15T14:30:00Z"}""");

        var tradeCountAfter = Published.Count(e => e.Type == MarketEventType.Trade);
        tradeCountAfter.Should().Be(windowSize + 2,
            "once the dedup window evicts the oldest entry that entry can be accepted again");
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private static string BuildTradeJson(string symbol, decimal price, long size, string? timestamp)
    {
        var ts = timestamp is null ? "" : $@",""t"":""{timestamp}""";
        return string.Create(System.Globalization.CultureInfo.InvariantCulture,
            $@"{{""T"":""t"",""S"":""{symbol}"",""p"":{price},""s"":{size},""i"":42{ts}}}");
    }
}
