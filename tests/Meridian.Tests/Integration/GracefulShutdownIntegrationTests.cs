using System.Collections.Concurrent;
using FluentAssertions;
using Meridian.Application.Pipeline;
using Meridian.Application.Services;
using Meridian.Contracts.Configuration;
using Meridian.Contracts.Domain.Enums;
using Meridian.Contracts.Domain.Models;
using Meridian.Domain.Events;
using Meridian.Domain.Models;
using Meridian.Storage.Interfaces;
using Xunit;
using Xunit.Abstractions;

namespace Meridian.Tests.Integration;

/// <summary>
/// Integration tests verifying zero data loss during graceful shutdown.
/// Creates a full pipeline (FixtureClient -> EventPipeline -> InMemoryStorageSink),
/// publishes events, then disposes the pipeline and verifies all events reached storage.
/// </summary>
[Trait("Category", "Integration")]
public sealed class GracefulShutdownIntegrationTests : IAsyncLifetime
{
    private readonly ITestOutputHelper _output;
    private CountingStorageSink _sink = null!;
    private EventPipeline _pipeline = null!;

    public GracefulShutdownIntegrationTests(ITestOutputHelper output)
    {
        _output = output;
    }

    public Task InitializeAsync()
    {
        _sink = new CountingStorageSink();
        _pipeline = new EventPipeline(
            _sink,
            capacity: 10_000,
            flushInterval: TimeSpan.FromMilliseconds(50),
            enablePeriodicFlush: false);
        return Task.CompletedTask;
    }

    public async Task DisposeAsync()
    {
        await _pipeline.DisposeAsync();
    }

    [Fact]
    public async Task GracefulShutdown_AllPublishedEventsReachStorage()
    {
        // Arrange: Publish a known number of events
        const int eventCount = 500;
        for (int i = 0; i < eventCount; i++)
        {
            var trade = CreateTestTrade("SPY", i);
            await _pipeline.PublishAsync(trade, CancellationToken.None);
        }

        // Act: Flush and dispose (simulates graceful shutdown)
        await _pipeline.FlushAsync(CancellationToken.None);
        await _pipeline.DisposeAsync();

        // Assert: All events should have reached the sink
        _output.WriteLine($"Published: {eventCount}, Stored: {_sink.StoredEvents.Count}");

        _sink.StoredEvents.Count.Should().Be(eventCount,
            "all published events should be persisted during graceful shutdown");
    }

    [Fact]
    public async Task GracefulShutdown_MultipleSymbols_NoDataLoss()
    {
        // Arrange: Publish events for multiple symbols concurrently
        var symbols = new[] { "SPY", "AAPL", "MSFT", "GOOGL", "QQQ" };
        const int eventsPerSymbol = 100;

        var tasks = symbols.Select(symbol => Task.Run(async () =>
        {
            for (int i = 0; i < eventsPerSymbol; i++)
            {
                var trade = CreateTestTrade(symbol, i);
                await _pipeline.PublishAsync(trade, CancellationToken.None);
            }
        }));

        await Task.WhenAll(tasks);

        // Act: Graceful shutdown
        await _pipeline.FlushAsync(CancellationToken.None);
        await _pipeline.DisposeAsync();

        // Assert
        var totalExpected = symbols.Length * eventsPerSymbol;
        _output.WriteLine($"Expected: {totalExpected}, Stored: {_sink.StoredEvents.Count}");
        _output.WriteLine($"Dropped: {_pipeline.DroppedCount}");

        _sink.StoredEvents.Count.Should().Be(totalExpected,
            "all events across all symbols should be persisted");
        _pipeline.DroppedCount.Should().Be(0,
            "no events should be dropped with sufficient capacity");
    }

    [Fact]
    public async Task GracefulShutdown_FlushableDispose_CompletesWithinTimeout()
    {
        // Arrange: Publish events
        const int eventCount = 200;
        for (int i = 0; i < eventCount; i++)
        {
            await _pipeline.PublishAsync(CreateTestTrade("SPY", i), CancellationToken.None);
        }

        // Act: Time the shutdown
        var sw = System.Diagnostics.Stopwatch.StartNew();
        await _pipeline.FlushAsync(CancellationToken.None);
        await _pipeline.DisposeAsync();
        sw.Stop();

        // Assert: Shutdown should complete quickly (well under the 30s default timeout)
        _output.WriteLine($"Shutdown completed in {sw.ElapsedMilliseconds}ms");
        sw.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(5),
            "graceful shutdown with small event count should complete quickly");
        _sink.StoredEvents.Count.Should().Be(eventCount);
    }

    [Fact]
    public async Task GracefulShutdown_PipelineStatistics_AreConsistent()
    {
        // Arrange
        const int eventCount = 300;
        for (int i = 0; i < eventCount; i++)
        {
            await _pipeline.PublishAsync(CreateTestTrade("AAPL", i), CancellationToken.None);
        }

        // Act
        await _pipeline.FlushAsync(CancellationToken.None);
        await _pipeline.DisposeAsync();

        // Assert: published = consumed + dropped
        var stats = _pipeline.GetStatistics();
        _output.WriteLine($"Stats - Published: {stats.PublishedCount}, Consumed: {stats.ConsumedCount}, Dropped: {stats.DroppedCount}");

        stats.PublishedCount.Should().Be(eventCount);
        (stats.ConsumedCount + stats.DroppedCount).Should().Be(eventCount,
            "consumed + dropped should equal published");
        _sink.StoredEvents.Count.Should().Be((int)stats.ConsumedCount);
    }

    [Fact]
    public async Task GracefulShutdown_SlowSink_StillFlushes()
    {
        // Arrange: Use a slow sink that simulates disk latency
        var slowSink = new SlowStorageSink(delayMs: 1);
        await using var slowPipeline = new EventPipeline(
            slowSink,
            capacity: 1_000,
            flushInterval: TimeSpan.FromMilliseconds(100),
            enablePeriodicFlush: false);

        const int eventCount = 50;
        for (int i = 0; i < eventCount; i++)
        {
            await slowPipeline.PublishAsync(CreateTestTrade("MSFT", i), CancellationToken.None);
        }

        // Act: Flush should wait for slow sink
        var sw = System.Diagnostics.Stopwatch.StartNew();
        await slowPipeline.FlushAsync(CancellationToken.None);
        sw.Stop();

        // Assert
        _output.WriteLine($"Slow flush completed in {sw.ElapsedMilliseconds}ms, stored {slowSink.StoredEvents.Count} events");
        slowSink.StoredEvents.Count.Should().Be(eventCount,
            "all events should reach storage even with a slow sink");
    }

    private static MarketEvent CreateTestTrade(string symbol, int sequence)
    {
        var trade = new Trade(
            Timestamp: DateTimeOffset.UtcNow,
            Symbol: symbol,
            Price: 100.0m + sequence * 0.01m,
            Size: 100,
            Aggressor: AggressorSide.Unknown,
            SequenceNumber: sequence,
            StreamId: "test");
        return MarketEvent.Trade(trade.Timestamp, symbol, trade, sequence, "test");
    }

    /// <summary>
    /// Storage sink that counts events in a thread-safe manner.
    /// </summary>
    private sealed class CountingStorageSink : IStorageSink
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
    /// Storage sink with artificial delay to simulate disk I/O latency.
    /// </summary>
    private sealed class SlowStorageSink : IStorageSink
    {
        private readonly int _delayMs;
        public ConcurrentBag<MarketEvent> StoredEvents { get; } = new();

        public SlowStorageSink(int delayMs = 5) => _delayMs = delayMs;

        public async ValueTask AppendAsync(MarketEvent evt, CancellationToken ct = default)
        {
            await Task.Delay(_delayMs, ct);
            StoredEvents.Add(evt);
        }

        public Task FlushAsync(CancellationToken ct = default) => Task.CompletedTask;
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
