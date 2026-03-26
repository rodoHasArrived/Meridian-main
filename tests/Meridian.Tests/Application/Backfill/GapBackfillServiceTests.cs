using System.Reflection;
using FluentAssertions;
using Meridian.Application.Backfill;
using Meridian.Infrastructure.Shared;
using Xunit;

namespace Meridian.Tests.Application.Backfill;

/// <summary>
/// Unit tests for <see cref="GapBackfillService"/>.
///
/// Tests drive the service through the <see cref="WebSocketReconnectionHelper.Reconnected"/>
/// event (fired via reflection, since the event can only be raised inside the helper class).
/// The async backfill task is fire-and-forget inside the service; each test that expects
/// the executor to be called uses a <see cref="TaskCompletionSource{T}"/> to await completion.
/// </summary>
public sealed class GapBackfillServiceTests
{
    // ── infrastructure ────────────────────────────────────────────────────────

    /// <summary>
    /// Raises <see cref="WebSocketReconnectionHelper.Reconnected"/> externally.
    /// The event's backing multicast delegate is obtained via reflection and invoked
    /// directly, bypassing the reconnect loop and its built-in delays.
    /// </summary>
    private static void RaiseReconnected(WebSocketReconnectionHelper helper, ReconnectionEvent evt)
    {
        var field = typeof(WebSocketReconnectionHelper)
            .GetField("Reconnected", BindingFlags.NonPublic | BindingFlags.Instance);
        field.Should().NotBeNull("WebSocketReconnectionHelper must have a 'Reconnected' backing field");

        var handler = (Action<ReconnectionEvent>?)field!.GetValue(helper);
        handler?.Invoke(evt);
    }

    private static ReconnectionEvent MakeEvent(TimeSpan gap, string provider = "test-provider")
    {
        var reconnectedAt = DateTimeOffset.UtcNow;
        var disconnectedAt = reconnectedAt - gap;
        return new ReconnectionEvent(provider, disconnectedAt, reconnectedAt, AttemptsUsed: 1);
    }

    private static BackfillResult SuccessResult(BackfillRequest req) =>
        new(true, req.Provider, req.Symbols.ToArray(), req.From, req.To,
            BarsWritten: 10, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow);

    private static BackfillResult FailureResult(BackfillRequest req) =>
        new(false, req.Provider, req.Symbols.ToArray(), req.From, req.To,
            BarsWritten: 0, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, Error: "provider error");

    // ── gap threshold ─────────────────────────────────────────────────────────

    [Fact]
    public async Task OnReconnected_GapBelowMinimum_DoesNotTriggerBackfill()
    {
        var executorCalled = false;
        var svc = new GapBackfillService(
            (req, ct) => { executorCalled = true; return Task.FromResult(SuccessResult(req)); },
            subscribedSymbols: ["AAPL"],
            minimumGap: TimeSpan.FromSeconds(30));

        var helper = new WebSocketReconnectionHelper("test");
        svc.Subscribe(helper);

        // 5 s gap is below the 30 s minimum — no executor call expected.
        RaiseReconnected(helper, MakeEvent(TimeSpan.FromSeconds(5)));
        await Task.Delay(50); // allow any unexpected async work to complete

        svc.GapBackfillsTriggered.Should().Be(0);
        executorCalled.Should().BeFalse();
    }

    [Fact]
    public async Task OnReconnected_GapExceedsMinimum_IncrementsTriggeredCounter()
    {
        var tcs = new TaskCompletionSource<BackfillRequest>();
        var svc = new GapBackfillService(
            async (req, ct) => { tcs.TrySetResult(req); await Task.Yield(); return SuccessResult(req); },
            subscribedSymbols: ["AAPL", "MSFT"],
            minimumGap: TimeSpan.FromSeconds(10));

        var helper = new WebSocketReconnectionHelper("test");
        svc.Subscribe(helper);

        RaiseReconnected(helper, MakeEvent(TimeSpan.FromSeconds(60)));

        await tcs.Task.WaitAsync(TimeSpan.FromSeconds(5));

        svc.GapBackfillsTriggered.Should().Be(1);
    }

    [Fact]
    public async Task OnReconnected_GapExceedsMinimum_PassesCorrectSymbolsToExecutor()
    {
        var tcs = new TaskCompletionSource<BackfillRequest>();
        var svc = new GapBackfillService(
            async (req, ct) => { tcs.TrySetResult(req); await Task.Yield(); return SuccessResult(req); },
            subscribedSymbols: ["AAPL", "MSFT"],
            minimumGap: TimeSpan.FromSeconds(10));

        var helper = new WebSocketReconnectionHelper("test");
        svc.Subscribe(helper);

        RaiseReconnected(helper, MakeEvent(TimeSpan.FromSeconds(60)));

        var request = await tcs.Task.WaitAsync(TimeSpan.FromSeconds(5));

        request.Symbols.Should().BeEquivalentTo(["AAPL", "MSFT"]);
    }

    // ── success / failure counters ────────────────────────────────────────────

    [Fact]
    public async Task OnReconnected_SuccessfulExecutor_IncrementsSucceededCounter()
    {
        var tcs = new TaskCompletionSource<bool>();
        var svc = new GapBackfillService(
            async (req, ct) =>
            {
                var result = SuccessResult(req);
                await Task.Yield();
                tcs.TrySetResult(true);
                return result;
            },
            subscribedSymbols: ["SPY"],
            minimumGap: TimeSpan.FromSeconds(5));

        var helper = new WebSocketReconnectionHelper("test");
        svc.Subscribe(helper);

        RaiseReconnected(helper, MakeEvent(TimeSpan.FromSeconds(30)));

        await tcs.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await Task.Delay(50); // allow counter increment after executor returns

        svc.GapBackfillsSucceeded.Should().Be(1);
    }

    [Fact]
    public async Task OnReconnected_FailedExecutorResult_DoesNotIncrementSucceededCounter()
    {
        var tcs = new TaskCompletionSource<bool>();
        var svc = new GapBackfillService(
            async (req, ct) =>
            {
                var result = FailureResult(req);
                await Task.Yield();
                tcs.TrySetResult(true);
                return result;
            },
            subscribedSymbols: ["SPY"],
            minimumGap: TimeSpan.FromSeconds(5));

        var helper = new WebSocketReconnectionHelper("test");
        svc.Subscribe(helper);

        RaiseReconnected(helper, MakeEvent(TimeSpan.FromSeconds(30)));

        await tcs.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await Task.Delay(50);

        svc.GapBackfillsTriggered.Should().Be(1);
        svc.GapBackfillsSucceeded.Should().Be(0);
    }

    [Fact]
    public async Task OnReconnected_ExecutorThrows_ServiceRemainsHealthyAndDoesNotPropagate()
    {
        var tcs = new TaskCompletionSource<bool>();
        var svc = new GapBackfillService(
            async (req, ct) =>
            {
                tcs.TrySetResult(true);
                await Task.Yield();
                throw new InvalidOperationException("network error");
            },
            subscribedSymbols: ["TSLA"],
            minimumGap: TimeSpan.FromSeconds(5));

        var helper = new WebSocketReconnectionHelper("test");
        svc.Subscribe(helper);

        // The fire-and-forget path must not propagate the exception to the caller.
        var act = () => RaiseReconnected(helper, MakeEvent(TimeSpan.FromSeconds(30)));
        act.Should().NotThrow();

        await tcs.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await Task.Delay(50);

        svc.GapBackfillsTriggered.Should().Be(1);
        svc.GapBackfillsSucceeded.Should().Be(0);
    }

    // ── disabled service ──────────────────────────────────────────────────────

    [Fact]
    public async Task WhenDisabled_Subscribe_DoesNotRegisterHandler()
    {
        var executorCalled = false;
        var svc = new GapBackfillService(
            (req, ct) => { executorCalled = true; return Task.FromResult(SuccessResult(req)); },
            subscribedSymbols: ["AAPL"],
            enabled: false,
            minimumGap: TimeSpan.FromSeconds(5));

        var helper = new WebSocketReconnectionHelper("test");
        svc.Subscribe(helper); // should be a no-op when disabled

        // Even a large gap should not trigger anything.
        RaiseReconnected(helper, MakeEvent(TimeSpan.FromSeconds(120)));
        await Task.Delay(50);

        svc.GapBackfillsTriggered.Should().Be(0);
        executorCalled.Should().BeFalse();
    }

    // ── no subscribed symbols ─────────────────────────────────────────────────

    [Fact]
    public async Task OnReconnected_NoSubscribedSymbols_DoesNotCallExecutor()
    {
        var executorCalled = false;
        var svc = new GapBackfillService(
            (req, ct) => { executorCalled = true; return Task.FromResult(SuccessResult(req)); },
            subscribedSymbols: [],
            minimumGap: TimeSpan.FromSeconds(5));

        var helper = new WebSocketReconnectionHelper("test");
        svc.Subscribe(helper);

        RaiseReconnected(helper, MakeEvent(TimeSpan.FromSeconds(30)));
        await Task.Delay(50);

        svc.GapBackfillsTriggered.Should().Be(0);
        executorCalled.Should().BeFalse();
    }

    // ── unsubscribe ───────────────────────────────────────────────────────────

    [Fact]
    public async Task AfterUnsubscribe_ReconnectionEvent_DoesNotCallExecutor()
    {
        var executorCalled = false;
        var svc = new GapBackfillService(
            (req, ct) => { executorCalled = true; return Task.FromResult(SuccessResult(req)); },
            subscribedSymbols: ["AAPL"],
            minimumGap: TimeSpan.FromSeconds(5));

        var helper = new WebSocketReconnectionHelper("test");
        svc.Subscribe(helper);
        svc.Unsubscribe(helper);

        RaiseReconnected(helper, MakeEvent(TimeSpan.FromSeconds(30)));
        await Task.Delay(50);

        svc.GapBackfillsTriggered.Should().Be(0);
        executorCalled.Should().BeFalse();
    }

    // ── request shape ─────────────────────────────────────────────────────────

    [Fact]
    public async Task OnReconnected_RequestUsesCompositeProvider()
    {
        var tcs = new TaskCompletionSource<BackfillRequest>();
        var svc = new GapBackfillService(
            async (req, ct) => { tcs.TrySetResult(req); await Task.Yield(); return SuccessResult(req); },
            subscribedSymbols: ["QQQ"],
            minimumGap: TimeSpan.FromSeconds(10));

        var helper = new WebSocketReconnectionHelper("test");
        svc.Subscribe(helper);

        RaiseReconnected(helper, MakeEvent(TimeSpan.FromSeconds(45)));

        var request = await tcs.Task.WaitAsync(TimeSpan.FromSeconds(5));

        request.Provider.Should().Be("composite");
    }

    [Fact]
    public async Task OnReconnected_RequestDateRange_MatchesReconnectionEventWindow()
    {
        var tcs = new TaskCompletionSource<BackfillRequest>();
        var svc = new GapBackfillService(
            async (req, ct) => { tcs.TrySetResult(req); await Task.Yield(); return SuccessResult(req); },
            subscribedSymbols: ["QQQ"],
            minimumGap: TimeSpan.FromSeconds(10));

        var helper = new WebSocketReconnectionHelper("test");
        svc.Subscribe(helper);

        var reconnectedAt = DateTimeOffset.UtcNow;
        var disconnectedAt = reconnectedAt.AddSeconds(-45);
        var evt = new ReconnectionEvent("test-provider", disconnectedAt, reconnectedAt, AttemptsUsed: 2);
        RaiseReconnected(helper, evt);

        var request = await tcs.Task.WaitAsync(TimeSpan.FromSeconds(5));

        request.From.Should().Be(DateOnly.FromDateTime(disconnectedAt.UtcDateTime));
        request.To.Should().Be(DateOnly.FromDateTime(reconnectedAt.UtcDateTime));
    }

    // ── multiple reconnections ────────────────────────────────────────────────

    [Fact]
    public async Task OnReconnected_CalledTwice_CountersAccumulateCorrectly()
    {
        var callCount = 0;
        var tcs1 = new TaskCompletionSource<bool>();
        var tcs2 = new TaskCompletionSource<bool>();

        var svc = new GapBackfillService(
            async (req, ct) =>
            {
                var n = Interlocked.Increment(ref callCount);
                await Task.Yield();
                if (n == 1) tcs1.TrySetResult(true);
                else tcs2.TrySetResult(true);
                return SuccessResult(req);
            },
            subscribedSymbols: ["AAPL"],
            minimumGap: TimeSpan.FromSeconds(5));

        var helper = new WebSocketReconnectionHelper("test");
        svc.Subscribe(helper);

        RaiseReconnected(helper, MakeEvent(TimeSpan.FromSeconds(30)));
        RaiseReconnected(helper, MakeEvent(TimeSpan.FromSeconds(30)));

        await Task.WhenAll(
            tcs1.Task.WaitAsync(TimeSpan.FromSeconds(5)),
            tcs2.Task.WaitAsync(TimeSpan.FromSeconds(5)));
        await Task.Delay(100); // allow both counter increments to finish

        svc.GapBackfillsTriggered.Should().Be(2);
        svc.GapBackfillsSucceeded.Should().Be(2);
    }

    // ── default minimum gap ───────────────────────────────────────────────────

    [Fact]
    public async Task DefaultMinimumGap_Is10Seconds_SmallGapIsIgnored()
    {
        var executorCalled = false;
        // No minimumGap specified — defaults to 10 s.
        var svc = new GapBackfillService(
            (req, ct) => { executorCalled = true; return Task.FromResult(SuccessResult(req)); },
            subscribedSymbols: ["AAPL"]);

        var helper = new WebSocketReconnectionHelper("test");
        svc.Subscribe(helper);

        // 9 s gap — just below the 10 s default minimum.
        RaiseReconnected(helper, MakeEvent(TimeSpan.FromSeconds(9)));
        await Task.Delay(50);

        executorCalled.Should().BeFalse();
        svc.GapBackfillsTriggered.Should().Be(0);
    }

    [Fact]
    public async Task DefaultMinimumGap_Is10Seconds_LargeGapIsProcessed()
    {
        var tcs = new TaskCompletionSource<bool>();
        var svc = new GapBackfillService(
            async (req, ct) => { tcs.TrySetResult(true); await Task.Yield(); return SuccessResult(req); },
            subscribedSymbols: ["AAPL"]);

        var helper = new WebSocketReconnectionHelper("test");
        svc.Subscribe(helper);

        // 60 s gap — well above the 10 s default minimum.
        RaiseReconnected(helper, MakeEvent(TimeSpan.FromSeconds(60)));

        await tcs.Task.WaitAsync(TimeSpan.FromSeconds(5));

        svc.GapBackfillsTriggered.Should().Be(1);
    }
}
