using System.Collections.Concurrent;
using System.Threading.Channels;
using Meridian.Application.Pipeline;
using Meridian.Execution.Sdk;
using Meridian.Execution.Services;
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
    private readonly ISecurityMasterGate? _securityMasterGate;
    private readonly ExecutionOperatorControlService? _operatorControls;
    private readonly ExecutionAuditTrailService? _auditTrail;
    private readonly Meridian.Execution.Models.IPortfolioState? _portfolioState;
    private readonly ILogger<OrderManagementSystem> _logger;
    private readonly Channel<ExecutionReport> _executionChannel;
    private int _orderSequence;

    public OrderManagementSystem(
        IExecutionGateway gateway,
        ILogger<OrderManagementSystem> logger,
        IRiskValidator? riskValidator = null,
        ISecurityMasterGate? securityMasterGate = null,
        ExecutionOperatorControlService? operatorControls = null,
        ExecutionAuditTrailService? auditTrail = null,
        Meridian.Execution.Models.IPortfolioState? portfolioState = null)
    {
        _gateway = gateway ?? throw new ArgumentNullException(nameof(gateway));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _riskValidator = riskValidator;
        _securityMasterGate = securityMasterGate;
        _operatorControls = operatorControls;
        _auditTrail = auditTrail;
        _portfolioState = portfolioState;
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

        // Extract metadata fields for audit correlation
        string? actor = null;
        string? correlationId = null;
        string? runId = null;
        request.Metadata?.TryGetValue("actor", out actor);
        request.Metadata?.TryGetValue("correlationId", out correlationId);
        request.Metadata?.TryGetValue("runId", out runId);

        // Operator controls gate — rejects orders when circuit breaker is open (unless bypassed)
        if (_operatorControls is not null)
        {
            var controlDecision = _operatorControls.EvaluateOrder(request, _portfolioState);
            if (!controlDecision.IsApproved)
            {
                _logger.LogWarning("Order {OrderId} for {Symbol} rejected by operator controls: {Reason}",
                    orderId, request.Symbol, controlDecision.RejectReason);

                if (_auditTrail is not null)
                {
                    await _auditTrail.RecordAsync(new ExecutionAuditEntry(
                        AuditId: Guid.NewGuid().ToString("N"),
                        Category: "Order",
                        Action: "OrderRejected",
                        Outcome: "Rejected",
                        OccurredAt: DateTimeOffset.UtcNow,
                        Actor: actor,
                        OrderId: orderId,
                        RunId: runId,
                        Symbol: request.Symbol,
                        CorrelationId: correlationId,
                        Message: controlDecision.RejectReason), ct).ConfigureAwait(false);
                }

                return new OrderResult
                {
                    Success = false,
                    OrderId = orderId,
                    ErrorMessage = controlDecision.RejectReason
                };
            }
        }

        // Security Master gate — reject orders for symbols not in the master (when gate is wired)
        if (_securityMasterGate is not null)
        {
            var gateResult = await _securityMasterGate.CheckAsync(request.Symbol, ct).ConfigureAwait(false);
            if (!gateResult.IsApproved)
            {
                _logger.LogWarning("Order {OrderId} for {Symbol} rejected by Security Master gate: {Reason}",
                    orderId, request.Symbol, gateResult.Reason);

                return new OrderResult
                {
                    Success = false,
                    OrderId = orderId,
                    ErrorMessage = gateResult.Reason
                };
            }
        }

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

            // Record submitted order in the audit trail when connected
            if (_auditTrail is not null)
            {
                await _auditTrail.RecordAsync(new ExecutionAuditEntry(
                    AuditId: Guid.NewGuid().ToString("N"),
                    Category: "Order",
                    Action: "OrderSubmitted",
                    Outcome: updatedState.Status.ToString(),
                    OccurredAt: DateTimeOffset.UtcNow,
                    Actor: actor,
                    OrderId: orderId,
                    RunId: runId,
                    Symbol: request.Symbol,
                    CorrelationId: correlationId), ct).ConfigureAwait(false);
            }

            // Publish fills to the execution channel so portfolio trackers and other
            // consumers can subscribe without coupling directly to the gateway.
            if (report.OrderStatus is OrderStatus.Filled or OrderStatus.PartiallyFilled)
            {
                _executionChannel.Writer.TryWrite(report);
            }

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
    public IReadOnlyList<OrderState> GetCompletedOrders(int take = 20)
    {
        return _orders.Values
            .Where(static o => o.Status is
                OrderStatus.Filled or
                OrderStatus.PartiallyFilled or
                OrderStatus.Cancelled or
                OrderStatus.Rejected or
                OrderStatus.Expired)
            .OrderByDescending(static o => o.LastUpdatedAt ?? o.CreatedAt)
            .Take(take)
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

    /// <summary>
    /// Provides a read-only view of fill and partial-fill execution reports for consumption
    /// by portfolio trackers and audit subscribers.  Reports are published as each order
    /// transitions to <see cref="OrderStatus.Filled"/> or <see cref="OrderStatus.PartiallyFilled"/>.
    /// Consumers must drain this reader promptly to avoid backpressure.
    /// </summary>
    public ChannelReader<ExecutionReport> ExecutionReports => _executionChannel.Reader;

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
