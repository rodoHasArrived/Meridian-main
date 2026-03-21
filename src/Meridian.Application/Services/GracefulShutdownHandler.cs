using System.Diagnostics;
using System.Threading;
using Meridian.Application.Logging;
using Meridian.Application.Pipeline;
using Serilog;

namespace Meridian.Application.Services;

/// <summary>
/// Handles graceful shutdown of the application, ensuring all buffered events
/// are flushed to storage before termination. Provides timeout handling,
/// progress reporting, and webhook notifications.
/// </summary>
public sealed class GracefulShutdownHandler : IAsyncDisposable
{
    private readonly ILogger _log = LoggingSetup.ForContext<GracefulShutdownHandler>();
    private readonly GracefulShutdownConfig _config;
    private readonly List<IFlushable> _flushables = new();
    private readonly List<IAsyncDisposable> _disposables = new();
    private readonly List<Func<ShutdownContext, Task>> _shutdownCallbacks = new();
    private readonly CancellationTokenSource _shutdownCts = new();
    private readonly TaskCompletionSource _shutdownRequested = new();

    private ShutdownReason _shutdownReason = ShutdownReason.Unknown;
    private DateTimeOffset _shutdownRequestedAt;
    private volatile bool _isShuttingDown;
    private volatile bool _isDisposed;

    /// <summary>
    /// Event raised when shutdown begins.
    /// </summary>
    public event Action<ShutdownContext>? OnShutdownStarted;

    /// <summary>
    /// Event raised when shutdown completes.
    /// </summary>
    public event Action<ShutdownResult>? OnShutdownCompleted;

    /// <summary>
    /// Event raised to report shutdown progress.
    /// </summary>
    public event Action<ShutdownProgress>? OnProgress;

    public GracefulShutdownHandler(GracefulShutdownConfig? config = null)
    {
        _config = config ?? GracefulShutdownConfig.Default;

        // Register for process termination signals
        Console.CancelKeyPress += OnCancelKeyPress;
        AppDomain.CurrentDomain.ProcessExit += OnProcessExit;

        _log.Information("GracefulShutdownHandler initialized with timeout {TimeoutSeconds}s", _config.TimeoutSeconds);
    }

    /// <summary>
    /// Registers a flushable component that needs to be flushed on shutdown.
    /// </summary>
    public void RegisterFlushable(IFlushable flushable)
    {
        if (_isShuttingDown)
            return;
        _flushables.Add(flushable);
        _log.Debug("Registered flushable: {Type}", flushable.GetType().Name);
    }

    /// <summary>
    /// Registers a disposable component that needs to be disposed on shutdown.
    /// </summary>
    public void RegisterDisposable(IAsyncDisposable disposable)
    {
        if (_isShuttingDown)
            return;
        _disposables.Add(disposable);
        _log.Debug("Registered disposable: {Type}", disposable.GetType().Name);
    }

    /// <summary>
    /// Registers a callback to be invoked during shutdown.
    /// Callbacks are invoked in registration order before flushing.
    /// </summary>
    public void RegisterShutdownCallback(Func<ShutdownContext, Task> callback)
    {
        if (_isShuttingDown)
            return;
        _shutdownCallbacks.Add(callback);
    }

    /// <summary>
    /// Gets a cancellation token that is cancelled when shutdown is requested.
    /// </summary>
    public CancellationToken ShutdownToken => _shutdownCts.Token;

    /// <summary>
    /// Gets a task that completes when shutdown is requested.
    /// </summary>
    public Task ShutdownRequestedTask => _shutdownRequested.Task;

    /// <summary>
    /// Returns true if shutdown has been initiated.
    /// </summary>
    public bool IsShuttingDown => _isShuttingDown;

    /// <summary>
    /// Initiates graceful shutdown with the specified reason.
    /// </summary>
    public async Task<ShutdownResult> InitiateShutdownAsync(ShutdownReason reason, string? message = null, CancellationToken ct = default)
    {
        if (_isShuttingDown)
        {
            _log.Warning("Shutdown already in progress, ignoring duplicate request");
            return new ShutdownResult(
                Success: false,
                Reason: _shutdownReason,
                ErrorMessage: "Shutdown already in progress"
            );
        }

        _isShuttingDown = true;
        _shutdownReason = reason;
        _shutdownRequestedAt = DateTimeOffset.UtcNow;
        var startTime = Stopwatch.GetTimestamp();

        _log.Information("Initiating graceful shutdown: {Reason} - {Message}", reason, message ?? "No message");

        var context = new ShutdownContext(
            Reason: reason,
            Message: message,
            RequestedAt: _shutdownRequestedAt,
            TimeoutSeconds: _config.TimeoutSeconds
        );

        // Signal shutdown requested
        _shutdownRequested.TrySetResult();

        OnShutdownStarted?.Invoke(context);

        try
        {
            // Create a timeout cancellation token
            using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(_config.TimeoutSeconds));
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(_shutdownCts.Token, timeoutCts.Token);

            // Phase 1: Execute shutdown callbacks
            await ExecuteShutdownCallbacksAsync(context, linkedCts.Token);

            // Phase 2: Stop accepting new events (signal producers)
            ReportProgress("Stopping event producers", 1, 4);
            _shutdownCts.Cancel();

            // Phase 3: Flush all pending events
            ReportProgress("Flushing pending events", 2, 4);
            var flushResult = await FlushAllAsync(linkedCts.Token);

            // Phase 4: Dispose resources
            ReportProgress("Disposing resources", 3, 4);
            var disposeResult = await DisposeAllAsync(linkedCts.Token);

            ReportProgress("Shutdown complete", 4, 4);

            var elapsedMs = GetElapsedMs(startTime);
            var result = new ShutdownResult(
                Success: true,
                Reason: reason,
                StartedAt: _shutdownRequestedAt,
                CompletedAt: DateTimeOffset.UtcNow,
                DurationMs: elapsedMs,
                EventsFlushed: flushResult.TotalEventsFlushed,
                FlushTimeoutOccurred: flushResult.TimeoutOccurred,
                ComponentsDisposed: disposeResult.DisposedCount,
                Warnings: flushResult.Warnings.Concat(disposeResult.Warnings).ToArray()
            );

            _log.Information("Graceful shutdown completed in {ElapsedMs}ms: {EventsFlushed} events flushed, {ComponentsDisposed} components disposed",
                elapsedMs, result.EventsFlushed, result.ComponentsDisposed);

            OnShutdownCompleted?.Invoke(result);
            return result;
        }
        catch (OperationCanceledException) when (!_shutdownCts.IsCancellationRequested)
        {
            // Timeout occurred
            var elapsedMs = GetElapsedMs(startTime);
            _log.Error("Graceful shutdown timed out after {TimeoutSeconds}s", _config.TimeoutSeconds);

            var result = new ShutdownResult(
                Success: false,
                Reason: reason,
                StartedAt: _shutdownRequestedAt,
                CompletedAt: DateTimeOffset.UtcNow,
                DurationMs: elapsedMs,
                FlushTimeoutOccurred: true,
                ErrorMessage: $"Shutdown timed out after {_config.TimeoutSeconds} seconds"
            );

            OnShutdownCompleted?.Invoke(result);

            if (_config.ForceExitOnTimeout)
            {
                _log.Warning("Force exiting due to timeout");
                Environment.Exit(1);
            }

            return result;
        }
        catch (Exception ex)
        {
            var elapsedMs = GetElapsedMs(startTime);
            _log.Error(ex, "Error during graceful shutdown");

            var result = new ShutdownResult(
                Success: false,
                Reason: reason,
                StartedAt: _shutdownRequestedAt,
                CompletedAt: DateTimeOffset.UtcNow,
                DurationMs: elapsedMs,
                ErrorMessage: ex.Message
            );

            OnShutdownCompleted?.Invoke(result);
            return result;
        }
    }

    /// <summary>
    /// Waits for shutdown to be requested, then performs graceful shutdown.
    /// Use this in your main loop.
    /// </summary>
    public async Task WaitForShutdownAsync(CancellationToken ct = default)
    {
        var tcs = _shutdownRequested.Task;
        if (ct.CanBeCanceled)
        {
            var cancel = Task.Delay(Timeout.Infinite, ct);
            await Task.WhenAny(tcs, cancel);
            ct.ThrowIfCancellationRequested();
        }
        else
        {
            await tcs;
        }
    }

    private async Task ExecuteShutdownCallbacksAsync(ShutdownContext context, CancellationToken ct)
    {
        if (_shutdownCallbacks.Count == 0)
            return;

        _log.Debug("Executing {Count} shutdown callbacks", _shutdownCallbacks.Count);

        foreach (var callback in _shutdownCallbacks)
        {
            try
            {
                await callback(context);
            }
            catch (Exception ex)
            {
                _log.Warning(ex, "Shutdown callback threw an exception");
            }
        }
    }

    private async Task<FlushResult> FlushAllAsync(CancellationToken ct)
    {
        var warnings = new List<string>();
        long totalEventsFlushed = 0;
        var timeoutOccurred = false;

        if (_flushables.Count == 0)
        {
            _log.Debug("No flushables registered");
            return new FlushResult(totalEventsFlushed, false, warnings);
        }

        _log.Information("Flushing {Count} component(s)...", _flushables.Count);

        foreach (var flushable in _flushables)
        {
            var componentName = flushable.GetType().Name;

            try
            {
                _log.Debug("Flushing {Component}...", componentName);

                // Get queue size before flush if available
                long queueSize = 0;
                if (flushable is EventPipeline pipeline)
                {
                    queueSize = pipeline.CurrentQueueSize;
                    totalEventsFlushed += queueSize;
                }

                var flushTask = flushable.FlushAsync(ct);
                var completedTask = await Task.WhenAny(
                    flushTask,
                    Task.Delay(TimeSpan.FromSeconds(_config.FlushTimeoutPerComponentSeconds), ct)
                );

                if (completedTask != flushTask)
                {
                    _log.Warning("Flush timeout for {Component} after {Timeout}s",
                        componentName, _config.FlushTimeoutPerComponentSeconds);
                    warnings.Add($"Flush timeout for {componentName}");
                    timeoutOccurred = true;
                }
                else
                {
                    _log.Debug("Flushed {Component} successfully", componentName);
                }
            }
            catch (OperationCanceledException)
            {
                _log.Warning("Flush cancelled for {Component}", componentName);
                warnings.Add($"Flush cancelled for {componentName}");
                timeoutOccurred = true;
            }
            catch (Exception ex)
            {
                _log.Error(ex, "Error flushing {Component}", componentName);
                warnings.Add($"Flush error for {componentName}: {ex.Message}");
            }
        }

        _log.Information("Flush complete: approximately {EventCount} events flushed", totalEventsFlushed);
        return new FlushResult(totalEventsFlushed, timeoutOccurred, warnings);
    }

    private async Task<DisposeResult> DisposeAllAsync(CancellationToken ct)
    {
        var warnings = new List<string>();
        var disposedCount = 0;

        if (_disposables.Count == 0)
        {
            return new DisposeResult(0, warnings);
        }

        _log.Debug("Disposing {Count} component(s)...", _disposables.Count);

        // Dispose in reverse order (LIFO)
        foreach (var disposable in Enumerable.Reverse(_disposables))
        {
            var componentName = disposable.GetType().Name;

            try
            {
                var disposeTask = disposable.DisposeAsync().AsTask();
                var completedTask = await Task.WhenAny(
                    disposeTask,
                    Task.Delay(TimeSpan.FromSeconds(_config.DisposeTimeoutPerComponentSeconds), ct)
                );

                if (completedTask != disposeTask)
                {
                    _log.Warning("Dispose timeout for {Component}", componentName);
                    warnings.Add($"Dispose timeout for {componentName}");
                }
                else
                {
                    disposedCount++;
                    _log.Debug("Disposed {Component}", componentName);
                }
            }
            catch (Exception ex)
            {
                _log.Warning(ex, "Error disposing {Component}", componentName);
                warnings.Add($"Dispose error for {componentName}: {ex.Message}");
            }
        }

        return new DisposeResult(disposedCount, warnings);
    }

    private void ReportProgress(string phase, int current, int total)
    {
        var progress = new ShutdownProgress(
            Phase: phase,
            CurrentStep: current,
            TotalSteps: total,
            PercentComplete: (int)((double)current / total * 100),
            Timestamp: DateTimeOffset.UtcNow
        );

        _log.Debug("Shutdown progress: {Phase} ({Current}/{Total})", phase, current, total);
        OnProgress?.Invoke(progress);
    }

    private void OnCancelKeyPress(object? sender, ConsoleCancelEventArgs e)
    {
        e.Cancel = true; // Prevent immediate termination
        _log.Information("Received Ctrl+C signal");

        if (!_isShuttingDown)
        {
            _ = InitiateShutdownAsync(ShutdownReason.UserRequested, "Ctrl+C received");
        }
        else if (_config.ForceExitOnSecondSignal)
        {
            _log.Warning("Second interrupt received, forcing immediate exit");
            Environment.Exit(1);
        }
    }

    private void OnProcessExit(object? sender, EventArgs e)
    {
        if (!_isShuttingDown)
        {
            _log.Information("Process exit signal received");
            // Use synchronous wait since we're in a ProcessExit handler
            InitiateShutdownAsync(ShutdownReason.ProcessExit, "ProcessExit event")
                .GetAwaiter().GetResult();
        }
    }

    private static double GetElapsedMs(long startTimestamp)
    {
        return (double)(Stopwatch.GetTimestamp() - startTimestamp) / Stopwatch.Frequency * 1000;
    }

    public ValueTask DisposeAsync()
    {
        if (_isDisposed)
            return default;
        _isDisposed = true;

        Console.CancelKeyPress -= OnCancelKeyPress;
        AppDomain.CurrentDomain.ProcessExit -= OnProcessExit;

        if (!_shutdownCts.IsCancellationRequested)
        {
            _shutdownCts.Cancel();
        }

        _shutdownCts.Dispose();
        return default;
    }

    private readonly record struct FlushResult(long TotalEventsFlushed, bool TimeoutOccurred, List<string> Warnings);
    private readonly record struct DisposeResult(int DisposedCount, List<string> Warnings);
}

/// <summary>
/// Configuration for graceful shutdown behavior.
/// </summary>
public sealed record GracefulShutdownConfig
{
    /// <summary>
    /// Maximum time to wait for graceful shutdown in seconds.
    /// </summary>
    public int TimeoutSeconds { get; init; } = 30;

    /// <summary>
    /// Maximum time to wait for each component to flush in seconds.
    /// </summary>
    public int FlushTimeoutPerComponentSeconds { get; init; } = 10;

    /// <summary>
    /// Maximum time to wait for each component to dispose in seconds.
    /// </summary>
    public int DisposeTimeoutPerComponentSeconds { get; init; } = 5;

    /// <summary>
    /// Whether to force exit if shutdown times out.
    /// </summary>
    public bool ForceExitOnTimeout { get; init; } = true;

    /// <summary>
    /// Whether to force exit on second Ctrl+C signal.
    /// </summary>
    public bool ForceExitOnSecondSignal { get; init; } = true;

    public static GracefulShutdownConfig Default => new();
}

/// <summary>
/// Context information passed to shutdown handlers.
/// </summary>
public readonly record struct ShutdownContext(
    ShutdownReason Reason,
    string? Message,
    DateTimeOffset RequestedAt,
    int TimeoutSeconds
);

/// <summary>
/// Result of the shutdown operation.
/// </summary>
public readonly record struct ShutdownResult(
    bool Success,
    ShutdownReason Reason,
    DateTimeOffset StartedAt = default,
    DateTimeOffset CompletedAt = default,
    double DurationMs = 0,
    long EventsFlushed = 0,
    bool FlushTimeoutOccurred = false,
    int ComponentsDisposed = 0,
    string? ErrorMessage = null,
    string[]? Warnings = null
);

/// <summary>
/// Progress information during shutdown.
/// </summary>
public readonly record struct ShutdownProgress(
    string Phase,
    int CurrentStep,
    int TotalSteps,
    int PercentComplete,
    DateTimeOffset Timestamp
);

/// <summary>
/// Reason for shutdown.
/// </summary>
public enum ShutdownReason : byte
{
    Unknown,
    UserRequested,
    ProcessExit,
    SignalReceived,
    Error,
    MaintenanceWindow,
    ConfigurationChange,
    HealthCheckFailed,
    ResourceExhausted
}

// IFlushable interface is defined in GracefulShutdownService.cs.
