using System.Collections.Concurrent;
using System.Threading.Channels;
using Meridian.Application.Pipeline;
using Meridian.Execution.Models;
using Meridian.Execution.Sdk;
using Meridian.Execution.Services;
using Microsoft.Extensions.Logging;
using SdkOrderStatus = Meridian.Execution.Sdk.OrderStatus;

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
    private readonly IPortfolioState? _portfolioState;
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
        IPortfolioState? portfolioState = null)
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
        var actor = TryGetMetadata(request.Metadata, "actor");
        var correlationId = TryGetMetadata(request.Metadata, "correlationId");
        var requestedOverrideId = TryGetMetadata(request.Metadata, "manualOverrideId");
        var runId = TryGetMetadata(request.Metadata, "runId") ?? request.StrategyId;

        await RecordAuditAsync(
            action: "OrderSubmitRequested",
            outcome: "Pending",
            actor: actor,
            orderId: orderId,
            runId: runId,
            symbol: request.Symbol,
            correlationId: correlationId,
            message: $"Submitting {request.Side} {request.Quantity:G29} {request.Symbol} as {request.Type}.",
            metadata: BuildOrderMetadata(request, ("requestedOverrideId", requestedOverrideId)),
            ct: ct).ConfigureAwait(false);

        if (_operatorControls is not null)
        {
            var controlDecision = _operatorControls.EvaluateOrder(request, _portfolioState);
            if (!controlDecision.IsApproved)
            {
                _logger.LogWarning(
                    "Order {OrderId} for {Symbol} rejected by execution controls: {Reason}",
                    orderId,
                    request.Symbol,
                    controlDecision.RejectReason);

                await RecordAuditAsync(
                    action: "OrderRejected",
                    outcome: "Rejected",
                    actor: actor,
                    orderId: orderId,
                    runId: runId,
                    symbol: request.Symbol,
                    correlationId: correlationId,
                    message: controlDecision.RejectReason,
                    metadata: BuildOrderMetadata(
                        request,
                        ("rejectedBy", "ExecutionControls"),
                        ("appliedOverrideId", controlDecision.AppliedManualOverrideId)),
                    ct: ct).ConfigureAwait(false);

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

                await RecordAuditAsync(
                    action: "OrderRejected",
                    outcome: "Rejected",
                    actor: actor,
                    orderId: orderId,
                    runId: runId,
                    symbol: request.Symbol,
                    correlationId: correlationId,
                    message: gateResult.Reason,
                    metadata: BuildOrderMetadata(request, ("rejectedBy", "SecurityMasterGate")),
                    ct: ct).ConfigureAwait(false);

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

                await RecordAuditAsync(
                    action: "OrderRejected",
                    outcome: "Rejected",
                    actor: actor,
                    orderId: orderId,
                    runId: runId,
                    symbol: request.Symbol,
                    correlationId: correlationId,
                    message: riskResult.RejectReason,
                    metadata: BuildOrderMetadata(request, ("rejectedBy", "RiskValidator")),
                    ct: ct).ConfigureAwait(false);

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
            Status = SdkOrderStatus.PendingNew,
            CreatedAt = DateTimeOffset.UtcNow,
            StrategyId = request.StrategyId
        };

        _orders[orderId] = orderState;

        try
        {
            await EnsureGatewayConnectedAsync(ct).ConfigureAwait(false);

            var report = await _gateway.SubmitOrderAsync(request with { ClientOrderId = orderId }, ct)
                .ConfigureAwait(false);

            var updatedState = ApplyReport(orderState, report);
            _orders[orderId] = updatedState;

            _logger.LogInformation("Order {OrderId} submitted for {Symbol} {Side} {Quantity} — status {Status}",
                orderId, request.Symbol, request.Side, request.Quantity, updatedState.Status);

            // Publish fills to the execution channel so portfolio trackers and other
            // consumers can subscribe without coupling directly to the gateway.
            if (report.OrderStatus is SdkOrderStatus.Filled or SdkOrderStatus.PartiallyFilled)
            {
                _executionChannel.Writer.TryWrite(report);
            }

            await RecordExecutionReportAsync(
                action: "OrderSubmitted",
                actor: actor,
                request: request,
                report: report,
                correlationId: correlationId,
                runId: runId,
                ct: ct).ConfigureAwait(false);

            return new OrderResult
            {
                Success = report.OrderStatus is not SdkOrderStatus.Rejected,
                OrderId = orderId,
                OrderState = updatedState,
                ErrorMessage = report.RejectReason
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to submit order {OrderId} for {Symbol}", orderId, request.Symbol);

            _orders[orderId] = orderState with
            {
                Status = SdkOrderStatus.Rejected,
                LastUpdatedAt = DateTimeOffset.UtcNow
            };

            await RecordAuditAsync(
                action: "OrderRejected",
                outcome: "Rejected",
                actor: actor,
                orderId: orderId,
                runId: runId,
                symbol: request.Symbol,
                correlationId: correlationId,
                message: ex.Message,
                metadata: BuildOrderMetadata(request, ("rejectedBy", "GatewayException")),
                ct: ct).ConfigureAwait(false);

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
            await RecordAuditAsync(
                action: "OrderCancelRequested",
                outcome: "Rejected",
                orderId: orderId,
                runId: null,
                symbol: null,
                message: "Order not found.",
                metadata: null,
                ct: ct).ConfigureAwait(false);

            return new OrderResult { Success = false, OrderId = orderId, ErrorMessage = "Order not found" };
        }

        await EnsureGatewayConnectedAsync(ct).ConfigureAwait(false);
        var report = await _gateway.CancelOrderAsync(orderId, ct).ConfigureAwait(false);
        var updated = ApplyReport(state, report);
        _orders[orderId] = updated;

        await RecordAuditAsync(
            action: "OrderCancelRequested",
            outcome: report.OrderStatus is SdkOrderStatus.Cancelled ? "Completed" : "Rejected",
            orderId: orderId,
            runId: state.StrategyId,
            symbol: state.Symbol,
            message: report.RejectReason ?? $"Cancel returned {report.OrderStatus}.",
            metadata: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["status"] = report.OrderStatus.ToString()
            },
            ct: ct).ConfigureAwait(false);

        return new OrderResult
        {
            Success = report.OrderStatus is SdkOrderStatus.Cancelled,
            OrderId = orderId,
            ErrorMessage = report.OrderStatus is SdkOrderStatus.Cancelled ? null : report.RejectReason ?? "Cancel rejected",
            OrderState = updated
        };
    }

    /// <inheritdoc />
    public async Task<OrderResult> ModifyOrderAsync(string orderId, OrderModification modification, CancellationToken ct = default)
    {
        if (!_orders.TryGetValue(orderId, out var state))
        {
            await RecordAuditAsync(
                action: "OrderModifyRequested",
                outcome: "Rejected",
                orderId: orderId,
                runId: null,
                symbol: null,
                message: "Order not found.",
                metadata: null,
                ct: ct).ConfigureAwait(false);

            return new OrderResult { Success = false, OrderId = orderId, ErrorMessage = "Order not found" };
        }

        await EnsureGatewayConnectedAsync(ct).ConfigureAwait(false);
        var report = await _gateway.ModifyOrderAsync(orderId, modification, ct).ConfigureAwait(false);
        var updated = ApplyReport(state, report);
        _orders[orderId] = updated;

        await RecordAuditAsync(
            action: "OrderModifyRequested",
            outcome: report.OrderStatus is SdkOrderStatus.Rejected ? "Rejected" : "Completed",
            orderId: orderId,
            runId: state.StrategyId,
            symbol: state.Symbol,
            message: report.RejectReason ?? $"Modify returned {report.OrderStatus}.",
            metadata: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["status"] = report.OrderStatus.ToString()
            },
            ct: ct).ConfigureAwait(false);

        return new OrderResult
        {
            Success = report.OrderStatus is not SdkOrderStatus.Rejected,
            OrderId = orderId,
            ErrorMessage = report.OrderStatus is SdkOrderStatus.Rejected ? report.RejectReason ?? "Modify rejected" : null,
            OrderState = updated
        };
    }

    /// <inheritdoc />
    public IReadOnlyList<OrderState> GetOpenOrders()
    {
        return _orders.Values
            .Where(o => o.Status is SdkOrderStatus.PendingNew or SdkOrderStatus.Accepted or SdkOrderStatus.PartiallyFilled)
            .ToList()
            .AsReadOnly();
    }

    /// <inheritdoc />
    public IReadOnlyList<OrderState> GetCompletedOrders(int take = 20)
    {
        return _orders.Values
            .Where(static o => o.Status is
                SdkOrderStatus.Filled or
                SdkOrderStatus.PartiallyFilled or
                SdkOrderStatus.Cancelled or
                SdkOrderStatus.Rejected or
                SdkOrderStatus.Expired)
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

    private async Task EnsureGatewayConnectedAsync(CancellationToken ct)
    {
        if (_gateway.IsConnected)
        {
            return;
        }

        await _gateway.ConnectAsync(ct).ConfigureAwait(false);
        await RecordAuditAsync(
            action: "GatewayConnected",
            outcome: "Completed",
            orderId: null,
            symbol: null,
            message: $"Connected execution gateway {_gateway.GatewayId}.",
            metadata: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["gatewayId"] = _gateway.GatewayId
            },
            ct: ct).ConfigureAwait(false);
    }

    private string GenerateOrderId()
    {
        var seq = Interlocked.Increment(ref _orderSequence);
        return $"MDN-{DateTimeOffset.UtcNow:yyyyMMdd}-{seq:D6}";
    }

    private async Task RecordExecutionReportAsync(
        string action,
        string? actor,
        OrderRequest request,
        ExecutionReport report,
        string? correlationId,
        string? runId,
        CancellationToken ct)
    {
        await RecordAuditAsync(
            action: action,
            outcome: report.OrderStatus is SdkOrderStatus.Rejected ? "Rejected" : "Completed",
            actor: actor,
            orderId: report.OrderId,
            runId: runId,
            symbol: report.Symbol,
            correlationId: correlationId,
            message: report.RejectReason ?? $"{report.OrderStatus} via {_gateway.GatewayId}.",
            metadata: BuildOrderMetadata(
                request,
                ("status", report.OrderStatus.ToString()),
                ("gatewayOrderId", report.GatewayOrderId),
                ("filledQuantity", report.FilledQuantity.ToString("G29")),
                ("fillPrice", report.FillPrice?.ToString("G29"))),
            ct: ct).ConfigureAwait(false);
    }

    private Task RecordAuditAsync(
        string action,
        string outcome,
        string? actor = null,
        string? orderId = null,
        string? runId = null,
        string? symbol = null,
        string? correlationId = null,
        string? message = null,
        IReadOnlyDictionary<string, string>? metadata = null,
        CancellationToken ct = default)
    {
        if (_auditTrail is null)
        {
            return Task.CompletedTask;
        }

        return _auditTrail.RecordAsync(
            category: "Order",
            action: action,
            outcome: outcome,
            actor: actor,
            brokerName: _gateway.GatewayId,
            orderId: orderId,
            runId: runId,
            symbol: symbol,
            correlationId: correlationId,
            message: message,
            metadata: metadata,
            ct: ct);
    }

    private static string? TryGetMetadata(IReadOnlyDictionary<string, string>? metadata, string key)
    {
        if (metadata is null)
        {
            return null;
        }

        return metadata.TryGetValue(key, out var value) ? value : null;
    }

    private static IReadOnlyDictionary<string, string> BuildOrderMetadata(
        OrderRequest request,
        params (string Key, string? Value)[] extras)
    {
        var metadata = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["side"] = request.Side.ToString(),
            ["type"] = request.Type.ToString(),
            ["quantity"] = request.Quantity.ToString("G29"),
            ["timeInForce"] = request.TimeInForce.ToString()
        };

        if (request.LimitPrice.HasValue)
        {
            metadata["limitPrice"] = request.LimitPrice.Value.ToString("G29");
        }

        if (request.StopPrice.HasValue)
        {
            metadata["stopPrice"] = request.StopPrice.Value.ToString("G29");
        }

        if (!string.IsNullOrWhiteSpace(request.StrategyId))
        {
            metadata["strategyId"] = request.StrategyId;
        }

        if (request.Metadata is not null)
        {
            foreach (var (key, value) in request.Metadata)
            {
                metadata[key] = value;
            }
        }

        foreach (var (key, value) in extras)
        {
            if (!string.IsNullOrWhiteSpace(value))
            {
                metadata[key] = value;
            }
        }

        return metadata;
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
