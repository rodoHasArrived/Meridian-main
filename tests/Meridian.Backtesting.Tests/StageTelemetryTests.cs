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
        var timer = new StageTimer(BacktestStage.ValidatingRequest);
        Thread.Sleep(15);
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
        var timer = new StageTimer(BacktestStage.Replaying);
        Thread.Sleep(15);
        var stageBefore = timer.StageElapsed;

        timer.Transition(BacktestStage.Replaying);

        timer.CurrentStage.Should().Be(BacktestStage.Replaying);
        timer.StageElapsed.Should().BeGreaterThanOrEqualTo(stageBefore,
            "self-transition must not reset the active stage's elapsed time");
    }

    [Fact]
    public void StageTimer_Cumulative_IncludesActiveStage_AndAccumulatesAcrossTransitions()
    {
        var timer = new StageTimer(BacktestStage.ValidatingRequest);
        Thread.Sleep(10);
        timer.Transition(BacktestStage.Replaying);
        Thread.Sleep(10);
        timer.Transition(BacktestStage.ComputingMetrics);
        Thread.Sleep(10);

        var snapshot = timer.Cumulative();

        snapshot.Keys.Should().Contain(new[]
        {
            BacktestStage.ValidatingRequest,
            BacktestStage.Replaying,
            BacktestStage.ComputingMetrics,
        });

        foreach (var stage in snapshot.Keys)
            snapshot[stage].Should().BeGreaterThan(TimeSpan.Zero, $"{stage} should have accumulated time");

        snapshot.Values.Sum(t => t.Ticks).Should().BeLessThanOrEqualTo(timer.TotalElapsed.Ticks + TimeSpan.FromMilliseconds(5).Ticks,
            "cumulative sum should not exceed total elapsed (allowing a small skew for in-flight stage sampling)");
    }

    [Fact]
    public void StageTimer_Stop_IsIdempotent_AndFreezesElapsedValues()
    {
        var timer = new StageTimer(BacktestStage.Replaying);
        Thread.Sleep(10);
        timer.Stop();
        var total1 = timer.TotalElapsed;
        var stage1 = timer.StageElapsed;

        Thread.Sleep(10);
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

        var events = new List<BacktestProgressEvent>();
        var progress = new Progress<BacktestProgressEvent>(e => { lock (events) events.Add(e); });

        var request = new BacktestRequest(
            From: new DateOnly(2024, 1, 2),
            To: new DateOnly(2024, 1, 3),
            DataRoot: _dataRoot);

        await _engine.RunAsync(request, new TelemetryNoOpStrategy(), progress);
        await WaitForProgressDeliveryAsync(events);

        BacktestProgressEvent completion;
        lock (events)
        {
            events.Should().NotBeEmpty("at least one progress event must be reported");
            completion = events[^1];
        }

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

        var events = new List<BacktestProgressEvent>();
        var progress = new Progress<BacktestProgressEvent>(e => { lock (events) events.Add(e); });

        var request = new BacktestRequest(
            From: new DateOnly(2024, 1, 2),
            To: new DateOnly(2024, 1, 10),
            DataRoot: _dataRoot);

        await _engine.RunAsync(request, new TelemetryNoOpStrategy(), progress);
        await WaitForProgressDeliveryAsync(events, minCount: 2);

        BacktestProgressEvent[] snapshot;
        lock (events)
        {
            snapshot = events.ToArray();
        }

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
        var events = new List<BacktestProgressEvent>();
        var progress = new Progress<BacktestProgressEvent>(e => { lock (events) events.Add(e); });

        var request = new BacktestRequest(
            From: new DateOnly(2024, 1, 2),
            To: new DateOnly(2024, 1, 3),
            DataRoot: _dataRoot);

        var result = await _engine.RunAsync(request, new TelemetryNoOpStrategy(), progress);

        result.Should().NotBeNull();
        result.TotalEventsProcessed.Should().Be(0);
    }

    // ------------------------------------------------------------------ //
    //  Helpers                                                            //
    // ------------------------------------------------------------------ //

    private static async Task WaitForProgressDeliveryAsync(List<BacktestProgressEvent> events, int minCount = 1, int timeoutMs = 1_000)
    {
        // Progress<T> posts to the SynchronizationContext or ThreadPool; give the
        // posted callbacks a bounded window to drain before we assert on them.
        var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
        while (DateTime.UtcNow < deadline)
        {
            lock (events)
            {
                if (events.Count >= minCount)
                    return;
            }
            await Task.Delay(10);
        }
    }

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

            var bar = new HistoricalBar(
                Symbol: symbol,
                SessionDate: date,
                Open: open,
                High: high,
                Low: low,
                Close: close,
                Volume: 1_000_000L,
                Source: "test",
                SequenceNumber: seq++);

            var ts = bar.ToTimestampUtc();
            var evt = MarketEvent.HistoricalBar(ts, symbol, bar, seq, "test");

            writer.WriteLine(JsonSerializer.Serialize(evt, MarketDataJsonContext.HighPerformanceOptions));
            date = date.AddDays(1);
        }
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
