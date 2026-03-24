using System.Runtime.CompilerServices;
using System.Threading.Channels;
using Meridian.Application.Pipeline;
using Meridian.Execution.Sdk;
using Microsoft.Extensions.Logging;

namespace Meridian.Execution.Adapters;

/// <summary>
/// Base class for live brokerage gateways. Provides shared infrastructure:
/// connection lifecycle, reconnection with exponential backoff, execution report
/// streaming via bounded channels, and rate limit scaffolding.
/// Concrete implementations override broker-specific methods.
/// </summary>
public abstract class BaseBrokerageGateway : IBrokerageGateway
{
    private readonly Channel<ExecutionReport> _reportChannel;
    private volatile bool _disposed;
    private volatile bool _connected;
    private CancellationTokenSource? _reconnectCts;

    protected readonly ILogger Logger;

    /// <summary>Maximum reconnection attempts before giving up.</summary>
    protected virtual int MaxReconnectAttempts => 5;

    /// <summary>Base delay for exponential backoff on reconnection.</summary>
    protected virtual TimeSpan ReconnectBaseDelay => TimeSpan.FromSeconds(2);

    protected BaseBrokerageGateway(ILogger logger)
    {
        Logger = logger ?? throw new ArgumentNullException(nameof(logger));

        // ADR-013: Use bounded channels for execution event pipeline
        var policy = new EventPipelinePolicy(
            Capacity: 500,
            FullMode: BoundedChannelFullMode.Wait,
            EnableMetrics: false);
        _reportChannel = policy.CreateChannel<ExecutionReport>(
            singleReader: false, singleWriter: false);
    }

    // ── IExecutionGateway ──────────────────────────────────────────────

    /// <inheritdoc />
    public abstract string GatewayId { get; }

    /// <inheritdoc />
    public bool IsConnected => _connected;

    /// <inheritdoc />
    public async Task ConnectAsync(CancellationToken ct = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (_connected)
        {
            Logger.LogWarning("{Gateway} already connected", GatewayId);
            return;
        }

        Logger.LogInformation("{Gateway} connecting...", GatewayId);

        await ConnectCoreAsync(ct).ConfigureAwait(false);
        _connected = true;
        _reconnectCts = new CancellationTokenSource();

        Logger.LogInformation("{Gateway} connected successfully", GatewayId);
    }

    /// <inheritdoc />
    public async Task DisconnectAsync(CancellationToken ct = default)
    {
        if (!_connected) return;

        Logger.LogInformation("{Gateway} disconnecting...", GatewayId);

        _reconnectCts?.Cancel();
        _reconnectCts?.Dispose();
        _reconnectCts = null;

        await DisconnectCoreAsync(ct).ConfigureAwait(false);
        _connected = false;

        Logger.LogInformation("{Gateway} disconnected", GatewayId);
    }

    /// <inheritdoc />
    public async Task<ExecutionReport> SubmitOrderAsync(OrderRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ObjectDisposedException.ThrowIf(_disposed, this);
        EnsureConnected();

        Logger.LogInformation(
            "{Gateway} submitting order: {Side} {Quantity} {Symbol} @ {Type}",
            GatewayId, request.Side, request.Quantity, request.Symbol, request.Type);

        var report = await SubmitOrderCoreAsync(request, ct).ConfigureAwait(false);
        await PublishReportAsync(report, ct).ConfigureAwait(false);
        return report;
    }

    /// <inheritdoc />
    public async Task<ExecutionReport> CancelOrderAsync(string orderId, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(orderId);
        ObjectDisposedException.ThrowIf(_disposed, this);
        EnsureConnected();

        Logger.LogInformation("{Gateway} cancelling order {OrderId}", GatewayId, orderId);

        var report = await CancelOrderCoreAsync(orderId, ct).ConfigureAwait(false);
        await PublishReportAsync(report, ct).ConfigureAwait(false);
        return report;
    }

    /// <inheritdoc />
    public async Task<ExecutionReport> ModifyOrderAsync(
        string orderId, OrderModification modification, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(orderId);
        ArgumentNullException.ThrowIfNull(modification);
        ObjectDisposedException.ThrowIf(_disposed, this);
        EnsureConnected();

        Logger.LogInformation("{Gateway} modifying order {OrderId}", GatewayId, orderId);

        var report = await ModifyOrderCoreAsync(orderId, modification, ct).ConfigureAwait(false);
        await PublishReportAsync(report, ct).ConfigureAwait(false);
        return report;
    }

    /// <inheritdoc />
    public async IAsyncEnumerable<ExecutionReport> StreamExecutionReportsAsync(
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        await foreach (var report in _reportChannel.Reader.ReadAllAsync(ct).ConfigureAwait(false))
        {
            yield return report;
        }
    }

    // ── IBrokerageGateway ──────────────────────────────────────────────

    /// <inheritdoc />
    public abstract string BrokerDisplayName { get; }

    /// <inheritdoc />
    public abstract BrokerageCapabilities BrokerageCapabilities { get; }

    /// <inheritdoc />
    public abstract Task<AccountInfo> GetAccountInfoAsync(CancellationToken ct = default);

    /// <inheritdoc />
    public abstract Task<IReadOnlyList<BrokerPosition>> GetPositionsAsync(CancellationToken ct = default);

    /// <inheritdoc />
    public abstract Task<IReadOnlyList<BrokerOrder>> GetOpenOrdersAsync(CancellationToken ct = default);

    /// <inheritdoc />
    public virtual async Task<BrokerHealthStatus> CheckHealthAsync(CancellationToken ct = default)
    {
        try
        {
            if (!_connected)
                return BrokerHealthStatus.Unhealthy("Not connected");

            var account = await GetAccountInfoAsync(ct).ConfigureAwait(false);
            return account.Status == "active"
                ? BrokerHealthStatus.Healthy($"Account {account.AccountId} active")
                : BrokerHealthStatus.Unhealthy($"Account status: {account.Status}");
        }
        catch (Exception ex)
        {
            return BrokerHealthStatus.Unhealthy(ex.Message);
        }
    }

    // ── Template methods for concrete implementations ──────────────────

    /// <summary>Establishes the broker-specific connection (API auth, WebSocket, etc.).</summary>
    protected abstract Task ConnectCoreAsync(CancellationToken ct);

    /// <summary>Tears down the broker-specific connection.</summary>
    protected abstract Task DisconnectCoreAsync(CancellationToken ct);

    /// <summary>Submits an order via the broker API. Returns the initial execution report.</summary>
    protected abstract Task<ExecutionReport> SubmitOrderCoreAsync(OrderRequest request, CancellationToken ct);

    /// <summary>Cancels an order via the broker API.</summary>
    protected abstract Task<ExecutionReport> CancelOrderCoreAsync(string orderId, CancellationToken ct);

    /// <summary>Modifies an order via the broker API. Override if the broker supports modification.</summary>
    protected virtual Task<ExecutionReport> ModifyOrderCoreAsync(
        string orderId, OrderModification modification, CancellationToken ct)
    {
        throw new NotSupportedException($"{GatewayId} does not support order modification.");
    }

    // ── Shared infrastructure ──────────────────────────────────────────

    /// <summary>
    /// Publishes an execution report to the bounded channel for downstream consumers.
    /// Subclasses should call this for asynchronous fill/status updates (e.g., WebSocket callbacks).
    /// </summary>
    protected async Task PublishReportAsync(ExecutionReport report, CancellationToken ct = default)
    {
        await _reportChannel.Writer.WriteAsync(report, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Attempts reconnection with exponential backoff. Subclasses can call this
    /// when a connection drop is detected.
    /// </summary>
    protected async Task AttemptReconnectAsync()
    {
        var ct = _reconnectCts?.Token ?? CancellationToken.None;

        for (int attempt = 1; attempt <= MaxReconnectAttempts; attempt++)
        {
            if (ct.IsCancellationRequested) return;

            var delay = TimeSpan.FromSeconds(ReconnectBaseDelay.TotalSeconds * Math.Pow(2, attempt - 1));
            Logger.LogWarning(
                "{Gateway} reconnect attempt {Attempt}/{Max} in {Delay}s",
                GatewayId, attempt, MaxReconnectAttempts, delay.TotalSeconds);

            try
            {
                await Task.Delay(delay, ct).ConfigureAwait(false);
                await ConnectCoreAsync(ct).ConfigureAwait(false);
                _connected = true;
                Logger.LogInformation("{Gateway} reconnected on attempt {Attempt}", GatewayId, attempt);
                return;
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "{Gateway} reconnect attempt {Attempt} failed", GatewayId, attempt);
            }
        }

        _connected = false;
        Logger.LogError("{Gateway} failed to reconnect after {Max} attempts", GatewayId, MaxReconnectAttempts);
    }

    /// <summary>Throws if not connected.</summary>
    protected void EnsureConnected()
    {
        if (!_connected)
            throw new InvalidOperationException($"{GatewayId} is not connected. Call ConnectAsync first.");
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;

        _reconnectCts?.Cancel();
        _reconnectCts?.Dispose();
        _reportChannel.Writer.TryComplete();

        if (_connected)
        {
            try
            {
                await DisconnectCoreAsync(CancellationToken.None).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                Logger.LogWarning(ex, "{Gateway} error during dispose disconnect", GatewayId);
            }
            _connected = false;
        }

        GC.SuppressFinalize(this);
    }
}
