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
/// wrapper exception types.
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
                captureTask = adapter.CaptureSnapshotAsync(request, attemptCts.Token);
                // The linked token asks the adapter to stop cooperatively; WaitAsync enforces the
                // deadline even when an adapter ignores its token or blocks in non-cancellable
                // I/O, so a stuck source times the attempt out instead of hanging the run.
                var snapshot = _options.PerSourceTimeout is { } deadline
                    ? await captureTask.WaitAsync(deadline, ct).ConfigureAwait(false)
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
                // WaitAsync hit the hard deadline while the adapter kept running.
                ObserveAbandonedCapture(captureTask);
                lastFailure = ExceptionDispatchInfo.Capture(new TimeoutException(
                    $"Reconciliation source {adapter.SourceType} exceeded the per-source capture timeout of {_options.PerSourceTimeout} on attempt {attempt}.",
                    hardTimeout));
            }
            catch (OperationCanceledException timedOut) when (attemptCts.IsCancellationRequested)
            {
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
