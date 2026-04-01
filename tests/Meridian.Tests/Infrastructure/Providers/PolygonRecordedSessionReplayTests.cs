using System.Reflection;
using System.Text.Json;
using FluentAssertions;
using Meridian.Application.Config;
using Meridian.Contracts.Domain.Enums;
using Meridian.Contracts.Domain.Models;
using Meridian.Domain.Collectors;
using Meridian.Domain.Events;
using Meridian.Domain.Models;
using Meridian.Infrastructure.Adapters.Polygon;
using Meridian.Tests.TestHelpers;
using Xunit;
using AggregateBarPayload = Meridian.Contracts.Domain.Models.AggregateBarPayload;
using AggregateTimeframe = Meridian.Contracts.Domain.Models.AggregateTimeframe;

namespace Meridian.Tests.Infrastructure.Providers;

/// <summary>
/// Replays committed Polygon websocket session fixtures through the real adapter parsing path
/// to catch feed-shape regressions without requiring live network access or credentials.
/// </summary>
public sealed class PolygonRecordedSessionReplayTests
{
    private static readonly string FixturesDir = ResolveFixturesDir();

    public static IEnumerable<object[]> FixtureFiles()
    {
        if (!Directory.Exists(FixturesDir))
            yield break;

        foreach (var file in Directory.GetFiles(FixturesDir, "*.json").OrderBy(static path => path, StringComparer.Ordinal))
            yield return new object[] { Path.GetFileName(file) };
    }

    [Fact]
    public void FixturesDirectory_ContainsAtLeastOneFile()
    {
        Directory.Exists(FixturesDir).Should().BeTrue(
            because: "the Polygon recorded-session fixture directory must exist");
        Directory.GetFiles(FixturesDir, "*.json").Should().NotBeEmpty(
            because: "at least one Polygon recorded-session fixture must be committed");
    }

    [Theory]
    [MemberData(nameof(FixtureFiles))]
    public void FixtureMetadata_ContainsDescriptionAndExpectedSections(string fixtureFileName)
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(Path.Combine(FixturesDir, fixtureFileName)));
        var root = doc.RootElement;

        root.TryGetProperty("description", out var description).Should().BeTrue(
            because: $"[{fixtureFileName}] should explain which Polygon feed shapes it is validating");
        description.GetString().Should().NotBeNullOrWhiteSpace(
            because: $"[{fixtureFileName}] should document the replay scenario for future operators and maintainers");

        root.TryGetProperty("expected", out var expected).Should().BeTrue(
            because: $"[{fixtureFileName}] should encode its expected replay output explicitly");
        expected.TryGetProperty("trade", out _).Should().BeTrue();
        expected.TryGetProperty("quote", out _).Should().BeTrue();
        expected.TryGetProperty("aggregates", out _).Should().BeTrue();
        expected.TryGetProperty("statusMarkers", out _).Should().BeTrue(
            because: $"[{fixtureFileName}] should encode expected status-frame variants for auth/reconnect/rate-limit coverage");
    }

    [Theory]
    [MemberData(nameof(FixtureFiles))]
    public void RecordedSessionReplay_EmitsExpectedTradeQuoteAndAggregateEvents(string fixtureFileName)
    {
        var fixture = LoadFixture(fixtureFileName);
        var publisher = new TestMarketEventPublisher();
        var tradeCollector = new TradeDataCollector(publisher, null);
        var quoteCollector = new QuoteCollector(publisher);
        var options = new PolygonOptions(
            ApiKey: "polygon_recorded_session_key_1234567890",
            SubscribeTrades: true,
            SubscribeQuotes: true,
            SubscribeAggregates: true);
        var client = new PolygonMarketDataClient(publisher, tradeCollector, quoteCollector, options);

        foreach (var symbol in fixture.TradeSubscriptions)
            client.SubscribeTrades(new SymbolConfig(symbol));
        foreach (var symbol in fixture.QuoteSubscriptions)
            client.SubscribeMarketDepth(new SymbolConfig(symbol));
        foreach (var symbol in fixture.AggregateSubscriptions)
            client.SubscribeAggregates(new SymbolConfig(symbol));

        foreach (var rawMessage in fixture.Messages)
            client.ProcessTestMessage(rawMessage);

        var tradeEvents = publisher.PublishedEvents.Where(static evt => evt.Type == MarketEventType.Trade).ToArray();
        var quoteEvents = publisher.PublishedEvents.Where(static evt => evt.Type == MarketEventType.BboQuote).ToArray();
        var aggregateEvents = publisher.PublishedEvents.Where(static evt => evt.Type == MarketEventType.AggregateBar).ToArray();
        var orderFlowEvents = publisher.PublishedEvents.Where(static evt => evt.Type == MarketEventType.OrderFlow).ToArray();
        var integrityEvents = publisher.PublishedEvents.Where(static evt => evt.Type == MarketEventType.Integrity).ToArray();
        var unexpectedEvents = publisher.PublishedEvents
            .Where(static evt => evt.Type is not MarketEventType.Trade
                and not MarketEventType.BboQuote
                and not MarketEventType.AggregateBar
                and not MarketEventType.OrderFlow
                and not MarketEventType.Integrity)
            .ToArray();

        tradeEvents.Should().ContainSingle(because: $"[{fixtureFileName}] only the subscribed trade should be emitted");
        quoteEvents.Should().ContainSingle(because: $"[{fixtureFileName}] only the subscribed quote should be emitted");
        aggregateEvents.Should().HaveCount(fixture.Expected.Aggregates.Length, because: $"[{fixtureFileName}] expected aggregate frames should be emitted");
        orderFlowEvents.Should().HaveCount(tradeEvents.Length, because: $"[{fixtureFileName}] each accepted trade should also emit order-flow statistics");
        integrityEvents.Should().HaveCount(fixture.Expected.ExpectedIntegrityEvents, because: $"[{fixtureFileName}] malformed-but-skipped frames should not create unexpected integrity events");
        unexpectedEvents.Should().BeEmpty(because: $"[{fixtureFileName}] replay fixtures should only produce the documented trade, quote, aggregate, order-flow, or integrity event types");
        foreach (var marker in fixture.Expected.StatusMarkers)
        {
            fixture.Messages.Should().Contain(msg => msg.Contains($"\"status\":\"{marker}\"", StringComparison.OrdinalIgnoreCase),
                because: $"[{fixtureFileName}] should include status marker '{marker}' to validate Polygon status variant parsing");
        }

        var trade = tradeEvents[0].Payload.Should().BeOfType<Trade>().Subject;
        trade.Symbol.Should().Be(fixture.Expected.Trade.Symbol);
        trade.Price.Should().Be(fixture.Expected.Trade.Price);
        trade.Size.Should().Be(fixture.Expected.Trade.Size);
        trade.Timestamp.Should().Be(DateTimeOffset.FromUnixTimeMilliseconds(fixture.Expected.Trade.TimestampMs));
        trade.StreamId.Should().Be(fixture.Expected.Trade.StreamId);
        trade.Venue.Should().Be(fixture.Expected.Trade.Venue);
        trade.Aggressor.Should().Be(ParseAggressor(fixture.Expected.Trade.Aggressor));
        trade.RawConditions.Should().Equal(fixture.Expected.Trade.RawConditions);

        var quote = quoteEvents[0].Payload.Should().BeOfType<BboQuotePayload>().Subject;
        quote.Symbol.Should().Be(fixture.Expected.Quote.Symbol);
        quote.BidPrice.Should().Be(fixture.Expected.Quote.BidPrice);
        quote.AskPrice.Should().Be(fixture.Expected.Quote.AskPrice);
        quote.BidSize.Should().Be(fixture.Expected.Quote.BidSize);
        quote.AskSize.Should().Be(fixture.Expected.Quote.AskSize);
        quote.Timestamp.Should().Be(DateTimeOffset.FromUnixTimeMilliseconds(fixture.Expected.Quote.TimestampMs));
        quote.Venue.Should().Be(fixture.Expected.Quote.Venue);

        foreach (var expectedAggregate in fixture.Expected.Aggregates)
        {
            var aggregate = aggregateEvents
                .Select(static evt => evt.Payload)
                .OfType<AggregateBarPayload>()
                .Single(bar => bar.Timeframe == ParseTimeframe(expectedAggregate.Timeframe));

            aggregate.Symbol.Should().Be(expectedAggregate.Symbol);
            aggregate.Open.Should().Be(expectedAggregate.Open);
            aggregate.High.Should().Be(expectedAggregate.High);
            aggregate.Low.Should().Be(expectedAggregate.Low);
            aggregate.Close.Should().Be(expectedAggregate.Close);
            aggregate.Volume.Should().Be(expectedAggregate.Volume);
            aggregate.Vwap.Should().Be(expectedAggregate.Vwap);
            aggregate.TradeCount.Should().Be(expectedAggregate.TradeCount);
            aggregate.StartTime.Should().Be(DateTimeOffset.FromUnixTimeMilliseconds(expectedAggregate.StartTimeMs));
            aggregate.EndTime.Should().Be(DateTimeOffset.FromUnixTimeMilliseconds(expectedAggregate.EndTimeMs));
        }
    }

    private static PolygonSessionFixture LoadFixture(string fixtureFileName)
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(Path.Combine(FixturesDir, fixtureFileName)));
        var root = doc.RootElement;
        var subscriptions = root.GetProperty("subscriptions");

        var expected = root.GetProperty("expected");

        return new PolygonSessionFixture(
            TradeSubscriptions: ReadStringArray(subscriptions, "trades"),
            QuoteSubscriptions: ReadStringArray(subscriptions, "quotes"),
            AggregateSubscriptions: ReadStringArray(subscriptions, "aggregates"),
            Messages: root.GetProperty("messages").EnumerateArray().Select(static item => item.GetString()!).ToArray(),
            Expected: new ExpectedFixture(
                Trade: ReadExpectedTrade(expected.GetProperty("trade")),
                Quote: ReadExpectedQuote(expected.GetProperty("quote")),
                Aggregates: expected.GetProperty("aggregates").EnumerateArray().Select(ReadExpectedAggregate).ToArray(),
                StatusMarkers: ReadStringArray(expected, "statusMarkers"),
                ExpectedIntegrityEvents: expected.TryGetProperty("expectedIntegrityEvents", out var integrityCount) ? integrityCount.GetInt32() : 0));
    }

    private static string[] ReadStringArray(JsonElement parent, string propertyName)
    {
        if (!parent.TryGetProperty(propertyName, out var property) || property.ValueKind != JsonValueKind.Array)
            return [];

        return property.EnumerateArray()
            .Select(static item => item.GetString())
            .Where(static item => !string.IsNullOrWhiteSpace(item))
            .Select(static item => item!)
            .ToArray();
    }

    private static string ResolveFixturesDir()
    {
        var assemblyDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location)!;
        var candidate = new DirectoryInfo(assemblyDir);

        while (candidate is not null && !File.Exists(Path.Combine(candidate.FullName, "Meridian.sln")))
            candidate = candidate.Parent;

        return Path.Combine(
            candidate?.FullName ?? assemblyDir,
            "tests", "Meridian.Tests",
            "Infrastructure", "Providers", "Fixtures", "Polygon");
    }

    private sealed record PolygonSessionFixture(
        string[] TradeSubscriptions,
        string[] QuoteSubscriptions,
        string[] AggregateSubscriptions,
        string[] Messages,
        ExpectedFixture Expected);

    private sealed record ExpectedFixture(
        ExpectedTrade Trade,
        ExpectedQuote Quote,
        ExpectedAggregate[] Aggregates,
        string[] StatusMarkers,
        int ExpectedIntegrityEvents);

    private sealed record ExpectedTrade(
        string Symbol,
        decimal Price,
        long Size,
        long TimestampMs,
        string StreamId,
        string Venue,
        string Aggressor,
        string[] RawConditions);

    private sealed record ExpectedQuote(
        string Symbol,
        decimal BidPrice,
        long BidSize,
        decimal AskPrice,
        long AskSize,
        long TimestampMs,
        string Venue);

    private sealed record ExpectedAggregate(
        string Timeframe,
        string Symbol,
        decimal Open,
        decimal High,
        decimal Low,
        decimal Close,
        long Volume,
        decimal Vwap,
        int TradeCount,
        long StartTimeMs,
        long EndTimeMs);

    private static ExpectedTrade ReadExpectedTrade(JsonElement element) => new(
        Symbol: element.GetProperty("symbol").GetString()!,
        Price: element.GetProperty("price").GetDecimal(),
        Size: element.GetProperty("size").GetInt64(),
        TimestampMs: element.GetProperty("timestampMs").GetInt64(),
        StreamId: element.GetProperty("streamId").GetString()!,
        Venue: element.GetProperty("venue").GetString()!,
        Aggressor: element.TryGetProperty("aggressor", out var aggressor) ? aggressor.GetString()! : nameof(AggressorSide.Unknown),
        RawConditions: ReadStringArray(element, "rawConditions"));

    private static ExpectedQuote ReadExpectedQuote(JsonElement element) => new(
        Symbol: element.GetProperty("symbol").GetString()!,
        BidPrice: element.GetProperty("bidPrice").GetDecimal(),
        BidSize: element.GetProperty("bidSize").GetInt64(),
        AskPrice: element.GetProperty("askPrice").GetDecimal(),
        AskSize: element.GetProperty("askSize").GetInt64(),
        TimestampMs: element.GetProperty("timestampMs").GetInt64(),
        Venue: element.GetProperty("venue").GetString()!);

    private static ExpectedAggregate ReadExpectedAggregate(JsonElement element) => new(
        Timeframe: element.GetProperty("timeframe").GetString()!,
        Symbol: element.GetProperty("symbol").GetString()!,
        Open: element.GetProperty("open").GetDecimal(),
        High: element.GetProperty("high").GetDecimal(),
        Low: element.GetProperty("low").GetDecimal(),
        Close: element.GetProperty("close").GetDecimal(),
        Volume: element.GetProperty("volume").GetInt64(),
        Vwap: element.GetProperty("vwap").GetDecimal(),
        TradeCount: element.GetProperty("tradeCount").GetInt32(),
        StartTimeMs: element.GetProperty("startTimeMs").GetInt64(),
        EndTimeMs: element.GetProperty("endTimeMs").GetInt64());

    private static AggregateTimeframe ParseTimeframe(string timeframe)
        => Enum.Parse<AggregateTimeframe>(timeframe, ignoreCase: true);

    private static AggressorSide ParseAggressor(string aggressor)
        => Enum.Parse<AggressorSide>(aggressor, ignoreCase: true);
}
