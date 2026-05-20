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
    private readonly PaperSessionPersistenceService? _sessionPersistence;
    private readonly BrokerageConfiguration? _brokerageConfiguration;
    private readonly ILogger<OrderManagementSystem> _logger;
    private readonly Channel<ExecutionReport> _executionChannel;
    private readonly ConcurrentDictionary<string, string> _orderSessionIds = new(StringComparer.OrdinalIgnoreCase);
    private int _orderSequence;

    public OrderManagementSystem(
        IExecutionGateway gateway,
        ILogger<OrderManagementSystem> logger,
        IRiskValidator? riskValidator = null,
        ISecurityMasterGate? securityMasterGate = null,
        ExecutionOperatorControlService? operatorControls = null,
        ExecutionAuditTrailService? auditTrail = null,
        Meridian.Execution.Models.IPortfolioState? portfolioState = null,
        PaperSessionPersistenceService? sessionPersistence = null,
        BrokerageConfiguration? brokerageConfiguration = null)
    {
        _gateway = gateway ?? throw new ArgumentNullException(nameof(gateway));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _riskValidator = riskValidator;
        _securityMasterGate = securityMasterGate;
        _operatorControls = operatorControls;
        _auditTrail = auditTrail;
        _portfolioState = portfolioState;
        _sessionPersistence = sessionPersistence;
        _brokerageConfiguration = brokerageConfiguration;
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
        var brokerName = _gateway.GatewayId;

        // Extract metadata fields for audit correlation
        string? actor = null;
        string? correlationId = null;
        string? runId = null;
        string? sessionId = null;
        request.Metadata?.TryGetValue("actor", out actor);
        request.Metadata?.TryGetValue("correlationId", out correlationId);
        request.Metadata?.TryGetValue("runId", out runId);
        request.Metadata?.TryGetValue("sessionId", out sessionId);

        var safeRequest = ExecutionOrderMetadataPolicy.RemoveBrokerAccountAndOverrideKeys(request);
        if (!ReferenceEquals(safeRequest, request))
        {
            _logger.LogWarning(
                "Order {OrderId} for {Symbol} contained server-owned broker routing metadata; routing keys were removed before gateway submission.",
                orderId,
                request.Symbol);
        }

        var placementGate = BrokerageOrderPlacementGate.Evaluate(_brokerageConfiguration);
        if (!placementGate.IsAllowed)
        {
            var rejectedState = CreateRejectedState(orderId, safeRequest, placementGate.RejectReason);
            _orders[orderId] = rejectedState;
            await RecordSessionOrderUpdateAsync(sessionId, rejectedState, ct).ConfigureAwait(false);
            await RecordOrderRejectionAsync(
                orderId,
                safeRequest,
                actor,
                brokerName,
                runId,
                correlationId,
                placementGate.RejectReason,
                ct).ConfigureAwait(false);

            return new OrderResult
            {
                Success = false,
                OrderId = orderId,
                ErrorMessage = placementGate.RejectReason,
                OrderState = rejectedState
            };
        }

        // Operator controls gate — rejects orders when circuit breaker is open (unless bypassed)
        if (_operatorControls is not null)
        {
            var controlDecision = _operatorControls.EvaluateOrder(safeRequest, _portfolioState);
            if (!controlDecision.IsApproved)
            {
                _logger.LogWarning("Order {OrderId} for {Symbol} rejected by operator controls: {Reason}",
                    orderId, safeRequest.Symbol, controlDecision.RejectReason);

                if (_auditTrail is not null)
                {
                    await _auditTrail.RecordAsync(new ExecutionAuditEntry(
                        AuditId: Guid.NewGuid().ToString("N"),
                        Category: "Order",
                        Action: "OrderRejected",
                        Outcome: "Rejected",
                        OccurredAt: DateTimeOffset.UtcNow,
                        Actor: actor,
                        BrokerName: brokerName,
                        OrderId: orderId,
                        RunId: runId,
                        Symbol: safeRequest.Symbol,
                        CorrelationId: correlationId,
                        Message: controlDecision.RejectReason), ct).ConfigureAwait(false);
                }

                var rejectedState = CreateRejectedState(orderId, safeRequest, controlDecision.RejectReason);
                _orders[orderId] = rejectedState;
                await RecordSessionOrderUpdateAsync(sessionId, rejectedState, ct).ConfigureAwait(false);

                return new OrderResult
                {
                    Success = false,
                    OrderId = orderId,
                    ErrorMessage = controlDecision.RejectReason,
                    OrderState = rejectedState
                };
            }
        }

        // Security Master gate — reject orders for symbols not in the master (when gate is wired)
        if (_securityMasterGate is not null)
        {
            var gateResult = await _securityMasterGate.CheckAsync(safeRequest.Symbol, ct).ConfigureAwait(false);
            if (!gateResult.IsApproved)
            {
                _logger.LogWarning("Order {OrderId} for {Symbol} rejected by Security Master gate: {Reason}",
                    orderId, safeRequest.Symbol, gateResult.Reason);

                if (_auditTrail is not null)
                {
                    await _auditTrail.RecordAsync(new ExecutionAuditEntry(
                        AuditId: Guid.NewGuid().ToString("N"),
                        Category: "Order",
                        Action: "OrderRejected",
                        Outcome: "Rejected",
                        OccurredAt: DateTimeOffset.UtcNow,
                        Actor: actor,
                        BrokerName: brokerName,
                        OrderId: orderId,
                        RunId: runId,
                        Symbol: safeRequest.Symbol,
                        CorrelationId: correlationId,
                        Message: gateResult.Reason), ct).ConfigureAwait(false);
                }

                var rejectedState = CreateRejectedState(orderId, safeRequest, gateResult.Reason);
                _orders[orderId] = rejectedState;
                await RecordSessionOrderUpdateAsync(sessionId, rejectedState, ct).ConfigureAwait(false);

                return new OrderResult
                {
                    Success = false,
                    OrderId = orderId,
                    ErrorMessage = gateResult.Reason,
                    OrderState = rejectedState
                };
            }
        }

        // Pre-trade risk check
        if (_riskValidator is not null)
        {
            var riskResult = await _riskValidator.ValidateOrderAsync(safeRequest, ct).ConfigureAwait(false);
            if (!riskResult.IsApproved)
            {
                _logger.LogWarning("Order {OrderId} for {Symbol} rejected by risk: {Reason}",
                    orderId, safeRequest.Symbol, riskResult.RejectReason);

                if (_auditTrail is not null)
                {
                    await _auditTrail.RecordAsync(new ExecutionAuditEntry(
                        AuditId: Guid.NewGuid().ToString("N"),
                        Category: "Order",
                        Action: "OrderRejected",
                        Outcome: "Rejected",
                        OccurredAt: DateTimeOffset.UtcNow,
                        Actor: actor,
                        BrokerName: brokerName,
                        OrderId: orderId,
                        RunId: runId,
                        Symbol: safeRequest.Symbol,
                        CorrelationId: correlationId,
                        Message: riskResult.RejectReason), ct).ConfigureAwait(false);
                }

                var rejectedState = CreateRejectedState(orderId, safeRequest, riskResult.RejectReason);
                _orders[orderId] = rejectedState;
                await RecordSessionOrderUpdateAsync(sessionId, rejectedState, ct).ConfigureAwait(false);

                return new OrderResult
                {
                    Success = false,
                    OrderId = orderId,
                    ErrorMessage = riskResult.RejectReason,
                    OrderState = rejectedState
                };
            }
        }

        var orderState = new OrderState
        {
            OrderId = orderId,
            Symbol = safeRequest.Symbol,
            Side = safeRequest.Side,
            Type = safeRequest.Type,
            Quantity = safeRequest.Quantity,
            LimitPrice = safeRequest.LimitPrice,
            StopPrice = safeRequest.StopPrice,
            Status = OrderStatus.PendingNew,
            CreatedAt = DateTimeOffset.UtcNow,
            StrategyId = safeRequest.StrategyId
        };

        _orders[orderId] = orderState;
        if (!string.IsNullOrWhiteSpace(sessionId))
        {
            _orderSessionIds[orderId] = sessionId;
        }

        try
        {
            var report = await _gateway.SubmitOrderAsync(safeRequest with { ClientOrderId = orderId }, ct)
                .ConfigureAwait(false);

            var updatedState = ApplyReport(orderState, report);
            _orders[orderId] = updatedState;

            _logger.LogInformation("Order {OrderId} submitted for {Symbol} {Side} {Quantity} — status {Status}",
                orderId, safeRequest.Symbol, safeRequest.Side, safeRequest.Quantity, updatedState.Status);

            await RecordSessionOrderUpdateAsync(sessionId, updatedState, ct).ConfigureAwait(false);

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
                    BrokerName: brokerName,
                    OrderId: orderId,
                    RunId: runId,
                    Symbol: safeRequest.Symbol,
                    CorrelationId: correlationId), ct).ConfigureAwait(false);
            }

            // Publish fills to the execution channel so portfolio trackers and other
            // consumers can subscribe without coupling directly to the gateway.
            if (report.OrderStatus is OrderStatus.Filled or OrderStatus.PartiallyFilled)
            {
                if (_portfolioState is PaperTradingPortfolio paperPortfolio)
                {
                    paperPortfolio.ApplyFill(report);
                }

                await RecordSessionFillAsync(sessionId, report, ct).ConfigureAwait(false);
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
            _logger.LogError(ex, "Failed to submit order {OrderId} for {Symbol}", orderId, safeRequest.Symbol);

            var rejectedState = orderState with
            {
                Status = OrderStatus.Rejected,
                LastUpdatedAt = DateTimeOffset.UtcNow
            };
            _orders[orderId] = rejectedState;
            await RecordSessionOrderUpdateAsync(sessionId, rejectedState, ct).ConfigureAwait(false);

            if (_auditTrail is not null)
            {
                await _auditTrail.RecordAsync(new ExecutionAuditEntry(
                    AuditId: Guid.NewGuid().ToString("N"),
                    Category: "Order",
                    Action: "OrderRejected",
                    Outcome: "Rejected",
                    OccurredAt: DateTimeOffset.UtcNow,
                    Actor: actor,
                    BrokerName: brokerName,
                    OrderId: orderId,
                    RunId: runId,
                    Symbol: safeRequest.Symbol,
                    CorrelationId: correlationId,
                    Message: ex.Message), ct).ConfigureAwait(false);
            }

            return new OrderResult
            {
                Success = false,
                OrderId = orderId,
                ErrorMessage = ex.Message,
                OrderState = rejectedState
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
        if (report.OrderStatus is not OrderStatus.Cancelled)
        {
            return new OrderResult
            {
                Success = false,
                OrderId = orderId,
                OrderState = state,
                ErrorMessage = report.RejectReason ?? "Cancel request failed"
            };
        }

        var updated = ApplyReport(state, report);
        _orders[orderId] = updated;
        await RecordSessionOrderUpdateAsync(ResolveSessionId(orderId), updated, ct).ConfigureAwait(false);

        return new OrderResult
        {
            Success = true,
            OrderId = orderId,
            OrderState = updated
        };
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
        await RecordSessionOrderUpdateAsync(ResolveSessionId(orderId), updated, ct).ConfigureAwait(false);

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

    private static OrderState CreateRejectedState(
        string orderId,
        OrderRequest request,
        string? reason)
    {
        return new OrderState
        {
            OrderId = orderId,
            Symbol = request.Symbol,
            Side = request.Side,
            Type = request.Type,
            Quantity = request.Quantity,
            LimitPrice = request.LimitPrice,
            StopPrice = request.StopPrice,
            Status = OrderStatus.Rejected,
            CreatedAt = DateTimeOffset.UtcNow,
            LastUpdatedAt = DateTimeOffset.UtcNow,
            StrategyId = request.StrategyId,
            AverageFillPrice = null,
            FilledQuantity = 0m
        };
    }

    private async Task RecordSessionOrderUpdateAsync(
        string? sessionId,
        OrderState orderState,
        CancellationToken ct)
    {
        if (_sessionPersistence is null || string.IsNullOrWhiteSpace(sessionId))
        {
            return;
        }

        await _sessionPersistence.RecordOrderUpdateAsync(sessionId, orderState, ct).ConfigureAwait(false);
    }

    private async Task RecordSessionFillAsync(
        string? sessionId,
        ExecutionReport report,
        CancellationToken ct)
    {
        if (_sessionPersistence is null || string.IsNullOrWhiteSpace(sessionId))
        {
            return;
        }

        await _sessionPersistence.RecordFillAsync(sessionId, report, ct).ConfigureAwait(false);
    }

    private string? ResolveSessionId(string orderId) =>
        _orderSessionIds.TryGetValue(orderId, out var sessionId) ? sessionId : null;

    private async Task RecordOrderRejectionAsync(
        string orderId,
        OrderRequest request,
        string? actor,
        string brokerName,
        string? runId,
        string? correlationId,
        string? message,
        CancellationToken ct)
    {
        _logger.LogWarning(
            "Order {OrderId} for {Symbol} rejected by brokerage placement gate: {Reason}",
            orderId,
            request.Symbol,
            message);

        if (_auditTrail is null)
        {
            return;
        }

        await _auditTrail.RecordAsync(new ExecutionAuditEntry(
            AuditId: Guid.NewGuid().ToString("N"),
            Category: "Order",
            Action: "OrderRejected",
            Outcome: "Rejected",
            OccurredAt: DateTimeOffset.UtcNow,
            Actor: actor,
            BrokerName: brokerName,
            OrderId: orderId,
            RunId: runId,
            Symbol: request.Symbol,
            CorrelationId: correlationId,
            Message: message), ct).ConfigureAwait(false);
    }
}

/// <summary>Placeholder attribute for ADR traceability.</summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = true)]
internal sealed class ImplementsAdrAttribute(string adr, string reason) : Attribute
{
    public string Adr { get; } = adr;
    public string Reason { get; } = reason;
}
