using System.Collections.Concurrent;
using FluentAssertions;
using Meridian.Contracts.Domain.Models;
using Meridian.Core.Config;
using Meridian.Infrastructure.Adapters.Core;
using Meridian.Storage.Archival;
using Meridian.Storage.Backfill;
using Meridian.Testing;

namespace Meridian.Tests.Backfill;

/// <summary>
/// Regression coverage for queued-backfill defects: job progress mutated by parallel request
/// workers without synchronization, the quadratic <c>GapsFilled</c> count, and daily bars being
/// fetched and stored under a non-daily granularity.
/// </summary>
public sealed class BackfillJobProgressAndGranularityTests
{
    private static readonly DateOnly RangeStart = new(2026, 1, 5);   // Monday
    private static readonly DateOnly RangeEnd = new(2026, 3, 27);    // Friday, 12 full weeks later
    private const int WeekdaysInRange = 60;
    private const int RequestsPerSymbol = 12;

    [Fact]
    public async Task UpdateJobProgressAsync_ParallelRequestCompletions_KeepsEveryCountAndCompletesOnce()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        using var artifacts = TestArtifactDirectory.Create(nameof(
            UpdateJobProgressAsync_ParallelRequestCompletions_KeepsEveryCountAndCompletesOnce));
        using var rateLimits = new ProviderRateLimitTracker();
        using var queue = new BackfillRequestQueue(rateLimits);
        using var manager = CreateManager(artifacts.RootPath, queue);
        var symbols = Enumerable.Range(0, 64).Select(i => $"SYM{i:D2}").ToArray();
        var providers = new[] { "alpha", "beta", "gamma" };
        var terminalTransitions = 0;
        manager.OnJobStatusChanged += (job, _) =>
        {
            if (job.IsComplete)
                Interlocked.Increment(ref terminalTransitions);
        };

        var job = await manager.CreateJobAsync(
            "parallel-progress", symbols, RangeStart, RangeEnd,
            options: WeeklyBatchFullBackfill(), ct: timeout.Token);
        await manager.StartJobAsync(job.JobId, timeout.Token);
        var requests = await queue.GetJobRequestsAsync(job.JobId, timeout.Token);
        requests.Should().HaveCount(symbols.Length * RequestsPerSymbol);

        for (var index = 0; index < requests.Count; index++)
        {
            requests[index].Status = BackfillRequestStatus.Completed;
            requests[index].AssignedProvider = providers[index % providers.Length];
            requests[index].BarsRetrieved = 5;
        }

        // Every request worker reports completion at once, as MaxConcurrentRequests > 1 allows.
        using var start = new ManualResetEventSlim(false);
        var updates = requests
            .Select(request => Task.Run(async () =>
            {
                start.Wait(timeout.Token);
                await manager.UpdateJobProgressAsync(request, timeout.Token);
            }, timeout.Token))
            .ToArray();
        start.Set();
        await Task.WhenAll(updates);

        var stats = job.Statistics;
        stats.SuccessfulRequests.Should().Be(requests.Count);
        stats.TotalRequestsMade.Should().Be(requests.Count);
        stats.FailedRequests.Should().Be(0);
        stats.TotalBarsRetrieved.Should().Be(requests.Count * 5L);
        stats.RequestsByProvider.Values.Sum().Should().Be(requests.Count);
        stats.BarsByProvider.Values.Sum().Should().Be(requests.Count * 5);
        stats.GapsFilled.Should().Be(symbols.Length * WeekdaysInRange);
        job.SymbolProgress.Values.Should().OnlyContain(progress =>
            progress.Status == SymbolBackfillStatus.Completed &&
            progress.CompletedRequests == RequestsPerSymbol &&
            progress.FilledDates.Count == WeekdaysInRange);
        job.Status.Should().Be(BackfillJobStatus.Completed);
        terminalTransitions.Should().Be(1, "only one worker performs the terminal transition");
    }

    [Fact]
    public async Task UpdateJobProgressAsync_MultipleChunksPerSymbol_CountsEachFilledDateOnce()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        using var artifacts = TestArtifactDirectory.Create(nameof(
            UpdateJobProgressAsync_MultipleChunksPerSymbol_CountsEachFilledDateOnce));
        using var rateLimits = new ProviderRateLimitTracker();
        using var queue = new BackfillRequestQueue(rateLimits);
        using var manager = CreateManager(artifacts.RootPath, queue);
        var job = await manager.CreateJobAsync(
            "gaps-filled", ["SPY"], RangeStart, RangeEnd,
            options: WeeklyBatchFullBackfill(), ct: timeout.Token);
        await manager.StartJobAsync(job.JobId, timeout.Token);
        var requests = await queue.GetJobRequestsAsync(job.JobId, timeout.Token);
        requests.Should().HaveCount(RequestsPerSymbol);

        foreach (var request in requests)
        {
            request.Status = BackfillRequestStatus.Completed;
            await manager.UpdateJobProgressAsync(request, timeout.Token);
        }

        // The previous code added the symbol's running FilledDates.Count per request:
        // 5 + 10 + ... + 60 = 390 for these twelve weekly chunks instead of 60.
        job.Statistics.GapsFilled.Should().Be(WeekdaysInRange);
    }

    [Theory]
    [InlineData(DataGranularity.Minute1)]
    [InlineData(DataGranularity.Hour1)]
    [InlineData(DataGranularity.Weekly)]
    public async Task Worker_NonDailyGranularity_FailsRequestWithoutFetchingOrWritingDailyBars(
        DataGranularity granularity)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        using var artifacts = TestArtifactDirectory.Create(
            $"{nameof(Worker_NonDailyGranularity_FailsRequestWithoutFetchingOrWritingDailyBars)}_{granularity}");
        var provider = new DailyBarProvider();
        var services = CreateServices(artifacts.RootPath, provider);
        var request = CreateRequest(provider.Name, granularity);

        try
        {
            await services.RequestQueue.EnqueueAsync(request, timeout.Token);
            services.Worker.Start();
            await WaitForTerminalStatusAsync(request, timeout.Token);
            await services.Worker.StopAsync(timeout.Token);

            request.Status.Should().Be(BackfillRequestStatus.Failed);
            request.ErrorMessage.Should().Contain("only supports daily bars");
            provider.FetchCount.Should().Be(0, "daily bars must not be fetched for another granularity");
            Directory.EnumerateFiles(artifacts.RootPath, "*.jsonl", SearchOption.AllDirectories)
                .Should().BeEmpty("daily bars must not be stored under a non-daily file prefix");
        }
        finally
        {
            services.Dispose();
        }
    }

    [Fact]
    public async Task Worker_DailyGranularity_StillFetchesAndWritesDailyBarFiles()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        using var artifacts = TestArtifactDirectory.Create(nameof(
            Worker_DailyGranularity_StillFetchesAndWritesDailyBarFiles));
        var provider = new DailyBarProvider();
        var services = CreateServices(artifacts.RootPath, provider);
        var request = CreateRequest(provider.Name, DataGranularity.Daily);

        try
        {
            await services.RequestQueue.EnqueueAsync(request, timeout.Token);
            services.Worker.Start();
            await WaitForTerminalStatusAsync(request, timeout.Token);
            await services.Worker.StopAsync(timeout.Token);

            request.Status.Should().Be(BackfillRequestStatus.Completed);
            provider.FetchCount.Should().Be(1);
            Directory.EnumerateFiles(artifacts.RootPath, "*.jsonl", SearchOption.AllDirectories)
                .Select(Path.GetFileName)
                .Should().BeEquivalentTo("bar_daily_2026-07-01.jsonl", "bar_daily_2026-07-02.jsonl");
        }
        finally
        {
            services.Dispose();
        }
    }

    [Fact]
    public async Task GapAnalyzer_FifteenMinuteBarFiles_AreNotCountedAsFiveMinuteCoverage()
    {
        using var artifacts = TestArtifactDirectory.Create(nameof(
            GapAnalyzer_FifteenMinuteBarFiles_AreNotCountedAsFiveMinuteCoverage));
        var session = new DateOnly(2026, 7, 1);
        var writer = new JsonlBackfillBarWriter(artifacts.RootPath);
        await writer.WriteAsync(
            DataGranularity.Minute15,
            [new HistoricalBar("SPY", session, 100, 105, 99, 103, 500, "alpaca", 1)],
            "composite");
        var analyzer = new DataGapAnalyzer(artifacts.RootPath);

        var fifteenMinute = await analyzer.AnalyzeSymbolGapsAsync("SPY", session, session, DataGranularity.Minute15);
        var fiveMinute = await analyzer.AnalyzeSymbolGapsAsync("SPY", session, session, DataGranularity.Minute5);

        // "bar_15min" contains "5min"; the classifier used to test the shorter token first.
        fifteenMinute.HasGaps.Should().BeFalse();
        fiveMinute.GapDates.Should().Equal(session);
    }

    private static BackfillJobOptions WeeklyBatchFullBackfill()
        => new() { SkipExistingData = false, FillGapsOnly = false, BatchSizeDays = 5 };

    private static BackfillJobManager CreateManager(string dataRoot, BackfillRequestQueue queue)
        => new(
            new DataGapAnalyzer(dataRoot),
            queue,
            Path.Combine(dataRoot, "_jobs"),
            // Persistence still serializes the live job; only the disk write is skipped.
            static (_, _, _) => Task.CompletedTask);

    private static BackfillServices CreateServices(string dataRoot, IHistoricalDataProvider provider)
        => new BackfillServiceFactory(new AtomicFileWriterAdapter(), root => new JsonlBackfillBarWriter(root))
            .CreateServices(
                new AppConfig(DataRoot: dataRoot),
                new BackfillConfig(
                    EnableSymbolResolution: false,
                    EnableRateLimitRotation: false,
                    Jobs: new BackfillJobsConfig(
                        PersistJobs: false,
                        MaxConcurrentRequests: 1,
                        MaxConcurrentPerProvider: 1,
                        WorkerErrorRetryDelayMs: 10)),
                dataRoot,
                [provider]);

    private static BackfillRequest CreateRequest(string providerName, DataGranularity granularity)
        => new()
        {
            JobId = "granularity-scenario",
            Symbol = "SPY",
            FromDate = new DateOnly(2026, 7, 1),
            ToDate = new DateOnly(2026, 7, 2),
            Granularity = granularity,
            PreferredProviders = [providerName],
            MaxRetries = 0
        };

    private static async Task WaitForTerminalStatusAsync(BackfillRequest request, CancellationToken ct)
    {
        while (request.Status is not (BackfillRequestStatus.Completed or BackfillRequestStatus.Failed))
            await Task.Delay(10, ct);
    }

    private sealed class DailyBarProvider : IHistoricalDataProvider
    {
        private int _fetchCount;

        public string Name => "daily-only";
        public string DisplayName => "Daily-only historical provider";
        public string Description => "Returns one daily bar per requested weekday.";
        public int FetchCount => Volatile.Read(ref _fetchCount);

        public Task<IReadOnlyList<HistoricalBar>> GetDailyBarsAsync(
            string symbol,
            DateOnly? from,
            DateOnly? to,
            CancellationToken ct = default)
        {
            Interlocked.Increment(ref _fetchCount);
            var bars = new List<HistoricalBar>();
            for (var date = from!.Value; date <= to!.Value; date = date.AddDays(1))
            {
                if (date.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday)
                    continue;
                bars.Add(new HistoricalBar(symbol, date, 100, 105, 99, 103, 500, Name, bars.Count + 1));
            }

            return Task.FromResult<IReadOnlyList<HistoricalBar>>(bars);
        }

        public void Dispose()
        {
        }
    }
}
