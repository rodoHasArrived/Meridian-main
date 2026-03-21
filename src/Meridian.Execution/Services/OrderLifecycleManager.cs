using Meridian.Execution.Models;

namespace Meridian.Execution.Services;

/// <summary>
/// Tracks in-flight orders and their state transitions for a single execution session.
/// Listens to the <see cref="IOrderGateway.StreamOrderUpdatesAsync"/> stream and
/// maintains a queryable view of all active and terminal orders.
/// </summary>
public sealed class OrderLifecycleManager : IAsyncDisposable
{
    private readonly IOrderGateway _gateway;
    private readonly ILogger<OrderLifecycleManager> _logger;
    private readonly Dictionary<string, OrderStatusUpdate> _orders = new();
    private readonly Lock _lock = new();
    private Task? _streamTask;
    private CancellationTokenSource? _cts;

    /// <summary>Creates a new order lifecycle manager bound to <paramref name="gateway"/>.</summary>
    public OrderLifecycleManager(IOrderGateway gateway, ILogger<OrderLifecycleManager> logger)
    {
        _gateway = gateway;
        _logger = logger;
    }

    /// <summary>
    /// Starts consuming the order update stream from the gateway.
    /// This method is idempotent — calling it a second time is a no-op.
    /// </summary>
    public void Start(CancellationToken ct = default)
    {
        if (_streamTask is not null)
        {
            return;
        }

        _cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        _streamTask = ConsumeUpdatesAsync(_cts.Token);
    }

    /// <summary>
    /// Returns the most recent status update for <paramref name="orderId"/>,
    /// or <c>null</c> if the order is not known.
    /// </summary>
    public OrderStatusUpdate? GetStatus(string orderId)
    {
        lock (_lock)
        {
            return _orders.GetValueOrDefault(orderId);
        }
    }

    /// <summary>Returns all tracked orders as a snapshot.</summary>
    public IReadOnlyList<OrderStatusUpdate> GetAllOrders()
    {
        lock (_lock)
        {
            return [.. _orders.Values];
        }
    }

    private async Task ConsumeUpdatesAsync(CancellationToken ct)
    {
        try
        {
            await foreach (var update in _gateway.StreamOrderUpdatesAsync(ct).ConfigureAwait(false))
            {
                lock (_lock)
                {
                    _orders[update.OrderId] = update;
                }

                _logger.LogDebug(
                    "Order {OrderId} → {Status} (filled: {Filled}, avgPx: {AvgPx})",
                    update.OrderId, update.Status, update.FilledQuantity, update.AverageFillPrice);
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Order lifecycle stream terminated unexpectedly");
        }
    }

    /// <inheritdoc/>
    public async ValueTask DisposeAsync()
    {
        if (_cts is not null)
        {
            await _cts.CancelAsync().ConfigureAwait(false);
        }

        if (_streamTask is not null)
        {
            await _streamTask.ConfigureAwait(false);
        }

        _cts?.Dispose();
    }
}
