using System.Collections.Concurrent;
using FluentAssertions;
using Meridian.Application.Backfill;
using Meridian.Application.Pipeline;
using Meridian.Contracts.Domain.Models;
using Meridian.Domain.Events;
using Meridian.Infrastructure.Adapters.Core;
using Meridian.Storage.Backfill;
using Meridian.Storage.Interfaces;
using Xunit;
using AppBackfillRequest = Meridian.Application.Backfill.BackfillRequest;

namespace Meridian.Tests.Application.Backfill;

/// <summary>
/// Resume may skip only dates recorded as durably written by the same provider and feed. A
/// checkpoint keyed by symbol and granularity alone let a wider request, or a request for another
/// provider, skip dates that were never fetched.
/// </summary>
public sealed class HistoricalBackfillCoverageResumeTests : IDisposable
{
    private readonly string _dataRoot = Path.Combine(
        Path.GetTempPath(), "meridian-backfill-coverage", Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_dataRoot))
            Directory.Delete(_dataRoot, recursive: true);
    }

    [Fact]
    public async Task Resume_WiderRange_FetchesDatesBeforeAndAfterTheRecordedCoverage()
    {
        var store = new BackfillStatusStore(_dataRoot);
        var provider = new WeekdayBarProvider("alpha");
        var service = new HistoricalBackfillService([provider], checkpointStore: store);

        // First run: Monday 4 March to Friday 15 March 2024.
        (await RunAsync(service, "alpha", new DateOnly(2024, 3, 4), new DateOnly(2024, 3, 15))).Success.Should().BeTrue();
        provider.Calls.Clear();

        // Wider request: the old symbol/granularity checkpoint (15 March) would have fetched only
        // from 16 March and silently skipped 5 February to 3 March.
        var wider = await RunAsync(service, "alpha", new DateOnly(2024, 2, 5), new DateOnly(2024, 4, 5), resume: true);

        wider.Success.Should().BeTrue();
        provider.Calls.OrderBy(call => call.From).Should().Equal(
            (new DateOnly(2024, 2, 5), new DateOnly(2024, 3, 3)),
            (new DateOnly(2024, 3, 16), new DateOnly(2024, 4, 5)));
        wider.BarsWritten.Should().Be(20 + 15, "20 sessions before and 15 sessions after the recorded coverage");
        store.TryReadSymbolCoverage(DataGranularity.Daily, "alpha", "default")!["SPY"].Should().ContainSingle()
            .Which.Should().Be(new BackfillCoverageRange(new DateOnly(2024, 2, 5), new DateOnly(2024, 4, 5), 45));

        // The same request again is fully covered and skipped without a provider call.
        provider.Calls.Clear();
        var repeat = await RunAsync(service, "alpha", new DateOnly(2024, 2, 5), new DateOnly(2024, 4, 5), resume: true);
        provider.Calls.Should().BeEmpty();
        repeat.SkippedSymbols.Should().Contain("SPY");
        var signal = repeat.SymbolValidationSignals.Should().ContainSingle().Which;
        signal.Status.Should().Be("Pass");
        signal.CheckpointBarsWritten.Should().Be(45);
    }

    [Fact]
    public async Task Resume_WithAnotherProvider_DoesNotReuseTheFirstProvidersCoverage()
    {
        var store = new BackfillStatusStore(_dataRoot);
        var alpha = new WeekdayBarProvider("alpha");
        var beta = new WeekdayBarProvider("beta");
        var service = new HistoricalBackfillService([alpha, beta], checkpointStore: store);
        var from = new DateOnly(2024, 3, 4);
        var to = new DateOnly(2024, 3, 15);

        await RunAsync(service, "alpha", from, to);
        var viaBeta = await RunAsync(service, "beta", from, to, resume: true);

        beta.Calls.Should().Equal((from, to));
        viaBeta.SkippedSymbols.Should().BeEmpty("beta never wrote these dates");
        viaBeta.BarsWritten.Should().Be(10);

        // Each provider's own coverage still stands.
        alpha.Calls.Clear();
        beta.Calls.Clear();
        (await RunAsync(service, "alpha", from, to, resume: true)).SkippedSymbols.Should().Contain("SPY");
        (await RunAsync(service, "beta", from, to, resume: true)).SkippedSymbols.Should().Contain("SPY");
        alpha.Calls.Should().BeEmpty();
        beta.Calls.Should().BeEmpty();
    }

    [Fact]
    public async Task Resume_WithAnotherFeedOfTheSameProvider_FetchesAgain()
    {
        var store = new BackfillStatusStore(_dataRoot);
        var from = new DateOnly(2024, 3, 4);
        var to = new DateOnly(2024, 3, 15);
        await RunAsync(new HistoricalBackfillService([new WeekdayBarProvider("alpha", feed: "iex:all")], checkpointStore: store), "alpha", from, to);

        var sip = new WeekdayBarProvider("alpha", feed: "sip:all");
        var viaSip = await RunAsync(new HistoricalBackfillService([sip], checkpointStore: store), "alpha", from, to, resume: true);

        sip.Calls.Should().Equal((from, to));
        viaSip.SkippedSymbols.Should().BeEmpty();
        store.TryReadSymbolCoverage(DataGranularity.Daily, "alpha", "iex:all").Should().ContainKey("SPY");
        store.TryReadSymbolCoverage(DataGranularity.Daily, "alpha", "sip:all").Should().ContainKey("SPY");
    }

    [Fact]
    public async Task Resume_WithInternalGaps_FetchesEveryUncoveredRange()
    {
        var store = new BackfillStatusStore(_dataRoot);
        await store.WriteSymbolCoverageAsync(DataGranularity.Daily, "alpha", "default",
        [
            new BackfillCoverageUpdate("SPY",
            [
                new BackfillCoverageRange(new DateOnly(2024, 1, 2), new DateOnly(2024, 1, 12), 9),
                new BackfillCoverageRange(new DateOnly(2024, 2, 5), new DateOnly(2024, 2, 16), 10)
            ], ["alpha"])
        ]);
        var provider = new WeekdayBarProvider("alpha");
        var service = new HistoricalBackfillService([provider], checkpointStore: store);

        var result = await RunAsync(service, "alpha", new DateOnly(2024, 1, 2), new DateOnly(2024, 3, 1), resume: true);

        result.Success.Should().BeTrue();
        provider.Calls.OrderBy(call => call.From).Should().Equal(
            (new DateOnly(2024, 1, 13), new DateOnly(2024, 2, 4)),
            (new DateOnly(2024, 2, 17), new DateOnly(2024, 3, 1)));
        var signal = result.SymbolValidationSignals.Should().ContainSingle().Which;
        signal.EffectiveFrom.Should().Be(new DateOnly(2024, 1, 13));
        signal.EffectiveTo.Should().Be(new DateOnly(2024, 3, 1));
        store.TryReadSymbolCoverage(DataGranularity.Daily, "alpha", "default")!["SPY"].Should().ContainSingle()
            .Which.Should().Be(new BackfillCoverageRange(new DateOnly(2024, 1, 2), new DateOnly(2024, 3, 1), 9 + 15 + 10 + 10));
    }

    [Fact]
    public async Task Resume_WhenOneGapFails_RecordsNoCoverageForThatSymbol()
    {
        var store = new BackfillStatusStore(_dataRoot);
        await store.WriteSymbolCoverageAsync(DataGranularity.Daily, "alpha", "default",
            [new BackfillCoverageUpdate("SPY", [new BackfillCoverageRange(new DateOnly(2024, 2, 5), new DateOnly(2024, 2, 16), 10)], ["alpha"])]);
        var provider = new WeekdayBarProvider("alpha") { FailFrom = new DateOnly(2024, 2, 17) };
        var service = new HistoricalBackfillService([provider], checkpointStore: store);

        var result = await RunAsync(service, "alpha", new DateOnly(2024, 1, 2), new DateOnly(2024, 3, 1), resume: true);

        result.Success.Should().BeFalse();
        store.TryReadSymbolCoverage(DataGranularity.Daily, "alpha", "default")!["SPY"].Should().ContainSingle()
            .Which.Should().Be(new BackfillCoverageRange(new DateOnly(2024, 2, 5), new DateOnly(2024, 2, 16), 10),
                "a symbol whose fetch failed part-way keeps only the coverage it already had");
    }

    [Fact]
    public async Task Resume_WithOnlyALegacyCheckpoint_FetchesTheWholeRequestedRange()
    {
        var store = new BackfillStatusStore(_dataRoot);
        var from = new DateOnly(2024, 3, 4);
        var to = new DateOnly(2024, 3, 15);
        // A pre-existing date-only checkpoint claims the symbol is complete through the range end.
        await store.WriteSymbolCheckpointAsync("SPY", DataGranularity.Daily, to, barsWritten: 10);
        var provider = new WeekdayBarProvider("alpha");
        var service = new HistoricalBackfillService([provider], checkpointStore: store);

        var result = await RunAsync(service, "alpha", from, to, resume: true);

        provider.Calls.Should().Equal((from, to));
        result.SkippedSymbols.Should().BeEmpty("a checkpoint without provider, feed and range start is not evidence");
        result.BarsWritten.Should().Be(10);
    }

    [Fact]
    public async Task Resume_AfterAFailedFlush_FetchesAgainAndRecordsCoverageOnlyAfterTheFlush()
    {
        var store = new BackfillStatusStore(_dataRoot);
        var provider = new WeekdayBarProvider("alpha");
        var service = new HistoricalBackfillService([provider], checkpointStore: store);
        var from = new DateOnly(2024, 3, 4);
        var to = new DateOnly(2024, 3, 15);

        var failingSink = new ProbeSink { OnFlush = static () => throw new IOException("disk full") };
        await using (var failingPipeline = new EventPipeline(failingSink, capacity: 1_000, enablePeriodicFlush: false))
        {
            var failed = await service.RunAsync(new AppBackfillRequest("alpha", ["SPY"], from, to), failingPipeline);
            failed.Success.Should().BeFalse();
            store.TryReadSymbolCoverage(DataGranularity.Daily, "alpha", "default").Should().BeNull(
                "bars that never reached storage must not be recorded as covered");
            failingSink.OnFlush = null; // let pipeline disposal flush cleanly
        }

        provider.Calls.Clear();
        var sink = new ProbeSink();
        sink.OnFlush = () => sink.CoveragePresentAtFirstFlush ??= store.TryReadSymbolCoverage(DataGranularity.Daily, "alpha", "default") is not null;
        await using var pipeline = new EventPipeline(sink, capacity: 1_000, enablePeriodicFlush: false);
        var restarted = await service.RunAsync(new AppBackfillRequest("alpha", ["SPY"], from, to, ResumeFromCheckpoint: true), pipeline);

        restarted.Success.Should().BeTrue();
        provider.Calls.Should().Equal((from, to));
        restarted.SkippedSymbols.Should().BeEmpty();
        sink.CoveragePresentAtFirstFlush.Should().BeFalse("coverage is written only after the flush succeeds");
        store.TryReadSymbolCoverage(DataGranularity.Daily, "alpha", "default")!["SPY"].Should().ContainSingle()
            .Which.Should().Be(new BackfillCoverageRange(from, to, 10));
    }

    [Fact]
    public async Task Resume_RequestStartingOnANonSessionDay_IsSkippedOnceCovered()
    {
        var store = new BackfillStatusStore(_dataRoot);
        var provider = new WeekdayBarProvider("alpha");
        var service = new HistoricalBackfillService([provider], checkpointStore: store);
        // Saturday 2 March to Friday 15 March: the first bar is Monday 4 March.
        var from = new DateOnly(2024, 3, 2);
        var to = new DateOnly(2024, 3, 15);

        await RunAsync(service, "alpha", from, to);
        provider.Calls.Clear();
        var repeat = await RunAsync(service, "alpha", from, to, resume: true);

        provider.Calls.Should().BeEmpty();
        repeat.SkippedSymbols.Should().Contain("SPY");
    }

    [Fact]
    public async Task Resume_CoverageNeverExtendsPastTheNewestWrittenBar()
    {
        var store = new BackfillStatusStore(_dataRoot);
        // The provider has published nothing after Wednesday 13 March yet.
        var provider = new WeekdayBarProvider("alpha") { NewestAvailable = new DateOnly(2024, 3, 13) };
        var service = new HistoricalBackfillService([provider], checkpointStore: store);
        var from = new DateOnly(2024, 3, 4);
        var to = new DateOnly(2024, 3, 15);

        await RunAsync(service, "alpha", from, to);
        provider.NewestAvailable = null;
        provider.Calls.Clear();
        var resumed = await RunAsync(service, "alpha", from, to, resume: true);

        provider.Calls.Should().Equal((new DateOnly(2024, 3, 14), to));
        resumed.BarsWritten.Should().Be(2);
    }

    private async Task<Meridian.Contracts.Backfill.BackfillResult> RunAsync(
        HistoricalBackfillService service, string provider, DateOnly from, DateOnly to, bool resume = false)
    {
        await using var pipeline = new EventPipeline(new ProbeSink(), capacity: 10_000, enablePeriodicFlush: false);
        return await service.RunAsync(new AppBackfillRequest(provider, ["SPY"], from, to, ResumeFromCheckpoint: resume), pipeline);
    }

    private sealed class ProbeSink : IStorageSink
    {
        public Action? OnFlush { get; set; }
        public bool? CoveragePresentAtFirstFlush { get; set; }

        public ValueTask AppendAsync(MarketEvent evt, CancellationToken ct = default) => ValueTask.CompletedTask;

        public Task FlushAsync(CancellationToken ct = default)
        {
            OnFlush?.Invoke();
            return Task.CompletedTask;
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    /// <summary>Synthetic provider: one bar per weekday in the requested range; every call is recorded.</summary>
    private sealed class WeekdayBarProvider(string name, string feed = "default") : IHistoricalDataProvider
    {
        public string Name => name;
        public string DisplayName => name;
        public string Description => string.Empty;
        public string CoverageFeed => feed;
        public ConcurrentQueue<(DateOnly? From, DateOnly? To)> Calls { get; } = new();
        public DateOnly? FailFrom { get; set; }
        public DateOnly? NewestAvailable { get; set; }

        public Task<IReadOnlyList<HistoricalBar>> GetDailyBarsAsync(
            string symbol, DateOnly? from, DateOnly? to, CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            Calls.Enqueue((from, to));
            if (FailFrom is { } failFrom && from == failFrom)
                throw new InvalidOperationException("simulated provider interruption");

            var bars = new List<HistoricalBar>();
            var end = NewestAvailable is { } newest && newest < to!.Value ? newest : to!.Value;
            for (var date = from!.Value; date <= end; date = date.AddDays(1))
            {
                if (date.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday)
                    continue;
                bars.Add(new HistoricalBar(symbol, date, 100m, 101m, 99m, 100.5m, 1_000, name));
            }

            return Task.FromResult<IReadOnlyList<HistoricalBar>>(bars);
        }
    }
}
