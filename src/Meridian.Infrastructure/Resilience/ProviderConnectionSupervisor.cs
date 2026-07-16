using Meridian.Core.Logging;
using Meridian.Core.Resilience;
using Serilog;

namespace Meridian.Infrastructure.Resilience;

/// <summary>
/// Coordinates a provider connection as one transport-neutral lifecycle transaction.
/// The transaction is not considered connected until every caller-supplied initialization
/// step (for example authentication and subscription replay) has completed successfully.
/// </summary>
public sealed class ProviderConnectionSupervisor : IAsyncDisposable
{
    private static readonly TimeSpan DefaultDisposeTimeout = TimeSpan.FromSeconds(10);
    private readonly object _sync = new();
    private readonly object _disposeSync = new();
    private readonly SemaphoreSlim _operationGate = new(1, 1);
    private readonly SemaphoreSlim _disconnectGate = new(1, 1);
    private readonly string _providerName;
    private readonly int _maxReconnectAttempts;
    private readonly TimeSpan _retryBaseDelay;
    private readonly TimeSpan _maxRetryDelay;
    private readonly Func<TimeSpan, CancellationToken, Task> _delayAsync;
    private readonly ILogger _log;

    private CancellationTokenSource? _lifetimeCts;
    private Task<bool>? _reconnectTask;
    private ProviderConnectionLifecycleState _lifecycleState = ProviderConnectionLifecycleState.Configured;
    private bool _isReconnecting;
    private bool _reconnectEnabled;
    private bool _stopping;
    private bool _disposed;
    private int _reconnectAttempts;
    private DateTimeOffset? _lastConnectedAt;
    private DateTimeOffset? _lastDisconnectedAt;
    private DateTimeOffset? _lastReconnectAttemptAt;
    private string? _lastError;
    private ProviderFailureKind? _lastFailureKind;
    private Task? _disposeTask;

    /// <summary>
    /// Raised whenever lifecycle or failure diagnostics change.
    /// </summary>
    public event Action<ProviderConnectionSupervisorSnapshot>? StateChanged;

    /// <summary>
    /// Creates a supervisor for a provider connection.
    /// </summary>
    public ProviderConnectionSupervisor(
        string providerName,
        int maxReconnectAttempts,
        TimeSpan retryBaseDelay,
        TimeSpan maxRetryDelay,
        ILogger? logger = null)
        : this(
            providerName,
            maxReconnectAttempts,
            retryBaseDelay,
            maxRetryDelay,
            static (delay, cancellationToken) => Task.Delay(delay, cancellationToken),
            logger)
    {
    }

    internal ProviderConnectionSupervisor(
        string providerName,
        int maxReconnectAttempts,
        TimeSpan retryBaseDelay,
        TimeSpan maxRetryDelay,
        Func<TimeSpan, CancellationToken, Task> delayAsync,
        ILogger? logger = null)
    {
        if (string.IsNullOrWhiteSpace(providerName))
            throw new ArgumentException("Provider name is required.", nameof(providerName));
        if (maxReconnectAttempts < 0)
            throw new ArgumentOutOfRangeException(nameof(maxReconnectAttempts));
        if (retryBaseDelay < TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(retryBaseDelay));
        if (maxRetryDelay < TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(maxRetryDelay));

        _providerName = providerName;
        _maxReconnectAttempts = maxReconnectAttempts;
        _retryBaseDelay = retryBaseDelay;
        _maxRetryDelay = maxRetryDelay;
        _delayAsync = delayAsync ?? throw new ArgumentNullException(nameof(delayAsync));
        _log = logger ?? LoggingSetup.ForContext<ProviderConnectionSupervisor>();
    }

    /// <summary>
    /// Gets whether the complete provider connection transaction is established.
    /// </summary>
    public bool IsConnected
    {
        get
        {
            lock (_sync)
                return _lifecycleState == ProviderConnectionLifecycleState.Connected;
        }
    }

    /// <summary>
    /// Gets whether the single supervised reconnect operation is active.
    /// </summary>
    public bool IsReconnecting
    {
        get
        {
            lock (_sync)
                return _isReconnecting;
        }
    }

    /// <summary>
    /// Gets the current lifecycle state.
    /// </summary>
    public ProviderConnectionLifecycleState LifecycleState
    {
        get
        {
            lock (_sync)
                return _lifecycleState;
        }
    }

    /// <summary>
    /// Gets a safe immutable snapshot of supervisor state.
    /// </summary>
    public ProviderConnectionSupervisorSnapshot GetSnapshot()
    {
        lock (_sync)
            return CreateSnapshotLocked(DateTimeOffset.UtcNow);
    }

    /// <summary>
    /// Runs the complete initial connection transaction. Connected is published only
    /// after the transaction returns successfully.
    /// </summary>
    public async Task ConnectAsync(
        Func<CancellationToken, Task> connectTransaction,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(connectTransaction);

        await _operationGate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            CancellationToken lifetimeToken;
            ProviderConnectionSupervisorSnapshot snapshot;

            lock (_sync)
            {
                ThrowIfDisposedLocked();

                if (_lifecycleState == ProviderConnectionLifecycleState.Connected)
                    return;
                if (_stopping)
                    throw new InvalidOperationException($"{_providerName} connection is stopping.");

                lifetimeToken = EnsureLifetimeLocked();
                _reconnectEnabled = false;
                _reconnectAttempts = 0;
                _lastError = null;
                _lastFailureKind = null;
                _lifecycleState = ProviderConnectionLifecycleState.Connecting;
                snapshot = CreateSnapshotLocked(DateTimeOffset.UtcNow);
            }

            Publish(snapshot);

            using var operationCts = CancellationTokenSource.CreateLinkedTokenSource(lifetimeToken, ct);
            try
            {
                await connectTransaction(operationCts.Token).ConfigureAwait(false);
                operationCts.Token.ThrowIfCancellationRequested();

                lock (_sync)
                {
                    if (_stopping)
                        throw new OperationCanceledException(lifetimeToken);

                    _lastConnectedAt = DateTimeOffset.UtcNow;
                    _lastError = null;
                    _lastFailureKind = null;
                    _reconnectEnabled = true;
                    _lifecycleState = ProviderConnectionLifecycleState.Connected;
                    snapshot = CreateSnapshotLocked(DateTimeOffset.UtcNow);
                }

                Publish(snapshot);
            }
            catch (OperationCanceledException ex)
            {
                RecordConnectCancellation(ex);
                throw;
            }
            catch (Exception ex)
            {
                RecordTerminalFailure(ex);
                throw;
            }
        }
        finally
        {
            _operationGate.Release();
        }
    }

    /// <summary>
    /// Runs one bounded, single-flight reconnect cycle. Each attempt includes the full
    /// caller-supplied transaction, so authentication or replay failures consume an attempt.
    /// Concurrent callers join the same tracked operation.
    /// </summary>
    public Task<bool> ReconnectAsync(
        Func<CancellationToken, Task> reconnectTransaction,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(reconnectTransaction);

        Task<bool> reconnectTask;
        ProviderConnectionSupervisorSnapshot snapshot;

        lock (_sync)
        {
            ThrowIfDisposedLocked();

            if (_reconnectTask is { IsCompleted: false } activeReconnect)
                return ct.CanBeCanceled ? activeReconnect.WaitAsync(ct) : activeReconnect;

            if (_stopping || !_reconnectEnabled)
                return Task.FromResult(false);

            var lifetimeToken = EnsureLifetimeLocked();
            var reconnectCts = CancellationTokenSource.CreateLinkedTokenSource(lifetimeToken, ct);

            _isReconnecting = true;
            _reconnectAttempts = 0;
            if (_lifecycleState == ProviderConnectionLifecycleState.Connected)
                _lastDisconnectedAt = DateTimeOffset.UtcNow;
            _lifecycleState = ProviderConnectionLifecycleState.Reconnecting;
            snapshot = CreateSnapshotLocked(DateTimeOffset.UtcNow);

            reconnectTask = RunReconnectAsync(reconnectTransaction, reconnectCts, ct);
            _reconnectTask = reconnectTask;
        }

        Publish(snapshot);
        return reconnectTask;
    }

    /// <summary>
    /// Cancels the connection lifetime, waits for the tracked reconnect operation, and
    /// then invokes transport-specific cleanup exactly once for this disconnect call.
    /// </summary>
    public async Task DisconnectAsync(
        Func<CancellationToken, Task> disconnectTransaction,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(disconnectTransaction);

        await _disconnectGate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            Task<bool>? reconnectTask;
            CancellationTokenSource? lifetimeCts;
            ProviderConnectionSupervisorSnapshot snapshot;

            lock (_sync)
            {
                ThrowIfDisposedLocked();

                _stopping = true;
                _reconnectEnabled = false;
                _lifecycleState = ProviderConnectionLifecycleState.Disconnecting;
                reconnectTask = _reconnectTask;
                lifetimeCts = _lifetimeCts;
                snapshot = CreateSnapshotLocked(DateTimeOffset.UtcNow);
            }

            Publish(snapshot);

            try
            {
                lifetimeCts?.Cancel();
            }
            catch (ObjectDisposedException)
            {
                // A concurrent completed operation may already have released the source.
            }

            if (reconnectTask != null)
            {
                try
                {
                    await reconnectTask.WaitAsync(ct).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (!ct.IsCancellationRequested)
                {
                    // Expected when disconnect cancels the connection lifetime.
                }
            }

            await _operationGate.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                try
                {
                    var disconnectTask = disconnectTransaction(ct);
                    try
                    {
                        await disconnectTask.WaitAsync(ct).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException) when (ct.IsCancellationRequested && !disconnectTask.IsCompleted)
                    {
                        ObserveDeferredDisconnect(disconnectTask);
                        throw;
                    }
                }
                finally
                {
                    lock (_sync)
                    {
                        _lastDisconnectedAt = DateTimeOffset.UtcNow;
                        _isReconnecting = false;
                        _reconnectTask = null;
                        _lifetimeCts = null;
                        _stopping = false;
                        _lastError = null;
                        _lastFailureKind = null;
                        _lifecycleState = ProviderConnectionLifecycleState.Disconnected;
                        snapshot = CreateSnapshotLocked(DateTimeOffset.UtcNow);
                    }

                    lifetimeCts?.Dispose();
                    Publish(snapshot);
                }
            }
            finally
            {
                _operationGate.Release();
            }
        }
        finally
        {
            _disconnectGate.Release();
        }
    }

    /// <summary>
    /// Marks a previously complete connection as degraded. Returns true only for the
    /// first active loss notification, allowing callers to suppress duplicate reconnects.
    /// </summary>
    public bool MarkConnectionLost(Exception? error = null)
    {
        ProviderConnectionSupervisorSnapshot snapshot;

        lock (_sync)
        {
            if (_disposed || _stopping || !_reconnectEnabled ||
                _lifecycleState != ProviderConnectionLifecycleState.Connected)
            {
                return false;
            }

            _lastDisconnectedAt = DateTimeOffset.UtcNow;
            if (error != null)
            {
                _lastError = error.Message;
                _lastFailureKind = ProviderFailureClassifier.Classify(error);
            }

            _lifecycleState = ProviderConnectionLifecycleState.Degraded;
            snapshot = CreateSnapshotLocked(DateTimeOffset.UtcNow);
        }

        Publish(snapshot);
        return true;
    }

    /// <summary>
    /// Records a provider failure without changing a healthy lifecycle state.
    /// </summary>
    public void RecordFailure(Exception error)
    {
        ArgumentNullException.ThrowIfNull(error);

        ProviderConnectionSupervisorSnapshot snapshot;
        lock (_sync)
        {
            if (_disposed)
                return;

            _lastError = error.Message;
            _lastFailureKind = ProviderFailureClassifier.Classify(error);
            snapshot = CreateSnapshotLocked(DateTimeOffset.UtcNow);
        }

        Publish(snapshot);
    }

    /// <inheritdoc />
    public ValueTask DisposeAsync()
    {
        lock (_disposeSync)
            return new ValueTask(_disposeTask ??= DisposeWithTimeoutAsync());
    }

    internal ValueTask DisposeAsync(CancellationToken ct)
    {
        lock (_disposeSync)
            return new ValueTask(_disposeTask ??= DisposeCoreAsync(ct));
    }

    private async Task DisposeWithTimeoutAsync()
    {
        using var shutdownCts = new CancellationTokenSource(DefaultDisposeTimeout);
        await DisposeCoreAsync(shutdownCts.Token).ConfigureAwait(false);
    }

    private async Task DisposeCoreAsync(CancellationToken ct)
    {
        lock (_sync)
        {
            if (_disposed)
                return;
        }

        try
        {
            await DisconnectAsync(static _ => Task.CompletedTask, ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            _log.Warning(
                "Timed out waiting for {Provider} connection operations during supervisor disposal; forcing terminal state",
                _providerName);
        }
        finally
        {
            CancellationTokenSource? lifetimeCts;
            ProviderConnectionSupervisorSnapshot snapshot;
            lock (_sync)
            {
                lifetimeCts = _lifetimeCts;
                _disposed = true;
                _stopping = true;
                _reconnectEnabled = false;
                _isReconnecting = false;
                _lifecycleState = ProviderConnectionLifecycleState.Disconnected;
                _lastDisconnectedAt ??= DateTimeOffset.UtcNow;
                snapshot = CreateSnapshotLocked(DateTimeOffset.UtcNow);
            }

            try
            {
                lifetimeCts?.Cancel();
            }
            catch (ObjectDisposedException)
            {
            }

            Publish(snapshot);
        }

        GC.SuppressFinalize(this);
    }

    internal CancellationToken ConnectionLifetimeToken
    {
        get
        {
            lock (_sync)
            {
                ThrowIfDisposedLocked();
                return _lifetimeCts?.Token
                    ?? throw new InvalidOperationException($"{_providerName} connection lifetime is not active.");
            }
        }
    }

    private async Task<bool> RunReconnectAsync(
        Func<CancellationToken, Task> reconnectTransaction,
        CancellationTokenSource reconnectCts,
        CancellationToken callerToken)
    {
        // Ensure the task is assigned under the supervisor lock before work can complete.
        await Task.Yield();

        var operationLockTaken = false;
        try
        {
            await _operationGate.WaitAsync(reconnectCts.Token).ConfigureAwait(false);
            operationLockTaken = true;

            for (var attempt = 1; attempt <= _maxReconnectAttempts; attempt++)
            {
                reconnectCts.Token.ThrowIfCancellationRequested();

                ProviderConnectionSupervisorSnapshot snapshot;
                lock (_sync)
                {
                    _reconnectAttempts = attempt;
                    _lastReconnectAttemptAt = DateTimeOffset.UtcNow;
                    snapshot = CreateSnapshotLocked(DateTimeOffset.UtcNow);
                }

                Publish(snapshot);

                var delay = CalculateReconnectDelay(attempt);
                _log.Information(
                    "{Provider} reconnection attempt {Attempt}/{Max} in {Delay}ms",
                    _providerName,
                    attempt,
                    _maxReconnectAttempts,
                    delay.TotalMilliseconds);

                await _delayAsync(delay, reconnectCts.Token).ConfigureAwait(false);

                try
                {
                    await reconnectTransaction(reconnectCts.Token).ConfigureAwait(false);
                    reconnectCts.Token.ThrowIfCancellationRequested();

                    lock (_sync)
                    {
                        if (_stopping)
                            return false;

                        _lastConnectedAt = DateTimeOffset.UtcNow;
                        _lastError = null;
                        _lastFailureKind = null;
                        _reconnectEnabled = true;
                        _lifecycleState = ProviderConnectionLifecycleState.Connected;
                        snapshot = CreateSnapshotLocked(DateTimeOffset.UtcNow);
                    }

                    Publish(snapshot);
                    _log.Information(
                        "{Provider} complete connection transaction succeeded after {Attempts} reconnect attempts",
                        _providerName,
                        attempt);
                    return true;
                }
                catch (OperationCanceledException) when (reconnectCts.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    var failureKind = ProviderFailureClassifier.Classify(ex);
                    var retryable = ProviderFailureClassifier.IsRetryable(failureKind);

                    lock (_sync)
                    {
                        _lastError = ex.Message;
                        _lastFailureKind = failureKind;
                        snapshot = CreateSnapshotLocked(DateTimeOffset.UtcNow);
                    }

                    Publish(snapshot);

                    if (!retryable)
                    {
                        _log.Error(
                            ex,
                            "{Provider} reconnection stopped after non-retryable {FailureKind} failure on attempt {Attempt}",
                            _providerName,
                            failureKind,
                            attempt);
                        MarkReconnectFailed();
                        return false;
                    }

                    _log.Warning(
                        ex,
                        "{Provider} complete reconnection transaction {Attempt}/{Max} failed with {FailureKind}",
                        _providerName,
                        attempt,
                        _maxReconnectAttempts,
                        failureKind);
                }
            }

            _log.Error(
                "{Provider} failed to complete a connection transaction after {Attempts} reconnect attempts",
                _providerName,
                _maxReconnectAttempts);
            MarkReconnectFailed();
            return false;
        }
        catch (OperationCanceledException) when (!callerToken.IsCancellationRequested)
        {
            // The connection lifetime was cancelled by explicit disconnect.
            return false;
        }
        catch (OperationCanceledException ex)
        {
            RecordReconnectCancellation(ex);
            throw;
        }
        finally
        {
            if (operationLockTaken)
                _operationGate.Release();

            reconnectCts.Dispose();

            ProviderConnectionSupervisorSnapshot snapshot;
            lock (_sync)
            {
                _isReconnecting = false;
                _reconnectTask = null;
                snapshot = CreateSnapshotLocked(DateTimeOffset.UtcNow);
            }

            Publish(snapshot);
        }
    }

    private void RecordConnectCancellation(OperationCanceledException error)
    {
        ProviderConnectionSupervisorSnapshot snapshot;
        lock (_sync)
        {
            _reconnectEnabled = false;
            _lastError = error.Message;
            _lastFailureKind = ProviderFailureKind.Cancelled;
            if (!_stopping)
                _lifecycleState = ProviderConnectionLifecycleState.Disconnected;
            snapshot = CreateSnapshotLocked(DateTimeOffset.UtcNow);
        }

        Publish(snapshot);
    }

    private void RecordReconnectCancellation(OperationCanceledException error)
    {
        ProviderConnectionSupervisorSnapshot snapshot;
        lock (_sync)
        {
            _lastError = error.Message;
            _lastFailureKind = ProviderFailureKind.Cancelled;
            if (!_stopping)
                _lifecycleState = ProviderConnectionLifecycleState.Degraded;
            snapshot = CreateSnapshotLocked(DateTimeOffset.UtcNow);
        }

        Publish(snapshot);
    }

    private void RecordTerminalFailure(Exception error)
    {
        CancellationTokenSource? lifetimeCts;
        ProviderConnectionSupervisorSnapshot snapshot;

        lock (_sync)
        {
            _reconnectEnabled = false;
            _lastError = error.Message;
            _lastFailureKind = ProviderFailureClassifier.Classify(error);
            if (!_stopping)
                _lifecycleState = ProviderConnectionLifecycleState.Failed;
            lifetimeCts = _lifetimeCts;
            snapshot = CreateSnapshotLocked(DateTimeOffset.UtcNow);
        }

        try
        {
            lifetimeCts?.Cancel();
        }
        catch (ObjectDisposedException)
        {
        }

        Publish(snapshot);
    }

    private void MarkReconnectFailed()
    {
        ProviderConnectionSupervisorSnapshot snapshot;
        lock (_sync)
        {
            _reconnectEnabled = false;
            if (!_stopping)
                _lifecycleState = ProviderConnectionLifecycleState.Failed;
            snapshot = CreateSnapshotLocked(DateTimeOffset.UtcNow);
        }

        Publish(snapshot);
    }

    private void ObserveDeferredDisconnect(Task disconnectTask)
        => _ = ObserveDeferredDisconnectAsync(disconnectTask);

    private async Task ObserveDeferredDisconnectAsync(Task disconnectTask)
    {
        try
        {
            await disconnectTask.ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // A cooperative transaction may finish cancellation after the caller's bounded wait.
        }
        catch (Exception ex)
        {
            _log.Warning(
                ex,
                "Deferred {Provider} disconnect transaction failed after the caller stopped waiting",
                _providerName);
        }
    }

    private CancellationToken EnsureLifetimeLocked()
    {
        if (_lifetimeCts is { IsCancellationRequested: false })
            return _lifetimeCts.Token;

        _lifetimeCts?.Dispose();
        _lifetimeCts = new CancellationTokenSource();
        return _lifetimeCts.Token;
    }

    private TimeSpan CalculateReconnectDelay(int attempt)
        => Backoff.ExponentialDelay(attempt, _retryBaseDelay, _maxRetryDelay, jitterFraction: 0.2);

    private ProviderConnectionSupervisorSnapshot CreateSnapshotLocked(DateTimeOffset now)
        => new(
            LifecycleState: _lifecycleState,
            IsConnected: _lifecycleState == ProviderConnectionLifecycleState.Connected,
            IsReconnecting: _isReconnecting,
            ReconnectAttempts: _reconnectAttempts,
            LastConnectedAt: _lastConnectedAt,
            LastDisconnectedAt: _lastDisconnectedAt,
            LastReconnectAttemptAt: _lastReconnectAttemptAt,
            LastError: _lastError,
            LastFailureKind: _lastFailureKind,
            ConnectionAge: _lifecycleState == ProviderConnectionLifecycleState.Connected && _lastConnectedAt.HasValue
                ? now - _lastConnectedAt.Value
                : null);

    private void Publish(ProviderConnectionSupervisorSnapshot snapshot)
    {
        _log.Debug(
            "{Provider} connection supervisor state {LifecycleState}, reconnecting: {IsReconnecting}, attempt: {Attempt}",
            _providerName,
            snapshot.LifecycleState,
            snapshot.IsReconnecting,
            snapshot.ReconnectAttempts);

        try
        {
            StateChanged?.Invoke(snapshot);
        }
        catch (Exception ex)
        {
            _log.Error(ex, "{Provider} connection supervisor state handler failed", _providerName);
        }
    }

    private void ThrowIfDisposedLocked()
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(ProviderConnectionSupervisor));
    }
}

/// <summary>
/// Immutable, transport-neutral provider connection lifecycle diagnostics.
/// </summary>
public sealed record ProviderConnectionSupervisorSnapshot(
    ProviderConnectionLifecycleState LifecycleState,
    bool IsConnected,
    bool IsReconnecting,
    int ReconnectAttempts,
    DateTimeOffset? LastConnectedAt,
    DateTimeOffset? LastDisconnectedAt,
    DateTimeOffset? LastReconnectAttemptAt,
    string? LastError,
    ProviderFailureKind? LastFailureKind,
    TimeSpan? ConnectionAge);
