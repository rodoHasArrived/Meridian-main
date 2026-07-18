using System.Diagnostics;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using Meridian.Core.Config;
using Meridian.Core.Exceptions;
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
public sealed class BackfillWorkerService : IDisposable
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
    private Task? _workerTask;
    private Task? _completionTask;
    private bool _disposed;
    private bool _isRunning;

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
        if (_isRunning)
            return;

        _isRunning = true;
        _workerTask = RunWorkerLoopAsync(_cts.Token);
        _completionTask = RunCompletionLoopAsync(_cts.Token);

        OnRunningStateChanged?.Invoke(true);
        _log.Information("Backfill worker service started");
    }

    /// <summary>
    /// Stop the worker service.
    /// </summary>
    public async Task StopAsync(CancellationToken ct = default)
    {
        if (!_isRunning)
            return;

        _cts.Cancel();

        try
        {
            if (_workerTask != null)
                await _workerTask.ConfigureAwait(false);
            if (_completionTask != null)
                await _completionTask.ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // Expected
        }

        _isRunning = false;
        OnRunningStateChanged?.Invoke(false);
        _log.Information("Backfill worker service stopped");
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
                // Wait for a slot
                await _concurrencySemaphore.WaitAsync(ct).ConfigureAwait(false);

                // Try to get a request
                var request = await _requestQueue.TryDequeueAsync(ct).ConfigureAwait(false);

                if (request == null)
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

                // Process request in background
                _ = ProcessRequestAsync(request, ct);
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

    /// <summary>
    /// Process a single backfill request with automatic retry and exponential backoff
    /// for rate-limited responses. In offline-first mode, queues requests when offline.
    /// </summary>
    private async Task ProcessRequestAsync(BackfillRequest request, CancellationToken ct)
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
            // Check offline-first mode
            if (_appConfig.OfflineFirstMode && _connectivityProbe != null && !_connectivityProbe.IsOnline)
            {
                scopedLog.Warning("Offline mode: queueing backfill for {Symbol} until connectivity restored", request.Symbol);
                activity?.SetTag("backfill.outcome", "offline_queued");
                await _requestQueue.EnqueueAsync(request, ct).ConfigureAwait(false);
                return;
            }

            var retryAttempt = 0;

            while (!ct.IsCancellationRequested)
            {
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
                    await _requestQueue.CompleteRequestAsync(request, true, ct: ct).ConfigureAwait(false);
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
                    await _requestQueue.CompleteRequestAsync(request, false, ex.Message, ct).ConfigureAwait(false);
                    await _jobManager.UpdateJobProgressAsync(request, ct).ConfigureAwait(false);
                    _progressTracker.MarkFailed(request.Symbol, ex.Message);
                    return;
                }
            }
        }
        finally
        {
            _concurrencySemaphore.Release();
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
            var exemplarEvent = MarketEvent.HistoricalBar(
                dateBars[0].ToTimestampUtc(),
                dateBars[0].Symbol,
                dateBars[0],
                dateBars[0].SequenceNumber,
                _provider.Name);
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
    private async Task RunCompletionLoopAsync(CancellationToken ct)
    {
        try
        {
            await foreach (var request in _requestQueue.CompletedRequests.ReadAllAsync(ct))
            {
                // Progress is already updated in ProcessRequestAsync
                // This loop is for additional processing if needed

                _log.Verbose("Request {RequestId} completed: {Status}",
                    request.RequestId, request.Status);
            }
        }
        catch (OperationCanceledException)
        {
            // Expected when stopping
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
    {
        if (_disposed)
            return;
        _disposed = true;

        if (_appConfig.OfflineFirstMode && _connectivityProbe != null)
        {
            _connectivityProbe.ConnectivityChanged -= OnConnectivityChanged;
        }

        _provider.OnProgressUpdate -= HandleProviderProgress;

        _cts.Cancel();
        _cts.Dispose();
        _concurrencySemaphore.Dispose();
    }
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
        var jobsDirectory = Path.Combine(dataRoot, jobsConfig.JobsDirectory);

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
}

/// <summary>
/// Container for all backfill-related services.
/// </summary>
public sealed class BackfillServices : IDisposable
{
    public BackfillJobManager JobManager { get; }
    public BackfillRequestQueue RequestQueue { get; }
    public DataGapAnalyzer GapAnalyzer { get; }
    public ProviderRateLimitTracker RateLimitTracker { get; }
    public CompositeHistoricalDataProvider Provider { get; }
    public BackfillWorkerService Worker { get; }

    private readonly IDisposable? _ownedSymbolResolver;

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
        await Worker.StopAsync().ConfigureAwait(false);
    }

    public void Dispose()
    {
        Worker.Dispose();
        RequestQueue.Dispose();
        JobManager.Dispose();
        RateLimitTracker.Dispose();
        Provider.Dispose();
        _ownedSymbolResolver?.Dispose();
    }
}
