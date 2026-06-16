using System.Diagnostics;
using System.Threading;
using Meridian.Core.Diagnostics;
using Meridian.Core.Logging;
using Meridian.Core.Services;
using Meridian.Platform.Diagnostics;
using Serilog;

namespace Meridian.Platform.Runtime;

/// <summary>
/// Handles graceful shutdown of the application, ensuring all buffered events
/// are flushed to storage before termination. Provides timeout handling,
/// progress reporting, and webhook notifications.
/// </summary>
public sealed class GracefulShutdownHandler : IAsyncDisposable
{
    private const string ShutdownOperationName = "runtime.shutdown.sequence";
    private const string ComponentName = nameof(GracefulShutdownHandler);

    private readonly ILogger _log = LoggingSetup.ForContext<GracefulShutdownHandler>();
    private readonly GracefulShutdownConfig _config;
    private readonly ShutdownDiagnosticsService? _shutdownDiagnostics;
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

    public GracefulShutdownHandler(
        GracefulShutdownConfig? config = null,
        ShutdownDiagnosticsService? shutdownDiagnostics = null)
    {
        _config = config ?? GracefulShutdownConfig.Default;
        _shutdownDiagnostics = shutdownDiagnostics;

        // Register for process termination signals
        Console.CancelKeyPress += OnCancelKeyPress;
        AppDomain.CurrentDomain.ProcessExit += OnProcessExit;

        _log.Information(
            "Graceful shutdown handler initialized for {OperationName}; componentName={ComponentName}; timeoutSeconds={TimeoutSeconds}",
            ShutdownOperationName,
            ComponentName,
            _config.TimeoutSeconds);
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
            _shutdownDiagnostics?.RecordDuplicateRequest(reason, _shutdownReason);
            _log.Warning(
                "Duplicate shutdown request ignored for {OperationName}; componentName={ComponentName}; reason={Reason}; activeReason={ActiveReason}; recoveryAction={RecoveryAction}",
                ShutdownOperationName,
                ComponentName,
                reason,
                _shutdownReason,
                "Wait for the active shutdown sequence to complete");
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
        var correlationId = Guid.NewGuid().ToString("N");
        var safeMessage = RuntimeDiagnosticRedactor.SanitizeText(message);

        _log.Information(
            "Shutdown sequence started for {OperationName}; componentName={ComponentName}; correlationId={CorrelationId}; reason={Reason}; message={Message}; timeoutSeconds={TimeoutSeconds}; flushableComponents={FlushableCount}; disposableComponents={DisposableCount}; callbackCount={CallbackCount}",
            ShutdownOperationName,
            ComponentName,
            correlationId,
            reason,
            string.IsNullOrWhiteSpace(safeMessage) ? "No message" : safeMessage,
            _config.TimeoutSeconds,
            _flushables.Count,
            _disposables.Count,
            _shutdownCallbacks.Count);

        var context = new ShutdownContext(
            Reason: reason,
            Message: safeMessage,
            RequestedAt: _shutdownRequestedAt,
            TimeoutSeconds: _config.TimeoutSeconds,
            CorrelationId: correlationId
        );
        _shutdownDiagnostics?.RecordStarted(
            correlationId,
            reason,
            _shutdownRequestedAt,
            _flushables.Count,
            _disposables.Count,
            _shutdownCallbacks.Count);

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
            ReportProgress("Stopping event producers", 1, 4, correlationId);
            _shutdownCts.Cancel();

            // Phase 3: Flush all pending events
            ReportProgress("Flushing pending events", 2, 4, correlationId);
            var flushResult = await FlushAllAsync(correlationId, linkedCts.Token);

            // Phase 4: Dispose resources
            ReportProgress("Disposing resources", 3, 4, correlationId);
            var disposeResult = await DisposeAllAsync(correlationId, linkedCts.Token);

            ReportProgress("Shutdown complete", 4, 4, correlationId);

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
                Warnings: flushResult.Warnings.Concat(disposeResult.Warnings).ToArray(),
                CorrelationId: correlationId
            );
            _shutdownDiagnostics?.RecordCompleted(result);

            _log.Information(
                "Shutdown sequence completed for {OperationName}; componentName={ComponentName}; correlationId={CorrelationId}; reason={Reason}; elapsedMs={ElapsedMs}; eventsFlushed={EventsFlushed}; flushTimeoutOccurred={FlushTimeoutOccurred}; componentsDisposed={ComponentsDisposed}; warningCount={WarningCount}",
                ShutdownOperationName,
                ComponentName,
                correlationId,
                reason,
                elapsedMs,
                result.EventsFlushed,
                result.FlushTimeoutOccurred,
                result.ComponentsDisposed,
                result.Warnings?.Length ?? 0);

            OnShutdownCompleted?.Invoke(result);
            return result;
        }
        catch (OperationCanceledException) when (!_shutdownCts.IsCancellationRequested)
        {
            // Timeout occurred
            var elapsedMs = GetElapsedMs(startTime);
            _log.Error(
                "Shutdown sequence timed out for {OperationName}; componentName={ComponentName}; correlationId={CorrelationId}; reason={Reason}; elapsedMs={ElapsedMs}; timeoutSeconds={TimeoutSeconds}; recoveryAction={RecoveryAction}",
                ShutdownOperationName,
                ComponentName,
                correlationId,
                reason,
                elapsedMs,
                _config.TimeoutSeconds,
                "Inspect shutdown phase logs and verify buffered data before restart");

            var result = new ShutdownResult(
                Success: false,
                Reason: reason,
                StartedAt: _shutdownRequestedAt,
                CompletedAt: DateTimeOffset.UtcNow,
                DurationMs: elapsedMs,
                FlushTimeoutOccurred: true,
                ErrorMessage: $"Shutdown timed out after {_config.TimeoutSeconds} seconds",
                CorrelationId: correlationId
            );
            _shutdownDiagnostics?.RecordTimedOut(result);

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
            _log.Error(
                "Shutdown sequence failed for {OperationName}; componentName={ComponentName}; correlationId={CorrelationId}; reason={Reason}; elapsedMs={ElapsedMs}; exceptionType={ExceptionType}; failureReason={FailureReason}; recoveryAction={RecoveryAction}",
                ShutdownOperationName,
                ComponentName,
                correlationId,
                reason,
                elapsedMs,
                ex.GetType().Name,
                RuntimeDiagnosticRedactor.SanitizeText(ex.Message),
                "Inspect failed shutdown component and verify buffered data before restart");

            var result = new ShutdownResult(
                Success: false,
                Reason: reason,
                StartedAt: _shutdownRequestedAt,
                CompletedAt: DateTimeOffset.UtcNow,
                DurationMs: elapsedMs,
                ErrorMessage: RuntimeDiagnosticRedactor.SanitizeText(ex.Message),
                CorrelationId: correlationId
            );
            _shutdownDiagnostics?.RecordFailed(result);

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

        _log.Debug(
            "Executing shutdown callbacks for {OperationName}; componentName={ComponentName}; correlationId={CorrelationId}; callbackCount={CallbackCount}",
            ShutdownOperationName,
            ComponentName,
            context.CorrelationId,
            _shutdownCallbacks.Count);

        foreach (var callback in _shutdownCallbacks)
        {
            try
            {
                await callback(context);
            }
            catch (Exception ex)
            {
                _log.Warning(
                    "Shutdown callback failed for {OperationName}; componentName={ComponentName}; correlationId={CorrelationId}; exceptionType={ExceptionType}; failureReason={FailureReason}; recoveryAction={RecoveryAction}",
                    ShutdownOperationName,
                    ComponentName,
                    context.CorrelationId,
                    ex.GetType().Name,
                    RuntimeDiagnosticRedactor.SanitizeText(ex.Message),
                    "Continue shutdown and inspect callback implementation");
            }
        }
    }

    private async Task<FlushResult> FlushAllAsync(string correlationId, CancellationToken ct)
    {
        var warnings = new List<string>();
        long totalEventsFlushed = 0;
        var timeoutOccurred = false;

        if (_flushables.Count == 0)
        {
            _log.Debug(
                "No flushable components registered for {OperationName}; componentName={ComponentName}; correlationId={CorrelationId}",
                ShutdownOperationName,
                ComponentName,
                correlationId);
            return new FlushResult(totalEventsFlushed, false, warnings);
        }

        _log.Information(
            "Flushing shutdown components for {OperationName}; componentName={ComponentName}; correlationId={CorrelationId}; flushableComponents={FlushableCount}",
            ShutdownOperationName,
            ComponentName,
            correlationId,
            _flushables.Count);

        foreach (var flushable in _flushables)
        {
            var componentName = flushable.GetType().Name;
            var flushStart = Stopwatch.GetTimestamp();

            try
            {
                _log.Debug(
                    "Flushing shutdown component for {OperationName}; componentName={ComponentName}; correlationId={CorrelationId}",
                    ShutdownOperationName,
                    componentName,
                    correlationId);

                // Capture pending buffered items before flush when the component exposes diagnostics.
                long queueSize = 0;
                if (flushable is IFlushableQueueDiagnostics queueDiagnostics)
                {
                    queueSize = queueDiagnostics.PendingFlushItemCount;
                    totalEventsFlushed += queueSize;
                }

                var flushTask = flushable.FlushAsync(ct);
                var completedTask = await Task.WhenAny(
                    flushTask,
                    Task.Delay(TimeSpan.FromSeconds(_config.FlushTimeoutPerComponentSeconds), ct)
                );

                if (completedTask != flushTask)
                {
                    _log.Warning(
                        "Flush timeout for {OperationName}; componentName={ComponentName}; correlationId={CorrelationId}; timeoutSeconds={TimeoutSeconds}; queueDepth={QueueDepth}; recoveryAction={RecoveryAction}",
                        ShutdownOperationName,
                        componentName,
                        correlationId,
                        _config.FlushTimeoutPerComponentSeconds,
                        queueSize,
                        "Verify latest buffered data before trusting this session");
                    warnings.Add($"Flush timeout for {componentName}");
                    timeoutOccurred = true;
                }
                else
                {
                    await flushTask.ConfigureAwait(false);
                    _log.Debug(
                        "Shutdown component flushed for {OperationName}; componentName={ComponentName}; correlationId={CorrelationId}; elapsedMs={ElapsedMs}; queueDepth={QueueDepth}",
                        ShutdownOperationName,
                        componentName,
                        correlationId,
                        GetElapsedMs(flushStart),
                        queueSize);
                }
            }
            catch (OperationCanceledException)
            {
                _log.Warning(
                    "Flush cancelled for {OperationName}; componentName={ComponentName}; correlationId={CorrelationId}; elapsedMs={ElapsedMs}; recoveryAction={RecoveryAction}",
                    ShutdownOperationName,
                    componentName,
                    correlationId,
                    GetElapsedMs(flushStart),
                    "Verify latest buffered data before trusting this session");
                warnings.Add($"Flush cancelled for {componentName}");
                timeoutOccurred = true;
            }
            catch (Exception ex)
            {
                _log.Error(
                    "Flush failed for {OperationName}; componentName={ComponentName}; correlationId={CorrelationId}; elapsedMs={ElapsedMs}; exceptionType={ExceptionType}; failureReason={FailureReason}; recoveryAction={RecoveryAction}",
                    ShutdownOperationName,
                    componentName,
                    correlationId,
                    GetElapsedMs(flushStart),
                    ex.GetType().Name,
                    RuntimeDiagnosticRedactor.SanitizeText(ex.Message),
                    "Inspect component logs and rerun affected ingestion or export");
                warnings.Add($"Flush error for {componentName}: {RuntimeDiagnosticRedactor.SanitizeText(ex.Message)}");
            }
        }

        _log.Information(
            "Shutdown component flush completed for {OperationName}; componentName={ComponentName}; correlationId={CorrelationId}; approximateEventsFlushed={EventCount}; timeoutOccurred={TimeoutOccurred}; warningCount={WarningCount}",
            ShutdownOperationName,
            ComponentName,
            correlationId,
            totalEventsFlushed,
            timeoutOccurred,
            warnings.Count);
        return new FlushResult(totalEventsFlushed, timeoutOccurred, warnings);
    }

    private async Task<DisposeResult> DisposeAllAsync(string correlationId, CancellationToken ct)
    {
        var warnings = new List<string>();
        var disposedCount = 0;

        if (_disposables.Count == 0)
        {
            return new DisposeResult(0, warnings);
        }

        _log.Debug(
            "Disposing shutdown components for {OperationName}; componentName={ComponentName}; correlationId={CorrelationId}; disposableComponents={DisposableCount}",
            ShutdownOperationName,
            ComponentName,
            correlationId,
            _disposables.Count);

        // Dispose in reverse order (LIFO)
        foreach (var disposable in Enumerable.Reverse(_disposables))
        {
            var componentName = disposable.GetType().Name;
            var disposeStart = Stopwatch.GetTimestamp();

            try
            {
                var disposeTask = disposable.DisposeAsync().AsTask();
                var completedTask = await Task.WhenAny(
                    disposeTask,
                    Task.Delay(TimeSpan.FromSeconds(_config.DisposeTimeoutPerComponentSeconds), ct)
                );

                if (completedTask != disposeTask)
                {
                    _log.Warning(
                        "Dispose timeout for {OperationName}; componentName={ComponentName}; correlationId={CorrelationId}; timeoutSeconds={TimeoutSeconds}; recoveryAction={RecoveryAction}",
                        ShutdownOperationName,
                        componentName,
                        correlationId,
                        _config.DisposeTimeoutPerComponentSeconds,
                        "Inspect resource lifecycle before restart");
                    warnings.Add($"Dispose timeout for {componentName}");
                }
                else
                {
                    await disposeTask.ConfigureAwait(false);
                    disposedCount++;
                    _log.Debug(
                        "Shutdown component disposed for {OperationName}; componentName={ComponentName}; correlationId={CorrelationId}; elapsedMs={ElapsedMs}",
                        ShutdownOperationName,
                        componentName,
                        correlationId,
                        GetElapsedMs(disposeStart));
                }
            }
            catch (Exception ex)
            {
                _log.Warning(
                    "Dispose failed for {OperationName}; componentName={ComponentName}; correlationId={CorrelationId}; elapsedMs={ElapsedMs}; exceptionType={ExceptionType}; failureReason={FailureReason}; recoveryAction={RecoveryAction}",
                    ShutdownOperationName,
                    componentName,
                    correlationId,
                    GetElapsedMs(disposeStart),
                    ex.GetType().Name,
                    RuntimeDiagnosticRedactor.SanitizeText(ex.Message),
                    "Inspect resource lifecycle before restart");
                warnings.Add($"Dispose error for {componentName}: {RuntimeDiagnosticRedactor.SanitizeText(ex.Message)}");
            }
        }

        return new DisposeResult(disposedCount, warnings);
    }

    private void ReportProgress(string phase, int current, int total, string correlationId)
    {
        var progress = new ShutdownProgress(
            Phase: phase,
            CurrentStep: current,
            TotalSteps: total,
            PercentComplete: (int)((double)current / total * 100),
            Timestamp: DateTimeOffset.UtcNow,
            CorrelationId: correlationId
        );

        _log.Debug(
            "Shutdown progress for {OperationName}; componentName={ComponentName}; correlationId={CorrelationId}; phase={ShutdownPhase}; currentStep={Current}; totalSteps={Total}; percentComplete={PercentComplete}",
            ShutdownOperationName,
            ComponentName,
            correlationId,
            phase,
            current,
            total,
            progress.PercentComplete);
        OnProgress?.Invoke(progress);
    }

    private void OnCancelKeyPress(object? sender, ConsoleCancelEventArgs e)
    {
        e.Cancel = true; // Prevent immediate termination
        _log.Information(
            "Shutdown signal received for {OperationName}; componentName={ComponentName}; signal={Signal}",
            ShutdownOperationName,
            ComponentName,
            "Ctrl+C");

        if (!_isShuttingDown)
        {
            _ = InitiateShutdownAsync(ShutdownReason.UserRequested, "Ctrl+C received");
        }
        else if (_config.ForceExitOnSecondSignal)
        {
            _log.Warning(
                "Second shutdown signal received for {OperationName}; componentName={ComponentName}; recoveryAction={RecoveryAction}",
                ShutdownOperationName,
                ComponentName,
                "Force immediate exit after active graceful shutdown did not finish");
            Environment.Exit(1);
        }
    }

    private void OnProcessExit(object? sender, EventArgs e)
    {
        if (!_isShuttingDown)
        {
            _log.Information(
                "Process exit signal received for {OperationName}; componentName={ComponentName}",
                ShutdownOperationName,
                ComponentName);
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

// Shutdown lifecycle DTOs are defined in Meridian.Platform.Diagnostics.
// IFlushable is defined in Meridian.Core.Services.
