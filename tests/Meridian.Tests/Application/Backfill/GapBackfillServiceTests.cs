using FluentAssertions;
using Meridian.Application.Backfill;
using Meridian.Application.Scheduling;
using Meridian.Infrastructure.Adapters.Core;
using Meridian.Infrastructure.Resilience;
using Xunit;
using Meridian.Contracts.Backfill;
using BackfillRequest = Meridian.Application.Backfill.BackfillRequest;

namespace Meridian.Tests.Application.Backfill;

/// <summary>
/// Unit tests for <see cref="GapBackfillService"/>.
///
/// <para>
/// <b>Event injection strategy:</b> the service subscribes to
/// <see cref="IReconnectionGapSource.ReconnectionGapDetected"/> — the same contract the
/// streaming providers surface from <see cref="WebSocketConnectionManager.GapDetected"/> in
/// production. Tests raise the event through a fake source, exactly as a provider would.
/// </para>
///
/// <para>
/// <b>Async synchronization strategy:</b>
/// <list type="bullet">
///   <item>Tests that expect the executor to be called use a
///     <see cref="TaskCompletionSource{T}"/> and poll the counter with a bounded retry loop
///     so counter increments that happen after the TCS fires are still observed correctly.</item>
///   <item>Tests that expect the executor <em>not</em> to be called set a TCS inside the executor
///     and verify it did not complete within a short timeout, avoiding fixed-delay sleeps.</item>
/// </list>
/// </para>
/// </summary>
public sealed class GapBackfillServiceTests
{
    // ── infrastructure ────────────────────────────────────────────────────────

    /// <summary>
    /// Minimal stand-in for a streaming provider surfacing reconnection gaps.
    /// </summary>
    private sealed class FakeGapSource : IReconnectionGapSource
    {
        public event Action<ReconnectionGap>? ReconnectionGapDetected;

        public void Raise(ReconnectionGap gap) => ReconnectionGapDetected?.Invoke(gap);
    }

    private sealed class DelegateBackfillGateway(
        Func<BackfillRequest, CancellationToken, Task<BackfillResult>> execute)
        : IBackfillExecutionGateway
    {
        public Task<BackfillResult> RunAsync(BackfillRequest request, CancellationToken ct = default)
            => execute(request, ct);
    }

    private static ReconnectionGap MakeGap(TimeSpan gap, string provider = "test-provider")
    {
        var reconnectedAt = DateTimeOffset.UtcNow;
        return new ReconnectionGap(provider, reconnectedAt - gap, reconnectedAt, ReconnectAttempts: 1);
    }

    private static BackfillResult SuccessResult(BackfillRequest req) =>
        new(true, req.Provider, req.Symbols.ToArray(), req.From, req.To,
            BarsWritten: 10, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow);

    private static BackfillResult FailureResult(BackfillRequest req) =>
        new(false, req.Provider, req.Symbols.ToArray(), req.From, req.To,
            BarsWritten: 0, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, Error: "provider error");

    /// <summary>
    /// Polls <paramref name="condition"/> every 10 ms until it returns <c>true</c> or
    /// <paramref name="timeout"/> elapses.  Used to avoid fixed-delay waits for async
    /// counter increments that happen just after the executor returns.
    /// </summary>
    private static async Task WaitUntilAsync(Func<bool> condition, TimeSpan? timeout = null)
    {
        var deadline = DateTime.UtcNow + (timeout ?? TimeSpan.FromSeconds(5));
        while (!condition() && DateTime.UtcNow < deadline)
            await Task.Delay(10);
    }

    // ── gap threshold ─────────────────────────────────────────────────────────

    [Fact]
    public async Task OnReconnectionGap_GapBelowMinimum_DoesNotTriggerBackfill()
    {
        // Use TCS so the test fails immediately if the executor is unexpectedly called.
        var executorCalled = new TaskCompletionSource<bool>();
        var svc = new GapBackfillService(
            (req, ct) => { executorCalled.TrySetResult(true); return Task.FromResult(SuccessResult(req)); },
            subscribedSymbols: () => ["AAPL"],
            minimumGap: TimeSpan.FromSeconds(30));

        var source = new FakeGapSource();
        svc.Subscribe(source);

        // 5 s gap is below the 30 s minimum — the early-return path fires; no async work is spawned.
        source.Raise(MakeGap(TimeSpan.FromSeconds(5)));

        var completedTask = await Task.WhenAny(executorCalled.Task, Task.Delay(100));
        completedTask.Should().NotBeSameAs(executorCalled.Task,
            "executor must not be called when gap is below minimum threshold");

        svc.GapBackfillsTriggered.Should().Be(0);
    }

    [Fact]
    public async Task OnReconnectionGap_GapExceedsMinimum_IncrementsTriggeredCounter()
    {
        var tcs = new TaskCompletionSource<BackfillRequest>();
        var svc = new GapBackfillService(
            async (req, ct) => { tcs.TrySetResult(req); await Task.Yield(); return SuccessResult(req); },
            subscribedSymbols: () => ["AAPL", "MSFT"],
            minimumGap: TimeSpan.FromSeconds(10));

        var source = new FakeGapSource();
        svc.Subscribe(source);

        source.Raise(MakeGap(TimeSpan.FromSeconds(60)));

        await tcs.Task.WaitAsync(TimeSpan.FromSeconds(5));

        svc.GapBackfillsTriggered.Should().Be(1);
    }

    [Fact]
    public async Task OnReconnectionGap_GapExceedsMinimum_PassesCorrectSymbolsToExecutor()
    {
        var tcs = new TaskCompletionSource<BackfillRequest>();
        var svc = new GapBackfillService(
            async (req, ct) => { tcs.TrySetResult(req); await Task.Yield(); return SuccessResult(req); },
            subscribedSymbols: () => ["AAPL", "MSFT"],
            minimumGap: TimeSpan.FromSeconds(10));

        var source = new FakeGapSource();
        svc.Subscribe(source);

        source.Raise(MakeGap(TimeSpan.FromSeconds(60)));

        var request = await tcs.Task.WaitAsync(TimeSpan.FromSeconds(5));

        request.Symbols.Should().BeEquivalentTo(["AAPL", "MSFT"]);
    }

    [Fact]
    public async Task OnReconnectionGap_SymbolsChangeAfterConstruction_UsesSymbolsAtGapTime()
    {
        // Hot-reloaded subscriptions must be honored: the provider delegate is
        // evaluated when the gap fires, not when the service is constructed.
        var symbols = new[] { "AAPL" };
        var tcs = new TaskCompletionSource<BackfillRequest>();
        var svc = new GapBackfillService(
            async (req, ct) => { tcs.TrySetResult(req); await Task.Yield(); return SuccessResult(req); },
            subscribedSymbols: () => symbols,
            minimumGap: TimeSpan.FromSeconds(10));

        var source = new FakeGapSource();
        svc.Subscribe(source);

        symbols = ["SPY", "QQQ"];
        source.Raise(MakeGap(TimeSpan.FromSeconds(60)));

        var request = await tcs.Task.WaitAsync(TimeSpan.FromSeconds(5));

        request.Symbols.Should().BeEquivalentTo(["SPY", "QQQ"]);
    }

    // ── success / failure counters ────────────────────────────────────────────

    [Fact]
    public async Task OnReconnectionGap_SuccessfulExecutor_IncrementsSucceededCounter()
    {
        var tcs = new TaskCompletionSource<bool>();
        var svc = new GapBackfillService(
            async (req, ct) =>
            {
                await Task.Yield();
                tcs.TrySetResult(true); // signal after executor body completes
                return SuccessResult(req);
            },
            subscribedSymbols: () => ["SPY"],
            minimumGap: TimeSpan.FromSeconds(5));

        var source = new FakeGapSource();
        svc.Subscribe(source);

        source.Raise(MakeGap(TimeSpan.FromSeconds(30)));

        // Wait for executor body to complete, then poll until the post-return counter
        // increment (GapBackfillsSucceeded++) has propagated.
        await tcs.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await WaitUntilAsync(() => svc.GapBackfillsSucceeded == 1);

        svc.GapBackfillsSucceeded.Should().Be(1);
    }

    [Fact]
    public async Task OnReconnectionGap_SuccessfulGuardedRemediation_IncrementsSucceededCounter()
    {
        var completed = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var gateway = new DelegateBackfillGateway((request, _) =>
        {
            completed.TrySetResult(true);
            return Task.FromResult(SuccessResult(request));
        });
        using var guardedRemediation = new AutoGapRemediationService(
            gateway,
            new BackfillExecutionHistory(),
            policy: new AutoGapRemediationPolicy(
                MinimumGapDuration: TimeSpan.FromSeconds(5),
                MinimumGapSize: 1,
                SymbolCooldown: TimeSpan.Zero,
                ProviderCooldown: TimeSpan.Zero,
                MaxConcurrentRemediations: 1,
                DefaultProvider: "composite"));
        var svc = new GapBackfillService(
            static (_, _) => throw new InvalidOperationException("direct executor should not be used"),
            subscribedSymbols: () => ["SPY"],
            minimumGap: TimeSpan.FromSeconds(5),
            guardedRemediation: guardedRemediation);

        var source = new FakeGapSource();
        svc.Subscribe(source);
        source.Raise(MakeGap(TimeSpan.FromSeconds(30)));

        await completed.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await WaitUntilAsync(() => svc.GapBackfillsSucceeded == 1);

        svc.GapBackfillsTriggered.Should().Be(1);
        svc.GapBackfillsSucceeded.Should().Be(1);
    }

    [Fact]
    public async Task OnReconnectionGap_FailedExecutorResult_DoesNotIncrementSucceededCounter()
    {
        var tcs = new TaskCompletionSource<bool>();
        var svc = new GapBackfillService(
            async (req, ct) =>
            {
                await Task.Yield();
                tcs.TrySetResult(true);
                return FailureResult(req);
            },
            subscribedSymbols: () => ["SPY"],
            minimumGap: TimeSpan.FromSeconds(5));

        var source = new FakeGapSource();
        svc.Subscribe(source);

        source.Raise(MakeGap(TimeSpan.FromSeconds(30)));

        await tcs.Task.WaitAsync(TimeSpan.FromSeconds(5));
        // Give the post-return path a moment to finish; succeeded must remain 0.
        await WaitUntilAsync(() => svc.GapBackfillsTriggered == 1);

        svc.GapBackfillsTriggered.Should().Be(1);
        svc.GapBackfillsSucceeded.Should().Be(0);
    }

    [Fact]
    public async Task OnReconnectionGap_ExecutorThrows_ServiceRemainsHealthyAndDoesNotPropagate()
    {
        var tcs = new TaskCompletionSource<bool>();
        var svc = new GapBackfillService(
            async (req, ct) =>
            {
                tcs.TrySetResult(true);
                await Task.Yield();
                throw new InvalidOperationException("network error");
            },
            subscribedSymbols: () => ["TSLA"],
            minimumGap: TimeSpan.FromSeconds(5));

        var source = new FakeGapSource();
        svc.Subscribe(source);

        // The fire-and-forget path must not propagate the exception to the caller.
        var act = () => source.Raise(MakeGap(TimeSpan.FromSeconds(30)));
        act.Should().NotThrow();

        await tcs.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await WaitUntilAsync(() => svc.GapBackfillsTriggered == 1);

        svc.GapBackfillsTriggered.Should().Be(1);
        svc.GapBackfillsSucceeded.Should().Be(0);
    }

    // ── disabled service ──────────────────────────────────────────────────────

    [Fact]
    public async Task WhenDisabled_Subscribe_DoesNotRegisterHandler()
    {
        var executorCalled = new TaskCompletionSource<bool>();
        var svc = new GapBackfillService(
            (req, ct) => { executorCalled.TrySetResult(true); return Task.FromResult(SuccessResult(req)); },
            subscribedSymbols: () => ["AAPL"],
            enabled: false,
            minimumGap: TimeSpan.FromSeconds(5));

        var source = new FakeGapSource();
        svc.Subscribe(source); // must be a no-op when disabled

        source.Raise(MakeGap(TimeSpan.FromSeconds(120)));

        var completedTask = await Task.WhenAny(executorCalled.Task, Task.Delay(100));
        completedTask.Should().NotBeSameAs(executorCalled.Task,
            "executor must not be called when service is disabled");

        svc.GapBackfillsTriggered.Should().Be(0);
    }

    // ── no subscribed symbols ─────────────────────────────────────────────────

    [Fact]
    public async Task OnReconnectionGap_NoSubscribedSymbols_DoesNotCallExecutor()
    {
        var executorCalled = new TaskCompletionSource<bool>();
        var svc = new GapBackfillService(
            (req, ct) => { executorCalled.TrySetResult(true); return Task.FromResult(SuccessResult(req)); },
            subscribedSymbols: () => [],
            minimumGap: TimeSpan.FromSeconds(5));

        var source = new FakeGapSource();
        svc.Subscribe(source);

        source.Raise(MakeGap(TimeSpan.FromSeconds(30)));

        var completedTask = await Task.WhenAny(executorCalled.Task, Task.Delay(100));
        completedTask.Should().NotBeSameAs(executorCalled.Task,
            "executor must not be called when no symbols are subscribed");

        svc.GapBackfillsTriggered.Should().Be(0);
    }

    // ── unsubscribe ───────────────────────────────────────────────────────────

    [Fact]
    public async Task AfterUnsubscribe_ReconnectionGap_DoesNotCallExecutor()
    {
        var executorCalled = new TaskCompletionSource<bool>();
        var svc = new GapBackfillService(
            (req, ct) => { executorCalled.TrySetResult(true); return Task.FromResult(SuccessResult(req)); },
            subscribedSymbols: () => ["AAPL"],
            minimumGap: TimeSpan.FromSeconds(5));

        var source = new FakeGapSource();
        svc.Subscribe(source);
        svc.Unsubscribe(source);

        source.Raise(MakeGap(TimeSpan.FromSeconds(30)));

        var completedTask = await Task.WhenAny(executorCalled.Task, Task.Delay(100));
        completedTask.Should().NotBeSameAs(executorCalled.Task,
            "executor must not be called after Unsubscribe");

        svc.GapBackfillsTriggered.Should().Be(0);
    }

    // ── request shape ─────────────────────────────────────────────────────────

    [Fact]
    public async Task OnReconnectionGap_RequestUsesCompositeProvider()
    {
        var tcs = new TaskCompletionSource<BackfillRequest>();
        var svc = new GapBackfillService(
            async (req, ct) => { tcs.TrySetResult(req); await Task.Yield(); return SuccessResult(req); },
            subscribedSymbols: () => ["QQQ"],
            minimumGap: TimeSpan.FromSeconds(10));

        var source = new FakeGapSource();
        svc.Subscribe(source);

        source.Raise(MakeGap(TimeSpan.FromSeconds(45)));

        var request = await tcs.Task.WaitAsync(TimeSpan.FromSeconds(5));

        request.Provider.Should().Be("composite");
    }

    [Fact]
    public async Task OnReconnectionGap_RequestDefaultsToMinuteGranularity()
    {
        // A reconnection gap is an intraday window; requesting the default daily bars would
        // report the gap as remediated without restoring the missed intraday interval.
        var tcs = new TaskCompletionSource<BackfillRequest>();
        var svc = new GapBackfillService(
            async (req, ct) => { tcs.TrySetResult(req); await Task.Yield(); return SuccessResult(req); },
            subscribedSymbols: () => ["QQQ"],
            minimumGap: TimeSpan.FromSeconds(10));

        var source = new FakeGapSource();
        svc.Subscribe(source);

        source.Raise(MakeGap(TimeSpan.FromSeconds(45)));

        var request = await tcs.Task.WaitAsync(TimeSpan.FromSeconds(5));

        request.Granularity.Should().Be(DataGranularity.Minute1);
    }

    [Fact]
    public async Task OnReconnectionGap_ExplicitRecoveryGranularity_IsPassedToExecutor()
    {
        var tcs = new TaskCompletionSource<BackfillRequest>();
        var svc = new GapBackfillService(
            async (req, ct) => { tcs.TrySetResult(req); await Task.Yield(); return SuccessResult(req); },
            subscribedSymbols: () => ["QQQ"],
            minimumGap: TimeSpan.FromSeconds(10),
            recoveryGranularity: DataGranularity.Minute5);

        var source = new FakeGapSource();
        svc.Subscribe(source);

        source.Raise(MakeGap(TimeSpan.FromSeconds(45)));

        var request = await tcs.Task.WaitAsync(TimeSpan.FromSeconds(5));

        request.Granularity.Should().Be(DataGranularity.Minute5);
    }

    [Fact]
    public async Task OnReconnectionGap_RequestDateRange_MatchesReconnectionGapWindow()
    {
        var tcs = new TaskCompletionSource<BackfillRequest>();
        var svc = new GapBackfillService(
            async (req, ct) => { tcs.TrySetResult(req); await Task.Yield(); return SuccessResult(req); },
            subscribedSymbols: () => ["QQQ"],
            minimumGap: TimeSpan.FromSeconds(10));

        var source = new FakeGapSource();
        svc.Subscribe(source);

        var reconnectedAt = DateTimeOffset.UtcNow;
        var disconnectedAt = reconnectedAt.AddSeconds(-45);
        var gap = new ReconnectionGap("test-provider", disconnectedAt, reconnectedAt, ReconnectAttempts: 2);
        source.Raise(gap);

        var request = await tcs.Task.WaitAsync(TimeSpan.FromSeconds(5));

        request.From.Should().Be(DateOnly.FromDateTime(disconnectedAt.UtcDateTime));
        request.To.Should().Be(DateOnly.FromDateTime(reconnectedAt.UtcDateTime));
    }

    // ── multiple reconnections ────────────────────────────────────────────────

    [Fact]
    public async Task OnReconnectionGap_CalledTwice_CountersAccumulateCorrectly()
    {
        int callCount = 0;
        var tcs1 = new TaskCompletionSource<bool>();
        var tcs2 = new TaskCompletionSource<bool>();

        var svc = new GapBackfillService(
            async (req, ct) =>
            {
                var n = Interlocked.Increment(ref callCount);
                await Task.Yield();
                if (n == 1)
                    tcs1.TrySetResult(true);
                else
                    tcs2.TrySetResult(true);
                return SuccessResult(req);
            },
            subscribedSymbols: () => ["AAPL"],
            minimumGap: TimeSpan.FromSeconds(5));

        var source = new FakeGapSource();
        svc.Subscribe(source);

        source.Raise(MakeGap(TimeSpan.FromSeconds(30)));
        source.Raise(MakeGap(TimeSpan.FromSeconds(30)));

        await Task.WhenAll(
            tcs1.Task.WaitAsync(TimeSpan.FromSeconds(5)),
            tcs2.Task.WaitAsync(TimeSpan.FromSeconds(5)));

        // Poll until both counter increments (which happen after executor returns) settle.
        await WaitUntilAsync(() => svc.GapBackfillsSucceeded == 2);

        svc.GapBackfillsTriggered.Should().Be(2);
        svc.GapBackfillsSucceeded.Should().Be(2);
    }

    // ── default minimum gap ───────────────────────────────────────────────────

    [Fact]
    public async Task DefaultMinimumGap_Is10Seconds_SmallGapIsIgnored()
    {
        var executorCalled = new TaskCompletionSource<bool>();
        // No minimumGap specified — defaults to 10 s inside GapBackfillService.
        var svc = new GapBackfillService(
            (req, ct) => { executorCalled.TrySetResult(true); return Task.FromResult(SuccessResult(req)); },
            subscribedSymbols: () => ["AAPL"]);

        var source = new FakeGapSource();
        svc.Subscribe(source);

        // 9 s gap — just below the 10 s default minimum.
        source.Raise(MakeGap(TimeSpan.FromSeconds(9)));

        var completedTask = await Task.WhenAny(executorCalled.Task, Task.Delay(100));
        completedTask.Should().NotBeSameAs(executorCalled.Task,
            "a 9 s gap should be below the 10 s default minimum");

        svc.GapBackfillsTriggered.Should().Be(0);
    }

    [Fact]
    public async Task DefaultMinimumGap_Is10Seconds_LargeGapIsProcessed()
    {
        var tcs = new TaskCompletionSource<bool>();
        var svc = new GapBackfillService(
            async (req, ct) => { tcs.TrySetResult(true); await Task.Yield(); return SuccessResult(req); },
            subscribedSymbols: () => ["AAPL"]);

        var source = new FakeGapSource();
        svc.Subscribe(source);

        // 60 s gap — well above the 10 s default minimum.
        source.Raise(MakeGap(TimeSpan.FromSeconds(60)));

        await tcs.Task.WaitAsync(TimeSpan.FromSeconds(5));

        svc.GapBackfillsTriggered.Should().Be(1);
    }
}
