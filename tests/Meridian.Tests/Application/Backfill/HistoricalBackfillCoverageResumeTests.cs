using System.Collections.Concurrent;
using System.Globalization;
using FluentAssertions;
using Meridian.Application.Backfill;
using Meridian.Application.Pipeline;
using Meridian.Contracts.Backfill;
using Meridian.Contracts.Domain.Models;
using Meridian.Contracts.Operations;
using Meridian.Domain.Events;
using Meridian.Infrastructure.Adapters.Core;
using Meridian.Storage;
using Meridian.Storage.Backfill;
using Meridian.Storage.Interfaces;
using Meridian.Storage.Policies;
using Meridian.Storage.Replay;
using Meridian.Storage.Sinks;
using Serilog;
using Serilog.Core;
using Serilog.Events;
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
    [Fact]
    public async Task Repair_receipt_pins_quality_flags_before_row_stamp_and_detects_metadata_tampering()
    {
        var from = new DateOnly(2024, 3, 4);
        var to = from.AddDays(2);
        var store = new BackfillStatusStore(_dataRoot);
        var provider = new WeekdayBarProvider("alpha");
        var assessmentTime = DateTimeOffset.UtcNow;
        var service = new HistoricalBackfillService([provider], checkpointStore: store)
        {
            ResolveSessionsAsync = WeekdaysOnlyAsync,
            CaptureInputEvidence = _ => new(QualityScore: .4, QualityThreshold: .85,
                QualityAssessedAt: assessmentTime, AssessmentFrom: from, AssessmentTo: to)
        };
        var options = new StorageOptions { RootPath = _dataRoot };
        await using var sink = new JsonlStorageSink(options, new JsonlStoragePolicy(options));
        await using var pipeline = new EventPipeline(sink, capacity: 1000, enablePeriodicFlush: false);
        var result = await service.RunAsync(new AppBackfillRequest("alpha", ["SPY"], from, to)
        { GapRepairReference = "retained-gap-operation" }, pipeline);
        var receipt = result.BatchReceipts.Should().ContainSingle().Which;
        var retained = (await store.Batches.ReadAsync(receipt.RevisionId))!;

        result.Success.Should().BeTrue();
        receipt.InputEvidence!.Repaired.Should().BeTrue();
        receipt.InputEvidence.GapFilled.Should().BeTrue();
        receipt.InputEvidence.RepairReceiptId.Should().Be("retained-gap-operation");
        receipt.InputEvidence.QualityScore.Should().Be(.4);
        receipt.InputEvidence.QualityThreshold.Should().Be(.85);
        receipt.InputEvidence.QualityAssessedAt.Should().Be(assessmentTime);
        await foreach (var row in new JsonlReplayer(_dataRoot).ReadEventsAsync())
            row.BackfillRevisionId.Should().Be(receipt.RevisionId);
        foreach (var changedEvidence in new[]
        {
            retained.Receipt.InputEvidence! with { Repaired = false },
            retained.Receipt.InputEvidence! with { GapFilled = false },
            retained.Receipt.InputEvidence! with { RepairReceiptId = "different-operation" },
            retained.Receipt.InputEvidence! with { QualityScore = .99 }
        })
        {
            var changed = retained with { Receipt = retained.Receipt with { InputEvidence = changedEvidence } };
            var validate = () => changed.Validate();
            validate.Should().Throw<InvalidDataException>();
        }
    }

    private readonly string _dataRoot = Path.Combine(
        Path.GetTempPath(), "meridian-backfill-coverage", Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_dataRoot))
            Directory.Delete(_dataRoot, recursive: true);
    }

    [Fact]
    public async Task ResumeAndStaleLogs_NeutralizeTextAndRenderInvariantDatesWithoutChangingSymbols()
    {
        const string symbol = "SPY\r\nforged";
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var coveredDay = today.AddDays(-30);
        var store = new BackfillStatusStore(_dataRoot);
        await store.WriteSymbolCoverageAsync(DataGranularity.Daily, "alpha", "default",
            [new BackfillCoverageUpdate(symbol, [new BackfillCoverageRange(coveredDay, coveredDay, 1)], ["alpha"])]);
        var provider = new WeekdayBarProvider("alpha", displayName: "Alpha\r\nforged")
        {
            NewestAvailable = today.AddDays(-BackfillBarValidation.DefaultStaleToleranceDays - 5)
        };
        var collector = new LogCollector();
        using var logger = new LoggerConfiguration().WriteTo.Sink(collector).CreateLogger();
        var service = new HistoricalBackfillService([provider], logger: logger, checkpointStore: store)
        {
            ResolveSessionsAsync = WeekdaysOnlyAsync
        };
        await using var pipeline = new EventPipeline(new ProbeSink(), capacity: 10_000, enablePeriodicFlush: false);

        var result = await service.RunAsync(new AppBackfillRequest("alpha", [symbol], coveredDay, today,
            ResumeFromCheckpoint: true), pipeline);

        result.BarsWritten.Should().BeGreaterThan(0);
        provider.RequestedSymbols.Should().OnlyContain(value => value == symbol);
        var resume = collector.Events.Should().ContainSingle(entry =>
            entry.MessageTemplate.Text.StartsWith("Resuming ")).Which;
        var stale = collector.Events.Should().ContainSingle(entry =>
            entry.MessageTemplate.Text.StartsWith("Stale backfill ")).Which;
        ((ScalarValue)resume.Properties["ResumeFrom"]).Value.Should().Be(coveredDay.AddDays(1).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
        ((ScalarValue)stale.Properties["ExpectedThrough"]).Value.Should().Be(today.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
        foreach (var entry in new[] { resume, stale })
        {
            ((ScalarValue)entry.Properties["Symbol"]).Value.Should().Be("SPY__forged");
            ((ScalarValue)entry.Properties["Provider"]).Value.Should().Be("Alpha__forged");
            entry.RenderMessage().Should().NotContain("\r").And.NotContain("\n");
        }
    }

    [Fact]
    public async Task Resume_WiderRange_FetchesDatesBeforeAndAfterTheRecordedCoverage()
    {
        var store = new BackfillStatusStore(_dataRoot);
        var provider = new WeekdayBarProvider("alpha");
        var service = CreateService(store, provider);

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
        var service = CreateService(store, alpha, beta);
        var from = new DateOnly(2024, 3, 4);
        var to = new DateOnly(2024, 3, 15);

        await RunAsync(service, "alpha", from, to);
        var viaBeta = await RunAsync(service, "beta", from, to);

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
        await RunAsync(CreateService(store, new WeekdayBarProvider("alpha", feed: "iex:all")), "alpha", from, to);

        var sip = new WeekdayBarProvider("alpha", feed: "sip:all");
        var viaSip = await RunAsync(CreateService(store, sip), "alpha", from, to, resume: true);

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
        var service = CreateService(store, provider);

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
    public async Task Resume_WhenALaterGapFails_KeepsEarlierVerifiedBatches()
    {
        var store = new BackfillStatusStore(_dataRoot);
        await store.WriteSymbolCoverageAsync(DataGranularity.Daily, "alpha", "default",
            [new BackfillCoverageUpdate("SPY", [new BackfillCoverageRange(new DateOnly(2024, 2, 5), new DateOnly(2024, 2, 16), 10)], ["alpha"])]);
        var provider = new WeekdayBarProvider("alpha") { FailFrom = new DateOnly(2024, 2, 17) };
        var service = CreateService(store, provider);

        var result = await RunAsync(service, "alpha", new DateOnly(2024, 1, 2), new DateOnly(2024, 3, 1), resume: true);

        result.Success.Should().BeFalse();
        result.BatchReceipts.Should().ContainSingle();
        var retained = store.TryReadSymbolCoverage(DataGranularity.Daily, "alpha", "default")!["SPY"].Should().ContainSingle().Which;
        retained.From.Should().Be(new DateOnly(2024, 1, 2));
        retained.To.Should().Be(new DateOnly(2024, 2, 16));

        provider.FailFrom = null;
        provider.Calls.Clear();
        var resumed = await RunAsync(CreateService(new BackfillStatusStore(_dataRoot), provider), "alpha",
            new DateOnly(2024, 1, 2), new DateOnly(2024, 3, 1), resume: true);
        resumed.Success.Should().BeTrue();
        provider.Calls.Should().Equal((new DateOnly(2024, 2, 17), new DateOnly(2024, 3, 1)));
    }

    [Fact]
    public async Task Resume_AfterCancellation_PreservesFlushedSymbolsAcrossRestart()
    {
        var store = new BackfillStatusStore(_dataRoot);
        using var cancellation = new CancellationTokenSource();
        var provider = new WeekdayBarProvider("alpha")
        {
            OnRequest = symbol =>
            {
                if (symbol == "QQQ")
                    cancellation.Cancel();
            }
        };
        var from = new DateOnly(2024, 3, 4);
        var to = new DateOnly(2024, 3, 15);
        var request = new AppBackfillRequest("alpha", ["SPY", "QQQ"], from, to, MaxConcurrentSymbols: 1);

        await using var firstPipeline = new EventPipeline(new ProbeSink(), capacity: 1_000, enablePeriodicFlush: false);
        var interrupted = () => CreateService(store, provider).RunAsync(request, firstPipeline, cancellation.Token);
        await interrupted.Should().ThrowAsync<OperationCanceledException>();

        provider.OnRequest = null;
        provider.RequestedSymbols.Clear();
        await using var newPipeline = new EventPipeline(new ProbeSink(), capacity: 1_000, enablePeriodicFlush: false);
        var resumed = await CreateService(new BackfillStatusStore(_dataRoot), provider)
            .RunAsync(request with { ResumeFromCheckpoint = true }, newPipeline);
        resumed.Success.Should().BeTrue();
        resumed.SkippedSymbols.Should().Equal("SPY");
        provider.RequestedSymbols.Should().Equal("QQQ");
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
        var service = CreateService(store, provider);

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
        var service = CreateService(store, provider);
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
        var service = CreateService(store, provider);
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
        var service = CreateService(store, provider);
        var from = new DateOnly(2024, 3, 4);
        var to = new DateOnly(2024, 3, 15);

        await RunAsync(service, "alpha", from, to);
        provider.NewestAvailable = null;
        provider.Calls.Clear();
        var resumed = await RunAsync(service, "alpha", from, to, resume: true);

        provider.Calls.Should().Equal((new DateOnly(2024, 3, 14), to));
        resumed.BarsWritten.Should().Be(2);
    }

    [Fact]
    public async Task Resume_UsesActualSessionCoverageAndRetainsRevisionsAcrossRetries()
    {
        var from = new DateOnly(2024, 3, 4);
        var missing = from.AddDays(2);
        var to = from.AddDays(4);
        var store = new BackfillStatusStore(_dataRoot);
        var provider = new WeekdayBarProvider("alpha", servingProvider: "fallback") { MissingDate = missing };
        var service = CreateService(store, provider);

        var partial = await RunAsync(service, "alpha", from, to);
        var receipt = partial.BatchReceipts.Should().ContainSingle().Which;
        receipt.Status.Should().Be("Partial");
        receipt.ExpectedSessions.Should().HaveCount(5);
        receipt.ReceivedSessions.Should().HaveCount(4).And.NotContain(missing);
        receipt.ServingProvider.Should().Be("fallback");
        partial.SymbolValidationSignals.Should().ContainSingle().Which.Status.Should().Be("Warn");

        var identical = await RunAsync(service, "alpha", from, to);
        identical.BarsWritten.Should().Be(0);
        identical.BatchReceipts.Should().ContainSingle().Which.RevisionId.Should().Be(receipt.RevisionId);

        provider.MissingDate = null;
        provider.Calls.Clear();
        var resumed = await RunAsync(CreateService(new BackfillStatusStore(_dataRoot), provider), "alpha", from, to, resume: true);
        provider.Calls.Should().Equal((missing, missing));
        resumed.BatchReceipts.Should().ContainSingle().Which.Status.Should().Be("Complete");

        provider.Close = 100.75m;
        var revised = await RunAsync(service, "alpha", from, to);
        var revision = revised.BatchReceipts.Should().ContainSingle().Which;
        revision.RevisionId.Should().NotBe(receipt.RevisionId);
        revision.ContentHash.Should().NotBe(receipt.ContentHash);
        (await store.Batches.ReadAsync(receipt.RevisionId)).Should().NotBeNull();
    }

    [Fact]
    public async Task Resume_DoesNotTreatAHolidayAsMissingWhenCalendarConfirmsIt()
    {
        var from = new DateOnly(2024, 7, 3);
        var holiday = from.AddDays(1);
        var to = from.AddDays(2);
        var store = new BackfillStatusStore(_dataRoot);
        var provider = new WeekdayBarProvider("alpha") { MissingDate = holiday };
        var resolver = new Meridian.Platform.Scheduling.HistoricalSessionScheduleResolver(null);
        var service = new HistoricalBackfillService([provider], checkpointStore: store)
        {
            ResolveSessionsAsync = (_, lower, upper, _) => Task.FromResult<HistoricalSessionSchedule?>(resolver.UsSessions(lower, upper))
        };

        var first = await RunAsync(service, "alpha", from, to);
        first.BatchReceipts.Should().ContainSingle().Which.ExpectedSessions.Should().Equal(from, to);
        provider.Calls.Clear();
        var resumed = await RunAsync(service, "alpha", from, to, resume: true);
        resumed.SkippedSymbols.Should().Contain("SPY");
        provider.Calls.Should().BeEmpty();
    }

    [Fact]
    public async Task FreshRetry_ReusesRetainedBatchAndPersistsMatchingRevisionOnReplayRows()
    {
        var from = new DateOnly(2024, 3, 4);
        var to = from.AddDays(2);
        var provider = new WeekdayBarProvider("alpha", servingProvider: "fallback");
        var options = new StorageOptions { RootPath = _dataRoot };
        var request = new AppBackfillRequest("alpha", ["SPY"], from, to);
        var first = await RunWithStorageAsync();
        var repeated = await RunWithStorageAsync();

        first.Success.Should().BeTrue();
        first.BarsWritten.Should().Be(3);
        repeated.Success.Should().BeTrue();
        repeated.BarsWritten.Should().Be(0);
        var receipt = first.BatchReceipts.Should().ContainSingle().Which;
        repeated.BatchReceipts.Should().ContainSingle().Which.Should().BeEquivalentTo(receipt);
        var retained = await new BackfillStatusStore(_dataRoot).Batches.ReadAsync(receipt.RevisionId);
        var observations = new List<MarketEvent>();
        await foreach (var item in new JsonlReplayer(_dataRoot).ReadEventsAsync())
            observations.Add(item);
        observations.Should().HaveCount(3).And.OnlyContain(item => item.Source == "fallback" && item.BackfillRevisionId == receipt.RevisionId);
        observations.Select(BackfillBatchRevision.HashRow).Should().BeEquivalentTo(retained!.RowHashes);

        async Task<Meridian.Contracts.Backfill.BackfillResult> RunWithStorageAsync()
        {
            await using var sink = new JsonlStorageSink(options, new JsonlStoragePolicy(options));
            await using var pipeline = new EventPipeline(sink, capacity: 1_000, enablePeriodicFlush: false);
            return await CreateService(new BackfillStatusStore(_dataRoot), provider).RunAsync(request, pipeline);
        }
    }

    [Fact]
    public async Task Resume_RecoversReceiptForExactRowsAlreadyFlushedBeforeInterruption()
    {
        var from = new DateOnly(2024, 3, 4);
        var to = from.AddDays(2);
        var provider = new WeekdayBarProvider("alpha");
        var request = new AppBackfillRequest("alpha", ["SPY"], from, to, ResumeFromCheckpoint: true);
        var bars = await provider.GetDailyBarsAsync("SPY", from, to);
        var events = bars.Select(bar => MarketEvent.HistoricalBar(bar.ToTimestampUtc(), bar.Symbol, bar, bar.Source, bar.SequenceNumber)).ToArray();
        var dates = bars.Select(bar => bar.SessionDate).ToArray();
        var batch = BackfillBatchRevision.Create(provider.Name, provider.Name, provider.CoverageFeed, "SPY", "Daily",
            from, to, "fixture-weekdays", dates, dates, events.Select(BackfillBatchRevision.HashRow));
        var options = new StorageOptions { RootPath = _dataRoot };
        var dedupDirectory = Path.Combine(_dataRoot, "_dedup");

        await using (var dedup = new PersistentDedupLedger(dedupDirectory))
        {
            await dedup.InitializeAsync();
            await using var sink = new JsonlStorageSink(options, new JsonlStoragePolicy(options));
            await using var pipeline = new EventPipeline(sink, enablePeriodicFlush: false, dedupLedger: dedup);
            foreach (var item in events)
                await pipeline.PublishAsync(item with { BackfillRevisionId = batch.Receipt.RevisionId });
            await pipeline.FlushAsync();
        }

        var store = new BackfillStatusStore(_dataRoot);
        (await store.Batches.ReadAsync(batch.Receipt.RevisionId)).Should().BeNull();
        await using (var dedup = new PersistentDedupLedger(dedupDirectory))
        {
            await dedup.InitializeAsync();
            await using var sink = new JsonlStorageSink(options, new JsonlStoragePolicy(options));
            await using var pipeline = new EventPipeline(sink, enablePeriodicFlush: false, dedupLedger: dedup);
            var resumed = await CreateService(store, provider).RunAsync(request, pipeline);
            resumed.Success.Should().BeTrue();
            resumed.BarsWritten.Should().Be(0);
            resumed.BatchReceipts.Should().ContainSingle().Which.Should().BeEquivalentTo(batch.Receipt);
        }

        var retainedEvents = new List<MarketEvent>();
        await foreach (var item in new JsonlReplayer(_dataRoot).ReadEventsAsync())
            retainedEvents.Add(item);
        retainedEvents.Select(BackfillBatchRevision.HashRow).Should().BeEquivalentTo(batch.RowHashes);
    }

    private static HistoricalBackfillService CreateService(BackfillStatusStore store, params IHistoricalDataProvider[] providers)
        => new(providers, checkpointStore: store) { ResolveSessionsAsync = WeekdaysOnlyAsync };

    private static Task<HistoricalSessionSchedule?> WeekdaysOnlyAsync(string symbol, DateOnly from, DateOnly to, CancellationToken ct)
        => Task.FromResult<HistoricalSessionSchedule?>(new("fixture-weekdays",
            Enumerable.Range(from.DayNumber, to.DayNumber - from.DayNumber + 1)
                .Select(DateOnly.FromDayNumber)
                .Where(static date => date.DayOfWeek is not (DayOfWeek.Saturday or DayOfWeek.Sunday)).ToArray()));

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
    private sealed class WeekdayBarProvider(string name, string feed = "default", string? displayName = null, string? servingProvider = null) : IHistoricalDataProvider
    {
        public string Name => name;
        public string DisplayName => displayName ?? name;
        public string Description => string.Empty;
        public string CoverageFeed => feed;
        public ConcurrentQueue<(DateOnly? From, DateOnly? To)> Calls { get; } = new();
        public ConcurrentQueue<string> RequestedSymbols { get; } = new();
        public DateOnly? FailFrom { get; set; }
        public DateOnly? NewestAvailable { get; set; }
        public DateOnly? MissingDate { get; set; }
        public decimal Close { get; set; } = 100.5m;
        public Action<string>? OnRequest { get; set; }

        public Task<IReadOnlyList<HistoricalBar>> GetDailyBarsAsync(
            string symbol, DateOnly? from, DateOnly? to, CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            Calls.Enqueue((from, to));
            RequestedSymbols.Enqueue(symbol);
            OnRequest?.Invoke(symbol);
            ct.ThrowIfCancellationRequested();
            if (FailFrom is { } failFrom && from == failFrom)
                throw new InvalidOperationException("simulated provider interruption");

            var bars = new List<HistoricalBar>();
            var end = NewestAvailable is { } newest && newest < to!.Value ? newest : to!.Value;
            for (var date = from!.Value; date <= end; date = date.AddDays(1))
            {
                if (date == MissingDate || date.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday)
                    continue;
                bars.Add(new HistoricalBar(symbol, date, 100m, 101m, 99m, Close, 1_000, servingProvider ?? name));
            }

            return Task.FromResult<IReadOnlyList<HistoricalBar>>(bars);
        }
    }

    private sealed class LogCollector : ILogEventSink
    {
        public ConcurrentQueue<LogEvent> Events { get; } = new();
        public void Emit(LogEvent logEvent) => Events.Enqueue(logEvent);
    }
}
