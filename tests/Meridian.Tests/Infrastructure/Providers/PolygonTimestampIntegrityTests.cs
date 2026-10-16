using FluentAssertions;
using Meridian.Core.Config;
using Meridian.Contracts.Domain.Enums;
using Meridian.Domain.Collectors;
using Meridian.Domain.Events;
using Meridian.Infrastructure.Adapters.Polygon;
using Meridian.Tests.TestHelpers;
using Xunit;
using AggregateBarPayload = Meridian.Contracts.Domain.Models.AggregateBarPayload;

namespace Meridian.Tests.Infrastructure.Providers;

public sealed class PolygonTimestampIntegrityTests
{
    private const long SourceTimestamp = 1704067200000;
    private readonly TestMarketEventPublisher _publisher = new();
    private readonly TradeDataCollector _trades;
    private readonly QuoteCollector _quotes;

    public PolygonTimestampIntegrityTests()
    {
        _trades = new TradeDataCollector(_publisher);
        _quotes = new QuoteCollector(_publisher);
    }

    private static IEnumerable<string?> InvalidSourceTimestamps =>
        new string?[] { null, "null", "0", "-1", "\"1704067200000\"", "true", "1.5", "253402300800000", "9223372036854775808" };

    public static IEnumerable<object?[]> InvalidTickTimestamps()
    {
        foreach (var eventType in new[] { "T", "Q" })
            foreach (var timestamp in InvalidSourceTimestamps)
                yield return new object?[] { eventType, timestamp };
    }

    public static IEnumerable<object?[]> InvalidAggregateTimestamps()
    {
        foreach (var eventType in new[] { "A", "AM" })
            foreach (var field in new[] { "s", "e" })
                foreach (var timestamp in InvalidSourceTimestamps)
                    yield return new object?[] { eventType, field, timestamp };
    }

    [Theory]
    [MemberData(nameof(InvalidTickTimestamps))]
    public void Tick_InvalidSourceTimestamp_IsRejectedBeforeCollectorStateChanges(string eventType, string? timestamp)
    {
        var client = CreateSubscribedClient();

        client.ProcessTestMessage($"[{TickFrame(eventType, timestamp)}]");

        _publisher.PublishedEvents.Should().BeEmpty();
        _trades.GetRecentTrades("AAPL").Should().BeEmpty();
        _quotes.TryGet("AAPL", out _).Should().BeFalse();
    }

    [Theory]
    [InlineData("T")]
    [InlineData("Q")]
    public void Tick_FutureSourceTimestamp_IsRejectedBySharedValidation(string eventType)
    {
        var client = CreateSubscribedClient();
        var timestamp = DateTimeOffset.UtcNow.AddMinutes(10).ToUnixTimeMilliseconds().ToString(System.Globalization.CultureInfo.InvariantCulture);

        client.ProcessTestMessage($"[{TickFrame(eventType, timestamp)}]");

        _publisher.PublishedEvents.Should().BeEmpty();
        _trades.GetRecentTrades("AAPL").Should().BeEmpty();
        _quotes.TryGet("AAPL", out _).Should().BeFalse();
    }

    [Theory]
    [MemberData(nameof(InvalidAggregateTimestamps))]
    public void Aggregate_InvalidSourceWindowBoundary_IsRejected(string eventType, string field, string? timestamp)
    {
        var client = CreateSubscribedClient();
        var start = field == "s" ? timestamp : SourceTimestamp.ToString(System.Globalization.CultureInfo.InvariantCulture);
        var end = field == "e" ? timestamp : (SourceTimestamp + 999).ToString(System.Globalization.CultureInfo.InvariantCulture);

        client.ProcessTestMessage($"[{AggregateFrame(eventType, start, end)}]");

        _publisher.PublishedEvents.Should().BeEmpty();
    }

    [Theory]
    [InlineData("A")]
    [InlineData("AM")]
    public void Aggregate_FutureWindow_IsRejectedBySharedValidation(string eventType)
    {
        var client = CreateSubscribedClient();
        var start = DateTimeOffset.UtcNow.AddMinutes(10).ToUnixTimeMilliseconds();

        client.ProcessTestMessage($"[{AggregateFrame(eventType, start.ToString(System.Globalization.CultureInfo.InvariantCulture), (start + 999).ToString(System.Globalization.CultureInfo.InvariantCulture))}]");

        _publisher.PublishedEvents.Should().BeEmpty();
    }

    [Fact]
    public void Aggregate_EndBeforeStart_IsRejected()
    {
        var client = CreateSubscribedClient();

        client.ProcessTestMessage($"[{AggregateFrame("A", "1704067200000", "1704067199999")}]");

        _publisher.PublishedEvents.Should().BeEmpty();
    }

    [Theory]
    [InlineData("bs")]
    [InlineData("as")]
    [InlineData("bp")]
    [InlineData("ap")]
    public void Quote_NegativePriceOrSize_IsRejectedBySharedValidation(string field)
    {
        var client = CreateSubscribedClient();
        var frame = TickFrame("Q", "1704067200000")
            .Replace($"\"{field}\":{(field is "bp" ? "150.20" : field is "ap" ? "150.25" : "100")}", $"\"{field}\":-1", StringComparison.Ordinal);

        client.ProcessTestMessage($"[{frame}]");

        _publisher.PublishedEvents.Should().BeEmpty();
        _quotes.TryGet("AAPL", out _).Should().BeFalse();
    }

    [Fact]
    public void RejectedTimestamp_DoesNotPreventValidNeighborFromBeingPublished()
    {
        var client = CreateSubscribedClient();

        client.ProcessTestMessage($"[{TickFrame("T", null)},{TickFrame("T", "1704067200000")}]");

        _publisher.PublishedEvents.Count(e => e.Type == MarketEventType.Trade).Should().Be(1);
        _publisher.PublishedEvents.Should().NotContain(e => e.Type == MarketEventType.Integrity);
    }

    [Theory]
    [InlineData("T", MarketEventType.Trade)]
    [InlineData("Q", MarketEventType.BboQuote)]
    [InlineData("A", MarketEventType.AggregateBar)]
    [InlineData("AM", MarketEventType.AggregateBar)]
    public void ValidSourceTimestamp_IsPreservedSeparatelyFromReceiptTime(string eventType, MarketEventType expectedType)
    {
        var client = CreateSubscribedClient();
        var frame = eventType is "T" or "Q"
            ? TickFrame(eventType, "1704067200000")
            : AggregateFrame(eventType, "1704067200000", "1704067259999");
        var expectedTimestamp = DateTimeOffset.FromUnixTimeMilliseconds(
            expectedType == MarketEventType.AggregateBar ? SourceTimestamp + 59999 : SourceTimestamp);
        var beforeReceipt = DateTimeOffset.UtcNow;

        client.ProcessTestMessage($"[{frame}]");

        var afterReceipt = DateTimeOffset.UtcNow;
        var published = _publisher.PublishedEvents.Single(e => e.Type == expectedType);
        published.Timestamp.Should().Be(expectedTimestamp);
        published.ExchangeTimestamp.Should().Be(expectedTimestamp);
        published.ReceivedAtUtc.Should().BeOnOrAfter(beforeReceipt).And.BeOnOrBefore(afterReceipt);
        published.ReceivedAtUtc.Should().BeAfter(published.Timestamp);
        published.ReceivedAtMonotonic.Should().BeGreaterThan(0);

        if (expectedType == MarketEventType.AggregateBar)
        {
            var bar = published.Payload.Should().BeOfType<AggregateBarPayload>().Subject;
            bar.StartTime.Should().Be(DateTimeOffset.FromUnixTimeMilliseconds(SourceTimestamp));
            bar.EndTime.Should().Be(expectedTimestamp);
        }
    }

    private PolygonMarketDataClient CreateSubscribedClient()
    {
        var client = new PolygonMarketDataClient(_publisher, _trades, _quotes,
            new PolygonOptions(SubscribeTrades: true, SubscribeQuotes: true, SubscribeAggregates: true));
        client.SubscribeTrades(new SymbolConfig("AAPL")).Should().BeGreaterThan(0);
        client.SubscribeMarketDepth(new SymbolConfig("AAPL")).Should().BeGreaterThan(0);
        client.SubscribeAggregates(new SymbolConfig("AAPL")).Should().BeGreaterThan(0);
        return client;
    }

    private static string TickFrame(string eventType, string? timestamp)
    {
        var fields = eventType == "T"
            ? "\"p\":150.25,\"s\":100,\"q\":1"
            : "\"bp\":150.20,\"bs\":100,\"ap\":150.25,\"as\":100";
        return $"{{\"ev\":\"{eventType}\",\"sym\":\"AAPL\",{fields}{TimestampField("t", timestamp)}}}";
    }

    private static string AggregateFrame(string eventType, string? start, string? end)
        => $"{{\"ev\":\"{eventType}\",\"sym\":\"AAPL\",\"o\":150.10,\"h\":150.50,\"l\":149.90,\"c\":150.25,\"v\":1200{TimestampField("s", start)}{TimestampField("e", end)}}}";

    private static string TimestampField(string field, string? timestamp)
        => timestamp is null ? string.Empty : $",\"{field}\":{timestamp}";
}
