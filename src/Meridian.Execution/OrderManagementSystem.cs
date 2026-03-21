using System.Collections.Concurrent;
using System.Threading.Channels;
using Meridian.Application.Pipeline;
using Meridian.Execution.Sdk;
using Microsoft.Extensions.Logging;

namespace Meridian.Execution;

/// <summary>
/// Central Order Management System (OMS). Coordinates order lifecycle between
/// strategies, risk checks, and execution gateways. Uses bounded channels
/// for backpressure-aware execution event processing.
/// </summary>
[ImplementsAdr("ADR-013", "Uses bounded channels for execution event pipeline")]
public sealed class OrderManagementSystem : IOrderManager, IDisposable
{
    private readonly ConcurrentDictionary<string, OrderState> _orders = new();
    private readonly IExecutionGateway _gateway;
    private readonly IRiskValidator? _riskValidator;
    private readonly ILogger<OrderManagementSystem> _logger;
    private readonly Channel<ExecutionReport> _executionChannel;
    private int _orderSequence;

    public OrderManagementSystem(
        IExecutionGateway gateway,
        ILogger<OrderManagementSystem> logger,
        IRiskValidator? riskValidator = null)
    {
        _gateway = gateway ?? throw new ArgumentNullException(nameof(gateway));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _riskValidator = riskValidator;
        // Use custom EventPipelinePolicy for execution reports: high capacity with backpressure
        var executionPolicy = new EventPipelinePolicy(
            Capacity: 1000,
            FullMode: BoundedChannelFullMode.Wait,
            EnableMetrics: false);
        _executionChannel = executionPolicy.CreateChannel<ExecutionReport>(
            singleReader: true,
            singleWriter: false);
    }

    /// <inheritdoc />
    public async Task<OrderResult> PlaceOrderAsync(OrderRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var orderId = request.ClientOrderId ?? GenerateOrderId();

        // Pre-trade risk check
        if (_riskValidator is not null)
        {
            var riskResult = await _riskValidator.ValidateOrderAsync(request, ct).ConfigureAwait(false);
            if (!riskResult.IsApproved)
            {
                _logger.LogWarning("Order {OrderId} for {Symbol} rejected by risk: {Reason}",
                    orderId, request.Symbol, riskResult.RejectReason);

                return new OrderResult
                {
                    Success = false,
                    OrderId = orderId,
                    ErrorMessage = riskResult.RejectReason
                };
            }
        }

        var orderState = new OrderState
        {
            OrderId = orderId,
            Symbol = request.Symbol,
            Side = request.Side,
            Type = request.Type,
            Quantity = request.Quantity,
            LimitPrice = request.LimitPrice,
            StopPrice = request.StopPrice,
            Status = OrderStatus.PendingNew,
            CreatedAt = DateTimeOffset.UtcNow,
            StrategyId = request.StrategyId
        };

        _orders[orderId] = orderState;

        try
        {
            var report = await _gateway.SubmitOrderAsync(request with { ClientOrderId = orderId }, ct)
                .ConfigureAwait(false);

            var updatedState = ApplyReport(orderState, report);
            _orders[orderId] = updatedState;

            _logger.LogInformation("Order {OrderId} submitted for {Symbol} {Side} {Quantity} — status {Status}",
                orderId, request.Symbol, request.Side, request.Quantity, updatedState.Status);

            return new OrderResult
            {
                Success = report.OrderStatus is not OrderStatus.Rejected,
                OrderId = orderId,
                OrderState = updatedState,
                ErrorMessage = report.RejectReason
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to submit order {OrderId} for {Symbol}", orderId, request.Symbol);

            _orders[orderId] = orderState with { Status = OrderStatus.Rejected };

            return new OrderResult
            {
                Success = false,
                OrderId = orderId,
                ErrorMessage = ex.Message
            };
        }
    }

    /// <inheritdoc />
    public async Task<OrderResult> CancelOrderAsync(string orderId, CancellationToken ct = default)
    {
        if (!_orders.TryGetValue(orderId, out var state))
        {
            return new OrderResult { Success = false, OrderId = orderId, ErrorMessage = "Order not found" };
        }

        var report = await _gateway.CancelOrderAsync(orderId, ct).ConfigureAwait(false);
        var updated = ApplyReport(state, report);
        _orders[orderId] = updated;

        return new OrderResult { Success = true, OrderId = orderId, OrderState = updated };
    }

    /// <inheritdoc />
    public async Task<OrderResult> ModifyOrderAsync(string orderId, OrderModification modification, CancellationToken ct = default)
    {
        if (!_orders.TryGetValue(orderId, out var state))
        {
            return new OrderResult { Success = false, OrderId = orderId, ErrorMessage = "Order not found" };
        }

        var report = await _gateway.ModifyOrderAsync(orderId, modification, ct).ConfigureAwait(false);
        var updated = ApplyReport(state, report);
        _orders[orderId] = updated;

        return new OrderResult { Success = true, OrderId = orderId, OrderState = updated };
    }

    /// <inheritdoc />
    public IReadOnlyList<OrderState> GetOpenOrders()
    {
        return _orders.Values
            .Where(o => o.Status is OrderStatus.PendingNew or OrderStatus.Accepted or OrderStatus.PartiallyFilled)
            .ToList()
            .AsReadOnly();
    }

    /// <inheritdoc />
    public OrderState? GetOrder(string orderId)
    {
        return _orders.TryGetValue(orderId, out var state) ? state : null;
    }

    /// <inheritdoc />
    public async Task CancelAllAsync(CancellationToken ct = default)
    {
        var openOrders = GetOpenOrders();
        _logger.LogInformation("Cancelling all {Count} open orders", openOrders.Count);

        await Parallel.ForEachAsync(openOrders, ct, async (order, token) =>
        {
            await CancelOrderAsync(order.OrderId, token).ConfigureAwait(false);
        }).ConfigureAwait(false);
    }

    public void Dispose()
    {
        _executionChannel.Writer.TryComplete();
    }

    private string GenerateOrderId()
    {
        var seq = Interlocked.Increment(ref _orderSequence);
        return $"MDN-{DateTimeOffset.UtcNow:yyyyMMdd}-{seq:D6}";
    }

    private static OrderState ApplyReport(OrderState current, ExecutionReport report)
    {
        return current with
        {
            Status = report.OrderStatus,
            FilledQuantity = report.FilledQuantity > 0 ? report.FilledQuantity : current.FilledQuantity,
            AverageFillPrice = report.FillPrice ?? current.AverageFillPrice,
            LastUpdatedAt = report.Timestamp
        };
    }
}

/// <summary>Placeholder attribute for ADR traceability.</summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = true)]
internal sealed class ImplementsAdrAttribute(string adr, string reason) : Attribute
{
    public string Adr { get; } = adr;
    public string Reason { get; } = reason;
}
