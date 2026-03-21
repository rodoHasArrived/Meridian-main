using System.Net.Http;
using System.Text.Json;
using System.Threading;
using Meridian.Application.Config;
using Meridian.Application.Exceptions;
using Meridian.Application.Logging;
using Meridian.Contracts.Domain.Models;
using Meridian.Domain.Models;
using Serilog;

namespace Meridian.Infrastructure.Adapters.Core;

/// <summary>
/// Background worker service that processes the backfill request queue.
/// Handles rate limits, retries, and writes data to storage.
/// </summary>
public sealed class BackfillWorkerService : IDisposable
{
    private readonly BackfillJobManager _jobManager;
    private readonly BackfillRequestQueue _requestQueue;
    private readonly CompositeHistoricalDataProvider _provider;
    private readonly ProviderRateLimitTracker _rateLimitTracker;
    private readonly BackfillJobsConfig _config;
    private readonly string _dataRoot;
    private readonly ILogger _log;
    private readonly CancellationTokenSource _cts = new();
    private readonly SemaphoreSlim _concurrencySemaphore;
    private readonly BackfillProgressTracker _progressTracker = new();
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
        string dataRoot,
        ILogger? log = null)
    {
        if (config.MaxConcurrentRequests < MinConcurrentRequests || config.MaxConcurrentRequests > MaxConcurrentRequests)
        {
            throw new ArgumentOutOfRangeException(
                nameof(config),
                config.MaxConcurrentRequests,
                $"MaxConcurrentRequests must be between {MinConcurrentRequests} and {MaxConcurrentRequests}");
        }

        _jobManager = jobManager;
        _requestQueue = requestQueue;
        _provider = provider;
        _rateLimitTracker = rateLimitTracker;
        _config = config;
        _dataRoot = dataRoot;
        _log = log ?? LoggingSetup.ForContext<BackfillWorkerService>();
        _concurrencySemaphore = new SemaphoreSlim(config.MaxConcurrentRequests);
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
                await Task.Delay(1000, ct).ConfigureAwait(false);
            }
        }
    }

    /// <summary>
    /// Process a single backfill request with automatic retry and exponential backoff
    /// for rate-limited responses.
    /// </summary>
    private async Task ProcessRequestAsync(BackfillRequest request, CancellationToken ct)
    {
        var retryAttempt = 0;

        try
        {
            while (!ct.IsCancellationRequested)
            {
                try
                {
                    _log.Debug("Processing request: {Symbol} {From}-{To} via {Provider} (attempt {Attempt})",
                        request.Symbol, request.FromDate, request.ToDate, request.AssignedProvider, retryAttempt + 1);

                    // Fetch data from provider
                    var bars = await FetchBarsAsync(request, ct).ConfigureAwait(false);

                    if (bars.Count > 0)
                    {
                        // Write to storage
                        await WriteBarsToStorageAsync(request, bars, ct).ConfigureAwait(false);
                        request.BarsRetrieved = bars.Count;

                        // Record progress
                        _progressTracker.RecordProgress(request.Symbol, bars.Count);
                    }

                    // Mark as complete
                    await _requestQueue.CompleteRequestAsync(request, true, ct: ct).ConfigureAwait(false);
                    await _jobManager.UpdateJobProgressAsync(request, ct).ConfigureAwait(false);
                    _progressTracker.MarkCompleted(request.Symbol);
                    return;
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested)
                {
                    throw;
                }
                catch (RateLimitException rle) when (request.AssignedProvider != null)
                {
                    // Typed rate limit exception with RetryAfter from HTTP headers
                    _requestQueue.RecordProviderRateLimitHit(request.AssignedProvider);

                    if (retryAttempt < MaxRetryAttemptsPerRequest)
                    {
                        retryAttempt++;
                        var providerDelay = rle.RetryAfter;
                        var delay = providerDelay ?? CalculateBackoff(retryAttempt, RateLimitBaseDelay, RateLimitMaxDelay);

                        _log.Information(
                            "Rate limited for {Symbol} via {Provider}, retrying in {Delay}ms via {DelaySource} (attempt {Attempt}/{Max})",
                            request.Symbol, request.AssignedProvider, delay.TotalMilliseconds,
                            providerDelay.HasValue ? "provider-specified cooldown" : "calculated exponential backoff",
                            retryAttempt, MaxRetryAttemptsPerRequest);
                        await Task.Delay(delay, ct).ConfigureAwait(false);
                        continue;
                    }

                    _log.Warning(
                        "Rate limit retry budget exhausted for {Symbol} via {Provider} after {Attempts} attempts",
                        request.Symbol, request.AssignedProvider, retryAttempt);

                    await _requestQueue.CompleteRequestAsync(request, false, rle.Message, ct).ConfigureAwait(false);
                    await _jobManager.UpdateJobProgressAsync(request, ct).ConfigureAwait(false);
                    _progressTracker.MarkFailed(request.Symbol, rle.Message);
                    return;
                }
                catch (Exception ex)
                {
                    var retryAfter = TryExtractRetryAfter(ex);
                    var isRateLimited = retryAfter.HasValue ||
                                        IsHttp429(ex) ||
                                        ex.Message.Contains("429") ||
                                        ex.Message.Contains("rate limit", StringComparison.OrdinalIgnoreCase);

                    if (isRateLimited && request.AssignedProvider != null)
                    {
                        _requestQueue.RecordProviderRateLimitHit(request.AssignedProvider);

                        // Retry with Retry-After or exponential backoff if within retry budget
                        if (retryAttempt < MaxRetryAttemptsPerRequest)
                        {
                            retryAttempt++;
                            var delay = retryAfter ?? CalculateBackoff(retryAttempt, RateLimitBaseDelay, RateLimitMaxDelay);

                            _log.Information(
                                "Rate limited for {Symbol} via {Provider}, retrying in {Delay}ms via {DelaySource} (attempt {Attempt}/{Max})",
                                request.Symbol, request.AssignedProvider, delay.TotalMilliseconds,
                                retryAfter.HasValue ? "provider-specified cooldown" : "calculated exponential backoff",
                                retryAttempt, MaxRetryAttemptsPerRequest);
                            await Task.Delay(delay, ct).ConfigureAwait(false);
                            continue;
                        }

                        _log.Warning(
                            "Rate limit retry budget exhausted for {Symbol} via {Provider} after {Attempts} attempts",
                            request.Symbol, request.AssignedProvider, retryAttempt);
                    }

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
    {
        var baseMs = baseDelay.TotalMilliseconds;
        var maxMs = maxDelay.TotalMilliseconds;
        var delay = Math.Min(baseMs * Math.Pow(2, attempt - 1), maxMs);
        // Add jitter (±25%) to prevent thundering herd
        var jitter = delay * 0.25 * (Random.Shared.NextDouble() * 2 - 1);
        return TimeSpan.FromMilliseconds(delay + jitter);
    }

    /// <summary>
    /// Extracts Retry-After delay from an exception chain.
    /// Supports both delta-seconds ("120") and HTTP-date ("Thu, 01 Dec 2024 16:00:00 GMT") formats
    /// as defined in RFC 7231 Section 7.1.3.
    /// </summary>
    internal static TimeSpan? TryExtractRetryAfter(Exception ex)
    {
        // Walk the exception chain looking for HttpRequestException with Retry-After info
        var current = ex;
        while (current != null)
        {
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

            current = current.InnerException;
        }

        return null;
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
        var current = ex;
        while (current != null)
        {
            if (current is HttpRequestException httpRequestException &&
                httpRequestException.StatusCode == System.Net.HttpStatusCode.TooManyRequests)
            {
                return true;
            }

            current = current.InnerException;
        }

        return false;
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

            // Build file path based on naming convention
            var filePath = BuildFilePath(request.Symbol, date, request.Granularity);

            // Ensure directory exists
            var directory = Path.GetDirectoryName(filePath);
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }

            // Write bars as JSONL
            var lines = dateBars.Select(b => JsonSerializer.Serialize(b));
            await File.AppendAllLinesAsync(filePath, lines, ct).ConfigureAwait(false);

            foreach (var bar in dateBars)
            {
                OnBarWritten?.Invoke(filePath, bar);
            }
        }

        _log.Debug("Wrote {BarCount} bars for {Symbol} to storage", bars.Count, request.Symbol);
    }

    /// <summary>
    /// Build the file path for storing bars.
    /// </summary>
    private string BuildFilePath(string symbol, DateOnly date, DataGranularity granularity)
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

        // Default: BySymbol naming convention
        // {DataRoot}/{Symbol}/bar_{granularity}/{date}.jsonl
        var symbolDir = Path.Combine(_dataRoot, symbol.ToUpperInvariant());
        var typeDir = Path.Combine(symbolDir, $"bar_{granularityName}");
        var fileName = $"{date:yyyy-MM-dd}.jsonl";

        return Path.Combine(typeDir, fileName);
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

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;

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

    public BackfillServiceFactory(ILogger? log = null)
    {
        _log = log ?? LoggingSetup.ForContext<BackfillServiceFactory>();
    }

    /// <summary>
    /// Create a complete backfill service stack from configuration.
    /// </summary>
    public BackfillServices CreateServices(
        BackfillConfig config,
        string dataRoot,
        IEnumerable<IHistoricalDataProvider> providers)
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

        // Create composite provider
        var composite = new CompositeHistoricalDataProvider(
            providers,
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

        // Create worker service
        var worker = new BackfillWorkerService(
            jobManager,
            requestQueue,
            composite,
            rateLimitTracker,
            jobsConfig,
            dataRoot,
            _log);

        return new BackfillServices(
            jobManager,
            requestQueue,
            gapAnalyzer,
            rateLimitTracker,
            composite,
            worker);
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

    public BackfillServices(
        BackfillJobManager jobManager,
        BackfillRequestQueue requestQueue,
        DataGapAnalyzer gapAnalyzer,
        ProviderRateLimitTracker rateLimitTracker,
        CompositeHistoricalDataProvider provider,
        BackfillWorkerService worker)
    {
        JobManager = jobManager;
        RequestQueue = requestQueue;
        GapAnalyzer = gapAnalyzer;
        RateLimitTracker = rateLimitTracker;
        Provider = provider;
        Worker = worker;
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
    }
}
