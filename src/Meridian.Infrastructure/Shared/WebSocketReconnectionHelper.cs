using Meridian.Application.Logging;
using Meridian.Application.Monitoring;
using Meridian.Infrastructure.Contracts;
using Serilog;

namespace Meridian.Infrastructure.Shared;

/// <summary>
/// Standardized reconnection helper for WebSocket-based providers.
/// Eliminates duplicated reconnection logic across Polygon (~40 LOC),
/// NYSE (~30 LOC), and StockSharp (~35 LOC) providers by providing
/// a single gated exponential-backoff-with-jitter reconnection algorithm.
/// </summary>
/// <remarks>
/// <para>
/// This helper consolidates the divergent reconnection algorithms used by:
/// - <c>PolygonMarketDataClient</c>: Manual exponential + jitter
/// - <c>NYSEDataSource</c>: Linear multiply backoff
/// - <c>StockSharpMarketDataClient</c>: Exponential + jitter with connector recreation
/// </para>
/// </remarks>
[ImplementsAdr("ADR-004", "All async methods support CancellationToken")]
public sealed class WebSocketReconnectionHelper
{
    private readonly SemaphoreSlim _reconnectGate = new(1, 1);
    private readonly ILogger _log;
    private readonly IReconnectionMetrics _metrics;
    private readonly string _providerName;
    private readonly int _maxAttempts;
    private readonly TimeSpan _baseDelay;
    private readonly TimeSpan _maxDelay;
    private volatile bool _isReconnecting;

    public WebSocketReconnectionHelper(
        string providerName,
        int maxAttempts = 10,
        TimeSpan? baseDelay = null,
        TimeSpan? maxDelay = null,
        ILogger? log = null,
        IReconnectionMetrics? metrics = null)
    {
        _providerName = providerName;
        _maxAttempts = maxAttempts;
        _baseDelay = baseDelay ?? TimeSpan.FromSeconds(2);
        _maxDelay = maxDelay ?? TimeSpan.FromSeconds(60);
        _log = log ?? LoggingSetup.ForContext<WebSocketReconnectionHelper>();
        _metrics = metrics ?? NullReconnectionMetrics.Instance;
    }

    /// <summary>
    /// Gets whether a reconnection attempt is currently in progress.
    /// </summary>
    public bool IsReconnecting => _isReconnecting;

    /// <summary>
    /// Event raised after a successful reconnection, providing the disconnect
    /// and reconnect timestamps. Subscribers can use this to enqueue targeted
    /// backfill requests covering the disconnection gap window.
    /// </summary>
    public event Action<ReconnectionEvent>? Reconnected;

    /// <summary>
    /// Attempts reconnection with exponential backoff and jitter.
    /// Guarantees only one reconnection attempt runs at a time via semaphore gating.
    /// On successful reconnection, raises the <see cref="Reconnected"/> event
    /// with the disconnect/reconnect timestamps for gap backfill.
    /// </summary>
    /// <param name="reconnectAction">The async action that performs the actual reconnection
    /// (connect WebSocket, authenticate, resubscribe).</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>True if reconnection succeeded; false if max attempts exhausted.</returns>
    public async Task<bool> TryReconnectAsync(
        Func<CancellationToken, Task> reconnectAction,
        CancellationToken ct = default)
    {
        if (!await _reconnectGate.WaitAsync(TimeSpan.Zero, ct).ConfigureAwait(false))
        {
            _log.Debug("{Provider} reconnection already in progress, skipping", _providerName);
            return false;
        }

        _isReconnecting = true;
        var disconnectedAt = DateTimeOffset.UtcNow;
        try
        {
            for (int attempt = 1; attempt <= _maxAttempts; attempt++)
            {
                ct.ThrowIfCancellationRequested();

                var delay = CalculateDelay(attempt);
                var nextDelay = attempt < _maxAttempts ? CalculateDelay(attempt + 1) : TimeSpan.Zero;
                _log.Information(
                    "{Provider} reconnection attempt {Attempt}/{MaxAttempts}, waiting {DelayMs}ms" +
                    (attempt < _maxAttempts ? " (next retry in {NextDelayMs}ms if this fails)" : ""),
                    _providerName, attempt, _maxAttempts,
                    (int)delay.TotalMilliseconds, (int)nextDelay.TotalMilliseconds);

                await Task.Delay(delay, ct).ConfigureAwait(false);

                try
                {
                    await reconnectAction(ct).ConfigureAwait(false);
                    _metrics.RecordAttempt(_providerName, success: true);

                    var reconnectedAt = DateTimeOffset.UtcNow;
                    var gapDuration = reconnectedAt - disconnectedAt;
                    _log.Information(
                        "{Provider} reconnected successfully on attempt {Attempt}/{MaxAttempts}. " +
                        "Gap window: {DisconnectedAt} to {ReconnectedAt} ({GapSeconds:F1}s)",
                        _providerName, attempt, _maxAttempts,
                        disconnectedAt, reconnectedAt, gapDuration.TotalSeconds);

                    // Raise reconnection event for gap backfill
                    RaiseReconnected(new ReconnectionEvent(
                        _providerName, disconnectedAt, reconnectedAt, attempt));

                    return true;
                }
                catch (OperationCanceledException) { throw; }
                catch (Exception ex)
                {
                    _metrics.RecordAttempt(_providerName, success: false);
                    _log.Warning(ex,
                        "{Provider} reconnection attempt {Attempt}/{MaxAttempts} failed: {ErrorMessage}",
                        _providerName, attempt, _maxAttempts, ex.Message);
                }
            }

            _log.Error(
                "{Provider} reconnection exhausted all {MaxAttempts} attempts — giving up",
                _providerName, _maxAttempts);
            return false;
        }
        finally
        {
            _isReconnecting = false;
            _reconnectGate.Release();
        }
    }

    private void RaiseReconnected(ReconnectionEvent evt)
    {
        try
        {
            Reconnected?.Invoke(evt);
        }
        catch (Exception ex)
        {
            _log.Warning(ex, "{Provider} error in Reconnected event handler", _providerName);
        }
    }

    /// <summary>
    /// Calculates delay with exponential backoff and ±20% jitter.
    /// </summary>
    private TimeSpan CalculateDelay(int attempt)
    {
        var exponentialDelay = _baseDelay * Math.Pow(2, attempt - 1);
        var cappedDelay = TimeSpan.FromMilliseconds(
            Math.Min(exponentialDelay.TotalMilliseconds, _maxDelay.TotalMilliseconds));

        // Add ±20% jitter to prevent thundering herd
        var jitterFactor = 0.8 + (Random.Shared.NextDouble() * 0.4);
        return TimeSpan.FromMilliseconds(cappedDelay.TotalMilliseconds * jitterFactor);
    }
}

/// <summary>
/// Event data for a successful reconnection, providing the gap window
/// that can be used to trigger targeted backfill for missing data.
/// </summary>
/// <param name="ProviderName">The provider that reconnected.</param>
/// <param name="DisconnectedAt">Approximate time the connection was lost.</param>
/// <param name="ReconnectedAt">Time the connection was restored.</param>
/// <param name="AttemptsUsed">Number of reconnection attempts before success.</param>
public sealed record ReconnectionEvent(
    string ProviderName,
    DateTimeOffset DisconnectedAt,
    DateTimeOffset ReconnectedAt,
    int AttemptsUsed)
{
    /// <summary>
    /// Duration of the gap window.
    /// </summary>
    public TimeSpan GapDuration => ReconnectedAt - DisconnectedAt;
}
