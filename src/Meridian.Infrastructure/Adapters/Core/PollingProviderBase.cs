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
    private readonly ProviderConnectionSupervisor _connectionSupervisor;

    private CancellationTokenSource? _pollCts;
    private Task? _pollTask;
    private volatile bool _disposed;
    private volatile ProviderConnectionLifecycleState _lifecycleState = ProviderConnectionLifecycleState.Configured;
    private DateTimeOffset? _connectedAt;
    private DateTimeOffset? _disconnectedAt;
    private DateTimeOffset? _lastPollAttemptAt;
    private DateTimeOffset? _lastSuccessfulApiCallAt;
    private DateTimeOffset? _lastMessageReceivedAt;
    private string? _lastError;
    private Exception? _lastPollFailure;
    private int _consecutivePollFailures;

    /// <summary>Logger for the derived provider.</summary>
    protected readonly ILogger Log;

    /// <summary>Interval between poll cycles.</summary>
    protected TimeSpan PollInterval { get; }

    protected PollingProviderBase(
        string providerName,
        ILogger logger,
        TimeSpan pollInterval,
        int maxReconnectAttempts = 5,
        TimeSpan? retryBaseDelay = null,
        TimeSpan? maxRetryDelay = null)
    {
        _providerName = providerName;
        Log = logger ?? throw new ArgumentNullException(nameof(logger));
        PollInterval = pollInterval;
        _connectionSupervisor = new ProviderConnectionSupervisor(
            providerName,
            maxReconnectAttempts,
            retryBaseDelay ?? TimeSpan.FromSeconds(1),
            maxRetryDelay ?? TimeSpan.FromSeconds(30));
        _connectionSupervisor.StateChanged += OnConnectionSupervisorStateChanged;
    }

    /// <summary>Raised whenever the connection diagnostics change (e.g. a lifecycle transition).</summary>
    public event Action<WebSocketConnectionDiagnostics>? ConnectionDiagnosticsChanged;

    /// <summary>Whether the provider is configured/enabled and may be connected.</summary>
    public abstract bool IsEnabled { get; }

    // ── Protected state accessors (for provider-specific diagnostics snapshots) ──

    protected bool Disposed => _disposed;
    protected bool Connected => _connectionSupervisor.IsConnected;
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
            _lifecycleState = ProviderConnectionLifecycleState.NotConfigured;
            _lastError = NotEnabledError;
            PublishDiagnosticsChanged();
            throw CreateNotEnabledException();
        }

        await _connectionSupervisor.ConnectAsync(StartPollingTransactionAsync, ct).ConfigureAwait(false);
        Log.LogInformation("{Provider} connected (polling interval {Interval})", _providerName, PollInterval);
    }

    /// <summary>Stops the polling loop and marks the provider disconnected.</summary>
    public async Task DisconnectAsync(CancellationToken ct = default)
    {
        await _connectionSupervisor.DisconnectAsync(StopPollingTransactionAsync, ct).ConfigureAwait(false);
        Log.LogInformation("{Provider} disconnected", _providerName);
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
            return;

        await DisconnectAsync().ConfigureAwait(false);
        _connectionSupervisor.StateChanged -= OnConnectionSupervisorStateChanged;
        await _connectionSupervisor.DisposeAsync().ConfigureAwait(false);
        _disposed = true;
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
        var supervisor = _connectionSupervisor.GetSnapshot();
        var now = DateTimeOffset.UtcNow;
        var lifecycleState = _lifecycleState is ProviderConnectionLifecycleState.NotConfigured
            || (_lifecycleState == ProviderConnectionLifecycleState.Failed
                && supervisor.LifecycleState == ProviderConnectionLifecycleState.Configured)
                ? _lifecycleState
                : supervisor.LifecycleState;
        return new WebSocketConnectionDiagnostics(
            ProviderName: _providerName,
            LifecycleState: lifecycleState,
            WebSocketState: System.Net.WebSockets.WebSocketState.None,
            IsConnected: supervisor.IsConnected,
            IsReconnecting: supervisor.IsReconnecting,
            ReconnectAttempts: supervisor.ReconnectAttempts,
            LastConnectedAt: supervisor.LastConnectedAt ?? _connectedAt,
            LastDisconnectedAt: supervisor.LastDisconnectedAt ?? _disconnectedAt,
            LastHeartbeatReceivedAt: _lastSuccessfulApiCallAt,
            LastMessageReceivedAt: _lastMessageReceivedAt,
            LastReconnectAttemptAt: supervisor.LastReconnectAttemptAt ?? _lastPollAttemptAt,
            LastError: _lastError ?? supervisor.LastError,
            LastFailureKind: supervisor.LastFailureKind,
            ConnectionAge: supervisor.ConnectionAge,
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
    protected void RecordError(string message)
    {
        _lastError = message;
        _lastPollFailure = null;
    }

    protected void RecordTerminalPollFailure(Exception error)
    {
        ArgumentNullException.ThrowIfNull(error);
        _lastError = error.Message;
        _lastPollFailure = error;

        if (_connectionSupervisor.LifecycleState == ProviderConnectionLifecycleState.Configured)
        {
            _lifecycleState = ProviderConnectionLifecycleState.Failed;
            PublishDiagnosticsChanged();
        }
    }

    protected void ClearError()
    {
        _lastError = null;
        _lastPollFailure = null;
    }
    protected void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(_disposed, this);

    private void OnConnectionSupervisorStateChanged(ProviderConnectionSupervisorSnapshot snapshot)
    {
        _lifecycleState = snapshot.LifecycleState;
        _connectedAt = snapshot.LastConnectedAt;
        _disconnectedAt = snapshot.LastDisconnectedAt;
        _consecutivePollFailures = snapshot.IsReconnecting ? snapshot.ReconnectAttempts : 0;
        if (snapshot.LifecycleState == ProviderConnectionLifecycleState.Connected)
            _lastError = null;
        else if (!string.IsNullOrWhiteSpace(snapshot.LastError))
            _lastError = snapshot.LastError;

        Log.LogInformation(
            "{Provider} lifecycle state changed to {LifecycleState}",
            _providerName,
            snapshot.LifecycleState);
        PublishDiagnosticsChanged();
    }

    private void PublishDiagnosticsChanged()
    {
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

    private Task StartPollingTransactionAsync(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        if (_pollTask is { IsCompleted: false })
            return Task.CompletedTask;

        _lastError = null;
        _lastPollFailure = null;
        _consecutivePollFailures = 0;
        _pollCts?.Dispose();
        _pollCts = CancellationTokenSource.CreateLinkedTokenSource(
            _connectionSupervisor.ConnectionLifetimeToken);
        _pollTask = RunPollLoopAsync(_pollCts.Token);
        return Task.CompletedTask;
    }

    private async Task StopPollingTransactionAsync(CancellationToken ct)
    {
        var pollCts = _pollCts;
        var pollTask = _pollTask;
        _pollCts = null;
        _pollTask = null;

        if (pollCts is null)
            return;

        await pollCts.CancelAsync().ConfigureAwait(false);
        if (pollTask is not null && pollTask.Id != Task.CurrentId)
        {
            try
            {
                await pollTask.WaitAsync(ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (pollCts.IsCancellationRequested)
            {
            }
        }

        pollCts.Dispose();
    }

    private async Task RunPollLoopAsync(CancellationToken ct)
    {
        Log.LogInformation("{Provider} poll loop started", _providerName);
        try
        {
            while (!ct.IsCancellationRequested)
            {
                await Task.Delay(PollInterval, ct).ConfigureAwait(false);

                Exception? pollFailure = null;
                try
                {
                    if (await PollOnceAsync(ct).ConfigureAwait(false))
                        continue;
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    pollFailure = ex;
                    _lastError = ex.Message;
                }

                _consecutivePollFailures++;
                var failure = pollFailure ?? _lastPollFailure ?? new HttpRequestException(
                    _lastError ?? $"{_providerName} polling cycle failed.");

                if (!_connectionSupervisor.MarkConnectionLost(failure))
                    break;

                Log.LogWarning(
                    "{Provider} polling degraded; starting one bounded supervised recovery transaction",
                    _providerName);

                var recovered = await _connectionSupervisor.ReconnectAsync(
                    async token =>
                    {
                        if (!await PollOnceAsync(token).ConfigureAwait(false))
                        {
                            throw _lastPollFailure ?? new HttpRequestException(
                                _lastError ?? $"{_providerName} polling recovery probe failed.");
                        }
                    },
                    ct).ConfigureAwait(false);

                if (!recovered)
                    break;
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            _connectionSupervisor.RecordFailure(ex);
            Log.LogError(ex, "{Provider} poll loop failed unexpectedly", _providerName);
        }
        finally
        {
            Log.LogInformation("{Provider} poll loop stopped", _providerName);
        }
    }

}
