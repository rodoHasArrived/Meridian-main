using FluentAssertions;
using Meridian.Application.Pipeline;
using Meridian.Contracts.Domain.Enums;
using Meridian.Contracts.Domain.Models;
using Meridian.Core.Performance;
using Meridian.Domain.Events;
using Meridian.Storage.Interfaces;
using Moq;
using Xunit;

namespace Meridian.Tests.Pipeline;

/// <summary>
/// Tests for <see cref="DualPathEventPipeline"/> routing, statistics, and lifecycle.
/// </summary>
public class DualPathEventPipelineTests : IAsyncLifetime
{
    private MockStorageSink _sink = null!;
    private EventPipeline _slowPath = null!;
    private SymbolTable _symbolTable = null!;
    private DualPathEventPipeline _pipeline = null!;

    public Task InitializeAsync()
    {
        _sink = new MockStorageSink();
        _slowPath = new EventPipeline(_sink, capacity: 10_000, enablePeriodicFlush: false);
        _symbolTable = new SymbolTable();
        _pipeline = new DualPathEventPipeline(_slowPath, _symbolTable, ringBufferCapacity: 256, batchDrainSize: 64);
        return Task.CompletedTask;
    }

    public async Task DisposeAsync()
    {
        await _pipeline.DisposeAsync();
        await _slowPath.DisposeAsync();
    }

    #region Constructor validation

    [Fact]
    public void Constructor_NullSlowPath_Throws()
    {
        var act = () => new DualPathEventPipeline(null!, new SymbolTable());
        act.Should().Throw<ArgumentNullException>().WithParameterName("slowPath");
    }

    [Fact]
    public void Constructor_NullSymbolTable_Throws()
    {
        var act = () => new DualPathEventPipeline(_slowPath, null!);
        act.Should().Throw<ArgumentNullException>().WithParameterName("symbolTable");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(-1)]
    public void Constructor_InvalidRingBufferCapacity_Throws(int cap)
    {
        var act = () => new DualPathEventPipeline(_slowPath, _symbolTable, ringBufferCapacity: cap);
        act.Should().Throw<ArgumentOutOfRangeException>().WithParameterName("ringBufferCapacity");
    }

    #endregion

    #region Routing tests

    [Fact]
    public async Task TryPublish_TradeEvent_RoutedThroughHotPath()
    {
        var evt = CreateTradeEvent("SPY", seq: 1);
        _pipeline.TryPublish(in evt);

        await WaitForSinkCount(1, timeout: TimeSpan.FromSeconds(5));

        _pipeline.HotTradePublished.Should().Be(1);
        _sink.ReceivedEvents.Should().ContainSingle(e => e.Symbol == "SPY");
    }

    [Fact]
    public async Task TryPublish_BboQuoteEvent_RoutedThroughHotPath()
    {
        var evt = CreateQuoteEvent("AAPL", seq: 1);
        _pipeline.TryPublish(in evt);

        await WaitForSinkCount(1, timeout: TimeSpan.FromSeconds(5));

        _pipeline.HotQuotePublished.Should().Be(1);
        _sink.ReceivedEvents.Should().ContainSingle(e => e.Symbol == "AAPL");
    }

    [Fact]
    public async Task QuoteHotPath_WhenSlowPathBackpressures_RetriesUntilPersisted()
    {
        var release = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var blockingSink = new BlockingStorageSink(release.Task);
        await using var constrainedSlowPath = new EventPipeline(
            blockingSink,
            capacity: 1,
            fullMode: System.Threading.Channels.BoundedChannelFullMode.Wait,
            batchSize: 1,
            enablePeriodicFlush: false);
        await using var pipeline = new DualPathEventPipeline(
            constrainedSlowPath,
            new SymbolTable(),
            ringBufferCapacity: 32,
            batchDrainSize: 4);

        pipeline.TryPublish(CreateQuoteEvent("AAPL", seq: 1));
        pipeline.TryPublish(CreateQuoteEvent("AAPL", seq: 2));
        pipeline.TryPublish(CreateQuoteEvent("AAPL", seq: 3));

        var firstBlockSw = System.Diagnostics.Stopwatch.StartNew();
        while (blockingSink.ReceivedCount < 1 && firstBlockSw.Elapsed < TimeSpan.FromSeconds(5))
        {
            await Task.Delay(10);
        }

        blockingSink.ReceivedCount.Should().BeGreaterThanOrEqualTo(1,
            "the slow path should have received at least one event before releasing backpressure in this test");
        release.SetResult(true);

        var sw = System.Diagnostics.Stopwatch.StartNew();
        while (blockingSink.ReceivedCount < 3 && sw.Elapsed < TimeSpan.FromSeconds(5))
        {
            await Task.Delay(10);
        }

        blockingSink.ReceivedCount.Should().Be(3,
            "quote hot-path fallback should retry instead of silently dropping when the slow path is full");
    }

    [Fact]
    public async Task TryPublish_IntegrityEvent_GoesToSlowPath_NotHotPath()
    {
        var evt = MarketEvent.Integrity(
            DateTimeOffset.UtcNow, "SPY",
            new IntegrityEvent(DateTimeOffset.UtcNow, "SPY", IntegritySeverity.Warning, "test", 0, 1));

        _pipeline.TryPublish(in evt);

        await WaitForSinkCount(1, timeout: TimeSpan.FromSeconds(5));

        _pipeline.HotTradePublished.Should().Be(0);
        _pipeline.HotQuotePublished.Should().Be(0);
        _sink.ReceivedEvents.Should().ContainSingle(e => e.Type == MarketEventType.Integrity);
    }

    [Fact]
    public async Task TryPublish_HeartbeatEvent_GoesToSlowPath()
    {
        var evt = MarketEvent.Heartbeat(DateTimeOffset.UtcNow);
        _pipeline.TryPublish(in evt);

        await WaitForSinkCount(1, timeout: TimeSpan.FromSeconds(5));

        _pipeline.HotTradePublished.Should().Be(0);
        _sink.ReceivedEvents.Should().ContainSingle(e => e.Type == MarketEventType.Heartbeat);
    }

    [Fact]
    public async Task TryPublish_MultipleTrades_AllReachSink()
    {
        const int count = 50;
        for (var i = 0; i < count; i++)
            _pipeline.TryPublish(CreateTradeEvent("SPY", seq: i));

        await WaitForSinkCount(count, timeout: TimeSpan.FromSeconds(5));

        _pipeline.HotTradePublished.Should().Be(count);
        _sink.ReceivedEvents.Should().HaveCount(count);
    }

    #endregion

    #region Zero-allocation API tests

    [Fact]
    public async Task TryPublishTrade_DirectStruct_ReachesSlowPath()
    {
        var symbolId = _symbolTable.GetOrAdd("SPY");
        var raw = new RawTradeEvent(DateTimeOffset.UtcNow.UtcTicks, symbolId, 100m, 10L, 1, 1L);

        _pipeline.TryPublishTrade(in raw).Should().BeTrue();

        await WaitForSinkCount(1, timeout: TimeSpan.FromSeconds(5));

        _pipeline.HotTradePublished.Should().Be(1);
        _sink.ReceivedEvents.Should().ContainSingle(e => e.Symbol == "SPY");
    }

    [Fact]
    public async Task TryPublishQuote_DirectStruct_ReachesSlowPath()
    {
        var symbolId = _symbolTable.GetOrAdd("AAPL");
        var raw = new RawQuoteEvent(DateTimeOffset.UtcNow.UtcTicks, symbolId, 189m, 100L, 190m, 200L, 1L);

        _pipeline.TryPublishQuote(in raw).Should().BeTrue();

        await WaitForSinkCount(1, timeout: TimeSpan.FromSeconds(5));

        _pipeline.HotQuotePublished.Should().Be(1);
        _sink.ReceivedEvents.Should().ContainSingle(e => e.Symbol == "AAPL");
    }

    [Fact]
    public async Task TryPublishTrade_WhenBufferFull_ReturnsFalse()
    {
        // Fill the ring buffer completely (consumers disabled so they cannot race to drain it)
        await using var tinyPipeline = new DualPathEventPipeline(_slowPath, _symbolTable, ringBufferCapacity: 2, batchDrainSize: 1, startConsumers: false);
        var symbolId = _symbolTable.GetOrAdd("SPY");

        // Fill the buffer (capacity rounds up to power of 2 = 2)
        var raw = new RawTradeEvent(DateTimeOffset.UtcNow.UtcTicks, symbolId, 1m, 1L, 0, 1L);
        tinyPipeline.TryPublishTrade(in raw);
        tinyPipeline.TryPublishTrade(in raw);

        // Buffer should now be full — next write should fail
        var result = tinyPipeline.TryPublishTrade(in raw);
        result.Should().BeFalse();
        tinyPipeline.HotTradeDropped.Should().BeGreaterThanOrEqualTo(1);
    }

    #endregion

    #region Backpressure delegation tests

    [Fact]
    public void IsUnderPressure_DelegatesToSlowPath()
    {
        // The slow path is not under pressure with a large, empty channel
        _pipeline.IsUnderPressure.Should().BeFalse();
    }

    [Fact]
    public void QueueUtilization_DelegatesToSlowPath()
    {
        _pipeline.QueueUtilization.Should().BeInRange(0.0, 100.0);
    }

    #endregion

    #region Statistics tests

    [Fact]
    public async Task HotTradeConsumed_IncreasesAfterDrain()
    {
        _pipeline.TryPublish(CreateTradeEvent("SPY", seq: 1));
        await WaitForConsumed(1, timeout: TimeSpan.FromSeconds(5));
        _pipeline.HotTradeConsumed.Should().BeGreaterThanOrEqualTo(1);
    }

    [Fact]
    public async Task HotQuoteConsumed_IncreasesAfterDrain()
    {
        _pipeline.TryPublish(CreateQuoteEvent("AAPL", seq: 1));
        await WaitForQuoteConsumed(1, timeout: TimeSpan.FromSeconds(5));
        _pipeline.HotQuoteConsumed.Should().BeGreaterThanOrEqualTo(1);
    }

    #endregion

    #region Mixed event type tests

    [Fact]
    public async Task MixedEvents_EachRoutedToCorrectPath()
    {
        _pipeline.TryPublish(CreateTradeEvent("SPY", seq: 1));
        _pipeline.TryPublish(CreateQuoteEvent("AAPL", seq: 2));
        _pipeline.TryPublish(MarketEvent.Heartbeat(DateTimeOffset.UtcNow));

        await WaitForSinkCount(3, timeout: TimeSpan.FromSeconds(5));

        _sink.ReceivedEvents.Should().HaveCount(3);
        _pipeline.HotTradePublished.Should().Be(1);
        _pipeline.HotQuotePublished.Should().Be(1);
    }

    #endregion

    #region Helpers

    private static MarketEvent CreateTradeEvent(string symbol, int seq = 1)
    {
        var trade = new Trade(
            Timestamp: DateTimeOffset.UtcNow,
            Symbol: symbol,
            Price: 100.50m,
            Size: 100L,
            Aggressor: AggressorSide.Buy,
            SequenceNumber: seq);
        return MarketEvent.Trade(DateTimeOffset.UtcNow, symbol, trade, seq);
    }

    private static MarketEvent CreateQuoteEvent(string symbol, int seq = 1)
    {
        var quote = BboQuotePayload.FromUpdate(
            timestamp: DateTimeOffset.UtcNow,
            symbol: symbol,
            bidPrice: 100m,
            bidSize: 100L,
            askPrice: 100.10m,
            askSize: 100L,
            sequenceNumber: seq);
        return MarketEvent.BboQuote(DateTimeOffset.UtcNow, symbol, quote, seq);
    }

    private async Task WaitForSinkCount(int expected, TimeSpan timeout)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        while (_sink.ReceivedEvents.Count < expected && sw.Elapsed < timeout)
            await Task.Delay(5);
    }

    private async Task WaitForConsumed(long expected, TimeSpan timeout)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        while (_pipeline.HotTradeConsumed < expected && sw.Elapsed < timeout)
            await Task.Delay(5);
    }

    private async Task WaitForQuoteConsumed(long expected, TimeSpan timeout)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        while (_pipeline.HotQuoteConsumed < expected && sw.Elapsed < timeout)
            await Task.Delay(5);
    }

    #endregion
}
