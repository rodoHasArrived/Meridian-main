using FluentAssertions;
using Meridian.Application.Pipeline;
using Meridian.Contracts.Domain.Enums;
using Meridian.Contracts.Domain.Models;
using Meridian.Domain.Events;
using Meridian.Domain.Models;
using Meridian.Storage.Interfaces;
using Xunit;

namespace Meridian.Tests.Pipeline;

/// <summary>
/// Golden-master end-to-end test: feeds a known set of <see cref="MarketEvent"/>s through the
/// full <see cref="EventPipeline"/> and asserts that the output matches a committed baseline.
/// </summary>
/// <remarks>
/// This test catches subtle behavioral regressions—ordering changes, dropped events, silent
/// mutations—by comparing the captured output against the baseline snapshot defined inline.
/// Add new baseline scenarios rather than modifying existing ones to preserve historical coverage.
/// </remarks>
public sealed class GoldenMasterPipelineReplayTests : IAsyncLifetime
{
    private CaptureSink _sink = null!;
    private EventPipeline _pipeline = null!;

    public Task InitializeAsync()
    {
        _sink = new CaptureSink();
        _pipeline = new EventPipeline(
            _sink,
            capacity: 1_000,
            enablePeriodicFlush: false);
        return Task.CompletedTask;
    }

    public async Task DisposeAsync()
    {
        await _pipeline.DisposeAsync();
    }

    // ------------------------------------------------------------------ //
    //  Baseline scenario 1: single Trade                                  //
    // ------------------------------------------------------------------ //

    [Fact]
    public async Task Replay_SingleTrade_OutputMatchesBaseline()
    {
        // Arrange – a deterministic trade with a fixed timestamp
        var ts = new DateTimeOffset(2025, 1, 15, 10, 0, 0, TimeSpan.Zero);
        var trade = new Trade(ts, "SPY", 520.00m, 100L, AggressorSide.Buy, 1L, Venue: "NYSE");
        var evt = MarketEvent.Trade(ts, "SPY", trade, seq: 1, source: "NYSE");

        // Act
        _pipeline.TryPublish(evt);
        await WaitFor(() => _sink.Count >= 1);

        // Assert – baseline snapshot
        _sink.Count.Should().Be(1, "exactly one event was published");

        var captured = _sink.Events[0];
        captured.Type.Should().Be(MarketEventType.Trade, "event type must be preserved");
        captured.Symbol.Should().Be("SPY", "symbol must be preserved");
        captured.Sequence.Should().Be(1L, "sequence number must be preserved");
        captured.Source.Should().Be("NYSE", "source must be preserved");

        var capturedTrade = captured.Payload.Should().BeOfType<Trade>().Subject;
        capturedTrade.Price.Should().Be(520.00m);
        capturedTrade.Size.Should().Be(100L);
        capturedTrade.Aggressor.Should().Be(AggressorSide.Buy);
        capturedTrade.Venue.Should().Be("NYSE");
    }

    // ------------------------------------------------------------------ //
    //  Baseline scenario 2: mixed event types in order                   //
    // ------------------------------------------------------------------ //

    [Fact]
    public async Task Replay_MixedEvents_OutputPreservesOrderAndTypes()
    {
        // Arrange – one trade followed by one quote
        var ts = new DateTimeOffset(2025, 1, 15, 10, 0, 0, TimeSpan.Zero);

        var trade = new Trade(ts, "AAPL", 230.00m, 50L, AggressorSide.Sell, 1L);
        var tradeEvt = MarketEvent.Trade(ts, "AAPL", trade, seq: 1, source: "ALPACA");

        var quoteUpdate = new MarketQuoteUpdate(ts, "AAPL", 229.95m, 200L, 230.05m, 150L);
        var quote = BboQuotePayload.FromUpdate(quoteUpdate, seq: 2);
        var quoteEvt = MarketEvent.BboQuote(ts, "AAPL", quote, seq: 2, source: "ALPACA");

        // Act – publish in defined order
        _pipeline.TryPublish(tradeEvt);
        _pipeline.TryPublish(quoteEvt);
        await WaitFor(() => _sink.Count >= 2);

        // Assert – order preserved and types correct
        _sink.Count.Should().Be(2, "both events must be captured");
        _sink.Events[0].Type.Should().Be(MarketEventType.Trade);
        _sink.Events[0].Symbol.Should().Be("AAPL");
        _sink.Events[1].Type.Should().Be(MarketEventType.BboQuote);
        _sink.Events[1].Symbol.Should().Be("AAPL");
        _sink.Events[1].Sequence.Should().BeGreaterThan(_sink.Events[0].Sequence,
            "quote sequence must be higher than trade sequence");
    }

    // ------------------------------------------------------------------ //
    //  Baseline scenario 3: heartbeat carries a HeartbeatPayload          //
    // ------------------------------------------------------------------ //

    [Fact]
    public async Task Replay_Heartbeat_PayloadIsHeartbeatPayload()
    {
        // Arrange
        var ts = new DateTimeOffset(2025, 1, 15, 10, 0, 0, TimeSpan.Zero);
        var heartbeat = MarketEvent.Heartbeat(ts, source: "IB");

        // Act
        _pipeline.TryPublish(heartbeat);
        await WaitFor(() => _sink.Count >= 1);

        // Assert
        var captured = _sink.Events[0];
        captured.Type.Should().Be(MarketEventType.Heartbeat);
        captured.Symbol.Should().Be("SYSTEM");
        captured.Payload.Should().BeOfType<Meridian.Contracts.Domain.Events.MarketEventPayload.HeartbeatPayload>(
            "heartbeat events carry a HeartbeatPayload, not null");
    }

    // ------------------------------------------------------------------ //
    //  Baseline scenario 4: historical bar                                //
    // ------------------------------------------------------------------ //

    [Fact]
    public async Task Replay_HistoricalBar_PriceDataPreserved()
    {
        // Arrange
        var date = new DateOnly(2024, 12, 31);
        var bar = new HistoricalBar("SPY", date, 580.00m, 582.50m, 579.00m, 581.75m, 95_000_000L, "stooq");
        var ts = bar.ToTimestampUtc();
        var evt = MarketEvent.HistoricalBar(ts, "SPY", bar, seq: 1, source: "stooq");

        // Act
        _pipeline.TryPublish(evt);
        await WaitFor(() => _sink.Count >= 1);

        // Assert – baseline for OHLCV data integrity
        var captured = _sink.Events[0];
        captured.Type.Should().Be(MarketEventType.HistoricalBar);
        var capturedBar = captured.Payload.Should().BeOfType<HistoricalBar>().Subject;
        capturedBar.Open.Should().Be(580.00m);
        capturedBar.High.Should().Be(582.50m);
        capturedBar.Low.Should().Be(579.00m);
        capturedBar.Close.Should().Be(581.75m);
        capturedBar.Volume.Should().Be(95_000_000L);
        capturedBar.SessionDate.Should().Be(date);
    }

    // ------------------------------------------------------------------ //
    //  Helpers                                                             //
    // ------------------------------------------------------------------ //

    private static async Task WaitFor(Func<bool> condition, int timeoutMs = 2_000)
    {
        var deadline = Environment.TickCount64 + timeoutMs;
        while (!condition() && Environment.TickCount64 < deadline)
        {
            await Task.Delay(1);
        }
    }

    // ------------------------------------------------------------------ //
    //  In-process capture sink                                             //
    // ------------------------------------------------------------------ //

    private sealed class CaptureSink : IStorageSink
    {
        private readonly List<MarketEvent> _events = new();
        private readonly object _lock = new();

        public int Count { get { lock (_lock) return _events.Count; } }

        public IReadOnlyList<MarketEvent> Events
        {
            get { lock (_lock) return _events.ToList(); }
        }

        public ValueTask AppendAsync(MarketEvent evt, CancellationToken ct = default)
        {
            lock (_lock)
                _events.Add(evt);
            return ValueTask.CompletedTask;
        }

        public Task FlushAsync(CancellationToken ct = default) => Task.CompletedTask;
        public void Dispose() { }
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
