using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Threading;
using Meridian.Core.Logging;
using Meridian.Core.Monitoring;
using Serilog;

namespace Meridian.DataIntegration.Monitoring;

/// <summary>
/// Monitors connection health for market data providers.
/// Tracks heartbeats, latency, and connection state with auto-reconnect support.
/// </summary>
public sealed class ConnectionHealthMonitor : IConnectionHealthMonitor, IDisposable, IAsyncDisposable
{
    private readonly ILogger _log = LoggingSetup.ForContext<ConnectionHealthMonitor>();
    private readonly ConcurrentDictionary<string, ConnectionState> _connections = new();
    private readonly ConcurrentDictionary<string, PingOperation> _inFlightPings = new();
    private readonly ConnectionHealthConfig _config;
    private readonly TimeProvider _timeProvider;
    private readonly CancellationTokenSource _lifetimeCancellation = new();
    private readonly CancellationToken _lifetimeToken;
    private readonly PeriodicTimer _heartbeatTimer;
    private readonly PeriodicTimer _statsTimer;
    private readonly Task _heartbeatLoop;
    private readonly Task _statsLoop;
    private readonly SemaphoreSlim _heartbeatScanGate = new(1, 1);
    private readonly SemaphoreSlim _pingWorkerGate;
    private readonly AsyncLocal<int> _heartbeatCallbackDepth = new();
    private readonly object _disposeSync = new();
    private Task? _disposeTask;
    private volatile bool _isDisposed;

    // Global latency tracking
    private long _totalLatencySamples;
    private long _totalLatencyTicks;
    private long _minLatencyTicks = long.MaxValue;
    private long _maxLatencyTicks;

    /// <summary>
    /// Event raised when a connection is lost.
    /// </summary>
    public event Action<ConnectionLostEvent>? OnConnectionLost;

    /// <summary>
    /// Event raised when a connection is recovered.
    /// </summary>
    public event Action<ConnectionRecoveredEvent>? OnConnectionRecovered;

    /// <summary>
    /// Event raised when heartbeat is missed (potential connection issue).
    /// </summary>
    public event Action<HeartbeatMissedEvent>? OnHeartbeatMissed;

    /// <summary>
    /// Event raised when high latency is detected.
    /// </summary>
    public event Action<HighLatencyEvent>? OnHighLatency;

    /// <summary>
    /// Delegate for sending ping messages to connections.
    /// </summary>
    public Func<string, CancellationToken, Task<bool>>? PingSender { get; set; }

    public ConnectionHealthMonitor(ConnectionHealthConfig? config = null)
        : this(config, TimeProvider.System)
    {
    }

    internal ConnectionHealthMonitor(ConnectionHealthConfig? config, TimeProvider timeProvider)
    {
        _config = config ?? ConnectionHealthConfig.Default;
        _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
        if (_config.HeartbeatIntervalSeconds <= 0)
            throw new ArgumentOutOfRangeException(nameof(config), "Heartbeat interval must be positive.");
        if (_config.HeartbeatTimeoutSeconds <= 0)
            throw new ArgumentOutOfRangeException(nameof(config), "Heartbeat timeout must be positive.");
        if (_config.PingTimeoutSeconds <= 0)
            throw new ArgumentOutOfRangeException(nameof(config), "Ping timeout must be positive.");
        if (_config.MaxConcurrentPingSenderInvocations <= 0)
            throw new ArgumentOutOfRangeException(
                nameof(config),
                "Maximum concurrent ping sender invocations must be positive.");

        _pingWorkerGate = new SemaphoreSlim(
            _config.MaxConcurrentPingSenderInvocations,
            _config.MaxConcurrentPingSenderInvocations);
        _lifetimeToken = _lifetimeCancellation.Token;
        _heartbeatTimer = new PeriodicTimer(
            TimeSpan.FromSeconds(_config.HeartbeatIntervalSeconds),
            _timeProvider);
        _statsTimer = new PeriodicTimer(TimeSpan.FromSeconds(10), _timeProvider);
        _heartbeatLoop = RunHeartbeatLoopAsync(_lifetimeToken);
        _statsLoop = RunStatsLoopAsync(_lifetimeToken);

        _log.Information("ConnectionHealthMonitor initialized with heartbeat interval {Interval}s, timeout {Timeout}s",
            _config.HeartbeatIntervalSeconds, _config.HeartbeatTimeoutSeconds);
    }

    /// <summary>
    /// Registers a new connection for monitoring.
    /// </summary>
    public void RegisterConnection(string connectionId, string providerName)
    {
        while (!_isDisposed)
        {
            var state = _connections.GetOrAdd(
                connectionId,
                _ => new ConnectionState(connectionId, providerName, _timeProvider));
            if (state.TryMarkConnected(_timeProvider.GetTimestamp(), out _))
            {
                if (_isDisposed)
                {
                    RetireConnection(connectionId, state, ConnectionRetirementReason.Disposed);
                    return;
                }

                break;
            }

            TryRemoveExact(_connections, connectionId, state);
        }

        if (_isDisposed)
            return;

        _log.Information("Registered connection {ConnectionId} for provider {Provider}", connectionId, providerName);
    }

    /// <summary>
    /// Unregisters a connection from monitoring.
    /// </summary>
    public void UnregisterConnection(string connectionId)
    {
        var removedAny = false;
        while (_connections.TryGetValue(connectionId, out var state))
        {
            state.Retire(ConnectionRetirementReason.Unregistered);
            var removed = TryRemoveExact(_connections, connectionId, state);
            CancelPingForGeneration(connectionId, state);
            removedAny |= removed;
        }

        if (removedAny)
            _log.Information("Unregistered connection {ConnectionId}", connectionId);
    }

    /// <summary>
    /// Records a heartbeat response (pong) for a connection.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void RecordHeartbeat(string connectionId)
    {
        RecordHeartbeat(connectionId, null);
    }

    /// <summary>
    /// Records a heartbeat response (pong) for a connection with optional round-trip time.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void RecordHeartbeat(string connectionId, long? roundTripTicks)
    {
        while (_connections.TryGetValue(connectionId, out var state))
        {
            if (!state.TryRecordHeartbeat(
                    _timeProvider.GetUtcNow(),
                    _timeProvider.GetTimestamp(),
                    roundTripTicks,
                    expectedStateVersion: null,
                    out var missedHeartbeats))
            {
                PrepareWriterRetry(connectionId, state);
                continue;
            }

            if (roundTripTicks.HasValue && roundTripTicks.Value > 0)
            {
                RecordGlobalLatency(roundTripTicks.Value);
            }

            // Check if this recovers from a missed heartbeat state
            if (missedHeartbeats > 0)
            {
                _log.Information(
                    "Connection {ConnectionId} heartbeat recovered after {Missed} missed",
                    connectionId,
                    missedHeartbeats);
            }

            return;
        }
    }

    /// <summary>
    /// Records data received from a connection (implicit heartbeat).
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void RecordDataReceived(string connectionId)
    {
        while (_connections.TryGetValue(connectionId, out var state))
        {
            if (state.TryRecordDataReceived(_timeProvider.GetUtcNow(), _timeProvider.GetTimestamp()))
                return;

            PrepareWriterRetry(connectionId, state);
        }
    }

    /// <summary>
    /// Records latency for a connection (interface implementation - latency in milliseconds).
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void RecordLatency(string connectionId, double latencyMs)
    {
        var latencyTicks = (long)(latencyMs * Stopwatch.Frequency / 1000);
        RecordLatency(connectionId, latencyTicks);
    }

    /// <summary>
    /// Records latency for a connection (in ticks).
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void RecordLatency(string connectionId, long latencyTicks)
    {
        while (_connections.TryGetValue(connectionId, out var state))
        {
            if (!state.TryRecordLatency(latencyTicks))
            {
                PrepareWriterRetry(connectionId, state);
                continue;
            }

            RecordGlobalLatency(latencyTicks);

            // Check for high latency
            var latencyMs = (double)latencyTicks / Stopwatch.Frequency * 1000;
            if (latencyMs > _config.HighLatencyThresholdMs)
            {
                _log.Warning("High latency detected on {ConnectionId}: {LatencyMs:F2}ms", connectionId, latencyMs);
                try
                {
                    OnHighLatency?.Invoke(new HighLatencyEvent(
                        connectionId,
                        state.ProviderName,
                        latencyMs,
                        _timeProvider.GetUtcNow()));
                }
                catch (Exception ex)
                {
                    _log.Error(ex, "Error in high latency event handler");
                }
            }

            return;
        }
    }

    /// <summary>
    /// Marks a connection as disconnected.
    /// </summary>
    public void MarkDisconnected(string connectionId, string? reason = null)
    {
        while (_connections.TryGetValue(connectionId, out var state))
        {
            var now = _timeProvider.GetUtcNow();
            if (!state.TryMarkDisconnected(
                    _timeProvider.GetTimestamp(),
                    out var transition))
            {
                TryRemoveExact(_connections, connectionId, state);
                continue;
            }

            if (transition.Transitioned &&
                IsCurrentState(
                    connectionId,
                    state,
                    transition.StateVersion,
                    expectedConnected: false))
            {
                CancelPingForGeneration(connectionId, state);
                _log.Warning("Connection {ConnectionId} disconnected: {Reason}", connectionId, reason ?? "Unknown");

                try
                {
                    OnConnectionLost?.Invoke(new ConnectionLostEvent(
                        connectionId,
                        state.ProviderName,
                        reason,
                        now,
                        transition.PreviousStateDuration));
                }
                catch (Exception ex)
                {
                    _log.Error(ex, "Error in connection lost event handler");
                }
            }

            return;
        }
    }

    /// <summary>
    /// Marks a connection as connected (e.g., after reconnection).
    /// </summary>
    public void MarkConnected(string connectionId)
    {
        while (_connections.TryGetValue(connectionId, out var state))
        {
            var now = _timeProvider.GetUtcNow();
            if (!state.TryMarkConnected(
                    _timeProvider.GetTimestamp(),
                    out var transition))
            {
                PrepareWriterRetry(connectionId, state);
                continue;
            }

            if (transition.Transitioned &&
                IsCurrentState(
                    connectionId,
                    state,
                    transition.StateVersion,
                    expectedConnected: true))
            {
                _log.Information("Connection {ConnectionId} restored", connectionId);

                try
                {
                    OnConnectionRecovered?.Invoke(new ConnectionRecoveredEvent(
                        connectionId,
                        state.ProviderName,
                        now,
                        transition.PreviousStateDuration));
                }
                catch (Exception ex)
                {
                    _log.Error(ex, "Error in connection recovered event handler");
                }
            }

            return;
        }
    }

    /// <summary>
    /// Gets the health snapshot for all connections.
    /// </summary>
    public ConnectionHealthSnapshot GetSnapshot()
    {
        var connections = new List<ConnectionStatus>();
        var nowTimestamp = _timeProvider.GetTimestamp();

        foreach (var kvp in _connections)
        {
            if (kvp.Value.TryGetStatus(nowTimestamp, out var status))
                connections.Add(status);
        }

        return new ConnectionHealthSnapshot(
            Timestamp: _timeProvider.GetUtcNow(),
            Connections: connections,
            TotalConnections: connections.Count,
            HealthyConnections: connections.Count(c => c.IsHealthy),
            UnhealthyConnections: connections.Count(c => !c.IsHealthy),
            GlobalAverageLatencyMs: GetAverageLatencyMs(),
            GlobalMinLatencyMs: GetMinLatencyMs(),
            GlobalMaxLatencyMs: GetMaxLatencyMs()
        );
    }

    /// <summary>
    /// Gets the status of a specific connection by connection ID.
    /// </summary>
    public ConnectionStatus? GetConnectionStatus(string connectionId)
    {
        while (_connections.TryGetValue(connectionId, out var state))
        {
            if (state.TryGetStatus(_timeProvider.GetTimestamp(), out var status))
                return status;

            TryRemoveExact(_connections, connectionId, state);
        }

        return null;
    }

    /// <summary>
    /// Gets the aggregate status for a provider by provider name.
    /// Returns the first connected connection's status, or the first disconnected one if none are connected.
    /// </summary>
    public ConnectionStatus? GetConnectionStatusByProvider(string providerName)
    {
        ConnectionStatus? firstDisconnected = null;
        foreach (var kvp in _connections)
        {
            if (ProviderMonitoringIdentity.Equals(kvp.Value.ProviderName, providerName))
            {
                if (!kvp.Value.TryGetStatus(_timeProvider.GetTimestamp(), out var status))
                    continue;
                if (status.IsConnected)
                    return status;
                firstDisconnected ??= status;
            }
        }
        return firstDisconnected;
    }

    /// <summary>
    /// Gets the average latency in milliseconds across all connections.
    /// </summary>
    public double GetAverageLatencyMs()
    {
        var samples = Interlocked.Read(ref _totalLatencySamples);
        if (samples == 0)
            return 0;

        var ticks = Interlocked.Read(ref _totalLatencyTicks);
        return (double)ticks / samples / Stopwatch.Frequency * 1000;
    }

    /// <summary>
    /// Gets the minimum latency in milliseconds.
    /// </summary>
    public double GetMinLatencyMs()
    {
        var ticks = Interlocked.Read(ref _minLatencyTicks);
        if (ticks == long.MaxValue)
            return 0;
        return (double)ticks / Stopwatch.Frequency * 1000;
    }

    /// <summary>
    /// Gets the maximum latency in milliseconds.
    /// </summary>
    public double GetMaxLatencyMs()
    {
        var ticks = Interlocked.Read(ref _maxLatencyTicks);
        return (double)ticks / Stopwatch.Frequency * 1000;
    }

    private void RecordGlobalLatency(long ticks)
    {
        Interlocked.Add(ref _totalLatencyTicks, ticks);
        Interlocked.Increment(ref _totalLatencySamples);

        // Update min
        var currentMin = Interlocked.Read(ref _minLatencyTicks);
        while (ticks < currentMin)
        {
            var prev = Interlocked.CompareExchange(ref _minLatencyTicks, ticks, currentMin);
            if (prev == currentMin)
                break;
            currentMin = prev;
        }

        // Update max
        var currentMax = Interlocked.Read(ref _maxLatencyTicks);
        while (ticks > currentMax)
        {
            var prev = Interlocked.CompareExchange(ref _maxLatencyTicks, ticks, currentMax);
            if (prev == currentMax)
                break;
            currentMax = prev;
        }
    }

    private bool IsCurrentState(
        string connectionId,
        ConnectionState state,
        long stateVersion,
        bool expectedConnected)
    {
        return !_isDisposed &&
               _connections.TryGetValue(connectionId, out var current) &&
               ReferenceEquals(current, state) &&
               state.MatchesLiveVersion(stateVersion, expectedConnected);
    }

    private void RetireConnection(
        string connectionId,
        ConnectionState state,
        ConnectionRetirementReason reason)
    {
        state.Retire(reason);
        TryRemoveExact(_connections, connectionId, state);
        CancelPingForGeneration(connectionId, state);
    }

    private void CancelPingForGeneration(string connectionId, ConnectionState state)
    {
        if (!_inFlightPings.TryGetValue(connectionId, out var operation) ||
            !ReferenceEquals(operation.ConnectionState, state))
        {
            return;
        }

        operation.TryCancel();
        TryRemoveExact(_inFlightPings, connectionId, operation);
    }

    private static bool TryRemoveExact<TValue>(
        ConcurrentDictionary<string, TValue> dictionary,
        string key,
        TValue value)
        where TValue : class
    {
        return ((ICollection<KeyValuePair<string, TValue>>)dictionary)
            .Remove(new KeyValuePair<string, TValue>(key, value));
    }

    private void PrepareWriterRetry(string connectionId, ConnectionState retiredState)
    {
        TryRemoveExact(_connections, connectionId, retiredState);
        if (_isDisposed || !retiredState.TryCreateSuccessor(out var successor))
            return;

        if (!_connections.TryAdd(connectionId, successor))
            return;

        // Unregister/disposal can upgrade the source generation's retirement after
        // the stale successor is copied but before it is installed. Recheck after
        // publication so either that operation sees the successor or this writer
        // removes it, preventing a late retry from resurrecting a retired ID.
        if (!_isDisposed && retiredState.IsRetiredForStaleCleanup)
            return;

        successor.Retire(
            _isDisposed
                ? ConnectionRetirementReason.Disposed
                : ConnectionRetirementReason.Unregistered);
        TryRemoveExact(_connections, connectionId, successor);
    }

    private async Task RunHeartbeatLoopAsync(CancellationToken ct)
    {
        try
        {
            while (await _heartbeatTimer.WaitForNextTickAsync(ct).ConfigureAwait(false))
            {
                await CheckHeartbeatsAsync(ct).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // Expected during disposal.
        }
        catch (Exception ex)
        {
            _log.Error(ex, "Connection heartbeat loop stopped unexpectedly");
        }
    }

    internal Task CheckHeartbeatsOnceAsync(CancellationToken cancellationToken = default)
    {
        return CheckHeartbeatsAsync(cancellationToken);
    }

    private async Task CheckHeartbeatsAsync(CancellationToken ct)
    {
        if (_isDisposed)
            return;

        using var scanCancellation = CancellationTokenSource.CreateLinkedTokenSource(ct, _lifetimeToken);
        var scanToken = scanCancellation.Token;
        await _heartbeatScanGate.WaitAsync(scanToken).ConfigureAwait(false);

        try
        {
            if (_isDisposed)
                return;

            _heartbeatCallbackDepth.Value++;

            var now = _timeProvider.GetUtcNow();
            var nowTimestamp = _timeProvider.GetTimestamp();
            var timeout = TimeSpan.FromSeconds(_config.HeartbeatTimeoutSeconds);
            var pingAfter = TimeSpan.FromSeconds(_config.HeartbeatIntervalSeconds / 2d);
            var pingSender = PingSender;
            var pingTasks = new List<Task>();

            foreach (var kvp in _connections)
            {
                scanToken.ThrowIfCancellationRequested();

                var conn = kvp.Value;
                if (!conn.TryCheckHeartbeat(
                        nowTimestamp,
                        timeout,
                        pingAfter,
                        _config.MaxMissedHeartbeats,
                        out var observation))
                {
                    TryRemoveExact(_connections, kvp.Key, conn);
                    continue;
                }

                if (observation.HeartbeatMissed &&
                    IsCurrentState(
                        kvp.Key,
                        conn,
                        observation.StateVersion,
                        expectedConnected: !observation.Disconnected))
                {
                    if (observation.Disconnected)
                        CancelPingForGeneration(kvp.Key, conn);

                    _log.Warning("Heartbeat missed for {ConnectionId}: {Elapsed:F1}s since last activity (missed: {Count})",
                        kvp.Key,
                        observation.TimeSinceLastActivity.TotalSeconds,
                        observation.MissedHeartbeats);

                    try
                    {
                        OnHeartbeatMissed?.Invoke(new HeartbeatMissedEvent(
                            kvp.Key,
                            conn.ProviderName,
                            observation.MissedHeartbeats,
                            observation.TimeSinceLastActivity,
                            now));
                    }
                    catch (Exception ex)
                    {
                        _log.Error(ex, "Error in heartbeat missed event handler");
                    }

                    if (observation.Disconnected &&
                        !_isDisposed &&
                        IsCurrentState(
                            kvp.Key,
                            conn,
                            observation.StateVersion,
                            expectedConnected: false))
                    {
                        var reason = $"Too many missed heartbeats ({observation.MissedHeartbeats})";
                        _log.Warning(
                            "Connection {ConnectionId} disconnected: {Reason}",
                            kvp.Key,
                            reason);

                        try
                        {
                            OnConnectionLost?.Invoke(new ConnectionLostEvent(
                                kvp.Key,
                                conn.ProviderName,
                                reason,
                                now,
                                observation.UptimeDuration));
                        }
                        catch (Exception ex)
                        {
                            _log.Error(ex, "Error in connection lost event handler");
                        }
                    }
                }

                // Send ping if configured
                if (pingSender != null &&
                    observation.ShouldPing &&
                    IsCurrentState(
                        kvp.Key,
                        conn,
                        observation.StateVersion,
                        expectedConnected: true))
                {
                    pingTasks.Add(SendPingAsync(
                        kvp.Key,
                        conn,
                        observation.StateVersion,
                        pingSender,
                        scanToken));
                }
            }

            if (pingTasks.Count > 0)
                await Task.WhenAll(pingTasks).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (scanToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _log.Error(ex, "Error during heartbeat check");
        }
        finally
        {
            if (_heartbeatCallbackDepth.Value > 0)
                _heartbeatCallbackDepth.Value--;
            _heartbeatScanGate.Release();
        }
    }

    private async Task SendPingAsync(
        string connectionId,
        ConnectionState connectionState,
        long expectedStateVersion,
        Func<string, CancellationToken, Task<bool>> pingSender,
        CancellationToken ct)
    {
        if (_isDisposed ||
            ct.IsCancellationRequested ||
            !IsCurrentState(
                connectionId,
                connectionState,
                expectedStateVersion,
                expectedConnected: true))
            return;

        var pingCancellation = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var operation = new PingOperation(connectionState, pingCancellation);
        if (!_inFlightPings.TryAdd(connectionId, operation))
        {
            pingCancellation.Dispose();
            return;
        }

        if (_isDisposed ||
            ct.IsCancellationRequested ||
            !IsCurrentState(
                connectionId,
                connectionState,
                expectedStateVersion,
                expectedConnected: true))
        {
            TryRemoveExact(_inFlightPings, connectionId, operation);
            operation.TryCancel();
            operation.DisposeCancellation();
            return;
        }

        var pingStart = Stopwatch.GetTimestamp();
        var pingToken = pingCancellation.Token;
        var pingTask = InvokePingSenderIsolatedAsync(
            pingSender,
            connectionId,
            pingToken,
            _pingWorkerGate);
        operation.Attach(pingTask);
        RegisterPingCompletion(connectionId, operation, pingTask);

        try
        {
            var success = await pingTask
                .WaitAsync(
                    TimeSpan.FromSeconds(_config.PingTimeoutSeconds),
                    _timeProvider,
                    pingToken)
                .ConfigureAwait(false);
            if (success)
            {
                var pingTicks = Stopwatch.GetTimestamp() - pingStart;
                if (connectionState.TryRecordHeartbeat(
                        _timeProvider.GetUtcNow(),
                        _timeProvider.GetTimestamp(),
                        pingTicks,
                        expectedStateVersion,
                        out var missedHeartbeats))
                {
                    RecordGlobalLatency(pingTicks);
                    if (missedHeartbeats > 0)
                    {
                        _log.Information(
                            "Connection {ConnectionId} heartbeat recovered after {Missed} missed",
                            connectionId,
                            missedHeartbeats);
                    }
                }
            }
        }
        catch (TimeoutException)
        {
            operation.TryCancel();
            _log.Warning(
                "Ping to {ConnectionId} exceeded the configured {Timeout}s timeout",
                connectionId,
                _config.PingTimeoutSeconds);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (OperationCanceledException)
        {
            _log.Warning(
                "Ping to {ConnectionId} was cancelled by its sender",
                connectionId);
        }
        catch (Exception ex)
        {
            _log.Warning(ex, "Failed to send ping to {ConnectionId}", connectionId);
        }
        finally
        {
            if (pingTask.IsCompleted)
            {
                TryRemoveExact(_inFlightPings, connectionId, operation);
                operation.DisposeCancellation();
            }
        }
    }

    private static Task<bool> InvokePingSenderIsolatedAsync(
        Func<string, CancellationToken, Task<bool>> pingSender,
        string connectionId,
        CancellationToken cancellationToken,
        SemaphoreSlim workerGate)
    {
        // Invoke provider code on the default scheduler behind a bounded gate so a
        // delegate that blocks before returning its Task cannot hold the heartbeat
        // scan, disposal path, or an unbounded number of worker threads. The returned
        // task remains observed if the provider ignores cancellation and outlives
        // the configured timeout.
        return Task.Run(
            async () =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                await workerGate.WaitAsync(cancellationToken).ConfigureAwait(false);
                try
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    return await pingSender(connectionId, cancellationToken).ConfigureAwait(false);
                }
                finally
                {
                    workerGate.Release();
                }
            },
            cancellationToken);
    }

    private void RegisterPingCompletion(
        string connectionId,
        PingOperation operation,
        Task<bool> pingTask)
    {
        _ = pingTask.ContinueWith(
            static (completed, state) =>
            {
                _ = completed.Exception;
                var registration = (PingCompletionRegistration)state!;
                registration.Operation.DisposeCancellation();
                if (registration.Owner.TryGetTarget(out var owner))
                {
                    TryRemoveExact(
                        owner._inFlightPings,
                        registration.ConnectionId,
                        registration.Operation);
                }
            },
            new PingCompletionRegistration(
                new WeakReference<ConnectionHealthMonitor>(this),
                connectionId,
                operation),
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
    }

    private async Task RunStatsLoopAsync(CancellationToken ct)
    {
        try
        {
            while (await _statsTimer.WaitForNextTickAsync(ct).ConfigureAwait(false))
            {
                UpdateStats();
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // Expected during disposal.
        }
        catch (Exception ex)
        {
            _log.Error(ex, "Connection statistics loop stopped unexpectedly");
        }
    }

    private void UpdateStats(Action<string>? afterStaleRetirement = null)
    {
        if (_isDisposed)
            return;

        // Update per-connection statistics
        foreach (var kvp in _connections)
        {
            kvp.Value.TryUpdateStatistics();
        }

        // Evict connections that have been disconnected and had no activity for
        // longer than the heartbeat timeout. This prevents unbounded dictionary
        // growth when connections are not explicitly unregistered.
        var staleThreshold = TimeSpan.FromSeconds(_config.HeartbeatTimeoutSeconds * 2);
        var nowTimestamp = _timeProvider.GetTimestamp();
        foreach (var kvp in _connections)
        {
            var conn = kvp.Value;
            if (!conn.RetireIfStale(nowTimestamp, staleThreshold))
                continue;

            afterStaleRetirement?.Invoke(kvp.Key);
            var removed = TryRemoveExact(_connections, kvp.Key, conn);
            CancelPingForGeneration(kvp.Key, conn);
            if (removed)
            {
                _log.Debug(
                    "Evicted stale disconnected connection {ConnectionId} from health monitor",
                    kvp.Key);
            }
        }
    }

    internal void UpdateStatsOnce(Action<string>? afterStaleRetirement = null)
    {
        UpdateStats(afterStaleRetirement);
    }

    public void Dispose()
    {
        var disposeTask = GetOrStartDisposal();
        if (_heartbeatCallbackDepth.Value == 0)
            disposeTask.GetAwaiter().GetResult();
    }

    public ValueTask DisposeAsync()
    {
        var disposeTask = GetOrStartDisposal();
        return _heartbeatCallbackDepth.Value > 0
            ? ValueTask.CompletedTask
            : new ValueTask(disposeTask);
    }

    private Task GetOrStartDisposal()
    {
        TaskCompletionSource? starter = null;
        Task disposeTask;
        lock (_disposeSync)
        {
            if (_disposeTask is null)
            {
                starter = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                _disposeTask = starter.Task;
            }

            disposeTask = _disposeTask;
        }

        if (starter is not null)
            _ = CompleteDisposalAsync(starter);

        return disposeTask;
    }

    private async Task CompleteDisposalAsync(TaskCompletionSource completion)
    {
        try
        {
            await DisposeCoreAsync().ConfigureAwait(false);
            completion.TrySetResult();
        }
        catch (Exception ex)
        {
            _log.Error(ex, "Connection health monitor disposal failed");
            completion.TrySetException(ex);
            _ = completion.Task.Exception;
        }
    }

    private async Task DisposeCoreAsync()
    {
        _isDisposed = true;
        try
        {
            _lifetimeCancellation.Cancel();
        }
        catch (AggregateException ex)
        {
            _log.Warning(ex, "One or more ping cancellation callbacks failed during shutdown");
        }

        foreach (var operation in _inFlightPings.Values)
            operation.TryCancel();

        _heartbeatTimer.Dispose();
        _statsTimer.Dispose();

        try
        {
            await Task.WhenAll(_heartbeatLoop, _statsLoop).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (_lifetimeCancellation.IsCancellationRequested)
        {
            // The loops normally observe cancellation internally; tolerate it if a callback propagates it.
        }

        // A test-triggered or future on-demand scan can run outside the periodic loop.
        // Wait for it to observe lifetime cancellation before releasing state.
        await _heartbeatScanGate.WaitAsync().ConfigureAwait(false);
        _heartbeatScanGate.Release();

        var abandonedPings = _inFlightPings.Values.Count(operation => !operation.IsCompleted);
        if (abandonedPings > 0)
        {
            _log.Warning(
                "Connection health monitor abandoned {Count} non-cooperative ping operation(s) during shutdown; faults remain observed",
                abandonedPings);
        }

        foreach (var kvp in _connections)
            RetireConnection(kvp.Key, kvp.Value, ConnectionRetirementReason.Disposed);

        _inFlightPings.Clear();
        _connections.Clear();
        _lifetimeCancellation.Dispose();
    }

    private sealed class PingOperation(
        ConnectionState connectionState,
        CancellationTokenSource cancellation)
    {
        private readonly object _sync = new();
        private Task<bool>? _task;
        private bool _cancellationDisposed;

        public ConnectionState ConnectionState { get; } = connectionState;
        public CancellationTokenSource Cancellation { get; } = cancellation;

        public bool IsCompleted => Volatile.Read(ref _task)?.IsCompleted ?? false;

        public void Attach(Task<bool> task)
        {
            Volatile.Write(ref _task, task);
        }

        public void TryCancel()
        {
            lock (_sync)
            {
                if (!_cancellationDisposed)
                {
                    try
                    {
                        Cancellation.Cancel();
                    }
                    catch (AggregateException)
                    {
                        // Delegate-owned cancellation callbacks must not break monitor shutdown.
                    }
                }
            }
        }

        public void DisposeCancellation()
        {
            lock (_sync)
            {
                if (_cancellationDisposed)
                    return;

                _cancellationDisposed = true;
                Cancellation.Dispose();
            }
        }
    }

    private sealed record PingCompletionRegistration(
        WeakReference<ConnectionHealthMonitor> Owner,
        string ConnectionId,
        PingOperation Operation);

    private readonly record struct ConnectionTransition(
        bool Transitioned,
        TimeSpan PreviousStateDuration,
        long StateVersion);

    private readonly record struct HeartbeatObservation(
        bool HeartbeatMissed,
        int MissedHeartbeats,
        TimeSpan TimeSinceLastActivity,
        bool Disconnected,
        TimeSpan UptimeDuration,
        bool ShouldPing,
        long StateVersion);

    private enum ConnectionRetirementReason
    {
        Stale,
        Unregistered,
        Disposed,
    }

    /// <summary>
    /// Internal state for tracking a single connection.
    /// </summary>
    private sealed class ConnectionState
    {
        public string ConnectionId { get; }
        public string ProviderName { get; }

        private readonly object _sync = new();
        private readonly TimeProvider _timeProvider;
        private bool _isConnected;
        private bool _isRetired;
        private ConnectionRetirementReason _retirementReason;
        private long _stateVersion;
        private long _lastHeartbeatUtcTicks;
        private long _lastDataReceivedUtcTicks;
        private long _lastActivityTimestamp;
        private long _connectedSinceTimestamp;
        private long _disconnectedSinceTimestamp;
        private int _missedHeartbeats;
        private long _reconnectCount;
        private long _totalDataReceived;

        // Latency tracking
        private long _latencySamples;
        private long _latencyTotalTicks;
        private long _minLatencyTicks = long.MaxValue;
        private long _maxLatencyTicks;
        private long _recentLatencyTicks;
        private long _recentLatencyCount;

        public bool TryCreateSuccessor(out ConnectionState successor)
        {
            lock (_sync)
            {
                if (!_isRetired ||
                    _retirementReason != ConnectionRetirementReason.Stale)
                {
                    successor = null!;
                    return false;
                }

                successor = new ConnectionState(ConnectionId, ProviderName, _timeProvider)
                {
                    _isConnected = _isConnected,
                    _lastHeartbeatUtcTicks = _lastHeartbeatUtcTicks,
                    _lastDataReceivedUtcTicks = _lastDataReceivedUtcTicks,
                    _lastActivityTimestamp = _lastActivityTimestamp,
                    _connectedSinceTimestamp = _connectedSinceTimestamp,
                    _disconnectedSinceTimestamp = _disconnectedSinceTimestamp,
                    _missedHeartbeats = _missedHeartbeats,
                    _reconnectCount = _reconnectCount,
                    _totalDataReceived = _totalDataReceived,
                    _latencySamples = _latencySamples,
                    _latencyTotalTicks = _latencyTotalTicks,
                    _minLatencyTicks = _minLatencyTicks,
                    _maxLatencyTicks = _maxLatencyTicks,
                    _recentLatencyTicks = _recentLatencyTicks,
                    _recentLatencyCount = _recentLatencyCount,
                    _stateVersion = _stateVersion,
                };
                return true;
            }
        }

        public bool IsRetiredForStaleCleanup
        {
            get
            {
                lock (_sync)
                {
                    return _isRetired &&
                           _retirementReason == ConnectionRetirementReason.Stale;
                }
            }
        }

        public ConnectionState(string connectionId, string providerName, TimeProvider timeProvider)
        {
            ConnectionId = connectionId;
            ProviderName = providerName;
            _timeProvider = timeProvider;

            var now = _timeProvider.GetUtcNow();
            var nowTimestamp = _timeProvider.GetTimestamp();
            _lastHeartbeatUtcTicks = now.UtcTicks;
            _lastDataReceivedUtcTicks = now.UtcTicks;
            _lastActivityTimestamp = nowTimestamp;
        }

        public bool TryMarkConnected(
            long nowTimestamp,
            out ConnectionTransition transition)
        {
            lock (_sync)
            {
                if (_isRetired)
                {
                    transition = default;
                    return false;
                }

                if (_isConnected)
                {
                    transition = new ConnectionTransition(
                        Transitioned: false,
                        PreviousStateDuration: TimeSpan.Zero,
                        StateVersion: _stateVersion);
                    return true;
                }

                var downtime = GetNonNegativeElapsed(_disconnectedSinceTimestamp, nowTimestamp);
                _reconnectCount++;
                _connectedSinceTimestamp = nowTimestamp;
                _lastActivityTimestamp = Math.Max(_lastActivityTimestamp, nowTimestamp);
                _isConnected = true;
                _missedHeartbeats = 0;
                _stateVersion++;
                transition = new ConnectionTransition(
                    Transitioned: true,
                    PreviousStateDuration: downtime,
                    StateVersion: _stateVersion);
                return true;
            }
        }

        public bool TryMarkDisconnected(
            long nowTimestamp,
            out ConnectionTransition transition)
        {
            lock (_sync)
            {
                if (_isRetired)
                {
                    transition = default;
                    return false;
                }

                if (!_isConnected)
                {
                    transition = new ConnectionTransition(
                        Transitioned: false,
                        PreviousStateDuration: TimeSpan.Zero,
                        StateVersion: _stateVersion);
                    return true;
                }

                var uptime = GetNonNegativeElapsed(_connectedSinceTimestamp, nowTimestamp);
                _disconnectedSinceTimestamp = nowTimestamp;
                _isConnected = false;
                _stateVersion++;
                transition = new ConnectionTransition(
                    Transitioned: true,
                    PreviousStateDuration: uptime,
                    StateVersion: _stateVersion);
                return true;
            }
        }

        public bool TryRecordHeartbeat(
            DateTimeOffset now,
            long nowTimestamp,
            long? roundTripTicks,
            long? expectedStateVersion,
            out int missedHeartbeats)
        {
            lock (_sync)
            {
                if (_isRetired ||
                    (expectedStateVersion.HasValue && expectedStateVersion.Value != _stateVersion))
                {
                    missedHeartbeats = 0;
                    return false;
                }

                _lastHeartbeatUtcTicks = Math.Max(_lastHeartbeatUtcTicks, now.UtcTicks);
                _lastActivityTimestamp = Math.Max(_lastActivityTimestamp, nowTimestamp);
                missedHeartbeats = _missedHeartbeats;
                _missedHeartbeats = 0;

                if (roundTripTicks.HasValue && roundTripTicks.Value > 0)
                    RecordLatencyCore(roundTripTicks.Value);

                return true;
            }
        }

        public bool TryRecordDataReceived(DateTimeOffset now, long nowTimestamp)
        {
            lock (_sync)
            {
                if (_isRetired)
                    return false;

                _lastDataReceivedUtcTicks = Math.Max(_lastDataReceivedUtcTicks, now.UtcTicks);
                _lastActivityTimestamp = Math.Max(_lastActivityTimestamp, nowTimestamp);
                _missedHeartbeats = 0;
                _totalDataReceived++;
                return true;
            }
        }

        public bool TryRecordLatency(long ticks)
        {
            lock (_sync)
            {
                if (_isRetired)
                    return false;

                RecordLatencyCore(ticks);
                return true;
            }
        }

        public bool TryCheckHeartbeat(
            long nowTimestamp,
            TimeSpan timeout,
            TimeSpan pingAfter,
            int maximumMissedHeartbeats,
            out HeartbeatObservation observation)
        {
            lock (_sync)
            {
                if (_isRetired)
                {
                    observation = default;
                    return false;
                }

                if (!_isConnected)
                {
                    observation = default;
                    return true;
                }

                var elapsed = GetNonNegativeElapsed(_lastActivityTimestamp, nowTimestamp);
                if (elapsed <= timeout)
                {
                    observation = new HeartbeatObservation(
                        HeartbeatMissed: false,
                        MissedHeartbeats: _missedHeartbeats,
                        TimeSinceLastActivity: elapsed,
                        Disconnected: false,
                        UptimeDuration: TimeSpan.Zero,
                        ShouldPing: elapsed > pingAfter,
                        StateVersion: _stateVersion);
                    return true;
                }

                _missedHeartbeats++;
                var disconnected = _missedHeartbeats >= maximumMissedHeartbeats;
                var uptime = TimeSpan.Zero;
                if (disconnected)
                {
                    uptime = GetNonNegativeElapsed(_connectedSinceTimestamp, nowTimestamp);
                    _disconnectedSinceTimestamp = nowTimestamp;
                    _isConnected = false;
                    _stateVersion++;
                }

                observation = new HeartbeatObservation(
                    HeartbeatMissed: true,
                    MissedHeartbeats: _missedHeartbeats,
                    TimeSinceLastActivity: elapsed,
                    Disconnected: disconnected,
                    UptimeDuration: uptime,
                    ShouldPing: !disconnected && elapsed > pingAfter,
                    StateVersion: _stateVersion);
                return true;
            }
        }

        public bool MatchesLiveVersion(long stateVersion, bool expectedConnected)
        {
            lock (_sync)
            {
                return !_isRetired &&
                       _stateVersion == stateVersion &&
                       _isConnected == expectedConnected;
            }
        }

        public void Retire(ConnectionRetirementReason reason)
        {
            lock (_sync)
            {
                if (_isRetired && (int)reason <= (int)_retirementReason)
                    return;

                _isRetired = true;
                _retirementReason = reason;
                _stateVersion++;
            }
        }

        public bool RetireIfStale(long nowTimestamp, TimeSpan staleThreshold)
        {
            lock (_sync)
            {
                if (_isRetired)
                    return true;
                if (_isConnected ||
                    GetNonNegativeElapsed(_lastActivityTimestamp, nowTimestamp) <= staleThreshold)
                {
                    return false;
                }

                _isRetired = true;
                _retirementReason = ConnectionRetirementReason.Stale;
                _stateVersion++;
                return true;
            }
        }

        public bool TryUpdateStatistics()
        {
            lock (_sync)
            {
                if (_isRetired)
                    return false;

                _recentLatencyTicks = 0;
                _recentLatencyCount = 0;
                return true;
            }
        }

        public bool TryGetStatus(long nowTimestamp, out ConnectionStatus status)
        {
            lock (_sync)
            {
                if (_isRetired)
                {
                    status = default;
                    return false;
                }

                var avgLatencyMs = _latencySamples > 0
                    ? (double)_latencyTotalTicks / _latencySamples / Stopwatch.Frequency * 1000
                    : 0;
                var minLatencyMs = _minLatencyTicks == long.MaxValue
                    ? 0
                    : (double)_minLatencyTicks / Stopwatch.Frequency * 1000;
                var maxLatencyMs = (double)_maxLatencyTicks / Stopwatch.Frequency * 1000;
                var recentAvgMs = _recentLatencyCount > 0
                    ? (double)_recentLatencyTicks / _recentLatencyCount / Stopwatch.Frequency * 1000
                    : avgLatencyMs;
                var uptime = _isConnected
                    ? GetNonNegativeElapsed(_connectedSinceTimestamp, nowTimestamp)
                    : TimeSpan.Zero;

                status = new ConnectionStatus(
                    ConnectionId: ConnectionId,
                    ProviderName: ProviderName,
                    IsConnected: _isConnected,
                    IsHealthy: _isConnected && _missedHeartbeats == 0,
                    LastHeartbeatTime: FromUtcTicks(_lastHeartbeatUtcTicks),
                    LastDataReceivedTime: FromUtcTicks(_lastDataReceivedUtcTicks),
                    MissedHeartbeats: _missedHeartbeats,
                    ReconnectCount: _reconnectCount,
                    UptimeDuration: uptime,
                    TotalDataReceived: _totalDataReceived,
                    AverageLatencyMs: avgLatencyMs,
                    MinLatencyMs: minLatencyMs,
                    MaxLatencyMs: maxLatencyMs,
                    RecentAverageLatencyMs: recentAvgMs);
                return true;
            }
        }

        private void RecordLatencyCore(long ticks)
        {
            _latencyTotalTicks += ticks;
            _latencySamples++;
            _recentLatencyTicks += ticks;
            _recentLatencyCount++;
            _minLatencyTicks = Math.Min(_minLatencyTicks, ticks);
            _maxLatencyTicks = Math.Max(_maxLatencyTicks, ticks);
        }

        private TimeSpan GetNonNegativeElapsed(long startTimestamp, long endTimestamp)
        {
            var elapsed = _timeProvider.GetElapsedTime(startTimestamp, endTimestamp);
            return elapsed < TimeSpan.Zero ? TimeSpan.Zero : elapsed;
        }

        private static DateTimeOffset FromUtcTicks(long ticks)
        {
            return new DateTimeOffset(ticks, TimeSpan.Zero);
        }
    }
}

/// <summary>
/// Configuration for connection health monitoring.
/// </summary>
public sealed record ConnectionHealthConfig
{
    /// <summary>
    /// Interval between heartbeat checks in seconds.
    /// </summary>
    public int HeartbeatIntervalSeconds { get; init; } = 30;

    /// <summary>
    /// Timeout before considering heartbeat missed in seconds.
    /// </summary>
    public int HeartbeatTimeoutSeconds { get; init; } = 60;

    /// <summary>
    /// Number of missed heartbeats before marking connection as lost.
    /// </summary>
    public int MaxMissedHeartbeats { get; init; } = 3;

    /// <summary>
    /// Maximum time allowed for a configured ping operation before the monitor
    /// stops awaiting it and requests cancellation. Ping senders are expected to
    /// observe the supplied token; late faults from non-cooperative senders are
    /// still observed by the monitor.
    /// </summary>
    public int PingTimeoutSeconds { get; init; } = 10;

    /// <summary>
    /// Maximum number of <see cref="ConnectionHealthMonitor.PingSender"/> callbacks that may
    /// execute concurrently. Calls waiting for capacity remain cancellable, which bounds the
    /// worker threads retained by senders that block before returning a task.
    /// </summary>
    public int MaxConcurrentPingSenderInvocations { get; init; } = 32;

    /// <summary>
    /// Latency threshold in milliseconds for high latency warnings.
    /// </summary>
    public double HighLatencyThresholdMs { get; init; } = 500;

    public static ConnectionHealthConfig Default => new();
}

/// <summary>
/// Snapshot of all connection health statuses.
/// </summary>
public readonly record struct ConnectionHealthSnapshot(
    DateTimeOffset Timestamp,
    IReadOnlyList<ConnectionStatus> Connections,
    int TotalConnections,
    int HealthyConnections,
    int UnhealthyConnections,
    double GlobalAverageLatencyMs,
    double GlobalMinLatencyMs,
    double GlobalMaxLatencyMs
);

/// <summary>
/// Status of a single connection.
/// </summary>
public readonly record struct ConnectionStatus(
    string ConnectionId,
    string ProviderName,
    bool IsConnected,
    bool IsHealthy,
    DateTimeOffset LastHeartbeatTime,
    DateTimeOffset LastDataReceivedTime,
    int MissedHeartbeats,
    long ReconnectCount,
    TimeSpan UptimeDuration,
    long TotalDataReceived,
    double AverageLatencyMs,
    double MinLatencyMs,
    double MaxLatencyMs,
    double RecentAverageLatencyMs
);

// Event record structs (ConnectionLostEvent, ConnectionRecoveredEvent, HeartbeatMissedEvent, HighLatencyEvent)
// are defined in Meridian.Core/Monitoring/IConnectionHealthMonitor.cs
