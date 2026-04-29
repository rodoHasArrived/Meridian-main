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
    private readonly ILogger<OrderManagementSystem> _logger;
    private readonly Channel<ExecutionReport> _executionChannel;
    private readonly SemaphoreSlim _gatewayConnectionLock = new(1, 1);
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
        PaperSessionPersistenceService? sessionPersistence = null)
    {
        _gateway = gateway ?? throw new ArgumentNullException(nameof(gateway));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _riskValidator = riskValidator;
        _securityMasterGate = securityMasterGate;
        _operatorControls = operatorControls;
        _auditTrail = auditTrail;
        _portfolioState = portfolioState;
        _sessionPersistence = sessionPersistence;
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
                        BrokerName: brokerName,
                        OrderId: orderId,
                        RunId: runId,
                        Symbol: request.Symbol,
                        CorrelationId: correlationId,
                        Message: controlDecision.RejectReason), ct).ConfigureAwait(false);
                }

                var rejectedState = CreateRejectedState(orderId, request, controlDecision.RejectReason);
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
            var gateResult = await _securityMasterGate.CheckAsync(request.Symbol, ct).ConfigureAwait(false);
            if (!gateResult.IsApproved)
            {
                _logger.LogWarning("Order {OrderId} for {Symbol} rejected by Security Master gate: {Reason}",
                    orderId, request.Symbol, gateResult.Reason);

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
                        Symbol: request.Symbol,
                        CorrelationId: correlationId,
                        Message: gateResult.Reason), ct).ConfigureAwait(false);
                }

                var rejectedState = CreateRejectedState(orderId, request, gateResult.Reason);
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
            var riskResult = await _riskValidator.ValidateOrderAsync(request, ct).ConfigureAwait(false);
            if (!riskResult.IsApproved)
            {
                _logger.LogWarning("Order {OrderId} for {Symbol} rejected by risk: {Reason}",
                    orderId, request.Symbol, riskResult.RejectReason);

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
                        Symbol: request.Symbol,
                        CorrelationId: correlationId,
                        Message: riskResult.RejectReason), ct).ConfigureAwait(false);
                }

                var rejectedState = CreateRejectedState(orderId, request, riskResult.RejectReason);
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
        if (!string.IsNullOrWhiteSpace(sessionId))
        {
            _orderSessionIds[orderId] = sessionId;
        }

        try
        {
            await EnsureGatewayConnectedAsync(correlationId, actor, runId, request.Symbol, ct).ConfigureAwait(false);

            var report = await _gateway.SubmitOrderAsync(request with { ClientOrderId = orderId }, ct)
                .ConfigureAwait(false);

            var updatedState = ApplyReport(orderState, report);
            _orders[orderId] = updatedState;

            _logger.LogInformation("Order {OrderId} submitted for {Symbol} {Side} {Quantity} — status {Status}",
                orderId, request.Symbol, request.Side, request.Quantity, updatedState.Status);

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
                    Symbol: request.Symbol,
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
            _logger.LogError(ex, "Failed to submit order {OrderId} for {Symbol}", orderId, request.Symbol);

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
                    Symbol: request.Symbol,
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

        await EnsureGatewayConnectedAsync(
            correlationId: null,
            actor: null,
            runId: null,
            symbol: state.Symbol,
            ct: ct).ConfigureAwait(false);
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

        await EnsureGatewayConnectedAsync(
            correlationId: null,
            actor: null,
            runId: null,
            symbol: state.Symbol,
            ct: ct).ConfigureAwait(false);
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
        _gatewayConnectionLock.Dispose();
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

    private async Task EnsureGatewayConnectedAsync(
        string? correlationId,
        string? actor,
        string? runId,
        string? symbol,
        CancellationToken ct)
    {
        if (_gateway.IsConnected)
        {
            return;
        }

        await _gatewayConnectionLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (_gateway.IsConnected)
            {
                return;
            }

            _logger.LogInformation(
                "Connecting execution gateway {GatewayId} on demand before order operation",
                _gateway.GatewayId);

            try
            {
                await _gateway.ConnectAsync(ct).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                if (_auditTrail is not null)
                {
                    await _auditTrail.RecordAsync(new ExecutionAuditEntry(
                        AuditId: Guid.NewGuid().ToString("N"),
                        Category: "Order",
                        Action: "GatewayConnectFailed",
                        Outcome: "Rejected",
                        OccurredAt: DateTimeOffset.UtcNow,
                        Actor: actor,
                        BrokerName: _gateway.GatewayId,
                        RunId: runId,
                        Symbol: symbol,
                        CorrelationId: correlationId,
                        Message: ex.Message), ct).ConfigureAwait(false);
                }

                throw;
            }

            if (_auditTrail is not null)
            {
                await _auditTrail.RecordAsync(new ExecutionAuditEntry(
                    AuditId: Guid.NewGuid().ToString("N"),
                    Category: "Order",
                    Action: "GatewayConnected",
                    Outcome: "Connected",
                    OccurredAt: DateTimeOffset.UtcNow,
                    Actor: actor,
                    BrokerName: _gateway.GatewayId,
                    RunId: runId,
                    Symbol: symbol,
                    CorrelationId: correlationId,
                    Message: $"Connected {_gateway.GatewayId} on demand before execution."), ct).ConfigureAwait(false);
            }
        }
        finally
        {
            _gatewayConnectionLock.Release();
        }
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
}

/// <summary>Placeholder attribute for ADR traceability.</summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = true)]
internal sealed class ImplementsAdrAttribute(string adr, string reason) : Attribute
{
    public string Adr { get; } = adr;
    public string Reason { get; } = reason;
}
