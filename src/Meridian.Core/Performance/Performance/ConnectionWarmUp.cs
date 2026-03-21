using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Threading;

namespace Meridian.Core.Performance;

/// <summary>
/// Connection warm-up utilities for establishing and priming network connections
/// before critical market hours to minimize latency variance.
/// </summary>
public sealed class ConnectionWarmUp
{
    private readonly TimeSpan _warmUpInterval;
    private readonly int _warmUpIterations;
    private long _lastWarmUpTimestamp;

    /// <summary>
    /// Statistics from the last warm-up operation.
    /// </summary>
    public WarmUpStatistics? LastWarmUpStats { get; private set; }

    public ConnectionWarmUp(TimeSpan? warmUpInterval = null, int warmUpIterations = 10)
    {
        _warmUpInterval = warmUpInterval ?? TimeSpan.FromMinutes(5);
        _warmUpIterations = Math.Max(1, warmUpIterations);
    }

    /// <summary>
    /// Determines if a warm-up cycle should be performed based on elapsed time.
    /// </summary>
    public bool ShouldWarmUp()
    {
        var now = Stopwatch.GetTimestamp();
        var elapsed = TimeSpan.FromTicks((long)((now - _lastWarmUpTimestamp) *
            (TimeSpan.TicksPerSecond / (double)Stopwatch.Frequency)));

        return elapsed >= _warmUpInterval;
    }

    /// <summary>
    /// Executes a warm-up cycle using the provided action.
    /// Measures latency statistics for monitoring.
    /// </summary>
    /// <param name="warmUpAction">Action to execute for each warm-up iteration (e.g., lightweight request)</param>
    /// <param name="ct">Cancellation token</param>
    public async Task<WarmUpStatistics> ExecuteWarmUpAsync(
        Func<CancellationToken, Task> warmUpAction,
        CancellationToken ct = default)
    {
        var latencies = new List<double>(_warmUpIterations);
        var errors = 0;
        var startTime = Stopwatch.GetTimestamp();

        for (int i = 0; i < _warmUpIterations; i++)
        {
            ct.ThrowIfCancellationRequested();

            var iterStart = Stopwatch.GetTimestamp();
            try
            {
                await warmUpAction(ct).ConfigureAwait(false);
                var latencyUs = HighResolutionTimestamp.GetElapsedMicroseconds(iterStart);
                latencies.Add(latencyUs);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch
            {
                errors++;
            }

            // Small delay between iterations to avoid flooding
            if (i < _warmUpIterations - 1)
                await Task.Delay(10, ct).ConfigureAwait(false);
        }

        _lastWarmUpTimestamp = Stopwatch.GetTimestamp();

        var stats = new WarmUpStatistics(
            IterationsCompleted: latencies.Count,
            IterationsFailed: errors,
            TotalDurationMs: HighResolutionTimestamp.GetElapsedMicroseconds(startTime) / 1000.0,
            MinLatencyUs: latencies.Count > 0 ? latencies.Min() : 0,
            MaxLatencyUs: latencies.Count > 0 ? latencies.Max() : 0,
            AvgLatencyUs: latencies.Count > 0 ? latencies.Average() : 0,
            P99LatencyUs: latencies.Count > 0 ? Percentile(latencies, 0.99) : 0,
            Timestamp: DateTimeOffset.UtcNow
        );

        LastWarmUpStats = stats;
        return stats;
    }

    /// <summary>
    /// Executes a synchronous warm-up cycle using the provided action.
    /// The action is executed synchronously, but delays between iterations use async Task.Delay.
    /// </summary>
    /// <param name="warmUpAction">Synchronous action to execute for each warm-up iteration</param>
    /// <param name="ct">Cancellation token</param>
    public async Task<WarmUpStatistics> ExecuteWarmUpAsync(Action warmUpAction, CancellationToken ct = default)
    {
        var latencies = new List<double>(_warmUpIterations);
        var errors = 0;
        var startTime = Stopwatch.GetTimestamp();

        for (int i = 0; i < _warmUpIterations; i++)
        {
            ct.ThrowIfCancellationRequested();

            var iterStart = Stopwatch.GetTimestamp();
            try
            {
                warmUpAction();
                var latencyUs = HighResolutionTimestamp.GetElapsedMicroseconds(iterStart);
                latencies.Add(latencyUs);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch
            {
                errors++;
            }

            // Small delay between iterations to avoid flooding
            if (i < _warmUpIterations - 1)
                await Task.Delay(10, ct).ConfigureAwait(false);
        }

        _lastWarmUpTimestamp = Stopwatch.GetTimestamp();

        var stats = new WarmUpStatistics(
            IterationsCompleted: latencies.Count,
            IterationsFailed: errors,
            TotalDurationMs: HighResolutionTimestamp.GetElapsedMicroseconds(startTime) / 1000.0,
            MinLatencyUs: latencies.Count > 0 ? latencies.Min() : 0,
            MaxLatencyUs: latencies.Count > 0 ? latencies.Max() : 0,
            AvgLatencyUs: latencies.Count > 0 ? latencies.Average() : 0,
            P99LatencyUs: latencies.Count > 0 ? Percentile(latencies, 0.99) : 0,
            Timestamp: DateTimeOffset.UtcNow
        );

        LastWarmUpStats = stats;
        return stats;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static double Percentile(List<double> values, double percentile)
    {
        if (values.Count == 0)
            return 0;
        if (values.Count == 1)
            return values[0];

        values.Sort();
        var index = (int)Math.Ceiling(percentile * values.Count) - 1;
        return values[Math.Max(0, Math.Min(index, values.Count - 1))];
    }
}

/// <summary>
/// Statistics from a connection warm-up operation.
/// </summary>
public readonly record struct WarmUpStatistics(
    int IterationsCompleted,
    int IterationsFailed,
    double TotalDurationMs,
    double MinLatencyUs,
    double MaxLatencyUs,
    double AvgLatencyUs,
    double P99LatencyUs,
    DateTimeOffset Timestamp
);

/// <summary>
/// Exponential backoff retry logic for connection management.
/// </summary>
public sealed class ExponentialBackoffRetry
{
    private readonly TimeSpan _initialDelay;
    private readonly TimeSpan _maxDelay;
    private readonly int _maxRetries;
    private readonly double _multiplier;
    private readonly double _jitterFactor;
    private readonly Random _random = new();

    private int _currentRetryCount;
    private TimeSpan _currentDelay;

    public int CurrentRetryCount => _currentRetryCount;
    public bool CanRetry => _maxRetries < 0 || _currentRetryCount < _maxRetries;

    public ExponentialBackoffRetry(
        TimeSpan? initialDelay = null,
        TimeSpan? maxDelay = null,
        int maxRetries = -1,
        double multiplier = 2.0,
        double jitterFactor = 0.1)
    {
        _initialDelay = initialDelay ?? TimeSpan.FromSeconds(1);
        _maxDelay = maxDelay ?? TimeSpan.FromMinutes(5);
        _maxRetries = maxRetries;
        _multiplier = Math.Max(1.0, multiplier);
        _jitterFactor = Math.Clamp(jitterFactor, 0.0, 0.5);
        _currentDelay = _initialDelay;
    }

    /// <summary>
    /// Gets the next delay duration and increments the retry counter.
    /// </summary>
    public TimeSpan GetNextDelay()
    {
        var delay = _currentDelay;

        // Add jitter to prevent thundering herd
        if (_jitterFactor > 0)
        {
            var jitter = delay.TotalMilliseconds * _jitterFactor * (_random.NextDouble() * 2 - 1);
            delay = TimeSpan.FromMilliseconds(delay.TotalMilliseconds + jitter);
        }

        // Update for next retry
        _currentRetryCount++;
        _currentDelay = TimeSpan.FromMilliseconds(
            Math.Min(_currentDelay.TotalMilliseconds * _multiplier, _maxDelay.TotalMilliseconds));

        return delay;
    }

    /// <summary>
    /// Waits for the next retry delay.
    /// </summary>
    public async Task WaitAsync(CancellationToken ct = default)
    {
        var delay = GetNextDelay();
        await Task.Delay(delay, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Resets the retry counter and delay to initial values.
    /// </summary>
    public void Reset()
    {
        _currentRetryCount = 0;
        _currentDelay = _initialDelay;
    }
}

/// <summary>
/// Heartbeat monitor for detecting connection health and latency issues.
/// </summary>
public sealed class HeartbeatMonitor : IDisposable
{
    private readonly TimeSpan _interval;
    private readonly TimeSpan _timeout;
    private readonly Func<CancellationToken, Task<bool>> _heartbeatFunc;
    private readonly Action<HeartbeatResult>? _onHeartbeat;

    private CancellationTokenSource? _cts;
    private Task? _monitorTask;

    private long _lastSuccessTimestamp;
    private long _consecutiveFailures;
    private long _totalHeartbeats;
    private long _totalFailures;
    private double _lastLatencyUs;

    public bool IsRunning => _monitorTask is not null && !_monitorTask.IsCompleted;
    public long ConsecutiveFailures => Interlocked.Read(ref _consecutiveFailures);
    public long TotalHeartbeats => Interlocked.Read(ref _totalHeartbeats);
    public long TotalFailures => Interlocked.Read(ref _totalFailures);
    public double LastLatencyUs => Volatile.Read(ref _lastLatencyUs);

    public TimeSpan TimeSinceLastSuccess
    {
        get
        {
            var last = Interlocked.Read(ref _lastSuccessTimestamp);
            if (last == 0)
                return TimeSpan.MaxValue;
            return TimeSpan.FromTicks((long)((Stopwatch.GetTimestamp() - last) *
                (TimeSpan.TicksPerSecond / (double)Stopwatch.Frequency)));
        }
    }

    public bool IsHealthy => ConsecutiveFailures == 0 && TimeSinceLastSuccess < _timeout * 2;

    public event EventHandler<HeartbeatResult>? HeartbeatCompleted;
    public event EventHandler? ConnectionUnhealthy;

    public HeartbeatMonitor(
        Func<CancellationToken, Task<bool>> heartbeatFunc,
        TimeSpan? interval = null,
        TimeSpan? timeout = null,
        Action<HeartbeatResult>? onHeartbeat = null)
    {
        _heartbeatFunc = heartbeatFunc ?? throw new ArgumentNullException(nameof(heartbeatFunc));
        _interval = interval ?? TimeSpan.FromSeconds(30);
        _timeout = timeout ?? TimeSpan.FromSeconds(10);
        _onHeartbeat = onHeartbeat;
    }

    public void Start()
    {
        if (IsRunning)
            return;

        _cts = new CancellationTokenSource();
        _monitorTask = MonitorLoopAsync(_cts.Token);
    }

    public async Task StopAsync(CancellationToken ct = default)
    {
        if (_cts is null || _monitorTask is null)
            return;

        _cts.Cancel();
        try
        {
            await _monitorTask.ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
        }
        finally
        {
            _cts.Dispose();
            _cts = null;
            _monitorTask = null;
        }
    }

    private async Task MonitorLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(_interval, ct).ConfigureAwait(false);
                await PerformHeartbeatAsync(ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    private async Task PerformHeartbeatAsync(CancellationToken ct)
    {
        var startTs = Stopwatch.GetTimestamp();
        var success = false;
        double latencyUs = 0;

        try
        {
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeoutCts.CancelAfter(_timeout);

            success = await _heartbeatFunc(timeoutCts.Token).ConfigureAwait(false);
            latencyUs = HighResolutionTimestamp.GetElapsedMicroseconds(startTs);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            // Timeout
            success = false;
            latencyUs = _timeout.TotalMilliseconds * 1000;
        }
        catch
        {
            success = false;
            latencyUs = HighResolutionTimestamp.GetElapsedMicroseconds(startTs);
        }

        Interlocked.Increment(ref _totalHeartbeats);
        Volatile.Write(ref _lastLatencyUs, latencyUs);

        if (success)
        {
            Interlocked.Exchange(ref _lastSuccessTimestamp, Stopwatch.GetTimestamp());
            Interlocked.Exchange(ref _consecutiveFailures, 0);
        }
        else
        {
            Interlocked.Increment(ref _totalFailures);
            var failures = Interlocked.Increment(ref _consecutiveFailures);

            if (failures >= 3)
            {
                ConnectionUnhealthy?.Invoke(this, EventArgs.Empty);
            }
        }

        var result = new HeartbeatResult(success, latencyUs, DateTimeOffset.UtcNow);
        _onHeartbeat?.Invoke(result);
        HeartbeatCompleted?.Invoke(this, result);
    }

    public void Dispose()
    {
        _cts?.Cancel();
        _cts?.Dispose();
    }
}

/// <summary>
/// Result of a heartbeat check.
/// </summary>
public readonly record struct HeartbeatResult(bool Success, double LatencyUs, DateTimeOffset Timestamp);
