using System.Diagnostics;
using System.Runtime.ExceptionServices;
using Meridian.Domain.Reconciliation;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Meridian.FinancialOperations.Reconciliation;

/// <summary>
/// Captures source snapshots for a reconciliation run with a real scheduling policy instead of a
/// sequential foreach: bounded concurrent capture across adapters, a per-attempt timeout, and
/// exponential-backoff retries for transient per-source failures. Results are returned in a
/// deterministic order (source type, then registration order) regardless of completion order, and
/// a source that exhausts its attempts fails the run by rethrowing its final attempt's exception
/// with the original type and stack intact — retry telemetry is carried in structured logs, not in
/// wrapper exception types. Adapters are invoked behind the deadline fence (their synchronous
/// prefixes cannot stall the run), cooperative timeouts retry, and a source that ignores
/// cancellation past the grace period fails terminally rather than stacking further live captures
/// outside the concurrency accounting. Every attempt that is counted is an attempt the adapter was
/// actually asked to serve: the dispatch carries no scheduling token, so a deadline that elapses
/// while the work item is queued cannot consume an attempt without the adapter running.
/// </summary>
public sealed class DefaultReconciliationIngestionScheduler : IReconciliationIngestionScheduler
{
    private readonly ReconciliationIngestionOptions _options;
    private readonly ILogger<DefaultReconciliationIngestionScheduler> _log;

    public DefaultReconciliationIngestionScheduler(
        ReconciliationIngestionOptions? options = null,
        ILogger<DefaultReconciliationIngestionScheduler>? log = null)
    {
        _options = options ?? ReconciliationIngestionOptions.Default;
        _options.Validate();
        _log = log ?? NullLogger<DefaultReconciliationIngestionScheduler>.Instance;
    }

    public async Task<IReadOnlyList<DataSourceSnapshot>> CaptureAsync(
        IReadOnlyList<IReconciliationSourceAdapter> adapters,
        ReconciliationIngestionRequest request,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(adapters);
        ArgumentNullException.ThrowIfNull(request);
        ct.ThrowIfCancellationRequested();

        // Deterministic capture plan: source type first, registration order as the stable tie-break.
        var plan = adapters
            .Select(static (adapter, index) => (Adapter: adapter, Index: index))
            .OrderBy(static entry => entry.Adapter.SourceType)
            .ThenBy(static entry => entry.Index)
            .ToArray();
        var snapshots = new DataSourceSnapshot[plan.Length];

        using var gate = new SemaphoreSlim(_options.MaxConcurrentCaptures, _options.MaxConcurrentCaptures);
        var captures = new Task[plan.Length];
        for (var slot = 0; slot < plan.Length; slot++)
        {
            var adapter = plan[slot].Adapter;
            var resultSlot = slot;
            captures[slot] = CaptureWithPolicyAsync(adapter, request, gate, snapshots, resultSlot, ct);
        }

        await Task.WhenAll(captures).ConfigureAwait(false);
        return snapshots;
    }

    private async Task CaptureWithPolicyAsync(
        IReconciliationSourceAdapter adapter,
        ReconciliationIngestionRequest request,
        SemaphoreSlim gate,
        DataSourceSnapshot[] snapshots,
        int resultSlot,
        CancellationToken ct)
    {
        await gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            snapshots[resultSlot] = await CaptureWithRetriesAsync(adapter, request, ct).ConfigureAwait(false);
        }
        finally
        {
            gate.Release();
        }
    }

    private async Task<DataSourceSnapshot> CaptureWithRetriesAsync(
        IReconciliationSourceAdapter adapter,
        ReconciliationIngestionRequest request,
        CancellationToken ct)
    {
        ExceptionDispatchInfo? lastFailure = null;
        for (var attempt = 1; attempt <= _options.MaxAttemptsPerSource; attempt++)
        {
            ct.ThrowIfCancellationRequested();
            if (attempt > 1)
            {
                // 250ms, 500ms, 1s, … — deterministic exponential backoff, cancellable.
                var delay = _options.RetryBaseDelay * Math.Pow(2, attempt - 2);
                if (delay > TimeSpan.Zero)
                {
                    await Task.Delay(delay, ct).ConfigureAwait(false);
                }
            }

            using var attemptCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            if (_options.PerSourceTimeout is { } timeout)
            {
                attemptCts.CancelAfter(timeout);
            }

            var stopwatch = Stopwatch.StartNew();
            Task<DataSourceSnapshot>? captureTask = null;
            try
            {
                // Task.Run puts even a fully synchronous adapter prefix (a blocking vendor-SDK call
                // before its first await) behind the deadline fence instead of letting it run
                // inline where neither timeout nor cancellation could reach it. The linked token
                // asks the adapter to stop cooperatively at PerSourceTimeout; WaitAsync enforces a
                // hard deadline of timeout + grace for adapters that never observe their token.
                //
                // The token is deliberately NOT passed to Task.Run itself. That overload's token
                // "can be used to cancel the work if it has not yet started" — it gates scheduling,
                // not the delegate, and is never handed to the delegate either. So when the
                // deadline fired while the work item was still queued behind a saturated pool, the
                // adapter was never invoked at all, yet the attempt was consumed and reported here
                // as a cooperative timeout: a source blamed for exceeding a deadline it was never
                // asked to meet, with every attempt burnable that way. Cancellation reaches the
                // adapter through the token argument below, which is the one that does the work;
                // dropping the scheduling token costs only the microseconds a doomed attempt
                // spends observing an already-cancelled token.
                captureTask = Task.Run(() => adapter.CaptureSnapshotAsync(request, attemptCts.Token));
                var snapshot = _options.PerSourceTimeout is { } deadline
                    ? await captureTask.WaitAsync(deadline + _options.CancellationGracePeriod, ct).ConfigureAwait(false)
                    : await captureTask.WaitAsync(ct).ConfigureAwait(false);
                _log.LogInformation(
                    "Captured reconciliation snapshot from {SourceType} in {ElapsedMs}ms on attempt {Attempt}",
                    adapter.SourceType,
                    stopwatch.ElapsedMilliseconds,
                    attempt);
                return snapshot;
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                // The run itself was cancelled — propagate, never retry.
                ObserveAbandonedCapture(captureTask);
                throw;
            }
            catch (TimeoutException hardTimeout) when (_options.PerSourceTimeout is not null)
            {
                // The adapter blew through the cooperative timeout AND the grace period without
                // observing its token: its capture is still live and cannot be stopped. Retrying
                // would stack another concurrent request onto an unresponsive source outside the
                // concurrency accounting, so a non-cooperative timeout is terminal for this run.
                ObserveAbandonedCapture(captureTask);
                _log.LogError(
                    "Reconciliation source {SourceType} ignored cancellation for {ElapsedMs}ms on attempt {Attempt}; failing the source without retry",
                    adapter.SourceType,
                    stopwatch.ElapsedMilliseconds,
                    attempt);
                throw new TimeoutException(
                    $"Reconciliation source {adapter.SourceType} exceeded the per-source capture timeout of {_options.PerSourceTimeout} and did not honor cancellation within the {_options.CancellationGracePeriod} grace period on attempt {attempt}; treating the source as non-cooperative and not retrying.",
                    hardTimeout);
            }
            catch (OperationCanceledException timedOut) when (attemptCts.IsCancellationRequested)
            {
                // Cooperative timeout: the adapter observed its token and the attempt actually
                // ended, so no capture is left running and a retry is safe.
                lastFailure = ExceptionDispatchInfo.Capture(new TimeoutException(
                    $"Reconciliation source {adapter.SourceType} exceeded the per-source capture timeout of {_options.PerSourceTimeout} on attempt {attempt}.",
                    timedOut));
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                lastFailure = ExceptionDispatchInfo.Capture(ex);
            }

            _log.LogWarning(
                lastFailure?.SourceException,
                "Reconciliation snapshot capture from {SourceType} failed on attempt {Attempt}/{MaxAttempts} after {ElapsedMs}ms",
                adapter.SourceType,
                attempt,
                _options.MaxAttemptsPerSource,
                stopwatch.ElapsedMilliseconds);
        }

        lastFailure!.Throw();
        throw new UnreachableException();
    }

    // An abandoned capture task (hard timeout / run cancellation) may still fault later; observe
    // its exception so it never surfaces as an unobserved-task failure.
    private static void ObserveAbandonedCapture(Task? captureTask)
    {
        if (captureTask is { IsCompleted: false })
        {
            _ = captureTask.ContinueWith(
                static task => _ = task.Exception,
                CancellationToken.None,
                TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously,
                TaskScheduler.Default);
        }
    }
}
