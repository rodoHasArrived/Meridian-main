using System.Collections.Concurrent;
using System.Reactive.Subjects;
using System.Threading;
using Meridian.Application.Logging;
using Serilog;

namespace Meridian.Infrastructure.DataSources;

/// <summary>
/// Base class providing common functionality for data source implementations.
/// Handles health tracking, rate limiting, retry logic, and lifecycle management.
/// </summary>
public abstract class DataSourceBase : IDataSource
{
    #region Fields

    protected readonly ILogger Log;
    protected readonly DataSourceOptions Options;
    private readonly HealthTracker _healthTracker;
    private readonly SemaphoreSlim _rateLimiter;
    private readonly Subject<DataSourceHealthChanged> _healthChanges = new();
    private DateTimeOffset _lastRequestTime = DateTimeOffset.MinValue;
    private int _currentRequests;
    private DateTimeOffset _windowStart = DateTimeOffset.UtcNow;
    private bool _disposed;

    #endregion

    #region Identity (Abstract)

    /// <inheritdoc />
    public abstract string Id { get; }

    /// <inheritdoc />
    public abstract string DisplayName { get; }

    /// <inheritdoc />
    public abstract string Description { get; }

    #endregion

    #region Classification (Abstract)

    /// <inheritdoc />
    public abstract DataSourceType Type { get; }

    /// <inheritdoc />
    public abstract DataSourceCategory Category { get; }

    /// <summary>
    /// Default priority, can be overridden by derived classes.
    /// </summary>
    public virtual int Priority => Options.Priority;

    #endregion

    #region Capabilities (Abstract/Virtual)

    /// <inheritdoc />
    public abstract DataSourceCapabilities Capabilities { get; }

    /// <inheritdoc />
    public virtual DataSourceCapabilityInfo CapabilityInfo => DataSourceCapabilityInfo.Default(Capabilities);

    /// <inheritdoc />
    public abstract IReadOnlySet<string> SupportedMarkets { get; }

    /// <inheritdoc />
    public abstract IReadOnlySet<AssetClass> SupportedAssetClasses { get; }

    #endregion

    #region Operational State

    /// <inheritdoc />
    public DataSourceHealth Health => _healthTracker.CurrentHealth;

    /// <inheritdoc />
    public DataSourceStatus Status { get; protected set; } = DataSourceStatus.Uninitialized;

    /// <inheritdoc />
    public RateLimitState RateLimitState => GetCurrentRateLimitState();

    /// <inheritdoc />
    public IObservable<DataSourceHealthChanged> HealthChanges => _healthChanges;

    #endregion

    #region Constructor

    /// <summary>
    /// Initializes a new instance of the DataSourceBase class.
    /// </summary>
    /// <param name="options">Configuration options for this data source.</param>
    /// <param name="logger">Optional logger instance.</param>
    protected DataSourceBase(DataSourceOptions options, ILogger? logger = null)
    {
        Options = options ?? throw new ArgumentNullException(nameof(options));
        Log = logger ?? LoggingSetup.ForContext(GetType());
        _healthTracker = new HealthTracker(Options.HealthCheck, OnHealthChanged);
        _rateLimiter = new SemaphoreSlim(Options.RateLimits.MaxConcurrentRequests);
    }

    #endregion

    #region Lifecycle

    /// <inheritdoc />
    public virtual async Task InitializeAsync(CancellationToken ct = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        Log.Information("Initializing {Source}...", DisplayName);
        Status = DataSourceStatus.Initializing;

        try
        {
            // Validate credentials first
            if (!await ValidateCredentialsAsync(ct).ConfigureAwait(false))
            {
                Status = DataSourceStatus.ConfigurationError;
                Log.Error("{Source} credentials validation failed", DisplayName);
                throw new InvalidOperationException($"{DisplayName} credentials validation failed");
            }

            // Test connectivity
            if (!await TestConnectivityAsync(ct).ConfigureAwait(false))
            {
                Status = DataSourceStatus.Unavailable;
                Log.Error("{Source} connectivity test failed", DisplayName);
                throw new InvalidOperationException($"{DisplayName} connectivity test failed");
            }

            Status = DataSourceStatus.Connected;
            _healthTracker.RecordSuccess();
            Log.Information("{Source} initialized successfully", DisplayName);
        }
        catch (Exception ex) when (ex is not InvalidOperationException)
        {
            Status = DataSourceStatus.Unavailable;
            _healthTracker.RecordFailure(ex, "Initialize");
            Log.Error(ex, "{Source} initialization failed", DisplayName);
            throw;
        }
    }

    /// <inheritdoc />
    public abstract Task<bool> ValidateCredentialsAsync(CancellationToken ct = default);

    /// <inheritdoc />
    public abstract Task<bool> TestConnectivityAsync(CancellationToken ct = default);

    /// <inheritdoc />
    public virtual async ValueTask DisposeAsync()
    {
        if (_disposed)
            return;
        _disposed = true;

        Log.Debug("Disposing {Source}", DisplayName);

        _healthChanges.OnCompleted();
        _healthChanges.Dispose();
        _rateLimiter.Dispose();

        await OnDisposeAsync().ConfigureAwait(false);

        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// Override to provide custom dispose logic.
    /// </summary>
    protected virtual ValueTask OnDisposeAsync() => ValueTask.CompletedTask;

    #endregion

    #region Request Execution

    /// <summary>
    /// Execute a request with automatic rate limiting, retry, and health tracking.
    /// </summary>
    /// <typeparam name="T">Result type.</typeparam>
    /// <param name="operation">The operation to execute.</param>
    /// <param name="operationName">Name for logging.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The operation result.</returns>
    protected async Task<T> ExecuteWithPoliciesAsync<T>(
        Func<CancellationToken, Task<T>> operation,
        string operationName,
        CancellationToken ct = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        await _rateLimiter.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            // Apply rate limit delay
            await ApplyRateLimitDelayAsync(ct).ConfigureAwait(false);

            // Execute with retry
            var result = await ExecuteWithRetryAsync(operation, operationName, ct).ConfigureAwait(false);

            _healthTracker.RecordSuccess();
            RecordRequest();

            return result;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _healthTracker.RecordFailure(ex, operationName);
            Log.Error(ex, "{Source} {Operation} failed", Id, operationName);
            throw;
        }
        finally
        {
            _rateLimiter.Release();
        }
    }

    /// <summary>
    /// Execute a void operation with automatic rate limiting, retry, and health tracking.
    /// </summary>
    protected async Task ExecuteWithPoliciesAsync(
        Func<CancellationToken, Task> operation,
        string operationName,
        CancellationToken ct = default)
    {
        await ExecuteWithPoliciesAsync(async token =>
        {
            await operation(token).ConfigureAwait(false);
            return true;
        }, operationName, ct).ConfigureAwait(false);
    }

    private async Task<T> ExecuteWithRetryAsync<T>(
        Func<CancellationToken, Task<T>> operation,
        string operationName,
        CancellationToken ct)
    {
        var maxRetries = Options.RetryPolicy.MaxRetries;
        var baseDelay = Options.RetryPolicy.BaseDelayMs;
        var maxDelay = Options.RetryPolicy.MaxDelayMs;
        Exception? lastException = null;

        for (int attempt = 0; attempt <= maxRetries; attempt++)
        {
            try
            {
                return await operation(ct).ConfigureAwait(false);
            }
            catch (HttpRequestException ex)
            {
                lastException = ex;
                if (attempt == maxRetries)
                    break;

                var delay = CalculateRetryDelay(attempt, baseDelay, maxDelay);
                Log.Warning(ex, "{Source} {Operation} attempt {Attempt}/{MaxRetries} failed, retrying in {Delay}ms",
                    Id, operationName, attempt + 1, maxRetries + 1, delay);
                await Task.Delay(delay, ct).ConfigureAwait(false);
            }
            catch (TimeoutException ex)
            {
                lastException = ex;
                if (attempt == maxRetries)
                    break;

                var delay = CalculateRetryDelay(attempt, baseDelay, maxDelay);
                Log.Warning(ex, "{Source} {Operation} timed out, attempt {Attempt}/{MaxRetries}, retrying in {Delay}ms",
                    Id, operationName, attempt + 1, maxRetries + 1, delay);
                await Task.Delay(delay, ct).ConfigureAwait(false);
            }
        }

        throw lastException!;
    }

    private static int CalculateRetryDelay(int attempt, int baseDelay, int maxDelay)
    {
        var delay = (int)(baseDelay * Math.Pow(2, attempt));
        return Math.Min(delay, maxDelay);
    }

    #endregion

    #region Rate Limiting

    private async Task ApplyRateLimitDelayAsync(CancellationToken ct)
    {
        var timeSinceLastRequest = DateTimeOffset.UtcNow - _lastRequestTime;
        var minDelay = TimeSpan.FromMilliseconds(Options.RateLimits.MinDelayBetweenRequestsMs);

        if (timeSinceLastRequest < minDelay)
        {
            var delayNeeded = minDelay - timeSinceLastRequest;
            Log.Debug("{Source} enforcing rate limit delay: {Delay}ms", Id, delayNeeded.TotalMilliseconds);
            await Task.Delay(delayNeeded, ct).ConfigureAwait(false);
        }

        // Check window-based rate limit
        CleanupRequestWindow();

        if (_currentRequests >= Options.RateLimits.MaxRequestsPerWindow)
        {
            var windowEnd = _windowStart.Add(Options.RateLimits.RateLimitWindow);
            var waitTime = windowEnd - DateTimeOffset.UtcNow;

            if (waitTime > TimeSpan.Zero)
            {
                Status = DataSourceStatus.RateLimited;
                Log.Warning("{Source} rate limit reached ({Current}/{Max}), waiting {Wait}ms",
                    Id, _currentRequests, Options.RateLimits.MaxRequestsPerWindow, waitTime.TotalMilliseconds);
                await Task.Delay(waitTime, ct).ConfigureAwait(false);
                CleanupRequestWindow();
            }

            Status = DataSourceStatus.Connected;
        }
    }

    private void RecordRequest()
    {
        _lastRequestTime = DateTimeOffset.UtcNow;
        _currentRequests++;
    }

    private void CleanupRequestWindow()
    {
        var now = DateTimeOffset.UtcNow;
        if (now - _windowStart >= Options.RateLimits.RateLimitWindow)
        {
            _windowStart = now;
            _currentRequests = 0;
        }
    }

    private RateLimitState GetCurrentRateLimitState()
    {
        CleanupRequestWindow();
        var remaining = Options.RateLimits.MaxRequestsPerWindow - _currentRequests;
        var windowEnd = _windowStart.Add(Options.RateLimits.RateLimitWindow);
        var resetIn = windowEnd - DateTimeOffset.UtcNow;

        return RateLimitState.Limited(
            remaining,
            Options.RateLimits.MaxRequestsPerWindow,
            resetIn > TimeSpan.Zero ? resetIn : null
        );
    }

    #endregion

    #region Health Tracking

    private void OnHealthChanged(DataSourceHealth previousHealth, DataSourceHealth newHealth)
    {
        var @event = new DataSourceHealthChanged(Id, previousHealth, newHealth, DateTimeOffset.UtcNow);
        _healthChanges.OnNext(@event);

        if (@event.BecameUnhealthy)
        {
            Log.Warning("{Source} became unhealthy: {Message}", Id, newHealth.Message);
        }
        else if (@event.BecameHealthy)
        {
            Log.Information("{Source} recovered and is now healthy", Id);
        }
    }

    #endregion

    #region Helpers

    /// <summary>
    /// Checks if this source has a specific capability.
    /// </summary>
    protected bool HasCapability(DataSourceCapabilities capability)
        => Capabilities.HasFlag(capability);

    /// <summary>
    /// Checks if this source supports a specific market.
    /// </summary>
    protected bool SupportsMarket(string market)
        => SupportedMarkets.Contains(market, StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Checks if this source supports a specific asset class.
    /// </summary>
    protected bool SupportsAssetClass(AssetClass assetClass)
        => SupportedAssetClasses.Contains(assetClass);

    #endregion
}

#region Health Tracker

/// <summary>
/// Internal helper for tracking data source health.
/// </summary>
internal sealed class HealthTracker
{
    private readonly HealthCheckOptions _options;
    private readonly Action<DataSourceHealth, DataSourceHealth>? _onHealthChanged;
    private readonly ConcurrentQueue<DataSourceError> _recentErrors = new();
    private readonly object _lock = new();
    private DataSourceHealth _currentHealth = DataSourceHealth.Healthy();
    private int _consecutiveFailures;
    private int _totalSuccesses;
    private int _totalFailures;

    public DataSourceHealth CurrentHealth
    {
        get
        {
            lock (_lock)
                return _currentHealth;
        }
    }

    public HealthTracker(HealthCheckOptions options, Action<DataSourceHealth, DataSourceHealth>? onHealthChanged = null)
    {
        _options = options;
        _onHealthChanged = onHealthChanged;
    }

    public void RecordSuccess(TimeSpan? responseTime = null)
    {
        lock (_lock)
        {
            _totalSuccesses++;
            _consecutiveFailures = 0;

            var newHealth = new DataSourceHealth(
                IsHealthy: true,
                Score: CalculateHealthScore(),
                Message: null,
                LastChecked: DateTimeOffset.UtcNow,
                LastResponseTime: responseTime,
                ConsecutiveFailures: 0,
                RecentErrors: GetRecentErrors()
            );

            UpdateHealth(newHealth);
        }
    }

    public void RecordFailure(Exception ex, string operation = "Unknown")
    {
        lock (_lock)
        {
            _totalFailures++;
            _consecutiveFailures++;

            // Track error
            var error = new DataSourceError(
                operation,
                ex.Message,
                DateTimeOffset.UtcNow,
                ex.GetType().Name,
                ex.StackTrace
            );
            _recentErrors.Enqueue(error);

            // Keep only recent errors
            while (_recentErrors.Count > 10)
                _recentErrors.TryDequeue(out _);

            var isHealthy = _consecutiveFailures < _options.UnhealthyThreshold;
            var score = CalculateHealthScore();

            var newHealth = new DataSourceHealth(
                IsHealthy: isHealthy,
                Score: score,
                Message: $"Last error: {ex.Message}",
                LastChecked: DateTimeOffset.UtcNow,
                LastResponseTime: null,
                ConsecutiveFailures: _consecutiveFailures,
                RecentErrors: GetRecentErrors()
            );

            UpdateHealth(newHealth);
        }
    }

    private double CalculateHealthScore()
    {
        var total = _totalSuccesses + _totalFailures;
        if (total == 0)
            return 100.0;

        var baseScore = (_totalSuccesses * 100.0) / total;

        // Apply penalty for consecutive failures
        var penalty = _consecutiveFailures * 10.0;

        return Math.Max(0, Math.Min(100, baseScore - penalty));
    }

    private IReadOnlyList<DataSourceError> GetRecentErrors()
        => _recentErrors.ToArray();

    private void UpdateHealth(DataSourceHealth newHealth)
    {
        var previousHealth = _currentHealth;
        _currentHealth = newHealth;

        if (previousHealth.IsHealthy != newHealth.IsHealthy)
        {
            _onHealthChanged?.Invoke(previousHealth, newHealth);
        }
    }
}

#endregion

#region Configuration Options

/// <summary>
/// Configuration options for a data source.
/// </summary>
public sealed record DataSourceOptions(
    int Priority = 100,
    RetryPolicyOptions? RetryPolicy = null,
    RateLimitOptions? RateLimits = null,
    HealthCheckOptions? HealthCheck = null
)
{
    public RetryPolicyOptions RetryPolicy { get; } = RetryPolicy ?? new RetryPolicyOptions();
    public RateLimitOptions RateLimits { get; } = RateLimits ?? new RateLimitOptions();
    public HealthCheckOptions HealthCheck { get; } = HealthCheck ?? new HealthCheckOptions();

    /// <summary>
    /// Creates default options.
    /// </summary>
    public static DataSourceOptions Default => new();
}

/// <summary>
/// Retry policy configuration.
/// </summary>
public sealed record RetryPolicyOptions(
    int MaxRetries = 3,
    int BaseDelayMs = 1000,
    int MaxDelayMs = 30000,
    bool UseExponentialBackoff = true
);

/// <summary>
/// Rate limit configuration.
/// </summary>
public sealed record RateLimitOptions(
    int MaxConcurrentRequests = 5,
    int MaxRequestsPerWindow = 100,
    TimeSpan? RateLimitWindowValue = null,
    int MinDelayBetweenRequestsMs = 0
)
{
    public TimeSpan RateLimitWindow { get; } = RateLimitWindowValue ?? TimeSpan.FromMinutes(1);
}

/// <summary>
/// Health check configuration.
/// </summary>
public sealed record HealthCheckOptions(
    int IntervalSeconds = 30,
    int TimeoutSeconds = 10,
    int UnhealthyThreshold = 3
);

#endregion
