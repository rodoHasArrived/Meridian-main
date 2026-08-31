using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using Meridian.Core.Config;
using Meridian.Core.Exceptions;
using Meridian.Core.IO;
using Meridian.Core.Logging;
using Meridian.Core.Resilience;
using Meridian.Core.Serialization;
using Meridian.Contracts.Domain.Models;
using Meridian.Contracts.Services;
using Meridian.Domain.Events;
using Meridian.Domain.Models;
using Meridian.Infrastructure.Adapters.Core.SymbolResolution;
using Meridian.Storage;
using Meridian.Storage.Archival;
using Meridian.Storage.Policies;
using Serilog;

namespace Meridian.Infrastructure.Adapters.Core;

/// <summary>
/// Background worker service that processes the backfill request queue.
/// Handles rate limits, retries, writes data to storage, and supports offline-first mode.
/// </summary>
public sealed class BackfillWorkerService : IDisposable, IAsyncDisposable
{
    private readonly BackfillJobManager _jobManager;
    private readonly BackfillRequestQueue _requestQueue;
    private readonly CompositeHistoricalDataProvider _provider;
    private readonly ProviderRateLimitTracker _rateLimitTracker;
    private readonly BackfillJobsConfig _config;
    private readonly AppConfig _appConfig;
    private readonly string _dataRoot;
    private readonly ILogger _log;
    private readonly IConnectivityProbeService? _connectivityProbe;
    private readonly CancellationTokenSource _cts = new();
    private readonly SemaphoreSlim _concurrencySemaphore;
    private readonly BackfillProgressTracker _progressTracker;
    private readonly ConcurrentDictionary<BackfillRequestAttemptToken, ActiveBackfillAttempt> _inFlightRequests = new();
    private readonly ConcurrentDictionary<string, byte> _jobsBeingCancelled =
        new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<BackfillRequest, byte> _requestsBeingCancelled =
        new(ReferenceEqualityComparer.Instance);
    private readonly ConcurrentQueue<Exception> _cleanupFailures = new();
    private readonly object _lifecycleSync = new();
    private readonly object _lifecycleNotificationSync = new();
    private readonly object _jobCancellationSync = new();
    private readonly TaskCompletionSource _startNotificationPublished =
        new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly IDisposable _jobCancellationRegistration;
    private Task? _workerTask;
    private Task? _completionTask;
    private Task? _stopTask;
    private Task? _disposeTask;
    private bool _disposeRequested;
    private volatile bool _isRunning;

    // Rate limit backoff tracking
    private int _consecutiveEmptyPolls;
    private const int MaxRetryAttemptsPerRequest = 3;
    private static readonly TimeSpan EmptyPollBaseDelay = TimeSpan.FromMilliseconds(200);
    private static readonly TimeSpan EmptyPollMaxDelay = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan RateLimitBaseDelay = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan RateLimitMaxDelay = TimeSpan.FromSeconds(60);

    /// <summary>
    /// Event raised when a bar is successfully written to storage.
    /// </summary>
    public event Action<string, HistoricalBar>? OnBarWritten;

    /// <summary>
    /// Event raised when worker status changes.
    /// </summary>
    public event Action<bool>? OnRunningStateChanged;

    /// <summary>
    /// Provider-level progress relayed from the composite provider:
    /// per-provider attempts, failover, rate limiting, and outcomes.
    /// </summary>
    public event Action<ProviderBackfillProgress>? OnProviderProgress;

    public bool IsRunning => _isRunning;

    /// <summary>
    /// Gets the progress tracker for monitoring backfill progress per symbol.
    /// </summary>
    public BackfillProgressTracker ProgressTracker => _progressTracker;

    private const int MinConcurrentRequests = 1;
    private const int MaxConcurrentRequests = 100;

    public BackfillWorkerService(
        BackfillJobManager jobManager,
        BackfillRequestQueue requestQueue,
        CompositeHistoricalDataProvider provider,
        ProviderRateLimitTracker rateLimitTracker,
        BackfillJobsConfig config,
        AppConfig appConfig,
        string dataRoot,
        IConnectivityProbeService? connectivityProbe = null,
        ILogger? log = null)
    {
        if (config.MaxConcurrentRequests < MinConcurrentRequests || config.MaxConcurrentRequests > MaxConcurrentRequests)
        {
            throw new ArgumentOutOfRangeException(
                nameof(config),
                config.MaxConcurrentRequests,
                $"MaxConcurrentRequests must be between {MinConcurrentRequests} and {MaxConcurrentRequests}");
        }

        if (config.WorkerErrorRetryDelayMs <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(config),
                config.WorkerErrorRetryDelayMs,
                "WorkerErrorRetryDelayMs must be positive so the worker loop backs off after errors");
        }

        _jobManager = jobManager;
        _requestQueue = requestQueue;
        _provider = provider;
        _progressTracker = provider.ProgressTracker;
        _rateLimitTracker = rateLimitTracker;
        _config = config;
        _appConfig = appConfig;
        _dataRoot = dataRoot;
        _connectivityProbe = connectivityProbe;
        _log = log ?? LoggingSetup.ForContext<BackfillWorkerService>();
        _concurrencySemaphore = new SemaphoreSlim(config.MaxConcurrentRequests);

        // Subscribe to connectivity changes if offline-first mode is enabled
        if (_appConfig.OfflineFirstMode && _connectivityProbe != null)
        {
            _connectivityProbe.ConnectivityChanged += OnConnectivityChanged;
        }

        _provider.OnProgressUpdate += HandleProviderProgress;
        _jobCancellationRegistration = _jobManager.RegisterJobCancellationHandler(
            CancelJobAttemptsAsync,
            CancelUncommittedBatchAsync);
    }

    private void HandleProviderProgress(ProviderBackfillProgress progress)
    {
        _log.Debug(
            "Provider progress for {Symbol} via {Provider}: {Status} ({BarsDownloaded} bars) {Error}",
            progress.Symbol, progress.Provider, progress.CurrentStatus, progress.BarsDownloaded, progress.Error);
        OnProviderProgress?.Invoke(progress);
    }

    /// <summary>
    /// Start the worker service.
    /// </summary>
    public void Start()
    {
        lock (_lifecycleSync)
        {
            if (_disposeRequested)
                throw new ObjectDisposedException(nameof(BackfillWorkerService));
            if (_isRunning)
                return;
            if (_stopTask is not null)
                throw new InvalidOperationException("A stopped backfill worker cannot be restarted.");

            _isRunning = true;
            _workerTask = RunWorkerLoopAsync(_cts.Token);
            _completionTask = RunCompletionLoopAsync();
        }

        try
        {
            NotifyRunningStateChanged(true);
        }
        finally
        {
            // Stop may race Start after the worker tasks are published. Its false
            // notification waits on this barrier so observers can never see false, true.
            _startNotificationPublished.TrySetResult();
        }

        _log.Information("Backfill worker service started");
    }

    /// <summary>
    /// Stop the worker service.
    /// </summary>
    public Task StopAsync(CancellationToken ct = default)
    {
        Task stopTask;
        lock (_lifecycleSync)
        {
            if (_stopTask is not null)
            {
                stopTask = _stopTask;
            }
            else if (!_isRunning)
            {
                return Task.CompletedTask;
            }
            else
            {
                _stopTask = StopCoreAsync();
                stopTask = _stopTask;
            }
        }

        return stopTask.WaitAsync(ct);
    }

    private async Task StopCoreAsync()
    {
        _cts.Cancel();
        var failures = new List<Exception>(capacity: 4);

        try
        {
            await AwaitShutdownTaskAsync(_workerTask, failures).ConfigureAwait(false);

            // The worker loop no longer admits requests once cancellation is observed.
            // Await the remaining request tasks before reporting a stopped state so the
            // queue and provider can be disposed without racing active work. The completion
            // reader remains live while those producers publish into the bounded channel.
            var inFlight = _inFlightRequests.Values
                .Select(static attempt => attempt.Task)
                .ToArray();
            if (inFlight.Length > 0)
                await AwaitShutdownTaskAsync(Task.WhenAll(inFlight), failures).ConfigureAwait(false);

            // No admitted request can publish after this point. Close the writer and let the
            // completion loop drain every retained notification before it terminates.
            _requestQueue.CompleteCompletionNotifications();
            await AwaitShutdownTaskAsync(_completionTask, failures).ConfigureAwait(false);

            // A persisted Running/RateLimited job has no durable request queue to resume after
            // process restart. Move it to a truthful resumable state before owned resources close.
            foreach (var job in _jobManager.GetAllJobs()
                         .Where(static job => job.Status is BackfillJobStatus.Running or BackfillJobStatus.RateLimited))
            {
                try
                {
                    await _jobManager.PauseJobAsync(
                        job.JobId,
                        BackfillJobManager.HostShutdownPauseReason,
                        CancellationToken.None).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    failures.Add(new InvalidOperationException(
                        $"Failed to persist the shutdown state for backfill job {job.JobId}.",
                        ex));
                }
            }
        }
        finally
        {
            await _startNotificationPublished.Task.ConfigureAwait(false);
            _isRunning = false;
            NotifyRunningStateChanged(false);
            _log.Information("Backfill worker service stopped");
        }

        while (_cleanupFailures.TryDequeue(out var cleanupFailure))
            failures.Add(cleanupFailure);

        if (failures.Count > 0)
            throw new AggregateException("One or more backfill tasks failed during shutdown.", failures);
    }

    private async Task AwaitShutdownTaskAsync(Task? task, ICollection<Exception> failures)
    {
        if (task is null)
            return;

        try
        {
            await task.ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (_cts.IsCancellationRequested)
        {
            // Expected while loops and active requests observe worker shutdown.
        }
        catch (Exception ex)
        {
            failures.Add(ex);
        }
    }

    /// <summary>
    /// Main worker loop that processes requests from the queue.
    /// Uses exponential backoff when the queue is empty or providers are rate-limited.
    /// </summary>
    private async Task RunWorkerLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                if (_appConfig.OfflineFirstMode &&
                    _connectivityProbe is { IsOnline: false })
                {
                    await Task.Delay(EmptyPollMaxDelay, ct).ConfigureAwait(false);
                    continue;
                }

                // Wait for a slot
                await _concurrencySemaphore.WaitAsync(ct).ConfigureAwait(false);

                // Try to get a request
                var dequeuedAttempt = await _requestQueue.TryDequeueAsync(ct).ConfigureAwait(false);

                if (dequeuedAttempt is null)
                {
                    _concurrencySemaphore.Release();

                    // No requests available, check if all providers are rate-limited
                    if (CheckAllProvidersRateLimited())
                    {
                        _consecutiveEmptyPolls = 0;
                        await HandleAllProvidersRateLimitedAsync(ct).ConfigureAwait(false);
                    }
                    else
                    {
                        // Exponential backoff on consecutive empty polls
                        _consecutiveEmptyPolls++;
                        var delay = CalculateBackoff(
                            _consecutiveEmptyPolls,
                            EmptyPollBaseDelay,
                            EmptyPollMaxDelay);
                        await Task.Delay(delay, ct).ConfigureAwait(false);
                    }
                    continue;
                }

                // Reset empty poll counter on successful dequeue
                _consecutiveEmptyPolls = 0;

                // Process requests concurrently, but retain every task so shutdown can
                // quiesce them before queue/provider resources are released.
                var attempt = dequeuedAttempt.Value;
                var request = attempt.Request;
                var attemptToken = attempt.Token;
                var attemptCancellation = CancellationTokenSource.CreateLinkedTokenSource(ct);
                var startSignal = new TaskCompletionSource(
                    TaskCreationOptions.RunContinuationsAsynchronously);
                var trackedTask = TrackRequestAttemptAsync(
                    attemptToken,
                    request,
                    startSignal.Task,
                    attemptCancellation);
                var activeAttempt = new ActiveBackfillAttempt(
                    request.JobId,
                    request,
                    attemptCancellation,
                    trackedTask);

                lock (_jobCancellationSync)
                {
                    if (!_inFlightRequests.TryAdd(attemptToken, activeAttempt))
                    {
                        attemptCancellation.Cancel();
                        startSignal.SetResult();
                        throw new InvalidOperationException(
                            $"Backfill queue issued duplicate attempt identity {attemptToken.Value}.");
                    }

                    if (_jobsBeingCancelled.ContainsKey(request.JobId) ||
                        _requestsBeingCancelled.ContainsKey(request))
                    {
                        attemptCancellation.Cancel();
                    }
                }

                startSignal.SetResult();
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _log.Error(ex, "Error in worker loop");
                await Task.Delay(_config.WorkerErrorRetryDelayMs, ct).ConfigureAwait(false);
            }
        }
    }

    private async Task TrackRequestAttemptAsync(
        BackfillRequestAttemptToken attemptToken,
        BackfillRequest request,
        Task startSignal,
        CancellationTokenSource attemptCancellation)
    {
        var ct = attemptCancellation.Token;
        try
        {
            await startSignal.ConfigureAwait(false);
            await ProcessRequestAsync(request, attemptToken, ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // Cancellation is recorded on the request by ProcessRequestAsync.
        }
        catch (Exception ex)
        {
            var retainedFailure = new InvalidOperationException(
                $"Backfill request {request.RequestId}, attempt {attemptToken.Value}, failed during terminal cleanup.",
                ex);
            _cleanupFailures.Enqueue(retainedFailure);
            _log.Error(
                ex,
                "Backfill request task {RequestId}, attempt {AttemptId}, terminated unexpectedly",
                request.RequestId,
                attemptToken.Value);
        }
        finally
        {
            lock (_jobCancellationSync)
            {
                _inFlightRequests.TryRemove(attemptToken, out _);
                attemptCancellation.Dispose();
            }
        }
    }

    private async Task CancelJobAttemptsAsync(string jobId, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(jobId);
        ct.ThrowIfCancellationRequested();

        // Publish the cancellation fence before removing pending entries. An attempt already
        // dequeued but not yet registered will see this fence and begin with a cancelled token.
        _jobsBeingCancelled.TryAdd(jobId, 0);

        // Once cancellation is accepted, every remaining step is deliberately non-cancellable.
        // Returning early after revoking ownership would leave a durable Running job behind a
        // permanent cancellation fence.
        await _requestQueue.CancelJobRequestsAsync(
            jobId,
            CancellationToken.None).ConfigureAwait(false);

        while (true)
        {
            ActiveBackfillAttempt[] activeAttempts;
            lock (_jobCancellationSync)
            {
                activeAttempts = _inFlightRequests.Values
                    .Where(attempt => string.Equals(
                        attempt.JobId,
                        jobId,
                        StringComparison.Ordinal))
                    .ToArray();

                foreach (var attempt in activeAttempts)
                    attempt.Cancellation.Cancel();
            }

            if (activeAttempts.Length > 0)
            {
                await Task.WhenAll(activeAttempts.Select(static attempt => attempt.Task))
                    .ConfigureAwait(false);
            }

            // Queue ownership closes the tiny dequeue-to-registration race. Do not return until
            // both views agree that no attempt for this job remains admitted.
            var remainingRequests = await _requestQueue.GetJobRequestsAsync(
                    jobId,
                    CancellationToken.None)
                .ConfigureAwait(false);
            lock (_jobCancellationSync)
            {
                if (remainingRequests.Count == 0 &&
                    !_inFlightRequests.Values.Any(attempt => string.Equals(
                        attempt.JobId,
                        jobId,
                        StringComparison.Ordinal)))
                {
                    return;
                }
            }

            await Task.Yield();
        }
    }

    private async Task CancelUncommittedBatchAsync(
        IReadOnlyCollection<BackfillRequest> requests)
    {
        if (requests.Count == 0)
            return;

        var requestSet = new HashSet<BackfillRequest>(
            requests,
            ReferenceEqualityComparer.Instance);
        foreach (var request in requestSet)
            _requestsBeingCancelled.TryAdd(request, 0);

        try
        {
            // Pending entries never became durable job work, so remove them silently. Any entry
            // already dequeued is fenced above and must observe cancellation before rollback.
            await _requestQueue.RollbackPendingRequestsAsync(
                requests,
                CancellationToken.None).ConfigureAwait(false);

            while (true)
            {
                ActiveBackfillAttempt[] activeAttempts;
                lock (_jobCancellationSync)
                {
                    activeAttempts = _inFlightRequests.Values
                        .Where(attempt => requestSet.Contains(attempt.Request))
                        .ToArray();
                    foreach (var attempt in activeAttempts)
                        attempt.Cancellation.Cancel();
                }

                if (activeAttempts.Length > 0)
                {
                    await Task.WhenAll(activeAttempts.Select(static attempt => attempt.Task))
                        .ConfigureAwait(false);
                }

                var queueOwnsBatch = await _requestQueue.ContainsAnyRequestsAsync(
                    requests,
                    CancellationToken.None).ConfigureAwait(false);
                lock (_jobCancellationSync)
                {
                    if (!queueOwnsBatch &&
                        !_inFlightRequests.Values.Any(
                            attempt => requestSet.Contains(attempt.Request)))
                    {
                        return;
                    }
                }

                await Task.Yield();
            }
        }
        finally
        {
            foreach (var request in requestSet)
                _requestsBeingCancelled.TryRemove(request, out _);
        }
    }

    /// <summary>
    /// Process a single backfill request with automatic retry and exponential backoff
    /// for rate-limited responses. In offline-first mode, queues requests when offline.
    /// </summary>
    private async Task ProcessRequestAsync(
        BackfillRequest request,
        BackfillRequestAttemptToken attemptToken,
        CancellationToken ct)
    {
        using var activity = MarketDataTracing.StartBackfillActivity(
            request.AssignedProvider ?? "unknown",
            request.Symbol,
            request.FromDate.ToString("yyyy-MM-dd"),
            request.ToDate.ToString("yyyy-MM-dd"));

        activity?.SetTag("backfill.job_id", request.JobId);
        activity?.SetTag("backfill.request_id", request.RequestId);
        var correlationId = activity?.TraceId.ToString() ?? request.RequestId;
        var scopedLog = _log
            .ForContext("CorrelationId", correlationId)
            .ForContext("BackfillJobId", request.JobId)
            .ForContext("BackfillRequestId", request.RequestId)
            .ForContext("Symbol", request.Symbol);

        try
        {
            ct.ThrowIfCancellationRequested();

            // Check offline-first mode
            if (_appConfig.OfflineFirstMode && _connectivityProbe != null && !_connectivityProbe.IsOnline)
            {
                scopedLog.Warning("Offline mode: queueing backfill for {Symbol} until connectivity restored", request.Symbol);
                activity?.SetTag("backfill.outcome", "offline_queued");
                var requeued = await _requestQueue.RequeueInFlightAttemptAsync(
                    request,
                    attemptToken,
                    "Waiting for connectivity to be restored.",
                    ct).ConfigureAwait(false);
                if (!requeued)
                {
                    throw new InvalidOperationException(
                        $"Backfill request {request.RequestId} attempt {attemptToken.Value} lost queue ownership before offline requeue.");
                }
                return;
            }

            var retryAttempt = 0;

            while (true)
            {
                ct.ThrowIfCancellationRequested();

                try
                {
                    var providerName = request.AssignedProvider ?? _provider.Name;

                    scopedLog.Debug("Processing request: {Symbol} {From}-{To} via {Provider} (attempt {Attempt})",
                        request.Symbol, request.FromDate, request.ToDate, providerName, retryAttempt + 1);

                    // Fetch data from provider
                    using var fetchActivity = MarketDataTracing.StartBackfillFetchActivity(
                        providerName,
                        request.Symbol);
                    var bars = await FetchBarsAsync(request, ct).ConfigureAwait(false);
                    MarketDataTracing.RecordEventCount(fetchActivity, bars.Count);

                    bars = BackfillBarValidation.RemoveFutureDatedBars(bars, out var futureDropped);
                    if (futureDropped > 0)
                    {
                        scopedLog.Warning(
                            "Dropped {FutureDropped} future-dated bars for {Symbol} from {Provider}",
                            futureDropped, request.Symbol, providerName);
                    }

                    if (BackfillBarValidation.EvaluateDailyRecency(bars, request.ToDate) is { } staleVerdict)
                    {
                        scopedLog.Warning(
                            "Stale backfill persisted for {Symbol} via {Provider}: {StaleReason}. The provider's dataset may be frozen or paywalled.",
                            request.Symbol, providerName, staleVerdict.Description);
                    }

                    if (bars.Count > 0)
                    {
                        // Write to storage
                        using var storageActivity = MarketDataTracing.StartBackfillStorageActivity(request.Symbol, bars.Count);
                        await WriteBarsToStorageAsync(request, bars, ct).ConfigureAwait(false);
                        MarketDataTracing.RecordEventCount(storageActivity, bars.Count);
                        request.BarsRetrieved = bars.Count;

                        // Record progress
                        _progressTracker.RecordProgress(request.Symbol, bars.Count);
                    }

                    // Mark as complete
                    await _requestQueue.CompleteRequestAttemptAsync(
                        request,
                        attemptToken,
                        success: true,
                        ct: ct).ConfigureAwait(false);
                    await _jobManager.UpdateJobProgressAsync(request, ct).ConfigureAwait(false);
                    _progressTracker.MarkCompleted(request.Symbol);

                    MarketDataTracing.RecordEventCount(activity, bars.Count);
                    activity?.SetTag("backfill.outcome", "success");
                    return;
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested)
                {
                    activity?.SetTag("backfill.outcome", "cancelled");
                    throw;
                }
                catch (Exception ex)
                {
                    // Typed RateLimitException (thrown directly or wrapped in aggregate/inner chains)
                    // is located here, so a dedicated typed catch would duplicate this path.
                    var rateLimit = FindRateLimitException(ex);
                    var isRateLimited = IsRateLimited(ex);
                    var retryAfter = isRateLimited
                        ? rateLimit?.RetryAfter ?? TryExtractRetryAfter(ex)
                        : null;
                    var rateLimitedProvider = ResolveRateLimitedProvider(ex, request.AssignedProvider);

                    if (isRateLimited && rateLimitedProvider is not null)
                    {
                        _requestQueue.RecordProviderRateLimitHit(rateLimitedProvider, retryAfter);

                        // Retry with Retry-After or exponential backoff if within retry budget
                        if (retryAttempt < MaxRetryAttemptsPerRequest)
                        {
                            retryAttempt++;
                            var delay = retryAfter ?? CalculateBackoff(retryAttempt, RateLimitBaseDelay, RateLimitMaxDelay);

                            activity?.SetTag("backfill.retry_count", retryAttempt);
                            scopedLog.Information(
                                "Rate limited for {Symbol} via {Provider}, retrying in {Delay}ms via {DelaySource} (attempt {Attempt}/{Max})",
                                request.Symbol, rateLimitedProvider, delay.TotalMilliseconds,
                                retryAfter.HasValue ? "provider-specified cooldown" : "calculated exponential backoff",
                                retryAttempt, MaxRetryAttemptsPerRequest);
                            await Task.Delay(delay, ct).ConfigureAwait(false);
                            continue;
                        }

                        scopedLog.Warning(
                            "Rate limit retry budget exhausted for {Symbol} via {Provider} after {Attempts} attempts",
                            request.Symbol, rateLimitedProvider, retryAttempt);
                    }

                    MarketDataTracing.RecordError(activity, ex);
                    activity?.SetTag("backfill.outcome", isRateLimited ? "rate_limit_exhausted" : "error");
                    await _requestQueue.CompleteRequestAttemptAsync(
                        request,
                        attemptToken,
                        success: false,
                        error: ex.Message,
                        ct: ct).ConfigureAwait(false);
                    await _jobManager.UpdateJobProgressAsync(request, ct).ConfigureAwait(false);
                    _progressTracker.MarkFailed(request.Symbol, ex.Message);
                    return;
                }
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            activity?.SetTag("backfill.outcome", "cancelled");
            var cancellationReason = _requestsBeingCancelled.ContainsKey(request)
                ? "Cancelled because the owning backfill job start did not commit."
                : _jobsBeingCancelled.ContainsKey(request.JobId)
                    ? "Cancelled with the owning backfill job."
                    : "Cancelled while the backfill worker was stopping.";
            await MarkRequestCancelledAsync(
                request,
                attemptToken,
                cancellationReason,
                scopedLog).ConfigureAwait(false);
            throw;
        }
        finally
        {
            _concurrencySemaphore.Release();
        }
    }

    private async Task MarkRequestCancelledAsync(
        BackfillRequest request,
        BackfillRequestAttemptToken attemptToken,
        string cancellationReason,
        ILogger scopedLog)
    {
        if (request.Status != BackfillRequestStatus.InProgress)
            return;

        var cancelled = await _requestQueue.CancelInFlightAttemptAsync(
            request,
            attemptToken,
            cancellationReason,
            CancellationToken.None).ConfigureAwait(false);

        if (cancelled)
        {
            scopedLog.Information("Backfill request cancelled: {Reason}", cancellationReason);
        }
        else
        {
            scopedLog.Warning(
                "Backfill request cancellation ignored because attempt {AttemptId} was no longer queue-owned",
                attemptToken.Value);
        }
    }

    private void NotifyRunningStateChanged(bool isRunning)
    {
        lock (_lifecycleNotificationSync)
        {
            var handlers = OnRunningStateChanged;
            if (handlers is null)
                return;

            foreach (var handler in handlers.GetInvocationList().Cast<Action<bool>>())
            {
                try
                {
                    handler(isRunning);
                }
                catch (Exception ex)
                {
                    _cleanupFailures.Enqueue(new InvalidOperationException(
                        $"A backfill lifecycle observer failed while publishing IsRunning={isRunning}.",
                        ex));
                    _log.Error(
                        ex,
                        "Backfill lifecycle observer failed while publishing IsRunning={IsRunning}",
                        isRunning);
                }
            }
        }
    }

    /// <summary>
    /// Calculates exponential backoff delay with jitter.
    /// </summary>
    private static TimeSpan CalculateBackoff(int attempt, TimeSpan baseDelay, TimeSpan maxDelay)
        => Backoff.ExponentialDelay(attempt, baseDelay, maxDelay, jitterFraction: 0.25);

    /// <summary>
    /// Find a typed <see cref="RateLimitException"/> anywhere in the exception chain,
    /// including inside <see cref="AggregateException"/> trees thrown by the composite provider.
    /// </summary>
    internal static RateLimitException? FindRateLimitException(Exception ex)
    {
        if (ex is RateLimitException rateLimit)
            return rateLimit;

        if (ex is AggregateException aggregate)
        {
            foreach (var inner in aggregate.InnerExceptions)
            {
                if (FindRateLimitException(inner) is { } found)
                    return found;
            }

            return null;
        }

        return ex.InnerException is { } innerException ? FindRateLimitException(innerException) : null;
    }

    /// <summary>
    /// Resolves the provider whose budget should be charged for a throttle response.
    /// Composite providers can fail over after assignment, so typed provider metadata
    /// is authoritative when it is present.
    /// </summary>
    internal static string? ResolveRateLimitedProvider(Exception ex, string? assignedProvider)
    {
        ArgumentNullException.ThrowIfNull(ex);
        var provider = EnumerateExceptionTree(ex)
            .OfType<RateLimitException>()
            .Select(static rateLimit => rateLimit.Provider)
            .FirstOrDefault(static candidate => !string.IsNullOrWhiteSpace(candidate));
        return string.IsNullOrWhiteSpace(provider) ? assignedProvider : provider;
    }

    /// <summary>
    /// Classifies rate limiting only from typed provider metadata or a preserved HTTP 429 status.
    /// Exception-message text is deliberately not treated as an accounting-relevant signal.
    /// </summary>
    internal static bool IsRateLimited(Exception ex)
    {
        ArgumentNullException.ThrowIfNull(ex);
        return FindRateLimitException(ex) is not null || IsHttp429(ex);
    }

    /// <summary>
    /// Extracts Retry-After delay from an exception chain.
    /// Supports both delta-seconds ("120") and HTTP-date ("Thu, 01 Dec 2024 16:00:00 GMT") formats
    /// as defined in RFC 7231 Section 7.1.3.
    /// </summary>
    internal static TimeSpan? TryExtractRetryAfter(Exception ex)
    {
        ArgumentNullException.ThrowIfNull(ex);

        // Walk every aggregate branch rather than AggregateException.InnerException,
        // which exposes only the first child and can hide the actual throttled provider.
        foreach (var current in EnumerateExceptionTree(ex))
        {
            if (current is RateLimitException { RetryAfter: { } typedRetryAfter })
                return CapRetryAfter(typedRetryAfter);

            if (TryExtractRetryAfterFromExceptionData(current) is { } retryAfterFromData)
                return retryAfterFromData;

            if (current is HttpRequestException httpEx)
            {
                if (TryExtractRetryAfterFromExceptionData(httpEx) is { } retryAfterFromHttpData)
                    return retryAfterFromHttpData;

                // Some providers embed the header value in the message
                var retryAfter = TryParseRetryAfterFromMessage(httpEx.Message);
                if (retryAfter.HasValue)
                    return retryAfter;
            }

            // Also check if message contains "Retry-After: <value>" pattern
            if (current.Message.Contains("Retry-After", StringComparison.OrdinalIgnoreCase))
            {
                var retryAfter = TryParseRetryAfterFromMessage(current.Message);
                if (retryAfter.HasValue)
                    return retryAfter;
            }
        }

        return null;
    }

    private static IEnumerable<Exception> EnumerateExceptionTree(Exception ex)
    {
        yield return ex;

        if (ex is AggregateException aggregate)
        {
            foreach (var inner in aggregate.InnerExceptions)
            {
                foreach (var descendant in EnumerateExceptionTree(inner))
                    yield return descendant;
            }

            yield break;
        }

        if (ex.InnerException is { } innerException)
        {
            foreach (var descendant in EnumerateExceptionTree(innerException))
                yield return descendant;
        }
    }

    private static TimeSpan? TryExtractRetryAfterFromExceptionData(Exception ex)
    {
        if (ex.Data.Count == 0)
            return null;

        foreach (System.Collections.DictionaryEntry entry in ex.Data)
        {
            if (entry.Key is string key &&
                (key.Equals("Retry-After", StringComparison.OrdinalIgnoreCase) ||
                 key.Equals("RetryAfter", StringComparison.OrdinalIgnoreCase)) &&
                entry.Value is not null)
            {
                var parsed = TryParseRetryAfterValue(entry.Value.ToString());
                if (parsed.HasValue)
                    return parsed;
            }

            if (entry.Value is HttpResponseMessage response)
            {
                var parsed = TryParseRetryAfterFromResponse(response);
                if (parsed.HasValue)
                    return parsed;
            }
        }

        return null;
    }

    private static TimeSpan? TryParseRetryAfterFromResponse(HttpResponseMessage response)
    {
        if (response.Headers.RetryAfter?.Delta is { } delta)
            return CapRetryAfter(delta);

        if (response.Headers.RetryAfter?.Date is { } date)
        {
            var delay = date - DateTimeOffset.UtcNow;
            if (delay > TimeSpan.Zero)
                return CapRetryAfter(delay);
        }

        if (response.Headers.TryGetValues("Retry-After", out var values))
        {
            var headerValue = values.FirstOrDefault();
            if (!string.IsNullOrWhiteSpace(headerValue))
                return TryParseRetryAfterValue(headerValue);
        }

        return null;
    }

    private static TimeSpan? TryParseRetryAfterValue(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        if (int.TryParse(value, out var seconds) && seconds > 0)
            return CapRetryAfter(TimeSpan.FromSeconds(seconds));

        if (DateTimeOffset.TryParse(value, out var retryDate))
        {
            var delay = retryDate - DateTimeOffset.UtcNow;
            if (delay > TimeSpan.Zero)
                return CapRetryAfter(delay);
        }

        return null;
    }

    private static TimeSpan CapRetryAfter(TimeSpan delay)
    {
        var cap = TimeSpan.FromMinutes(5);
        return delay > cap ? cap : delay;
    }

    private static bool IsHttp429(Exception ex)
    {
        if (ex is HttpRequestException { StatusCode: System.Net.HttpStatusCode.TooManyRequests })
            return true;

        if (ex is AggregateException aggregate)
            return aggregate.Flatten().InnerExceptions.Any(IsHttp429);

        return ex.InnerException is not null && IsHttp429(ex.InnerException);
    }

    /// <summary>
    /// Attempts to parse a Retry-After value from a message string.
    /// Supports delta-seconds and HTTP-date formats.
    /// </summary>
    private static TimeSpan? TryParseRetryAfterFromMessage(string message)
    {
        // Look for "Retry-After: <value>" or "retry-after: <value>" pattern
        const string prefix = "Retry-After:";
        var idx = message.IndexOf(prefix, StringComparison.OrdinalIgnoreCase);
        if (idx < 0)
            return null;

        var valueStart = idx + prefix.Length;
        var valueEnd = message.IndexOf('\n', valueStart);
        if (valueEnd < 0)
            valueEnd = message.Length;

        var value = message[valueStart..valueEnd].Trim().TrimEnd('\r');

        return TryParseRetryAfterValue(value);
    }

    /// <summary>
    /// Fetch bars from the assigned provider.
    /// </summary>
    private async Task<IReadOnlyList<HistoricalBar>> FetchBarsAsync(BackfillRequest request, CancellationToken ct)
    {
        // Track the request
        if (request.AssignedProvider != null)
        {
            _rateLimitTracker.RecordRequest(request.AssignedProvider);
        }

        // Use composite provider which handles fallback
        var bars = await _provider.GetDailyBarsAsync(
            request.Symbol,
            request.FromDate,
            request.ToDate,
            ct).ConfigureAwait(false);

        return bars;
    }

    /// <summary>
    /// Write bars to storage.
    /// </summary>
    private async Task WriteBarsToStorageAsync(BackfillRequest request, IReadOnlyList<HistoricalBar> bars, CancellationToken ct)
    {
        // Group by date for daily partitioning
        var barsByDate = bars.GroupBy(b => b.SessionDate);

        foreach (var dateGroup in barsByDate)
        {
            ct.ThrowIfCancellationRequested();

            var date = dateGroup.Key;
            var dateBars = dateGroup.ToList();

            // Route historical writes through the shared storage policy and atomic writer
            // so backfill data lands in the same durable, predictable structure as the
            // rest of the JSONL storage stack.
            // Partition the storage path by the provider that actually served the bars
            // (each bar carries its own Source stamp); the top-level provider name is only
            // a fallback and may be "composite" on the failover path.
            var exemplarSource = string.IsNullOrWhiteSpace(dateBars[0].Source) ||
                                 string.Equals(dateBars[0].Source, "composite", StringComparison.OrdinalIgnoreCase)
                ? _provider.Name
                : dateBars[0].Source;
            var exemplarEvent = MarketEvent.HistoricalBar(
                dateBars[0].ToTimestampUtc(),
                dateBars[0].Symbol,
                dateBars[0],
                exemplarSource,
                dateBars[0].SequenceNumber);
            var filePath = BuildFilePath(request.Granularity, exemplarEvent);

            var lines = dateBars.Select(b => JsonSerializer.Serialize(
                b,
                MarketDataJsonContext.Default.HistoricalBar));
            await AtomicFileWriter.AppendLinesAsync(filePath, lines, ct).ConfigureAwait(false);

            foreach (var bar in dateBars)
            {
                OnBarWritten?.Invoke(filePath, bar);
            }
        }

    }

    /// <summary>
    /// Build the file path for storing bars.
    /// </summary>
    private string BuildFilePath(DataGranularity granularity, MarketEvent evt)
    {
        var granularityName = granularity switch
        {
            DataGranularity.Daily => "daily",
            DataGranularity.Hour1 => "hourly",
            DataGranularity.Minute1 => "1min",
            DataGranularity.Minute5 => "5min",
            DataGranularity.Minute15 => "15min",
            DataGranularity.Minute30 => "30min",
            _ => "daily"
        };

        var policy = new JsonlStoragePolicy(new StorageOptions
        {
            RootPath = _dataRoot,
            NamingConvention = FileNamingConvention.BySymbol,
            DatePartition = DatePartition.Daily,
            FilePrefix = $"bar_{granularityName}"
        });

        return policy.GetPath(evt);
    }

    /// <summary>
    /// Check if all providers are rate-limited.
    /// </summary>
    private bool CheckAllProvidersRateLimited()
    {
        var status = _rateLimitTracker.GetAllStatus();
        return status.Values.All(s => s.IsRateLimited);
    }

    /// <summary>
    /// Handle situation where all providers are rate-limited.
    /// </summary>
    private async Task HandleAllProvidersRateLimitedAsync(CancellationToken ct)
    {
        var status = _rateLimitTracker.GetAllStatus();
        var shortestWait = status.Values
            .Where(s => s.TimeUntilReset.HasValue)
            .Select(s => s.TimeUntilReset!.Value)
            .DefaultIfEmpty(TimeSpan.FromMinutes(1))
            .Min();

        if (shortestWait > TimeSpan.FromMinutes(_config.MaxRateLimitWaitMinutes))
        {
            // Pause all running jobs if wait is too long
            if (_config.AutoPauseOnRateLimit)
            {
                var runningJobs = _jobManager.GetJobsByStatus(BackfillJobStatus.Running);
                foreach (var job in runningJobs)
                {
                    await _jobManager.SetJobRateLimitedAsync(job.JobId, shortestWait, ct).ConfigureAwait(false);
                }
            }

            _log.Information("All providers rate-limited for {Wait}, jobs paused", shortestWait);
        }
        else
        {
            _log.Information("All providers rate-limited, waiting {Wait} for reset", shortestWait);
            await Task.Delay(shortestWait, ct).ConfigureAwait(false);

            // Resume rate-limited jobs if auto-resume is enabled
            if (_config.AutoResumeAfterRateLimit)
            {
                var rateLimitedJobs = _jobManager.GetJobsByStatus(BackfillJobStatus.RateLimited);
                foreach (var job in rateLimitedJobs)
                {
                    await _jobManager.ResumeJobAsync(job.JobId, ct).ConfigureAwait(false);
                }
            }
        }
    }

    /// <summary>
    /// Process completed requests and update job progress.
    /// </summary>
    private async Task RunCompletionLoopAsync()
    {
        await foreach (var request in _requestQueue.CompletedRequests.ReadAllAsync())
        {
            // Progress is already updated in ProcessRequestAsync.
            // This loop owns the lossless bounded completion drain.

            _log.Verbose("Request {RequestId} completed: {Status}",
                request.RequestId, request.Status);
        }
    }

    /// <summary>
    /// Handles connectivity state changes. When going online, triggers reprocessing of offline-queued requests.
    /// </summary>
    private void OnConnectivityChanged(object? sender, bool isOnline)
    {
        if (isOnline)
        {
            _log.Information("Connectivity restored, reprocessing offline-queued backfill requests");
            // Signal the worker loop to check for queued requests
            // This happens naturally as the loop will process any items in the queue
        }
        else
        {
            _log.Information("Connectivity lost, future backfill requests will be queued offline");
        }
    }

    public void Dispose()
        => DisposeAsync().AsTask().GetAwaiter().GetResult();

    public ValueTask DisposeAsync()
    {
        Task disposeTask;
        lock (_lifecycleSync)
        {
            if (_disposeTask is null)
            {
                _disposeRequested = true;
                _disposeTask = DisposeCoreAsync();
            }

            disposeTask = _disposeTask;
        }

        return new ValueTask(disposeTask);
    }

    private async Task DisposeCoreAsync()
    {
        var failures = new List<Exception>(capacity: 4);

        try
        {
            await StopAsync(CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            failures.Add(ex);
        }

        CaptureCleanupFailure(
            failures,
            () =>
            {
                if (_appConfig.OfflineFirstMode && _connectivityProbe != null)
                    _connectivityProbe.ConnectivityChanged -= OnConnectivityChanged;
            });
        CaptureCleanupFailure(
            failures,
            () =>
            {
                _provider.OnProgressUpdate -= HandleProviderProgress;
            });
        CaptureCleanupFailure(failures, _jobCancellationRegistration.Dispose);
        CaptureCleanupFailure(failures, _cts.Dispose);
        CaptureCleanupFailure(failures, _concurrencySemaphore.Dispose);

        if (failures.Count > 0)
            throw new AggregateException("Backfill worker disposal completed with failures.", failures);
    }

    private static void CaptureCleanupFailure(ICollection<Exception> failures, Action cleanup)
    {
        try
        {
            cleanup();
        }
        catch (Exception ex)
        {
            failures.Add(ex);
        }
    }

    private sealed record ActiveBackfillAttempt(
        string JobId,
        BackfillRequest Request,
        CancellationTokenSource Cancellation,
        Task Task);
}

/// <summary>
/// Factory for creating backfill service instances.
/// </summary>
public sealed class BackfillServiceFactory
{
    private readonly ILogger _log;
    private readonly ISymbolResolver? _symbolResolver;

    public BackfillServiceFactory(ILogger? log = null, ISymbolResolver? symbolResolver = null)
    {
        _log = log ?? LoggingSetup.ForContext<BackfillServiceFactory>();
        _symbolResolver = symbolResolver;
    }

    /// <summary>
    /// Create a complete backfill service stack from configuration.
    /// </summary>
    public BackfillServices CreateServices(
        AppConfig appConfig,
        BackfillConfig config,
        string dataRoot,
        IEnumerable<IHistoricalDataProvider> providers,
        IConnectivityProbeService? connectivityProbe = null)
    {
        var jobsConfig = config.Jobs ?? new BackfillJobsConfig();
        var dataRootGuard = new RootedPathGuard(dataRoot);
        var jobsDirectorySegments = ResolveRelativeDirectorySegments(
            jobsConfig.JobsDirectory,
            nameof(jobsConfig.JobsDirectory));
        var jobsDirectory = dataRootGuard.ResolvePath(jobsDirectorySegments);

        // Create rate limit tracker
        var rateLimitTracker = new ProviderRateLimitTracker(_log);

        // Register providers with rate limit tracker
        foreach (var provider in providers)
        {
            rateLimitTracker.RegisterProvider(provider);
        }

        var composite = new CompositeHistoricalDataProvider(
            providers,
            config.EnableSymbolResolution ? _symbolResolver : null,
            enableRateLimitRotation: config.EnableRateLimitRotation,
            rateLimitRotationThreshold: config.RateLimitRotationThreshold,
            log: _log);

        // Create gap analyzer
        var gapAnalyzer = new DataGapAnalyzer(dataRoot, _log);

        // Create request queue
        var requestQueue = new BackfillRequestQueue(rateLimitTracker, _log)
        {
            MaxConcurrentRequests = jobsConfig.MaxConcurrentRequests,
            MaxConcurrentPerProvider = jobsConfig.MaxConcurrentPerProvider
        };

        // Create job manager
        var jobManager = new BackfillJobManager(gapAnalyzer, requestQueue, jobsDirectory, _log);

        // Create worker service with offline-first support
        var worker = new BackfillWorkerService(
            jobManager,
            requestQueue,
            composite,
            rateLimitTracker,
            jobsConfig,
            appConfig,
            dataRoot,
            connectivityProbe,
            _log);

        return new BackfillServices(
            jobManager,
            requestQueue,
            gapAnalyzer,
            rateLimitTracker,
            composite,
            worker,
            ownedSymbolResolver: null);
    }

    private static string[] ResolveRelativeDirectorySegments(
        string relativeDirectory,
        string parameterName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(relativeDirectory, parameterName);
        if (Path.IsPathRooted(relativeDirectory))
            throw new ArgumentException("The jobs directory must be relative to DataRoot.", parameterName);

        var segments = relativeDirectory.Split(
            ['/', '\\'],
            StringSplitOptions.None);
        if (segments.Length == 0)
            throw new ArgumentException("The jobs directory must contain at least one path segment.", parameterName);

        foreach (var segment in segments)
        {
            RootedPathGuard.ValidatePathSegment(segment, parameterName);
        }

        return segments;
    }
}

/// <summary>
/// Container for all backfill-related services.
/// </summary>
public sealed class BackfillServices : IDisposable, IAsyncDisposable
{
    public BackfillJobManager JobManager { get; }
    public BackfillRequestQueue RequestQueue { get; }
    public DataGapAnalyzer GapAnalyzer { get; }
    public ProviderRateLimitTracker RateLimitTracker { get; }
    public CompositeHistoricalDataProvider Provider { get; }
    public BackfillWorkerService Worker { get; }

    private readonly IDisposable? _ownedSymbolResolver;
    private readonly object _disposeSync = new();
    private Task? _disposeTask;

    public BackfillServices(
        BackfillJobManager jobManager,
        BackfillRequestQueue requestQueue,
        DataGapAnalyzer gapAnalyzer,
        ProviderRateLimitTracker rateLimitTracker,
        CompositeHistoricalDataProvider provider,
        BackfillWorkerService worker,
        IDisposable? ownedSymbolResolver = null)
    {
        JobManager = jobManager;
        RequestQueue = requestQueue;
        GapAnalyzer = gapAnalyzer;
        RateLimitTracker = rateLimitTracker;
        Provider = provider;
        Worker = worker;
        _ownedSymbolResolver = ownedSymbolResolver;
    }

    /// <summary>
    /// Initialize services (load persisted jobs).
    /// </summary>
    public async Task InitializeAsync(CancellationToken ct = default)
    {
        await JobManager.LoadJobsAsync(ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Start the worker service.
    /// </summary>
    public void StartWorker()
    {
        Worker.Start();
    }

    /// <summary>
    /// Stop the worker service.
    /// </summary>
    public async Task StopWorkerAsync(CancellationToken ct = default)
    {
        await Worker.StopAsync(ct).ConfigureAwait(false);
    }

    public void Dispose()
        => DisposeAsync().AsTask().GetAwaiter().GetResult();

    public ValueTask DisposeAsync()
    {
        Task disposeTask;
        lock (_disposeSync)
        {
            _disposeTask ??= DisposeCoreAsync();
            disposeTask = _disposeTask;
        }

        return new ValueTask(disposeTask);
    }

    private async Task DisposeCoreAsync()
    {
        var failures = new List<Exception>(capacity: 6);

        try
        {
            await Worker.DisposeAsync().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            failures.Add(ex);
        }

        CaptureCleanupFailure(failures, RequestQueue.Dispose);
        CaptureCleanupFailure(failures, JobManager.Dispose);
        CaptureCleanupFailure(failures, RateLimitTracker.Dispose);
        CaptureCleanupFailure(failures, Provider.Dispose);

        if (_ownedSymbolResolver is not null)
            CaptureCleanupFailure(failures, _ownedSymbolResolver.Dispose);

        if (failures.Count > 0)
            throw new AggregateException("Backfill service disposal completed with failures.", failures);
    }

    private static void CaptureCleanupFailure(ICollection<Exception> failures, Action cleanup)
    {
        try
        {
            cleanup();
        }
        catch (Exception ex)
        {
            failures.Add(ex);
        }
    }
}
