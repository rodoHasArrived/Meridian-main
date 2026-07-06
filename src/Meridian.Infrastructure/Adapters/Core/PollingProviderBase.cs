using Meridian.Core.Exceptions;
using Meridian.Infrastructure.Contracts;
using Meridian.Infrastructure.Resilience;
using Microsoft.Extensions.Logging;

namespace Meridian.Infrastructure.Adapters.Core;

/// <summary>
/// Abstract base class for polling-based market data providers (REST providers that do not
/// expose a streaming WebSocket API). Consolidates the connection lifecycle, honest
/// connection diagnostics, and degraded-state backoff that would otherwise be hand-rolled per
/// provider — the polling counterpart to <see cref="WebSocketProviderBase"/>.
///
/// <para>
/// Derived classes implement the provider-specific poll via <see cref="PollOnceAsync"/> and
/// report their subscription count via <see cref="ActiveSubscriptionCount"/>. They may record
/// poll activity through the protected <c>Record*</c> helpers so the shared diagnostics stay
/// accurate.
/// </para>
/// </summary>
[ImplementsAdr("ADR-001", "Unified polling provider base class for REST-polling market data providers")]
[ImplementsAdr("ADR-004", "All async methods support CancellationToken")]
public abstract class PollingProviderBase
{
    private readonly string _providerName;
    private readonly SemaphoreSlim _lifecycleLock = new(1, 1);

    private CancellationTokenSource? _pollCts;
    private Task? _pollTask;
    private volatile bool _connected;
    private volatile bool _disposed;
    private volatile ProviderConnectionLifecycleState _lifecycleState = ProviderConnectionLifecycleState.Configured;
    private DateTimeOffset? _connectedAt;
    private DateTimeOffset? _disconnectedAt;
    private DateTimeOffset? _lastPollAttemptAt;
    private DateTimeOffset? _lastSuccessfulApiCallAt;
    private DateTimeOffset? _lastMessageReceivedAt;
    private string? _lastError;
    private int _consecutivePollFailures;

    /// <summary>Logger for the derived provider.</summary>
    protected readonly ILogger Log;

    /// <summary>Interval between poll cycles.</summary>
    protected TimeSpan PollInterval { get; }

    protected PollingProviderBase(string providerName, ILogger logger, TimeSpan pollInterval)
    {
        _providerName = providerName;
        Log = logger ?? throw new ArgumentNullException(nameof(logger));
        PollInterval = pollInterval;
    }

    /// <summary>Raised whenever the connection diagnostics change (e.g. a lifecycle transition).</summary>
    public event Action<WebSocketConnectionDiagnostics>? ConnectionDiagnosticsChanged;

    /// <summary>Whether the provider is configured/enabled and may be connected.</summary>
    public abstract bool IsEnabled { get; }

    // ── Protected state accessors (for provider-specific diagnostics snapshots) ──

    protected bool Disposed => _disposed;
    protected bool Connected => _connected;
    protected ProviderConnectionLifecycleState LifecycleState => _lifecycleState;
    protected DateTimeOffset? ConnectedAt => _connectedAt;
    protected DateTimeOffset? DisconnectedAt => _disconnectedAt;
    protected DateTimeOffset? LastPollAttemptAt => _lastPollAttemptAt;
    protected DateTimeOffset? LastSuccessfulApiCallAt => _lastSuccessfulApiCallAt;
    protected DateTimeOffset? LastMessageReceivedAt => _lastMessageReceivedAt;
    protected string? LastError => _lastError;
    protected int ConsecutivePollFailures => _consecutivePollFailures;

    /// <summary>Message recorded to <see cref="LastError"/> when a connect is attempted while not enabled.</summary>
    protected virtual string NotEnabledError => $"{_providerName} is not configured.";

    /// <summary>Exception thrown by <see cref="ConnectAsync"/> when the provider is not enabled.</summary>
    protected virtual Exception CreateNotEnabledException() => new ConnectionException(NotEnabledError);

    // ── Lifecycle ──────────────────────────────────────────────────────────

    /// <summary>Starts the polling loop after validating that the provider is enabled.</summary>
    public async Task ConnectAsync(CancellationToken ct = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (!IsEnabled)
        {
            SetLifecycleState(ProviderConnectionLifecycleState.NotConfigured);
            _lastError = NotEnabledError;
            throw CreateNotEnabledException();
        }

        await _lifecycleLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (_connected)
                return;

            SetLifecycleState(ProviderConnectionLifecycleState.Connecting);
            _pollCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            _connected = true;
            _connectedAt = DateTimeOffset.UtcNow;
            _lastError = null;
            _consecutivePollFailures = 0;
            SetLifecycleState(ProviderConnectionLifecycleState.Connected);
            _pollTask = RunPollLoopAsync(_pollCts.Token);
            Log.LogInformation("{Provider} connected (polling interval {Interval})", _providerName, PollInterval);
        }
        finally
        {
            _lifecycleLock.Release();
        }
    }

    /// <summary>Stops the polling loop and marks the provider disconnected.</summary>
    public async Task DisconnectAsync(CancellationToken ct = default)
    {
        await _lifecycleLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (!_connected)
            {
                if (_lifecycleState is not ProviderConnectionLifecycleState.NotConfigured)
                    SetLifecycleState(ProviderConnectionLifecycleState.Disconnected);
                return;
            }

            SetLifecycleState(ProviderConnectionLifecycleState.Disconnecting);
            _connected = false;

            if (_pollCts is not null)
            {
                await _pollCts.CancelAsync().ConfigureAwait(false);
                if (_pollTask is not null)
                {
                    try
                    { await _pollTask.WaitAsync(ct).ConfigureAwait(false); }
                    catch (OperationCanceledException) { }
                }
                _pollCts.Dispose();
                _pollCts = null;
            }

            _pollTask = null;
            _disconnectedAt = DateTimeOffset.UtcNow;
            SetLifecycleState(ProviderConnectionLifecycleState.Disconnected);
            Log.LogInformation("{Provider} disconnected", _providerName);
        }
        finally
        {
            _lifecycleLock.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
            return;
        _disposed = true;
        await DisconnectAsync().ConfigureAwait(false);
        _lifecycleLock.Dispose();
        GC.SuppressFinalize(this);
    }

    // ── Diagnostics ────────────────────────────────────────────────────────

    /// <summary>
    /// Honest connection diagnostics for a polling provider. REST polling holds no WebSocket, so
    /// <see cref="WebSocketConnectionDiagnostics.WebSocketState"/> is reported as
    /// <see cref="System.Net.WebSockets.WebSocketState.None"/>; poll activity maps onto the
    /// heartbeat/message fields and consecutive poll failures onto reconnect attempts.
    /// </summary>
    public WebSocketConnectionDiagnostics GetConnectionDiagnosticsSnapshot()
    {
        var now = DateTimeOffset.UtcNow;
        return new WebSocketConnectionDiagnostics(
            ProviderName: _providerName,
            LifecycleState: _lifecycleState,
            WebSocketState: System.Net.WebSockets.WebSocketState.None,
            IsConnected: _connected,
            IsReconnecting: _connected && _consecutivePollFailures > 0,
            ReconnectAttempts: _consecutivePollFailures,
            LastConnectedAt: _connectedAt,
            LastDisconnectedAt: _disconnectedAt,
            LastHeartbeatReceivedAt: _lastSuccessfulApiCallAt,
            LastMessageReceivedAt: _lastMessageReceivedAt,
            LastReconnectAttemptAt: _lastPollAttemptAt,
            LastError: _lastError,
            LastFailureKind: null,
            ConnectionAge: _connected && _connectedAt is { } connectedAt ? now - connectedAt : null,
            IdleDuration: _lastMessageReceivedAt is { } lastMessageAt ? now - lastMessageAt : null,
            ActiveSubscriptions: ActiveSubscriptionCount);
    }

    // ── Hooks for derived providers ─────────────────────────────────────────

    /// <summary>
    /// Performs a single poll cycle across the provider's subscriptions, returning
    /// <see langword="true"/> when the cycle succeeded and <see langword="false"/> when it failed
    /// (which drives the shared degraded-state backoff). Implementations should update poll
    /// diagnostics via the protected <c>Record*</c> helpers.
    /// </summary>
    protected abstract Task<bool> PollOnceAsync(CancellationToken ct);

    /// <summary>Current number of active subscriptions, surfaced in diagnostics.</summary>
    protected abstract int ActiveSubscriptionCount { get; }

    // ── Diagnostics mutators for derived providers ──────────────────────────

    protected void RecordPollAttempt() => _lastPollAttemptAt = DateTimeOffset.UtcNow;
    protected void RecordSuccessfulApiCall() => _lastSuccessfulApiCallAt = DateTimeOffset.UtcNow;
    protected void RecordMessageReceived() => _lastMessageReceivedAt = DateTimeOffset.UtcNow;
    protected void RecordError(string message) => _lastError = message;
    protected void ClearError() => _lastError = null;
    protected void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(_disposed, this);

    /// <summary>Transitions the lifecycle state and raises <see cref="ConnectionDiagnosticsChanged"/>.</summary>
    protected void SetLifecycleState(ProviderConnectionLifecycleState state)
    {
        if (_lifecycleState == state)
            return;

        _lifecycleState = state;
        Log.LogInformation("{Provider} lifecycle state changed to {LifecycleState}", _providerName, state);

        try
        {
            ConnectionDiagnosticsChanged?.Invoke(GetConnectionDiagnosticsSnapshot());
        }
        catch (Exception ex)
        {
            Log.LogWarning(ex, "{Provider} connection diagnostics subscriber threw", _providerName);
        }
    }

    // ── Polling loop ────────────────────────────────────────────────────────

    private async Task RunPollLoopAsync(CancellationToken ct)
    {
        Log.LogInformation("{Provider} poll loop started", _providerName);
        try
        {
            while (!ct.IsCancellationRequested)
            {
                await Task.Delay(PollInterval, ct).ConfigureAwait(false);

                var pollSucceeded = await PollOnceAsync(ct).ConfigureAwait(false);

                if (pollSucceeded)
                {
                    _consecutivePollFailures = 0;
                    if (_lifecycleState is ProviderConnectionLifecycleState.Degraded)
                        SetLifecycleState(ProviderConnectionLifecycleState.Connected);
                }
                else
                {
                    _consecutivePollFailures++;
                    if (_lifecycleState is not ProviderConnectionLifecycleState.Failed)
                        SetLifecycleState(ProviderConnectionLifecycleState.Degraded);
                    var backoff = CalculatePollBackoff(_consecutivePollFailures);
                    Log.LogWarning(
                        "{Provider} polling degraded after {Failures} consecutive failed poll cycles; backing off for {Delay}",
                        _providerName,
                        _consecutivePollFailures,
                        backoff);
                    await Task.Delay(backoff, ct).ConfigureAwait(false);
                }
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            Log.LogError(ex, "{Provider} poll loop failed unexpectedly", _providerName);
        }
        finally
        {
            Log.LogInformation("{Provider} poll loop stopped", _providerName);
        }
    }

    /// <summary>Exponential backoff (capped at 30s) applied after consecutive failed poll cycles.</summary>
    private static TimeSpan CalculatePollBackoff(int consecutiveFailures)
    {
        var attempt = Math.Clamp(consecutiveFailures, 1, 5);
        var delay = TimeSpan.FromSeconds(Math.Pow(2, attempt - 1));
        return delay > TimeSpan.FromSeconds(30) ? TimeSpan.FromSeconds(30) : delay;
    }
}
