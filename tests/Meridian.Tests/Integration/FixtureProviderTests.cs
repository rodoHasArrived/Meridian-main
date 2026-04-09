using System.Collections.Concurrent;
using FluentAssertions;
using Meridian.Application.Pipeline;
using Meridian.Contracts.Configuration;
using Meridian.Contracts.Domain.Enums;
using Meridian.Contracts.Domain.Models;
using Meridian.Domain.Events;
using Meridian.Domain.Models;
using Meridian.Infrastructure;
using Meridian.Infrastructure.Adapters.Core;
using Meridian.Infrastructure.Contracts;
using Meridian.Storage.Interfaces;
using Xunit;

namespace Meridian.Tests.Integration;

/// <summary>
/// Fixture-based market data client that generates deterministic trade and quote data
/// for full pipeline integration testing without live API connections.
/// Implements Roadmap I1: Integration Test Harness with Fixture Providers.
/// </summary>
public sealed class FixtureMarketDataClient : IMarketDataClient
{
    private readonly ConcurrentDictionary<int, (SymbolConfig Config, CancellationTokenSource Cts)> _depthSubs = new();
    private readonly ConcurrentDictionary<int, (SymbolConfig Config, CancellationTokenSource Cts)> _tradeSubs = new();
    private readonly ConcurrentDictionary<int, Task> _depthTasks = new();
    private readonly ConcurrentDictionary<int, Task> _tradeTasks = new();
    private readonly IMarketEventPublisher _publisher;
    private int _nextSubId;
    private volatile bool _connected;

    /// <summary>Number of events generated per symbol per subscription tick.</summary>
    public int EventsPerTick { get; set; } = 5;

    /// <summary>Interval between batches of fixture events.</summary>
    public TimeSpan TickInterval { get; set; } = TimeSpan.FromMilliseconds(5);

    /// <summary>Tracks all generated events for assertion.</summary>
    public ConcurrentBag<MarketEvent> GeneratedEvents { get; } = new();

    public bool IsEnabled => true;
    public string ProviderId => "fixture";
    public string ProviderDisplayName => "Fixture Data Provider";
    public string ProviderDescription => "Deterministic fixture data for integration testing";
    public int ProviderPriority => 0;
    public ProviderCapabilities ProviderCapabilities { get; } = ProviderCapabilities.Streaming(trades: true, quotes: true, depth: true);

    public FixtureMarketDataClient(IMarketEventPublisher publisher)
    {
        _publisher = publisher ?? throw new ArgumentNullException(nameof(publisher));
    }

    public Task ConnectAsync(CancellationToken ct = default)
    {
        _connected = true;
        return Task.CompletedTask;
    }

    public async Task DisconnectAsync(CancellationToken ct = default)
    {
        _connected = false;
        foreach (var sub in _tradeSubs.Values)
            sub.Cts.Cancel();
        foreach (var sub in _depthSubs.Values)
            sub.Cts.Cancel();

        var runningTasks = _tradeTasks.Values.Concat(_depthTasks.Values).ToArray();
        if (runningTasks.Length > 0)
            await Task.WhenAll(runningTasks).ConfigureAwait(false);

        _tradeSubs.Clear();
        _depthSubs.Clear();
        _tradeTasks.Clear();
        _depthTasks.Clear();
    }

    public int SubscribeTrades(SymbolConfig cfg)
    {
        var id = Interlocked.Increment(ref _nextSubId);
        var cts = new CancellationTokenSource();
        _tradeSubs[id] = (cfg, cts);
        _tradeTasks[id] = GenerateTradeEventsAsync(cfg, cts.Token);
        return id;
    }

    public void UnsubscribeTrades(int subscriptionId)
    {
        if (_tradeSubs.TryRemove(subscriptionId, out var sub))
            sub.Cts.Cancel();
        _tradeTasks.TryRemove(subscriptionId, out _);
    }

    public int SubscribeMarketDepth(SymbolConfig cfg)
    {
        var id = Interlocked.Increment(ref _nextSubId);
        var cts = new CancellationTokenSource();
        _depthSubs[id] = (cfg, cts);
        _depthTasks[id] = GenerateDepthEventsAsync(cfg, cts.Token);
        return id;
    }

    public void UnsubscribeMarketDepth(int subscriptionId)
    {
        if (_depthSubs.TryRemove(subscriptionId, out var sub))
            sub.Cts.Cancel();
        _depthTasks.TryRemove(subscriptionId, out _);
    }

    public ValueTask DisposeAsync()
    {
        return new ValueTask(DisconnectAsync());
    }

    private async Task GenerateTradeEventsAsync(SymbolConfig cfg, CancellationToken ct)
    {
        var rng = new Random(cfg.Symbol.GetHashCode());
        long seq = 1;
        var basePrice = 100.0m + rng.Next(0, 400);

        while (!ct.IsCancellationRequested && _connected)
        {
            for (int i = 0; i < EventsPerTick && !ct.IsCancellationRequested; i++)
            {
                var price = basePrice + (decimal)(rng.NextDouble() * 2.0 - 1.0);
                var volume = rng.Next(1, 1000);
                var trade = new Trade(
                    Timestamp: DateTimeOffset.UtcNow,
                    Symbol: cfg.Symbol,
                    Price: price,
                    Size: volume,
                    Aggressor: AggressorSide.Unknown,
                    SequenceNumber: seq++,
                    StreamId: null,
                    Venue: "XNGS");

                var evt = MarketEvent.Trade(DateTimeOffset.UtcNow, cfg.Symbol, trade, source: "fixture");
                GeneratedEvents.Add(evt);
                _publisher.TryPublish(in evt);
            }

            try
            { await Task.Delay(TickInterval, ct); }
            catch (OperationCanceledException) { break; }
        }
    }

    private async Task GenerateDepthEventsAsync(SymbolConfig cfg, CancellationToken ct)
    {
        var rng = new Random(cfg.Symbol.GetHashCode() + 1);
        long seq = 1;
        var basePrice = 100.0m + rng.Next(0, 400);

        while (!ct.IsCancellationRequested && _connected)
        {
            var bidPrice = basePrice - 0.01m * rng.Next(1, 10);
            var askPrice = basePrice + 0.01m * rng.Next(1, 10);

            var quote = new BboQuotePayload(
                Timestamp: DateTimeOffset.UtcNow,
                Symbol: cfg.Symbol,
                BidPrice: bidPrice,
                BidSize: rng.Next(100, 5000),
                AskPrice: askPrice,
                AskSize: rng.Next(100, 5000),
                MidPrice: (bidPrice + askPrice) / 2m,
                Spread: askPrice - bidPrice,
                SequenceNumber: seq++,
                StreamId: null,
                Venue: "XNGS");

            var evt = MarketEvent.BboQuote(DateTimeOffset.UtcNow, cfg.Symbol, quote, source: "fixture");
            GeneratedEvents.Add(evt);
            _publisher.TryPublish(in evt);

            try
            { await Task.Delay(TickInterval, ct); }
            catch (OperationCanceledException) { break; }
        }
    }
}

/// <summary>
/// In-memory storage sink that captures events for verification.
/// </summary>
public sealed class InMemoryStorageSink : IStorageSink
{
    public ConcurrentBag<MarketEvent> StoredEvents { get; } = new();

    public ValueTask AppendAsync(MarketEvent evt, CancellationToken ct = default)
    {
        StoredEvents.Add(evt);
        return ValueTask.CompletedTask;
    }

    public Task FlushAsync(CancellationToken ct = default) => Task.CompletedTask;
    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}

/// <summary>
/// Integration tests that exercise the full pipeline (provider -> pipeline -> storage)
/// using fixture data, without requiring any live API connections.
/// Implements Roadmap I1: Integration Test Harness with Fixture Providers.
/// </summary>
[Trait("Category", "Integration")]
public sealed class FixtureProviderPipelineTests : IAsyncLifetime
{
    private InMemoryStorageSink _sink = null!;
    private EventPipeline _pipeline = null!;
    private FixtureMarketDataClient _client = null!;

    public Task InitializeAsync()
    {
        _sink = new InMemoryStorageSink();
        _pipeline = new EventPipeline(
            _sink,
            capacity: 10_000,
            flushInterval: TimeSpan.FromMilliseconds(50),
            enablePeriodicFlush: false);
        _client = new FixtureMarketDataClient(_pipeline)
        {
            EventsPerTick = 10,
            TickInterval = TimeSpan.FromMilliseconds(1)
        };
        return Task.CompletedTask;
    }

    public async Task DisposeAsync()
    {
        await _client.DisposeAsync();
        await _pipeline.DisposeAsync();
    }

    [Fact]
    public async Task FullPipeline_TradeSubscription_EventsFlowToStorage()
    {
        // Arrange
        await _client.ConnectAsync();
        var cfg = new SymbolConfig("SPY", SubscribeTrades: true, SubscribeDepth: false);

        // Act - Subscribe and let events flow
        var subId = _client.SubscribeTrades(cfg);
        await Task.Delay(15); // Let fixture generate events
        _client.UnsubscribeTrades(subId);

        // Flush pipeline to storage
        await _pipeline.FlushAsync();

        // Assert - Events should have reached storage
        _client.GeneratedEvents.Should().NotBeEmpty("fixture should have generated trade events");
        _sink.StoredEvents.Should().NotBeEmpty("events should have flowed through pipeline to storage");
        _sink.StoredEvents.Should().AllSatisfy(e =>
        {
            e.Symbol.Should().Be("SPY");
            e.Source.Should().Be("fixture");
        });
    }

    [Fact]
    public async Task FullPipeline_DepthSubscription_QuoteEventsFlowToStorage()
    {
        // Arrange
        await _client.ConnectAsync();
        var cfg = new SymbolConfig("AAPL", SubscribeTrades: false, SubscribeDepth: true);

        // Act
        var subId = _client.SubscribeMarketDepth(cfg);
        await Task.Delay(15);
        _client.UnsubscribeMarketDepth(subId);
        await _pipeline.FlushAsync();

        // Assert
        _client.GeneratedEvents.Should().NotBeEmpty();
        _sink.StoredEvents.Should().NotBeEmpty();
        _sink.StoredEvents.Should().AllSatisfy(e => e.Symbol.Should().Be("AAPL"));
    }

    [Fact]
    public async Task FullPipeline_MultipleSymbols_AllEventsReachStorage()
    {
        // Arrange
        await _client.ConnectAsync();
        var symbols = new[] { "SPY", "AAPL", "MSFT" };
        var subIds = new List<int>();

        // Act - Subscribe all symbols
        foreach (var sym in symbols)
        {
            subIds.Add(_client.SubscribeTrades(new SymbolConfig(sym)));
        }

        await Task.Delay(15);

        foreach (var id in subIds)
            _client.UnsubscribeTrades(id);

        await _pipeline.FlushAsync();

        // Assert - All symbols should have events
        var storedSymbols = _sink.StoredEvents.Select(e => e.Symbol).Distinct().ToList();
        storedSymbols.Should().Contain(symbols);
    }

    [Fact]
    public async Task FullPipeline_DisconnectStopsEvents()
    {
        // Arrange
        await _client.ConnectAsync();
        _client.SubscribeTrades(new SymbolConfig("SPY"));
        await Task.Delay(15);

        // Act - Disconnect
        await _client.DisconnectAsync();
        var countAfterDisconnect = _client.GeneratedEvents.Count;
        await Task.Delay(15);
        var countLater = _client.GeneratedEvents.Count;

        // Assert - No new events after disconnect
        countLater.Should().Be(countAfterDisconnect);
    }

    [Fact]
    public async Task FullPipeline_TradeEventsHaveValidSequenceNumbers()
    {
        // Arrange
        await _client.ConnectAsync();
        var subId = _client.SubscribeTrades(new SymbolConfig("SPY"));

        // Act
        await Task.Delay(15);
        _client.UnsubscribeTrades(subId);
        await _pipeline.FlushAsync();

        // Assert - Events should have sequential sequence numbers
        var events = _sink.StoredEvents
            .Where(e => e.Type == MarketEventType.Trade)
            .OrderBy(e => e.Sequence)
            .ToList();

        events.Should().NotBeEmpty();
        for (int i = 1; i < events.Count; i++)
        {
            events[i].Sequence.Should().BeGreaterThan(events[i - 1].Sequence);
        }
    }

    [Fact]
    public async Task FullPipeline_QuoteEventsHaveValidPrices()
    {
        // Arrange
        await _client.ConnectAsync();
        var subId = _client.SubscribeMarketDepth(new SymbolConfig("MSFT"));

        // Act
        await Task.Delay(15);
        _client.UnsubscribeMarketDepth(subId);
        await _pipeline.FlushAsync();

        // Assert - BBO quotes should have valid bid < ask
        var quoteEvents = _sink.StoredEvents
            .Where(e => e.Type == MarketEventType.BboQuote)
            .ToList();

        quoteEvents.Should().NotBeEmpty();
        foreach (var evt in quoteEvents)
        {
            var payload = evt.Payload as BboQuotePayload;
            payload.Should().NotBeNull();
            payload!.BidPrice.Should().BePositive();
            payload.AskPrice.Should().BePositive();
            payload.AskPrice.Should().BeGreaterThan(payload.BidPrice);
            payload.Spread.Should().BePositive();
        }
    }

    [Fact]
    public void FixtureClient_IsEnabled_ReturnsTrue()
    {
        _client.IsEnabled.Should().BeTrue();
    }

    [Fact]
    public void FixtureClient_ProviderMetadata_IsCorrect()
    {
        _client.ProviderId.Should().Be("fixture");
        _client.ProviderDisplayName.Should().NotBeNullOrEmpty();
        _client.ProviderCapabilities.SupportsStreaming.Should().BeTrue();
        _client.ProviderCapabilities.SupportsRealtimeTrades.Should().BeTrue();
    }

    [Fact]
    public async Task FullPipeline_HighThroughput_NoDuplicates()
    {
        // Arrange - Configure high-throughput settings
        _client.EventsPerTick = 50;
        _client.TickInterval = TimeSpan.FromMilliseconds(1);

        await _client.ConnectAsync();
        var subId = _client.SubscribeTrades(new SymbolConfig("SPY"));

        // Act - Let it run for a bit
        await Task.Delay(20);
        _client.UnsubscribeTrades(subId);
        await _pipeline.FlushAsync();

        // Assert - Should have many events with no duplicate sequences
        var sequences = _sink.StoredEvents
            .Where(e => e.Type == MarketEventType.Trade && e.Symbol == "SPY")
            .Select(e => e.Sequence)
            .ToList();

        sequences.Should().HaveCountGreaterThan(10);
        sequences.Distinct().Count().Should().Be(sequences.Count, "no duplicate sequence numbers");
    }
}
