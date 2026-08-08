using FluentAssertions;
using Meridian.Domain.Reconciliation;
using Meridian.FinancialOperations.Reconciliation;

namespace Meridian.Tests.Application.Reconciliation;

/// <summary>
/// Coverage for the rebuilt ingestion scheduler: bounded concurrency, retry with backoff,
/// per-source timeout, type-preserving failure propagation, deterministic result ordering, and
/// cancellation flow. Orchestrator-level behavior sits in
/// <see cref="ReconciliationRunOrchestratorTests"/>.
/// </summary>
public sealed class DefaultReconciliationIngestionSchedulerTests
{
    private static readonly ReconciliationIngestionRequest Request =
        new(new DateOnly(2026, 5, 28), new DateTimeOffset(2026, 5, 28, 22, 0, 0, TimeSpan.Zero), "USD", 1);

    [Fact]
    public async Task CaptureAsync_TransientFailure_RetriesAndSucceeds()
    {
        var adapter = new FlakyAdapter(ReconciliationSourceType.Prime, failuresBeforeSuccess: 2);
        var scheduler = CreateScheduler(new ReconciliationIngestionOptions
        {
            MaxAttemptsPerSource = 3,
            RetryBaseDelay = TimeSpan.FromMilliseconds(1)
        });

        var snapshots = await scheduler.CaptureAsync([adapter], Request, CancellationToken.None);

        snapshots.Should().ContainSingle();
        adapter.Attempts.Should().Be(3);
    }

    [Fact]
    public async Task CaptureAsync_ExhaustedRetries_RethrowsOriginalExceptionType()
    {
        var adapter = new FlakyAdapter(ReconciliationSourceType.Prime, failuresBeforeSuccess: int.MaxValue);
        var scheduler = CreateScheduler(new ReconciliationIngestionOptions
        {
            MaxAttemptsPerSource = 2,
            RetryBaseDelay = TimeSpan.FromMilliseconds(1)
        });

        var act = async () => await scheduler.CaptureAsync([adapter], Request, CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>(
            "the scheduler surfaces the final attempt's exception with its original type");
        adapter.Attempts.Should().Be(2);
    }

    [Fact]
    public async Task CaptureAsync_ReturnsSnapshotsInSourceTypeOrderRegardlessOfCompletionOrder()
    {
        // Registered out of order and completing out of order: the custodian finishes first, the
        // prime last — results must still come back prime-first (source-type order).
        var custodian = new DelayedAdapter(ReconciliationSourceType.Custodian, TimeSpan.Zero);
        var prime = new DelayedAdapter(ReconciliationSourceType.Prime, TimeSpan.FromMilliseconds(80));
        var scheduler = CreateScheduler(ReconciliationIngestionOptions.Default);

        var snapshots = await scheduler.CaptureAsync([custodian, prime], Request, CancellationToken.None);

        snapshots.Select(static s => s.SourceType).Should().Equal(
            ReconciliationSourceType.Prime,
            ReconciliationSourceType.Custodian);
    }

    [Fact]
    public async Task CaptureAsync_HonorsConcurrencyLimit()
    {
        var tracker = new ConcurrencyTracker();
        var adapters = Enumerable.Range(0, 4)
            .Select(_ => (IReconciliationSourceAdapter)new TrackedAdapter(ReconciliationSourceType.Prime, tracker))
            .ToArray();
        var scheduler = CreateScheduler(new ReconciliationIngestionOptions { MaxConcurrentCaptures = 1 });

        await scheduler.CaptureAsync(adapters, Request, CancellationToken.None);

        tracker.MaxObserved.Should().Be(1);
    }

    [Fact]
    public async Task CaptureAsync_RunsCapturesConcurrentlyWhenAllowed()
    {
        var tracker = new ConcurrencyTracker(holdUntilCount: 3);
        var adapters = Enumerable.Range(0, 3)
            .Select(_ => (IReconciliationSourceAdapter)new TrackedAdapter(ReconciliationSourceType.Prime, tracker))
            .ToArray();
        var scheduler = CreateScheduler(new ReconciliationIngestionOptions { MaxConcurrentCaptures = 3 });

        await scheduler.CaptureAsync(adapters, Request, CancellationToken.None);

        tracker.MaxObserved.Should().Be(3, "captures must overlap up to the configured limit");
    }

    [Fact]
    public async Task CaptureAsync_PerSourceTimeout_SurfacesTimeoutAfterRetries()
    {
        var adapter = new HangingAdapter(ReconciliationSourceType.Prime);
        var scheduler = CreateScheduler(new ReconciliationIngestionOptions
        {
            MaxAttemptsPerSource = 2,
            RetryBaseDelay = TimeSpan.FromMilliseconds(1),
            PerSourceTimeout = TimeSpan.FromMilliseconds(40)
        });

        var act = async () => await scheduler.CaptureAsync([adapter], Request, CancellationToken.None);

        await act.Should().ThrowAsync<TimeoutException>();
        adapter.Attempts.Should().Be(2);
    }

    [Fact]
    public async Task CaptureAsync_DeadlineElapsedBeforeTheAdapterFinishes_StillEntersTheAdapter()
    {
        // Regression. Each attempt used to be dispatched as `Task.Run(..., attemptCts.Token)`, and
        // that overload's token gates *scheduling* — per the API contract it "can be used to cancel
        // the work if it has not yet started", and it is never handed to the delegate. So when the
        // per-source deadline fired while the work item was still queued behind a saturated pool,
        // the adapter was never invoked, yet the attempt was consumed and reported as a cooperative
        // timeout. A source got blamed for missing a deadline it was never asked to meet, and every
        // attempt could burn that way without the adapter running once.
        //
        // A 1ms deadline puts every attempt past its deadline while the adapter is still hanging.
        // The adapter must be entered on each of them: cancellation is the token argument's job,
        // not the scheduler's excuse to skip the call.
        var adapter = new HangingAdapter(ReconciliationSourceType.Prime);
        var scheduler = CreateScheduler(new ReconciliationIngestionOptions
        {
            MaxAttemptsPerSource = 3,
            RetryBaseDelay = TimeSpan.Zero,
            PerSourceTimeout = TimeSpan.FromMilliseconds(1),
            // Wide enough that the hard fence cannot fire first and turn this terminal: the
            // adapter honours its token, so each attempt ends cooperatively and retries.
            CancellationGracePeriod = TimeSpan.FromSeconds(5)
        });

        var act = async () => await scheduler.CaptureAsync([adapter], Request, CancellationToken.None);

        await act.Should().ThrowAsync<TimeoutException>();
        adapter.Attempts.Should().Be(3,
            "every attempt must reach the adapter — a capture the scheduler never dispatched is not a source timeout");
    }

    [Fact]
    public async Task CaptureAsync_AdapterIgnoringCancellation_IsTerminalWithoutRetry()
    {
        var adapter = new StubbornAdapter(ReconciliationSourceType.Prime);
        // The budget is widened for pool-dispatch latency. The attempt is dispatched through
        // Task.Run, and on a loaded runner the work item can sit queued: with a 40ms deadline the
        // hard fence (deadline + grace) could elapse before the delegate is dequeued, leaving
        // Attempts at 0 and the non-cooperative adapter this test is about never entered. The
        // deadline still fires well inside the test.
        var scheduler = CreateScheduler(new ReconciliationIngestionOptions
        {
            MaxAttemptsPerSource = 5,
            RetryBaseDelay = TimeSpan.FromMilliseconds(1),
            PerSourceTimeout = TimeSpan.FromMilliseconds(500),
            CancellationGracePeriod = TimeSpan.FromMilliseconds(250)
        });

        var act = async () => await scheduler.CaptureAsync([adapter], Request, CancellationToken.None);

        await act.Should().ThrowAsync<TimeoutException>(
            "the hard deadline must hold even when an adapter never observes its cancellation token");
        adapter.Attempts.Should().Be(1,
            "retrying a source whose capture is still live would stack concurrent requests outside the concurrency accounting");
    }

    [Fact]
    public async Task CaptureAsync_BlockingSynchronousAdapter_IsFencedByHardDeadline()
    {
        var adapter = new BlockingSynchronousAdapter(ReconciliationSourceType.Prime);
        var scheduler = CreateScheduler(new ReconciliationIngestionOptions
        {
            MaxAttemptsPerSource = 3,
            RetryBaseDelay = TimeSpan.FromMilliseconds(1),
            PerSourceTimeout = TimeSpan.FromMilliseconds(40),
            CancellationGracePeriod = TimeSpan.FromMilliseconds(25)
        });

        var act = async () => await scheduler.CaptureAsync([adapter], Request, CancellationToken.None);

        // A vendor SDK that blocks before returning its task must not stall the run inline: the
        // pool dispatch keeps the deadline fence in control, and the blocked capture is terminal.
        await act.Should().ThrowAsync<TimeoutException>();
        adapter.Attempts.Should().Be(1);
    }

    [Fact]
    public async Task CaptureAsync_UserCancellation_PropagatesWithoutRetry()
    {
        var adapter = new HangingAdapter(ReconciliationSourceType.Prime);
        var scheduler = CreateScheduler(new ReconciliationIngestionOptions
        {
            MaxAttemptsPerSource = 5,
            RetryBaseDelay = TimeSpan.FromMilliseconds(1)
        });
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(40));

        var act = async () => await scheduler.CaptureAsync([adapter], Request, cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
        // Still an upper bound rather than an exact count, but for a different reason now that the
        // attempt is dispatched without a scheduling token: the adapter is guaranteed to be entered
        // eventually, yet the abandoned capture may not have reached its counter by the time run
        // cancellation propagates here. Zero is a timing artefact of reading it early; what run
        // cancellation must never do is retry.
        adapter.Attempts.Should().BeLessThanOrEqualTo(1, "run cancellation must never be retried");
    }

    [Fact]
    public void Options_InvalidValues_AreRejected()
    {
        var act = () => CreateScheduler(new ReconciliationIngestionOptions { MaxConcurrentCaptures = 0 });

        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    private static DefaultReconciliationIngestionScheduler CreateScheduler(ReconciliationIngestionOptions options) =>
        new(options);

    private static DataSourceSnapshot CreateSnapshot(ReconciliationSourceType sourceType) =>
        new($"snap-{sourceType}-{Guid.NewGuid():N}", sourceType, DateTimeOffset.UtcNow, "v1", [], []);

    private sealed class FlakyAdapter(ReconciliationSourceType sourceType, int failuresBeforeSuccess) : IReconciliationSourceAdapter
    {
        public ReconciliationSourceType SourceType { get; } = sourceType;

        public int Attempts { get; private set; }

        public Task<DataSourceSnapshot> CaptureSnapshotAsync(ReconciliationIngestionRequest request, CancellationToken ct)
        {
            Attempts++;
            if (Attempts <= failuresBeforeSuccess)
            {
                throw new InvalidOperationException($"transient capture failure #{Attempts}");
            }

            return Task.FromResult(CreateSnapshot(SourceType));
        }
    }

    private sealed class DelayedAdapter(ReconciliationSourceType sourceType, TimeSpan delay) : IReconciliationSourceAdapter
    {
        public ReconciliationSourceType SourceType { get; } = sourceType;

        public async Task<DataSourceSnapshot> CaptureSnapshotAsync(ReconciliationIngestionRequest request, CancellationToken ct)
        {
            if (delay > TimeSpan.Zero)
            {
                await Task.Delay(delay, ct);
            }

            return CreateSnapshot(SourceType);
        }
    }

    private sealed class HangingAdapter(ReconciliationSourceType sourceType) : IReconciliationSourceAdapter
    {
        private int _attempts;

        public ReconciliationSourceType SourceType { get; } = sourceType;

        // Same cross-thread read as StubbornAdapter: the scheduler runs each attempt on the pool.
        public int Attempts => Volatile.Read(ref _attempts);

        public async Task<DataSourceSnapshot> CaptureSnapshotAsync(ReconciliationIngestionRequest request, CancellationToken ct)
        {
            Interlocked.Increment(ref _attempts);
            await Task.Delay(Timeout.InfiniteTimeSpan, ct);
            throw new InvalidOperationException("unreachable");
        }
    }

    // Simulates an adapter stuck in non-cancellable I/O: it never observes its token, so only the
    // scheduler's hard deadline can end the attempt.
    private sealed class StubbornAdapter(ReconciliationSourceType sourceType) : IReconciliationSourceAdapter
    {
        private int _attempts;

        public ReconciliationSourceType SourceType { get; } = sourceType;

        // Written on a thread-pool thread and read from the test thread, so both sides need a
        // barrier rather than a plain auto-property.
        public int Attempts => Volatile.Read(ref _attempts);

        public async Task<DataSourceSnapshot> CaptureSnapshotAsync(ReconciliationIngestionRequest request, CancellationToken ct)
        {
            Interlocked.Increment(ref _attempts);
            await Task.Delay(TimeSpan.FromSeconds(5), CancellationToken.None);
            throw new InvalidOperationException("unreachable");
        }
    }

    // Simulates a vendor SDK that blocks synchronously before ever returning a task — the worst
    // case for a deadline: without a pool dispatch, the call would stall the capture loop inline.
    private sealed class BlockingSynchronousAdapter(ReconciliationSourceType sourceType) : IReconciliationSourceAdapter
    {
        public ReconciliationSourceType SourceType { get; } = sourceType;

        public int Attempts { get; private set; }

        public Task<DataSourceSnapshot> CaptureSnapshotAsync(ReconciliationIngestionRequest request, CancellationToken ct)
        {
            Attempts++;
            Thread.Sleep(TimeSpan.FromSeconds(2));
            return Task.FromResult(CreateSnapshot(SourceType));
        }
    }

    private sealed class ConcurrencyTracker(int holdUntilCount = 0)
    {
        private int _current;
        private int _max;
        private readonly TaskCompletionSource _allArrived = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public int MaxObserved => Volatile.Read(ref _max);

        public async Task EnterAsync(CancellationToken ct)
        {
            var current = Interlocked.Increment(ref _current);
            InterlockedMax(ref _max, current);
            if (holdUntilCount > 0)
            {
                if (current >= holdUntilCount)
                {
                    _allArrived.TrySetResult();
                }

                // Hold every capture open until the expected overlap materializes, so the test
                // cannot pass by accident on a machine that serialized the captures.
                await _allArrived.Task.WaitAsync(TimeSpan.FromSeconds(5), ct);
            }
            else
            {
                await Task.Delay(TimeSpan.FromMilliseconds(20), ct);
            }
        }

        public void Exit() => Interlocked.Decrement(ref _current);

        private static void InterlockedMax(ref int location, int value)
        {
            int snapshot;
            do
            {
                snapshot = Volatile.Read(ref location);
                if (value <= snapshot)
                {
                    return;
                }
            }
            while (Interlocked.CompareExchange(ref location, value, snapshot) != snapshot);
        }
    }

    private sealed class TrackedAdapter(ReconciliationSourceType sourceType, ConcurrencyTracker tracker) : IReconciliationSourceAdapter
    {
        public ReconciliationSourceType SourceType { get; } = sourceType;

        public async Task<DataSourceSnapshot> CaptureSnapshotAsync(ReconciliationIngestionRequest request, CancellationToken ct)
        {
            await tracker.EnterAsync(ct);
            try
            {
                return CreateSnapshot(SourceType);
            }
            finally
            {
                tracker.Exit();
            }
        }
    }
}
