using System.Text.Json;
using FluentAssertions;
using Meridian.Application.Serialization;
using Meridian.Backtesting.Engine;
using Meridian.Domain.Events;
using Meridian.Storage;
using Meridian.Storage.Services;
using Microsoft.Extensions.Logging.Abstractions;

namespace Meridian.Backtesting.Tests;

/// <summary>
/// Exercises stage-aware telemetry on <see cref="BacktestProgressEvent"/> and the
/// internal <see cref="StageTimer"/> that backs it.  Coverage:
///  • StageTimer transition semantics (durations, idempotency, cumulative).
///  • Engine progress events carry the expected stage enum, elapsed durations,
///    and monotonically non-decreasing <c>TotalElapsed</c> across the run.
/// </summary>
public sealed class StageTelemetryTests : IDisposable
{
    private readonly string _dataRoot;
    private readonly BacktestEngine _engine;

    public StageTelemetryTests()
    {
        _dataRoot = Path.Combine(Path.GetTempPath(), $"meridian-stage-telemetry-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_dataRoot);

        var catalog = new StorageCatalogService(_dataRoot, new StorageOptions());
        _engine = new BacktestEngine(NullLogger<BacktestEngine>.Instance, catalog);
    }

    public void Dispose()
    {
        if (Directory.Exists(_dataRoot))
            Directory.Delete(_dataRoot, recursive: true);
    }

    // ------------------------------------------------------------------ //
    //  StageTimer unit behaviour                                          //
    // ------------------------------------------------------------------ //

    [Fact]
    public void StageTimer_StartsInRequestedStage_WithRunningStopwatches()
    {
        var timer = new StageTimer(BacktestStage.ValidatingRequest);

        timer.CurrentStage.Should().Be(BacktestStage.ValidatingRequest);
        timer.TotalElapsed.Should().BeGreaterThanOrEqualTo(TimeSpan.Zero);
        timer.StageElapsed.Should().BeGreaterThanOrEqualTo(TimeSpan.Zero);
    }

    [Fact]
    public void StageTimer_Transition_ResetsStageElapsed_AndPreservesTotalElapsed()
    {
        var clock = new ManualClock();
        var timer = new StageTimer(BacktestStage.ValidatingRequest, clock.Now);
        clock.Advance(TimeSpan.FromMilliseconds(15));
        var totalBefore = timer.TotalElapsed;

        timer.Transition(BacktestStage.Replaying);

        timer.CurrentStage.Should().Be(BacktestStage.Replaying);
        timer.StageElapsed.Should().BeLessThan(totalBefore,
            "a fresh stage timer starts at (near) zero, while total elapsed keeps the pre-transition history");
        timer.TotalElapsed.Should().BeGreaterThanOrEqualTo(totalBefore,
            "TotalElapsed is monotonic across transitions");
    }

    [Fact]
    public void StageTimer_TransitionToSameStage_IsIdempotent()
    {
        var clock = new ManualClock();
        var timer = new StageTimer(BacktestStage.Replaying, clock.Now);
        clock.Advance(TimeSpan.FromMilliseconds(15));
        var stageBefore = timer.StageElapsed;

        timer.Transition(BacktestStage.Replaying);

        timer.CurrentStage.Should().Be(BacktestStage.Replaying);
        timer.StageElapsed.Should().BeGreaterThanOrEqualTo(stageBefore,
            "self-transition must not reset the active stage's elapsed time");
    }

    [Fact]
    public void StageTimer_Cumulative_IncludesActiveStage_AndAccumulatesAcrossTransitions()
    {
        var clock = new ManualClock();
        var timer = new StageTimer(BacktestStage.ValidatingRequest, clock.Now);
        clock.Advance(TimeSpan.FromMilliseconds(10));
        timer.Transition(BacktestStage.Replaying);
        clock.Advance(TimeSpan.FromMilliseconds(20));
        timer.Transition(BacktestStage.ComputingMetrics);
        clock.Advance(TimeSpan.FromMilliseconds(30));

        var snapshot = timer.Cumulative();

        snapshot.Keys.Should().Contain(new[]
        {
            BacktestStage.ValidatingRequest,
            BacktestStage.Replaying,
            BacktestStage.ComputingMetrics,
        });

        snapshot[BacktestStage.ValidatingRequest].Should().Be(TimeSpan.FromMilliseconds(10));
        snapshot[BacktestStage.Replaying].Should().Be(TimeSpan.FromMilliseconds(20));
        snapshot[BacktestStage.ComputingMetrics].Should().Be(TimeSpan.FromMilliseconds(30));

        snapshot.Values.Sum(t => t.Ticks).Should().Be(timer.TotalElapsed.Ticks,
            "cumulative stage time should match total elapsed when sampled from a deterministic clock");
    }

    [Fact]
    public void StageTimer_Stop_IsIdempotent_AndFreezesElapsedValues()
    {
        var clock = new ManualClock();
        var timer = new StageTimer(BacktestStage.Replaying, clock.Now);
        clock.Advance(TimeSpan.FromMilliseconds(10));
        timer.Stop();
        var total1 = timer.TotalElapsed;
        var stage1 = timer.StageElapsed;

        clock.Advance(TimeSpan.FromMilliseconds(10));
        timer.Stop();

        timer.TotalElapsed.Should().Be(total1);
        timer.StageElapsed.Should().Be(stage1);
    }

    // ------------------------------------------------------------------ //
    //  Engine-level stage emission                                        //
    // ------------------------------------------------------------------ //

    [Fact]
    public async Task RunAsync_EmitsCompletionEvent_WithStageCompleted_AndPositiveTotalElapsed()
    {
        WriteBarJsonl("AAPL", new DateOnly(2024, 1, 2), new DateOnly(2024, 1, 3), basePrice: 180m);

        var progress = new RecordingProgress();

        var request = new BacktestRequest(
            From: new DateOnly(2024, 1, 2),
            To: new DateOnly(2024, 1, 3),
            DataRoot: _dataRoot);

        await _engine.RunAsync(request, new TelemetryNoOpStrategy(), progress);

        BacktestProgressEvent completion;
        var events = progress.Snapshot();
        events.Should().NotBeEmpty("at least one progress event must be reported");
        completion = events[^1];

        completion.ProgressFraction.Should().Be(1.0);
        completion.Stage.Should().Be(BacktestStage.Completed,
            "the terminal event must report the Completed stage");
        completion.TotalElapsed.Should().BeGreaterThan(TimeSpan.Zero,
            "TotalElapsed on the completion event must reflect real wall-clock time");
        completion.Message.Should().Be("Complete");
    }

    [Fact]
    public async Task RunAsync_ProgressReports_TotalElapsedNonDecreasing_AndStagesInExpectedSet()
    {
        // Multi-day range forces AdvanceDays to emit per-day progress events,
        // giving us multiple samples to assert monotonicity.
        WriteBarJsonl("SPY", new DateOnly(2024, 1, 2), new DateOnly(2024, 1, 10), basePrice: 470m);

        var progress = new RecordingProgress();

        var request = new BacktestRequest(
            From: new DateOnly(2024, 1, 2),
            To: new DateOnly(2024, 1, 10),
            DataRoot: _dataRoot);

        await _engine.RunAsync(request, new TelemetryNoOpStrategy(), progress);

        var snapshot = progress.Snapshot();

        snapshot.Should().HaveCountGreaterThan(1, "multi-day replay should emit more than a single terminal event");

        var allowedStages = new[] { BacktestStage.Replaying, BacktestStage.Completed };
        snapshot.Select(e => e.Stage).Distinct().Should().OnlyContain(s => allowedStages.Contains(s),
            "the engine currently emits Replaying during the loop and Completed at the end");

        for (var i = 1; i < snapshot.Length; i++)
        {
            snapshot[i].TotalElapsed.Should().BeGreaterThanOrEqualTo(snapshot[i - 1].TotalElapsed,
                $"TotalElapsed must be non-decreasing across progress events (index {i})");
        }

        snapshot.Last().Stage.Should().Be(BacktestStage.Completed);
    }

    [Fact]
    public async Task RunAsync_EmptyUniverse_StillReportsNothing_ButDoesNotThrow()
    {
        // The empty-universe fast path exits without reporting progress, which is
        // the pre-existing contract. We assert the path continues to return a
        // well-formed result after the telemetry refactor.
        var progress = new RecordingProgress();

        var request = new BacktestRequest(
            From: new DateOnly(2024, 1, 2),
            To: new DateOnly(2024, 1, 3),
            DataRoot: _dataRoot);

        var result = await _engine.RunAsync(request, new TelemetryNoOpStrategy(), progress);

        result.Should().NotBeNull();
        result.TotalEventsProcessed.Should().Be(0);
        progress.Snapshot().Should().BeEmpty("the empty-universe fast path returns before progress reporting starts");
    }

    // ------------------------------------------------------------------ //
    //  Helpers                                                            //
    // ------------------------------------------------------------------ //

    private void WriteBarJsonl(string symbol, DateOnly from, DateOnly to, decimal basePrice, decimal dailyGain = 0m)
    {
        var symbolDir = Path.Combine(_dataRoot, symbol.ToUpperInvariant());
        Directory.CreateDirectory(symbolDir);
        var filePath = Path.Combine(symbolDir, $"{symbol}_bars_{from:yyyy-MM-dd}.jsonl");

        using var writer = new StreamWriter(filePath);
        var date = from;
        var seq = 1L;
        while (date <= to)
        {
            var open = basePrice + (date.DayNumber - from.DayNumber) * dailyGain;
            var high = open + 5m;
            var low = open - 5m;
            var close = open + dailyGain;

            var sequence = seq++;
            var bar = new HistoricalBar(
                Symbol: symbol,
                SessionDate: date,
                Open: open,
                High: high,
                Low: low,
                Close: close,
                Volume: 1_000_000L,
                Source: "test",
                SequenceNumber: sequence);

            var ts = bar.ToTimestampUtc();
            var evt = MarketEvent.HistoricalBar(ts, symbol, bar, bar.SequenceNumber, "test");

            writer.WriteLine(JsonSerializer.Serialize(evt, MarketDataJsonContext.HighPerformanceOptions));
            date = date.AddDays(1);
        }
    }

    private sealed class ManualClock
    {
        private TimeSpan _elapsed;

        public TimeSpan Now() => _elapsed;

        public void Advance(TimeSpan elapsed) => _elapsed += elapsed;
    }

    private sealed class RecordingProgress : IProgress<BacktestProgressEvent>
    {
        private readonly List<BacktestProgressEvent> _events = new();

        public void Report(BacktestProgressEvent value) => _events.Add(value);

        public BacktestProgressEvent[] Snapshot() => _events.ToArray();
    }
}

file sealed class TelemetryNoOpStrategy : IBacktestStrategy
{
    public string Name => "TelemetryNoOp";
    public void Initialize(IBacktestContext ctx) { }
    public void OnTrade(Trade trade, IBacktestContext ctx) { }
    public void OnQuote(BboQuotePayload quote, IBacktestContext ctx) { }
    public void OnBar(HistoricalBar bar, IBacktestContext ctx) { }
    public void OnOrderBook(LOBSnapshot snapshot, IBacktestContext ctx) { }
    public void OnOrderFill(FillEvent fill, IBacktestContext ctx) { }
    public void OnDayEnd(DateOnly date, IBacktestContext ctx) { }
    public void OnFinished(IBacktestContext ctx) { }
}
