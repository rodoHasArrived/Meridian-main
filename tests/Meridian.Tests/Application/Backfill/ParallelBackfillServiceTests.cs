using System.Collections.Concurrent;
using System.Linq;
using System.Threading;
using FluentAssertions;
using Meridian.Application.Backfill;
using Meridian.Application.Config;
using Meridian.Application.Exceptions;
using Meridian.Application.Pipeline;
using Meridian.Contracts.Domain.Models;
using Meridian.Domain.Events;
using Meridian.Infrastructure.Adapters.Core;
using Meridian.Storage.Interfaces;
using Xunit;

// Disambiguate from the infrastructure-layer BackfillRequest used by the job-queue subsystem
using AppBackfillRequest = Meridian.Application.Backfill.BackfillRequest;

namespace Meridian.Tests.Application.Backfill;

/// <summary>
/// Tests for HistoricalBackfillService parallel execution, priority ordering,
/// adaptive throttling, thread-safe aggregation, and cancellation behaviour.
/// </summary>
public sealed class ParallelBackfillServiceTests : IAsyncLifetime
{
    private NoOpStorageSink _sink = null!;
    private EventPipeline _pipeline = null!;

    public Task InitializeAsync()
    {
        _sink = new NoOpStorageSink();
        _pipeline = new EventPipeline(_sink, capacity: 10_000, enablePeriodicFlush: false);
        return Task.CompletedTask;
    }

    public async Task DisposeAsync()
    {
        await _pipeline.DisposeAsync();
    }

    // -------------------------------------------------------------------------
    // 1. Concurrency gate – verify at most N symbols run simultaneously
    // -------------------------------------------------------------------------

    [Fact]
    public async Task RunAsync_WithMaxConcurrencyN_NeverExceedsNParallelSymbols()
    {
        // Arrange
        const int concurrency = 3;
        const int totalSymbols = 9;
        int maxObserved = 0;
        int currentInFlight = 0;
        var gate = new SemaphoreSlim(0);
        var releaseAll = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var provider = new ControllableProvider("test", async (symbol, ct) =>
        {
            int inFlight = Interlocked.Increment(ref currentInFlight);
            int observed;
            do
            {
                observed = Volatile.Read(ref maxObserved);
                if (inFlight <= observed)
                    break;
            }
            while (Interlocked.CompareExchange(ref maxObserved, inFlight, observed) != observed);

            gate.Release();
            await releaseAll.Task;
            Interlocked.Decrement(ref currentInFlight);
            return new[] { MakeBar(symbol) };
        });

        var svc = new HistoricalBackfillService([provider]);
        var symbols = Enumerable.Range(0, totalSymbols).Select(i => $"SYM{i}").ToArray();
        var request = new AppBackfillRequest("test", symbols, MaxConcurrentSymbols: concurrency);

        // Act – start and wait until the first batch of N slots are in-flight
        var runTask = svc.RunAsync(request, _pipeline, CancellationToken.None);
        for (int i = 0; i < concurrency; i++)
            await gate.WaitAsync(TimeSpan.FromSeconds(5));

        releaseAll.SetResult();
        await runTask;

        // Assert
        maxObserved.Should().BeLessThanOrEqualTo(concurrency);
    }

    // -------------------------------------------------------------------------
    // 2. Per-request concurrency override beats the service config
    // -------------------------------------------------------------------------

    [Fact]
    public async Task RunAsync_RequestOverridesServiceConfig_UsesRequestConcurrency()
    {
        // Arrange
        const int requestConcurrency = 1;
        int maxObserved = 0;
        int currentInFlight = 0;

        var provider = new ControllableProvider("test", async (symbol, ct) =>
        {
            int inFlight = Interlocked.Increment(ref currentInFlight);
            int observed;
            do
            {
                observed = Volatile.Read(ref maxObserved);
                if (inFlight <= observed)
                    break;
            }
            while (Interlocked.CompareExchange(ref maxObserved, inFlight, observed) != observed);

            await Task.Yield();
            Interlocked.Decrement(ref currentInFlight);
            return new[] { MakeBar(symbol) };
        });

        var jobsConfig = new BackfillJobsConfig(MaxConcurrentRequests: 5);
        var svc = new HistoricalBackfillService([provider], jobsConfig: jobsConfig);
        var symbols = Enumerable.Range(0, 4).Select(i => $"SYM{i}").ToArray();
        var request = new AppBackfillRequest("test", symbols, MaxConcurrentSymbols: requestConcurrency);

        // Act
        await svc.RunAsync(request, _pipeline, CancellationToken.None);

        // Assert
        maxObserved.Should().BeLessThanOrEqualTo(requestConcurrency);
    }

    // -------------------------------------------------------------------------
    // 3. Config default used when request doesn't specify a concurrency cap
    // -------------------------------------------------------------------------

    [Fact]
    public async Task RunAsync_NoRequestOverride_UsesConfigMaxConcurrentRequests()
    {
        // Arrange
        const int configConcurrency = 2;
        int maxObserved = 0;
        int currentInFlight = 0;
        var gate = new SemaphoreSlim(0);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var provider = new ControllableProvider("test", async (symbol, ct) =>
        {
            int inFlight = Interlocked.Increment(ref currentInFlight);
            int observed;
            do
            {
                observed = Volatile.Read(ref maxObserved);
                if (inFlight <= observed)
                    break;
            }
            while (Interlocked.CompareExchange(ref maxObserved, inFlight, observed) != observed);
            gate.Release();
            await release.Task;
            Interlocked.Decrement(ref currentInFlight);
            return new[] { MakeBar(symbol) };
        });

        var jobsConfig = new BackfillJobsConfig(MaxConcurrentRequests: configConcurrency);
        var svc = new HistoricalBackfillService([provider], jobsConfig: jobsConfig);
        var symbols = Enumerable.Range(0, 6).Select(i => $"SYM{i}").ToArray();
        var request = new AppBackfillRequest("test", symbols); // no per-request override

        var runTask = svc.RunAsync(request, _pipeline, CancellationToken.None);
        for (int i = 0; i < configConcurrency; i++)
            await gate.WaitAsync(TimeSpan.FromSeconds(5));

        release.SetResult();
        await runTask;

        maxObserved.Should().BeLessThanOrEqualTo(configConcurrency);
    }

    // -------------------------------------------------------------------------
    // 4. RateLimitException is reported as failure; remaining symbols continue
    // -------------------------------------------------------------------------

    [Fact]
    public async Task RunAsync_ProviderThrowsRateLimitException_SymbolReportedFailedOthersContinue()
    {
        // Arrange
        const string rateLimitedSymbol = "RATELIMITED";
        const string okSymbol = "OK";
        var provider = new ControllableProvider("test", (symbol, ct) =>
        {
            if (symbol == rateLimitedSymbol)
                throw new RateLimitException($"Rate limited: {symbol}");
            return Task.FromResult<IReadOnlyList<HistoricalBar>>(new[] { MakeBar(symbol) });
        });

        var svc = new HistoricalBackfillService([provider]);
        var request = new AppBackfillRequest("test", [rateLimitedSymbol, okSymbol], MaxConcurrentSymbols: 1);

        // Act
        var result = await svc.RunAsync(request, _pipeline, CancellationToken.None);

        // Assert
        result.Success.Should().BeFalse();
        result.Error.Should().Contain(rateLimitedSymbol);
        result.BarsWritten.Should().Be(1, "the OK symbol's bar must still be counted");
    }

    // -------------------------------------------------------------------------
    // 5. Adaptive throttling: multiple RateLimitExceptions floor currentConcurrency at 1
    // -------------------------------------------------------------------------

    [Fact]
    public async Task RunAsync_MultipleRateLimitExceptions_AllSymbolsFailGracefully()
    {
        // Arrange – every symbol throws RateLimitException
        var provider = new ControllableProvider("test", (symbol, ct) =>
            throw new RateLimitException($"Rate limited: {symbol}"));

        var svc = new HistoricalBackfillService([provider]);
        var symbols = Enumerable.Range(0, 5).Select(i => $"SYM{i}").ToArray();
        var request = new AppBackfillRequest("test", symbols, MaxConcurrentSymbols: 3);

        // Act – must not throw
        var result = await svc.RunAsync(request, _pipeline, CancellationToken.None);

        // Assert
        result.Success.Should().BeFalse();
        result.BarsWritten.Should().Be(0);
        result.Error.Should().NotBeNullOrEmpty();
    }

    // -------------------------------------------------------------------------
    // 6. Priority ordering (concurrency=1 → deterministic processing order)
    // -------------------------------------------------------------------------

    [Fact]
    public async Task RunAsync_WithSymbolPriorities_ProcessesLowerPriorityValueFirst()
    {
        // Arrange
        var processingOrder = new ConcurrentQueue<string>();
        var provider = new ControllableProvider("test", async (symbol, ct) =>
        {
            await Task.Yield();
            processingOrder.Enqueue(symbol);
            return new[] { MakeBar(symbol) };
        });

        var svc = new HistoricalBackfillService([provider]);
        var priorities = new Dictionary<string, int>
        {
            ["SPY"] = 3,
            ["AAPL"] = 1,
            ["MSFT"] = 2,
        };
        var request = new AppBackfillRequest(
            "test",
            ["SPY", "MSFT", "AAPL"],       // intentionally shuffled input
            MaxConcurrentSymbols: 1,         // serial execution → deterministic order
            SymbolPriorities: priorities);

        // Act
        await svc.RunAsync(request, _pipeline, CancellationToken.None);

        // Assert
        processingOrder.ToArray().Should().BeEquivalentTo(
            new[] { "AAPL", "MSFT", "SPY" },
            options => options.WithStrictOrdering(),
            "symbols must be processed ascending by priority value");
    }

    // -------------------------------------------------------------------------
    // 7. Input order preserved when no priorities supplied
    // -------------------------------------------------------------------------

    [Fact]
    public async Task RunAsync_NoPriorities_ProcessesSymbolsInInputOrder()
    {
        // Arrange
        var processingOrder = new ConcurrentQueue<string>();
        var provider = new ControllableProvider("test", async (symbol, ct) =>
        {
            await Task.Yield();
            processingOrder.Enqueue(symbol);
            return new[] { MakeBar(symbol) };
        });

        var svc = new HistoricalBackfillService([provider]);
        var symbols = new[] { "ZZZ", "AAA", "MMM" };
        var request = new AppBackfillRequest("test", symbols, MaxConcurrentSymbols: 1); // serial

        // Act
        await svc.RunAsync(request, _pipeline, CancellationToken.None);

        // Assert
        processingOrder.ToArray().Should().Equal(symbols,
            "input order must be preserved when no priorities are given");
    }

    // -------------------------------------------------------------------------
    // 8. Priority keys are matched case-insensitively
    // -------------------------------------------------------------------------

    [Fact]
    public async Task RunAsync_PriorityKeysCaseInsensitive_MatchesSymbolsRegardlessOfCase()
    {
        // Arrange
        var processingOrder = new ConcurrentQueue<string>();
        var provider = new ControllableProvider("test", async (symbol, ct) =>
        {
            await Task.Yield();
            processingOrder.Enqueue(symbol);
            return new[] { MakeBar(symbol) };
        });

        var svc = new HistoricalBackfillService([provider]);
        // priority map uses lowercase; symbols are uppercase in the request
        var priorities = new Dictionary<string, int>
        {
            ["spy"] = 2,
            ["aapl"] = 1,
        };
        var request = new AppBackfillRequest(
            "test",
            ["SPY", "AAPL"],
            MaxConcurrentSymbols: 1,
            SymbolPriorities: priorities);

        // Act
        await svc.RunAsync(request, _pipeline, CancellationToken.None);

        // Assert
        processingOrder.ToArray().Should().BeEquivalentTo(
            new[] { "AAPL", "SPY" },
            options => options.WithStrictOrdering(),
            "priority keys should match case-insensitively");
    }

    // -------------------------------------------------------------------------
    // 9. Partial failure: successful bars are still counted
    // -------------------------------------------------------------------------

    [Fact]
    public async Task RunAsync_SomeSymbolsFail_SuccessfulBarsAreStillCounted()
    {
        // Arrange
        const int barsPerSymbol = 5;
        var provider = new ControllableProvider("test", (symbol, ct) =>
        {
            if (symbol == "BAD")
                throw new InvalidOperationException("simulated provider error");
            IReadOnlyList<HistoricalBar> bars = Enumerable.Range(0, barsPerSymbol)
                .Select(_ => MakeBar(symbol)).ToArray();
            return Task.FromResult(bars);
        });

        var svc = new HistoricalBackfillService([provider]);
        var request = new AppBackfillRequest("test", ["GOOD1", "BAD", "GOOD2"], MaxConcurrentSymbols: 3);

        // Act
        var result = await svc.RunAsync(request, _pipeline, CancellationToken.None);

        // Assert
        result.Success.Should().BeFalse();
        result.BarsWritten.Should().Be(barsPerSymbol * 2, "both GOOD1 and GOOD2 bars must be counted");
        result.Error.Should().Contain("BAD");
    }

    // -------------------------------------------------------------------------
    // 10. BarsWritten is counted correctly across concurrent symbol tasks
    // -------------------------------------------------------------------------

    [Fact]
    public async Task RunAsync_ConcurrentSymbols_BarsWrittenCountIsExact()
    {
        // Arrange
        const int symbolCount = 6;
        const int barsPerSymbol = 10;
        const int concurrency = 3;

        var provider = new ControllableProvider("test", async (symbol, ct) =>
        {
            await Task.Yield();
            return (IReadOnlyList<HistoricalBar>)Enumerable.Range(0, barsPerSymbol)
                .Select(_ => MakeBar(symbol)).ToArray();
        });

        var svc = new HistoricalBackfillService([provider]);
        var symbols = Enumerable.Range(0, symbolCount).Select(i => $"SYM{i}").ToArray();
        var request = new AppBackfillRequest("test", symbols, MaxConcurrentSymbols: concurrency);

        // Act
        var result = await svc.RunAsync(request, _pipeline, CancellationToken.None);

        // Assert
        result.BarsWritten.Should().Be(symbolCount * barsPerSymbol);
        result.Success.Should().BeTrue();
    }

    // -------------------------------------------------------------------------
    // 11. Cancellation before RunAsync starts throws OperationCanceledException
    // -------------------------------------------------------------------------

    [Fact]
    public async Task RunAsync_CancelledBeforeStart_ThrowsOperationCanceledException()
    {
        // Arrange
        var provider = new ControllableProvider("test", (_, _) =>
            Task.FromResult<IReadOnlyList<HistoricalBar>>(new[] { MakeBar("X") }));
        var svc = new HistoricalBackfillService([provider]);
        var request = new AppBackfillRequest("test", ["SPY"]);

        using var cts = new CancellationTokenSource();
        cts.Cancel();

        // Act & Assert
        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => svc.RunAsync(request, _pipeline, cts.Token));
    }

    // -------------------------------------------------------------------------
    // 12. Cancellation mid-execution stops processing
    // -------------------------------------------------------------------------

    [Fact]
    public async Task RunAsync_CancelledMidExecution_StopsProcessingAndThrows()
    {
        // Arrange
        using var cts = new CancellationTokenSource();
        var firstSymbolStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var provider = new ControllableProvider("test", async (symbol, ct) =>
        {
            firstSymbolStarted.TrySetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, ct); // blocks until cancelled
            return new[] { MakeBar(symbol) };
        });

        var svc = new HistoricalBackfillService([provider]);
        var request = new AppBackfillRequest("test", ["SPY", "AAPL"], MaxConcurrentSymbols: 1);

        // Act
        var runTask = svc.RunAsync(request, _pipeline, cts.Token);
        await firstSymbolStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => runTask);
    }

    // -------------------------------------------------------------------------
    // 13. Empty symbol list throws InvalidOperationException
    // -------------------------------------------------------------------------

    [Fact]
    public async Task RunAsync_EmptySymbolList_ThrowsInvalidOperationException()
    {
        // Arrange
        var provider = new ControllableProvider("test", (_, _) =>
            Task.FromResult<IReadOnlyList<HistoricalBar>>(Array.Empty<HistoricalBar>()));
        var svc = new HistoricalBackfillService([provider]);
        var request = new AppBackfillRequest("test", Array.Empty<string>());

        // Act & Assert
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => svc.RunAsync(request, _pipeline, CancellationToken.None));
    }

    // -------------------------------------------------------------------------
    // 14. Unknown provider name throws InvalidOperationException
    // -------------------------------------------------------------------------

    [Fact]
    public async Task RunAsync_UnknownProvider_ThrowsInvalidOperationException()
    {
        // Arrange
        var provider = new ControllableProvider("test", (_, _) =>
            Task.FromResult<IReadOnlyList<HistoricalBar>>(Array.Empty<HistoricalBar>()));
        var svc = new HistoricalBackfillService([provider]);
        var request = new AppBackfillRequest("unknown-provider", ["SPY"]);

        // Act & Assert
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => svc.RunAsync(request, _pipeline, CancellationToken.None));
    }

    // -------------------------------------------------------------------------
    // 15. Result.Success is true only when all symbols succeed
    // -------------------------------------------------------------------------

    [Fact]
    public async Task RunAsync_AllSymbolsSucceed_ResultSuccessIsTrue()
    {
        // Arrange
        var provider = new ControllableProvider("test", async (symbol, ct) =>
        {
            await Task.Yield();
            return new[] { MakeBar(symbol) };
        });
        var svc = new HistoricalBackfillService([provider]);
        var request = new AppBackfillRequest("test", ["A", "B", "C"], MaxConcurrentSymbols: 2);

        // Act
        var result = await svc.RunAsync(request, _pipeline, CancellationToken.None);

        // Assert
        result.Success.Should().BeTrue();
        result.Error.Should().BeNull();
    }

    // -------------------------------------------------------------------------
    // 16. BackfillRequest.FromConfig propagates MaxConcurrentRequests
    // -------------------------------------------------------------------------

    [Fact]
    public void FromConfig_WithJobsConfig_PropagatesMaxConcurrentRequests()
    {
        // Arrange
        var cfg = new AppConfig(
            Backfill: new BackfillConfig(
                Provider: "stooq",
                Symbols: ["SPY"],
                Jobs: new BackfillJobsConfig(MaxConcurrentRequests: 7)));

        // Act
        var request = AppBackfillRequest.FromConfig(cfg);

        // Assert
        request.MaxConcurrentSymbols.Should().Be(7);
    }

    // -------------------------------------------------------------------------
    // 17. BackfillRequest.FromConfig leaves MaxConcurrentSymbols null when Jobs not set
    // -------------------------------------------------------------------------

    [Fact]
    public void FromConfig_WithoutJobsConfig_MaxConcurrentSymbolsIsNull()
    {
        // Arrange
        var cfg = new AppConfig(
            Backfill: new BackfillConfig(
                Provider: "stooq",
                Symbols: ["SPY"]));

        // Act
        var request = AppBackfillRequest.FromConfig(cfg);

        // Assert – null → service will use its BackfillJobsConfig default (3)
        request.MaxConcurrentSymbols.Should().BeNull();
    }

    // -------------------------------------------------------------------------
    // 18. Default concurrency is 3 when neither request nor config overrides
    // -------------------------------------------------------------------------

    [Fact]
    public async Task RunAsync_DefaultConfig_AllowsUpTo3ConcurrentSymbols()
    {
        // Arrange – BackfillJobsConfig default MaxConcurrentRequests is 3
        const int expectedDefault = 3;
        int maxObserved = 0;
        int currentInFlight = 0;
        var gate = new SemaphoreSlim(0);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var provider = new ControllableProvider("test", async (symbol, ct) =>
        {
            int inFlight = Interlocked.Increment(ref currentInFlight);
            int observed;
            do
            {
                observed = Volatile.Read(ref maxObserved);
                if (inFlight <= observed)
                    break;
            }
            while (Interlocked.CompareExchange(ref maxObserved, inFlight, observed) != observed);
            gate.Release();
            await release.Task;
            Interlocked.Decrement(ref currentInFlight);
            return new[] { MakeBar(symbol) };
        });

        // No jobsConfig → new BackfillJobsConfig() → MaxConcurrentRequests = 3
        var svc = new HistoricalBackfillService([provider]);
        var symbols = Enumerable.Range(0, expectedDefault + 3).Select(i => $"SYM{i}").ToArray();
        var request = new AppBackfillRequest("test", symbols); // no MaxConcurrentSymbols

        var runTask = svc.RunAsync(request, _pipeline, CancellationToken.None);
        for (int i = 0; i < expectedDefault; i++)
            await gate.WaitAsync(TimeSpan.FromSeconds(5));

        release.SetResult();
        await runTask;

        maxObserved.Should().BeLessThanOrEqualTo(expectedDefault,
            $"default concurrency should be {expectedDefault}");
    }

    // =========================================================================
    // Checkpoint / resume tests
    // =========================================================================

    [Fact]
    public async Task RunAsync_WithCheckpointStore_WritesPerSymbolCheckpoint()
    {
        var testRoot = Path.Combine(Path.GetTempPath(), $"mdc_cp_{Guid.NewGuid():N}");
        try
        {
            // Arrange
            var provider = new ControllableProvider("test", (symbol, ct) =>
                Task.FromResult<IReadOnlyList<HistoricalBar>>(new[] { MakeBar(symbol) }));

            var checkpointStore = new BackfillStatusStore(testRoot);
            var svc = new HistoricalBackfillService(new[] { provider }, checkpointStore: checkpointStore);
            var request = new AppBackfillRequest("test", new[] { "SPY", "AAPL" },
                From: new DateOnly(2024, 1, 1), To: new DateOnly(2024, 1, 2));

            // Act
            await svc.RunAsync(request, _pipeline);

            // Assert — both symbols should have a checkpoint for the bar's date
            var checkpoints = checkpointStore.TryReadSymbolCheckpoints();
            checkpoints.Should().NotBeNull();
            checkpoints!.Should().ContainKey("SPY");
            checkpoints.Should().ContainKey("AAPL");
        }
        finally
        {
            if (Directory.Exists(testRoot))
                Directory.Delete(testRoot, recursive: true);
        }
    }

    [Fact]
    public async Task RunAsync_ResumeFromCheckpoint_SkipsFullyCoveredSymbol()
    {
        var testRoot = Path.Combine(Path.GetTempPath(), $"mdc_cp_{Guid.NewGuid():N}");
        try
        {
            var fetchedSymbols = new ConcurrentBag<string>();

            var provider = new ControllableProvider("test", (symbol, ct) =>
            {
                fetchedSymbols.Add(symbol);
                return Task.FromResult<IReadOnlyList<HistoricalBar>>(new[] { MakeBar(symbol) });
            });

            var checkpointStore = new BackfillStatusStore(testRoot);

            // Pre-populate checkpoint: SPY is already fully covered through the requested To date
            var toDate = new DateOnly(2024, 1, 31);
            await checkpointStore.WriteSymbolCheckpointAsync("SPY", toDate);

            var svc = new HistoricalBackfillService(new[] { provider }, checkpointStore: checkpointStore);
            var request = new AppBackfillRequest("test", new[] { "SPY", "AAPL" },
                From: new DateOnly(2024, 1, 1), To: toDate,
                ResumeFromCheckpoint: true);

            await svc.RunAsync(request, _pipeline);

            // SPY should be skipped; AAPL should be fetched
            fetchedSymbols.Should().NotContain("SPY");
            fetchedSymbols.Should().Contain("AAPL");
        }
        finally
        {
            if (Directory.Exists(testRoot))
                Directory.Delete(testRoot, recursive: true);
        }
    }

    [Fact]
    public async Task RunAsync_ResumeFromCheckpoint_AdvancesFromDateForPartialSymbol()
    {
        var testRoot = Path.Combine(Path.GetTempPath(), $"mdc_cp_{Guid.NewGuid():N}");
        try
        {
            DateOnly? capturedFrom = null;

            var provider = new ControllableProvider("test", (symbol, from, to, ct) =>
            {
                capturedFrom = from;
                return Task.FromResult<IReadOnlyList<HistoricalBar>>(new[] { MakeBar(symbol) });
            });

            var checkpointStore = new BackfillStatusStore(testRoot);

            // SPY has checkpoint through Jan 15 but request covers until Jan 31 — partial coverage
            await checkpointStore.WriteSymbolCheckpointAsync("SPY", new DateOnly(2024, 1, 15));

            var svc = new HistoricalBackfillService(new[] { provider }, checkpointStore: checkpointStore);
            var request = new AppBackfillRequest("test", new[] { "SPY" },
                From: new DateOnly(2024, 1, 1), To: new DateOnly(2024, 1, 31),
                ResumeFromCheckpoint: true);

            await svc.RunAsync(request, _pipeline);

            // From should be advanced to Jan 16 (day after last checkpoint)
            capturedFrom.Should().Be(new DateOnly(2024, 1, 16));
        }
        finally
        {
            if (Directory.Exists(testRoot))
                Directory.Delete(testRoot, recursive: true);
        }
    }

    [Fact]
    public async Task RunAsync_ResumeFromCheckpoint_WithYahooProvider_PreservesLongerWindowMetadataAndSignals()
    {
        var testRoot = Path.Combine(Path.GetTempPath(), $"mdc_cp_{Guid.NewGuid():N}");
        try
        {
            var requestedFrom = new ConcurrentDictionary<string, DateOnly?>(StringComparer.OrdinalIgnoreCase);
            var provider = new ControllableProvider("yahoo", (symbol, from, to, ct) =>
            {
                requestedFrom[symbol] = from;

                if (symbol == "QQQ")
                    throw new InvalidOperationException("simulated yahoo interruption");

                IReadOnlyList<HistoricalBar> bars =
                [
                    new(symbol, new DateOnly(2024, 2, 16), 100m, 101m, 99m, 100.5m, 1000),
                    new(symbol, new DateOnly(2024, 3, 29), 101m, 102m, 100m, 101.5m, 1200),
                ];
                return Task.FromResult(bars);
            });

            var checkpointStore = new BackfillStatusStore(testRoot);
            await checkpointStore.WriteSymbolCheckpointAsync("SPY", new DateOnly(2024, 2, 15), barsWritten: 31);

            var svc = new HistoricalBackfillService([provider], checkpointStore: checkpointStore);
            var request = new AppBackfillRequest(
                "yahoo",
                ["SPY", "QQQ"],
                From: new DateOnly(2024, 1, 1),
                To: new DateOnly(2024, 3, 31),
                ResumeFromCheckpoint: true);

            var result = await svc.RunAsync(request, _pipeline);

            requestedFrom["SPY"].Should().Be(new DateOnly(2024, 2, 16));
            requestedFrom["QQQ"].Should().Be(new DateOnly(2024, 1, 1));
            result.Provider.Should().Be("yahoo");
            result.From.Should().Be(new DateOnly(2024, 1, 1));
            result.To.Should().Be(new DateOnly(2024, 3, 31));
            result.Success.Should().BeFalse();
            result.Error.Should().Contain("QQQ");

            var spySignal = result.SymbolValidationSignals.Should().Contain(s => s.Symbol == "SPY").Which;
            spySignal.Status.Should().Be("Pass");
            spySignal.EffectiveFrom.Should().Be(new DateOnly(2024, 2, 16));
            spySignal.EffectiveTo.Should().Be(new DateOnly(2024, 3, 29));

            var qqqSignal = result.SymbolValidationSignals.Should().Contain(s => s.Symbol == "QQQ").Which;
            qqqSignal.Status.Should().Be("Fail");
            qqqSignal.EffectiveFrom.Should().Be(new DateOnly(2024, 1, 1));
            qqqSignal.EffectiveTo.Should().Be(new DateOnly(2024, 3, 31));
        }
        finally
        {
            if (Directory.Exists(testRoot))
                Directory.Delete(testRoot, recursive: true);
        }
    }

    [Fact]
    public async Task RunAsync_ResumeFromCheckpoint_IgnoresCheckpointFromDifferentGranularity()
    {
        var testRoot = Path.Combine(Path.GetTempPath(), $"mdc_cp_{Guid.NewGuid():N}");
        try
        {
            var fetchedSymbols = new ConcurrentBag<string>();
            var provider = new ControllableProvider("test", (symbol, ct) =>
            {
                fetchedSymbols.Add(symbol);
                return Task.FromResult<IReadOnlyList<HistoricalBar>>(new[] { MakeBar(symbol) });
            });

            var checkpointStore = new BackfillStatusStore(testRoot);
            var toDate = new DateOnly(2024, 1, 31);
            await checkpointStore.WriteSymbolCheckpointAsync("SPY", DataGranularity.Minute1, toDate, barsWritten: 390);

            var svc = new HistoricalBackfillService(new[] { provider }, checkpointStore: checkpointStore);
            var request = new AppBackfillRequest(
                "test",
                new[] { "SPY" },
                From: new DateOnly(2024, 1, 1),
                To: toDate,
                Granularity: DataGranularity.Daily,
                ResumeFromCheckpoint: true);

            await svc.RunAsync(request, _pipeline);

            fetchedSymbols.Should().Contain("SPY",
                "daily resumes must not be suppressed by checkpoints recorded for intraday storage lanes");
        }
        finally
        {
            if (Directory.Exists(testRoot))
                Directory.Delete(testRoot, recursive: true);
        }
    }

    [Fact]
    public async Task RunAsync_WithoutResumeFlag_ClearsExistingCheckpoints()
    {
        var testRoot = Path.Combine(Path.GetTempPath(), $"mdc_cp_{Guid.NewGuid():N}");
        try
        {
            var provider = new ControllableProvider("test", (symbol, ct) =>
                Task.FromResult<IReadOnlyList<HistoricalBar>>(new[] { MakeBar(symbol) }));

            var checkpointStore = new BackfillStatusStore(testRoot);

            // Pre-populate checkpoints from a previous run
            await checkpointStore.WriteSymbolCheckpointAsync("SPY", new DateOnly(2024, 1, 15));
            await checkpointStore.WriteSymbolCheckpointAsync("AAPL", new DateOnly(2024, 1, 10));

            var svc = new HistoricalBackfillService(new[] { provider }, checkpointStore: checkpointStore);
            // Fresh run — ResumeFromCheckpoint defaults to false
            var request = new AppBackfillRequest("test", new[] { "SPY" },
                From: new DateOnly(2024, 1, 1), To: new DateOnly(2024, 1, 31));

            await svc.RunAsync(request, _pipeline);

            // Old checkpoints should have been cleared at start of fresh run;
            // new checkpoint for SPY should exist after completion
            var checkpoints = checkpointStore.TryReadSymbolCheckpoints();
            checkpoints.Should().NotBeNull();
            // AAPL should be gone (cleared by the fresh run)
            checkpoints!.Should().NotContainKey("AAPL");
            // SPY should have a new checkpoint from this run's bar
            checkpoints.Should().ContainKey("SPY");
        }
        finally
        {
            if (Directory.Exists(testRoot))
                Directory.Delete(testRoot, recursive: true);
        }
    }

    [Fact]
    public async Task RunAsync_WithoutResumeFlag_ClearsOnlyMatchingGranularityCheckpoints()
    {
        var testRoot = Path.Combine(Path.GetTempPath(), $"mdc_cp_{Guid.NewGuid():N}");
        try
        {
            var provider = new ControllableProvider("test", (symbol, ct) =>
                Task.FromResult<IReadOnlyList<HistoricalBar>>(new[] { MakeBar(symbol) }));

            var checkpointStore = new BackfillStatusStore(testRoot);
            await checkpointStore.WriteSymbolCheckpointAsync("SPY", DataGranularity.Daily, new DateOnly(2024, 1, 15));
            await checkpointStore.WriteSymbolCheckpointAsync("SPY", DataGranularity.Minute1, new DateOnly(2024, 1, 20), barsWritten: 390);

            var svc = new HistoricalBackfillService(new[] { provider }, checkpointStore: checkpointStore);
            var request = new AppBackfillRequest(
                "test",
                new[] { "SPY" },
                From: new DateOnly(2024, 1, 1),
                To: new DateOnly(2024, 1, 31),
                Granularity: DataGranularity.Daily);

            await svc.RunAsync(request, _pipeline);

            var dailyCheckpoints = checkpointStore.TryReadSymbolCheckpoints(DataGranularity.Daily);
            var intradayCheckpoints = checkpointStore.TryReadSymbolCheckpoints(DataGranularity.Minute1);

            dailyCheckpoints.Should().NotBeNull();
            intradayCheckpoints.Should().NotBeNull();
            dailyCheckpoints!.Should().ContainKey("SPY");
            intradayCheckpoints!.Should().ContainKey("SPY",
                "fresh daily runs should not wipe resume state for intraday backfills");
        }
        finally
        {
            if (Directory.Exists(testRoot))
                Directory.Delete(testRoot, recursive: true);
        }
    }

    // -------------------------------------------------------------------------
    // Checkpoint validation signals
    // -------------------------------------------------------------------------

    [Fact]
    public async Task RunAsync_SuccessfulRun_EmitsPassSignalForEachSymbol()
    {
        // Arrange
        var provider = new ControllableProvider("test", (symbol, ct) =>
            Task.FromResult<IReadOnlyList<HistoricalBar>>(new[] { MakeBar(symbol) }));

        var svc = new HistoricalBackfillService([provider]);
        var request = new AppBackfillRequest("test", new[] { "SPY", "AAPL" });

        // Act
        var result = await svc.RunAsync(request, _pipeline);

        // Assert
        result.SymbolValidationSignals.Should().HaveCount(2);
        result.SymbolValidationSignals.Should().OnlyContain(s => s.Status == "Pass");
        result.SymbolValidationSignals.Should().Contain(s => s.Symbol == "SPY" && s.BarsWritten == 1);
        result.SymbolValidationSignals.Should().Contain(s => s.Symbol == "AAPL" && s.BarsWritten == 1);
        result.SkippedSymbols.Should().BeEmpty();
    }

    [Fact]
    public async Task RunAsync_ZeroBarsReturned_EmitsWarnSignal()
    {
        // Arrange — provider returns empty list for TSLA
        var provider = new ControllableProvider("test", (symbol, ct) =>
            Task.FromResult<IReadOnlyList<HistoricalBar>>(
                symbol == "TSLA" ? [] : new[] { MakeBar(symbol) }));

        var svc = new HistoricalBackfillService([provider]);
        var request = new AppBackfillRequest("test", new[] { "SPY", "TSLA" });

        // Act
        var result = await svc.RunAsync(request, _pipeline);

        // Assert
        result.SymbolValidationSignals.Should().Contain(s => s.Symbol == "TSLA" && s.Status == "Warn");
        result.SymbolValidationSignals.Should().Contain(s => s.Symbol == "SPY" && s.Status == "Pass");
    }

    [Fact]
    public async Task RunAsync_FailedSymbol_EmitsFailSignal()
    {
        // Arrange — TSLA always throws
        var provider = new ControllableProvider("test", (symbol, ct) =>
        {
            if (symbol == "TSLA")
                throw new InvalidOperationException("provider error");
            return Task.FromResult<IReadOnlyList<HistoricalBar>>(new[] { MakeBar(symbol) });
        });

        var svc = new HistoricalBackfillService([provider]);
        var request = new AppBackfillRequest("test", new[] { "SPY", "TSLA" });

        // Act
        var result = await svc.RunAsync(request, _pipeline);

        // Assert
        result.SymbolValidationSignals.Should().Contain(s => s.Symbol == "TSLA" && s.Status == "Fail");
        result.SymbolValidationSignals.Should().Contain(s => s.Symbol == "SPY" && s.Status == "Pass");
        result.Success.Should().BeFalse();
    }

    [Fact]
    public async Task RunAsync_SkippedSymbol_AppearsInSkippedSymbolsAndEmitsPassSignal()
    {
        // Arrange — SPY is already fully covered, AAPL is not
        var testRoot = Path.Combine(Path.GetTempPath(), $"mdc_sig_{Guid.NewGuid():N}");
        try
        {
            var fetchedSymbols = new ConcurrentBag<string>();
            var provider = new ControllableProvider("test", (symbol, ct) =>
            {
                fetchedSymbols.Add(symbol);
                return Task.FromResult<IReadOnlyList<HistoricalBar>>(new[] { MakeBar(symbol) });
            });

            var checkpointStore = new BackfillStatusStore(testRoot);
            var toDate = new DateOnly(2024, 1, 31);
            // Write SPY checkpoint WITH bar count so the sidecar is populated
            await checkpointStore.WriteSymbolCheckpointAsync("SPY", toDate, barsWritten: 10);

            var svc = new HistoricalBackfillService([provider], checkpointStore: checkpointStore);
            var request = new AppBackfillRequest("test", new[] { "SPY", "AAPL" },
                From: new DateOnly(2024, 1, 1), To: toDate,
                ResumeFromCheckpoint: true);

            // Act
            var result = await svc.RunAsync(request, _pipeline);

            // Assert — SPY is skipped, AAPL is fetched
            result.SkippedSymbols.Should().Contain("SPY");
            result.SkippedSymbols.Should().NotContain("AAPL");

            var spySignal = result.SymbolValidationSignals.Should().Contain(s => s.Symbol == "SPY").Which;
            spySignal.Status.Should().Be("Pass");
            spySignal.BarsWritten.Should().Be(0);                  // not fetched this run
            spySignal.CheckpointBarsWritten.Should().Be(10);       // from sidecar

            var aaplSignal = result.SymbolValidationSignals.Should().Contain(s => s.Symbol == "AAPL").Which;
            aaplSignal.Status.Should().Be("Pass");
            aaplSignal.BarsWritten.Should().Be(1);

            // SPY was NOT re-fetched — no data duplication
            fetchedSymbols.Should().NotContain("SPY");

            // AAPL was fetched without a checkpoint-to-request gap: effectiveFrom == original From
            aaplSignal.EffectiveFrom.Should().Be(new DateOnly(2024, 1, 1));
        }
        finally
        {
            if (Directory.Exists(testRoot))
                Directory.Delete(testRoot, recursive: true);
        }
    }

    [Fact]
    public async Task RunAsync_ResumedPartialSymbol_FetchStartsAfterCheckpointNoDuplication()
    {
        // Arrange — SPY has a partial checkpoint (covered Jan 1–15, needs Jan 16–31).
        // The test verifies the provider is called with from=Jan16 (no overlap) and
        // that only the new bars are counted toward BarsWritten in the result.
        var testRoot = Path.Combine(Path.GetTempPath(), $"mdc_nodup_{Guid.NewGuid():N}");
        try
        {
            DateOnly? capturedFrom = null;
            var provider = new ControllableProvider("test", (symbol, from, to, ct) =>
            {
                capturedFrom = from;
                // Return bars for Jan 16 and Jan 31 only (post-checkpoint range)
                IReadOnlyList<HistoricalBar> bars =
                [
                    new(symbol, new DateOnly(2024, 1, 16), 100m, 110m, 90m, 105m, 500),
                    new(symbol, new DateOnly(2024, 1, 31), 102m, 112m, 92m, 107m, 600),
                ];
                return Task.FromResult(bars);
            });

            var checkpointStore = new BackfillStatusStore(testRoot);
            // Previous run ended Jan 15 with 10 bars
            await checkpointStore.WriteSymbolCheckpointAsync("SPY", new DateOnly(2024, 1, 15), barsWritten: 10);

            var svc = new HistoricalBackfillService([provider], checkpointStore: checkpointStore);
            var request = new AppBackfillRequest("test", new[] { "SPY" },
                From: new DateOnly(2024, 1, 1), To: new DateOnly(2024, 1, 31),
                ResumeFromCheckpoint: true);

            // Act
            var result = await svc.RunAsync(request, _pipeline);

            // Assert — provider was called starting from Jan 16 (checkpoint + 1 day), not Jan 1
            capturedFrom.Should().Be(new DateOnly(2024, 1, 16), "resume must start the day after the checkpoint");

            // Only the 2 new bars were written — the pre-checkpoint bars are NOT re-fetched
            result.BarsWritten.Should().Be(2, "only post-checkpoint bars should be counted");

            var signal = result.SymbolValidationSignals.Should().Contain(s => s.Symbol == "SPY").Which;
            signal.Status.Should().Be("Pass");
            signal.BarsWritten.Should().Be(2);
            signal.EffectiveFrom.Should().Be(new DateOnly(2024, 1, 16));
        }
        finally
        {
            if (Directory.Exists(testRoot))
                Directory.Delete(testRoot, recursive: true);
        }
    }

    // =========================================================================
    // Helpers
    // =========================================================================

    private static HistoricalBar MakeBar(string symbol) =>
        new(symbol, new DateOnly(2024, 1, 2), 100m, 110m, 90m, 105m, 1000);

    // =========================================================================
    // Test doubles
    // =========================================================================

    /// <summary>
    /// Minimal <see cref="IHistoricalDataProvider"/> whose <see cref="GetDailyBarsAsync"/>
    /// delegates to a caller-supplied function, giving tests full control over timing and failures.
    /// Supports two delegate signatures: with or without explicit <c>from</c>/<c>to</c> parameters.
    /// </summary>
    private sealed class ControllableProvider : IHistoricalDataProvider
    {
        private readonly Func<string, DateOnly?, DateOnly?, CancellationToken, Task<IReadOnlyList<HistoricalBar>>> _fetch;

        public ControllableProvider(
            string name,
            Func<string, CancellationToken, Task<IReadOnlyList<HistoricalBar>>> fetch)
        {
            Name = DisplayName = name;
            _fetch = (s, _, _, ct) => fetch(s, ct);
        }

        public ControllableProvider(
            string name,
            Func<string, DateOnly?, DateOnly?, CancellationToken, Task<IReadOnlyList<HistoricalBar>>> fetch)
        {
            Name = DisplayName = name;
            _fetch = fetch;
        }

        public string Name { get; }
        public string DisplayName { get; }
        public string Description => string.Empty;

        public Task<IReadOnlyList<HistoricalBar>> GetDailyBarsAsync(
            string symbol, DateOnly? from, DateOnly? to, CancellationToken ct = default)
            => _fetch(symbol, from, to, ct);
    }

    /// <summary>No-op storage sink that discards all events without I/O.</summary>
    private sealed class NoOpStorageSink : IStorageSink
    {
        public ValueTask AppendAsync(MarketEvent evt, CancellationToken ct = default) => ValueTask.CompletedTask;
        public Task FlushAsync(CancellationToken ct = default) => Task.CompletedTask;
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
