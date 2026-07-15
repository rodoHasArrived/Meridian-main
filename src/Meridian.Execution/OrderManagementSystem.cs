using System.Collections.Concurrent;
using System.Threading.Channels;
using Meridian.Application.Pipeline;
using Meridian.Execution.Sdk;
using Meridian.Execution.Services;
using Microsoft.Extensions.Logging;
using Meridian.Core.Pipeline;

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
    private readonly ILiveOrderReadinessGate? _liveOrderReadinessGate;
    private readonly ExecutionAuditTrailService? _auditTrail;
    private readonly Meridian.Execution.Models.IPortfolioState? _portfolioState;
    private readonly PaperSessionPersistenceService? _sessionPersistence;
    private readonly BrokerageConfiguration? _brokerageConfiguration;
    private readonly OrderManagementSystemOptions _options;
    private readonly ExecutionMode _gatewayExecutionMode;
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
        BrokerageConfiguration? brokerageConfiguration = null,
        ILiveOrderReadinessGate? liveOrderReadinessGate = null,
        OrderManagementSystemOptions? options = null)
    {
        _gateway = gateway ?? throw new ArgumentNullException(nameof(gateway));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _riskValidator = riskValidator;
        _securityMasterGate = securityMasterGate;
        _operatorControls = operatorControls;
        _liveOrderReadinessGate = liveOrderReadinessGate;
        _auditTrail = auditTrail;
        _portfolioState = portfolioState;
        _sessionPersistence = sessionPersistence;
        _brokerageConfiguration = brokerageConfiguration;
        _options = options ?? new OrderManagementSystemOptions();
        _gatewayExecutionMode = gateway is IExecutionGatewayModeProvider modeProvider
            ? modeProvider.ExecutionMode
            : BrokerageOrderPlacementGate.ResolveExecutionMode(brokerageConfiguration, gateway.GatewayId);
        // Use custom EventPipelinePolicy for execution reports: high capacity with backpressure
        var executionPolicy = new EventPipelinePolicy(
            Capacity: _options.ValidatedExecutionChannelCapacity,
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

        // A duplicate client order id must never reach the state table or the gateway: every
        // downstream write in this method (including gate rejections) keys on orderId, so a
        // replayed or colliding id would overwrite the tracked state (fills, status history)
        // of the order already working under that id.
        if (request.ClientOrderId is not null
            && _orders.TryGetValue(orderId, out var existingOrder)
            && !IsTerminalStatus(existingOrder.Status))
        {
            return await RejectDuplicateClientOrderIdAsync(
                orderId,
                safeRequest,
                actor,
                brokerName,
                runId,
                correlationId,
                ct).ConfigureAwait(false);
        }

        var placementGate = BrokerageOrderPlacementGate.Evaluate(
            _brokerageConfiguration,
            _gateway.GatewayId,
            _gatewayExecutionMode);
        if (!placementGate.IsAllowed)
        {
            return await RejectOrderAsync(
                orderId,
                safeRequest,
                actor,
                brokerName,
                runId,
                correlationId,
                placementGate.RejectReason,
                sessionId,
                ct,
                rejectionSource: "execution placement gate")
                .ConfigureAwait(false);
        }

        var requiresLiveOrderReadinessGate = RequiresLiveOrderReadinessGate();
        LiveOrderReadinessDecision? liveOrderReadinessDecision = null;
        if (requiresLiveOrderReadinessGate)
        {
            liveOrderReadinessDecision = await EvaluateLiveOrderReadinessAsync(
                safeRequest,
                brokerName,
                runId,
                actor,
                correlationId,
                ct).ConfigureAwait(false);

            if (!liveOrderReadinessDecision.IsApproved)
            {
                var rejectReason = liveOrderReadinessDecision.Reason ?? "Live order readiness gate rejected the order.";
                return await RejectOrderAsync(
                    orderId,
                    safeRequest,
                    actor,
                    brokerName,
                    runId,
                    correlationId,
                    rejectReason,
                    sessionId,
                    ct,
                    rejectionSource: "live order readiness gate",
                    reasonCode: "LIVE_ORDER_READINESS_REJECTED",
                    metadata: BuildLiveOrderReadinessRejectedAuditMetadata(liveOrderReadinessDecision))
                    .ConfigureAwait(false);
            }
        }

        // Operator controls gate — rejects orders when circuit breaker is open (unless bypassed)
        ExecutionControlDecision? operatorControlDecision = null;
        if (_operatorControls is not null)
        {
            // Live orders must not let client-owned override metadata bypass operator controls.
            var controlRequest = requiresLiveOrderReadinessGate ? safeRequest : request;
            var controlDecision = _operatorControls.EvaluateOrder(controlRequest, _portfolioState, runId);
            operatorControlDecision = controlDecision;
            if (!controlDecision.IsApproved)
            {
                return await RejectOrderAsync(
                    orderId,
                    safeRequest,
                    actor,
                    brokerName,
                    runId,
                    correlationId,
                    controlDecision.RejectReason,
                    sessionId,
                    ct,
                    rejectionSource: "operator controls",
                    reasonCode: controlDecision.RejectCode ?? "OPERATOR_CONTROL_REJECTED",
                    metadata: BuildOrderRejectedByControlAuditMetadata(controlDecision))
                    .ConfigureAwait(false);
            }
        }

        // Security Master gate — reject orders for symbols not in the master (when gate is wired)
        if (_securityMasterGate is not null)
        {
            var gateResult = await _securityMasterGate.CheckAsync(safeRequest.Symbol, ct).ConfigureAwait(false);
            if (!gateResult.IsApproved)
            {
                return await RejectOrderAsync(
                    orderId,
                    safeRequest,
                    actor,
                    brokerName,
                    runId,
                    correlationId,
                    gateResult.Reason,
                    sessionId,
                    ct,
                    rejectionSource: "Security Master gate")
                    .ConfigureAwait(false);
            }
        }

        // Pre-trade risk check
        if (_riskValidator is not null)
        {
            var riskResult = await _riskValidator.ValidateOrderAsync(safeRequest, ct).ConfigureAwait(false);
            if (!riskResult.IsApproved)
            {
                return await RejectOrderAsync(
                    orderId,
                    safeRequest,
                    actor,
                    brokerName,
                    runId,
                    correlationId,
                    riskResult.RejectReason,
                    sessionId,
                    ct,
                    rejectionSource: "risk validator")
                    .ConfigureAwait(false);
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

        if (!TryRegisterOrder(orderId, orderState))
        {
            // Lost a race with a concurrent submission that claimed the same client order id
            // after the guard above ran; the winner's state must survive untouched.
            return await RejectDuplicateClientOrderIdAsync(
                orderId,
                safeRequest,
                actor,
                brokerName,
                runId,
                correlationId,
                ct).ConfigureAwait(false);
        }

        TrimRetainedOrdersIfNeeded();
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
                    CorrelationId: correlationId,
                    Reason: operatorControlDecision?.AppliedManualOverrideId is null
                        ? null
                        : "ManualOverrideApplied",
                    Scope: BuildOrderAuditScope(safeRequest, runId),
                    Metadata: BuildOrderSubmittedAuditMetadata(
                        operatorControlDecision,
                        liveOrderReadinessDecision)), ct).ConfigureAwait(false);
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
            TrimRetainedOrdersIfNeeded();
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
            await RecordOrderLifecycleAuditAsync(
                action: "OrderCancelRejected",
                outcome: "Rejected",
                orderId: orderId,
                state: null,
                report: null,
                message: "Order not found",
                ct: ct).ConfigureAwait(false);

            return new OrderResult { Success = false, OrderId = orderId, ErrorMessage = "Order not found" };
        }

        var report = await _gateway.CancelOrderAsync(orderId, ct).ConfigureAwait(false);
        if (report.OrderStatus is not OrderStatus.Cancelled)
        {
            await RecordOrderLifecycleAuditAsync(
                action: "OrderCancelRejected",
                outcome: report.OrderStatus.ToString(),
                orderId: orderId,
                state: state,
                report: report,
                message: report.RejectReason ?? "Cancel request failed",
                ct: ct).ConfigureAwait(false);

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
        await RecordOrderLifecycleAuditAsync(
            action: "OrderCancelled",
            outcome: updated.Status.ToString(),
            orderId: orderId,
            state: updated,
            report: report,
            message: report.RejectReason,
            ct: ct).ConfigureAwait(false);

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
            await RecordOrderLifecycleAuditAsync(
                action: "OrderModifyRejected",
                outcome: "Rejected",
                orderId: orderId,
                state: null,
                report: null,
                message: "Order not found",
                ct: ct).ConfigureAwait(false);

            return new OrderResult { Success = false, OrderId = orderId, ErrorMessage = "Order not found" };
        }

        var report = await _gateway.ModifyOrderAsync(orderId, modification, ct).ConfigureAwait(false);
        var updated = ApplyReport(state, report);
        _orders[orderId] = updated;
        await RecordSessionOrderUpdateAsync(ResolveSessionId(orderId), updated, ct).ConfigureAwait(false);
        await RecordOrderLifecycleAuditAsync(
            action: report.OrderStatus is OrderStatus.Rejected ? "OrderModifyRejected" : "OrderModified",
            outcome: updated.Status.ToString(),
            orderId: orderId,
            state: updated,
            report: report,
            message: report.RejectReason,
            metadata: BuildOrderModificationAuditMetadata(modification, updated, report),
            ct: ct).ConfigureAwait(false);

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

        await Parallel.ForEachAsync(
            openOrders,
            new ParallelOptions
            {
                CancellationToken = ct,
                MaxDegreeOfParallelism = _options.ValidatedCancelAllMaxConcurrency
            },
            async (order, token) =>
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

    /// <summary>
    /// Terminal statuses whose order ids may be reclaimed by a later submission. Excludes
    /// <see cref="OrderStatus.PendingCancel"/>: an order awaiting cancel confirmation is still
    /// working at the broker and its id must not be reused.
    /// </summary>
    private static bool IsTerminalStatus(OrderStatus status)
        => status is OrderStatus.Filled or OrderStatus.Cancelled or OrderStatus.Rejected or OrderStatus.Expired;

    /// <summary>
    /// Atomically claims <paramref name="orderId"/> in the order table. A terminal entry may be
    /// reclaimed (retention trimming already makes terminal ids reusable once evicted, so
    /// reuse-after-terminal keeps the same semantics); an active entry may not.
    /// </summary>
    private bool TryRegisterOrder(string orderId, OrderState orderState)
    {
        while (!_orders.TryAdd(orderId, orderState))
        {
            if (!_orders.TryGetValue(orderId, out var existing))
            {
                continue; // Entry was trimmed between TryAdd and TryGetValue; retry.
            }

            if (!IsTerminalStatus(existing.Status))
            {
                return false;
            }

            if (_orders.TryUpdate(orderId, orderState, existing))
            {
                break;
            }
        }

        return true;
    }

    private async Task<OrderResult> RejectDuplicateClientOrderIdAsync(
        string orderId,
        OrderRequest request,
        string? actor,
        string brokerName,
        string? runId,
        string? correlationId,
        CancellationToken ct)
    {
        var message = $"Duplicate client order id '{orderId}': an order with this id is already being tracked and is not in a terminal state.";

        await RecordOrderRejectionAsync(
            orderId,
            request,
            actor,
            brokerName,
            runId,
            correlationId,
            message,
            ct,
            rejectionSource: "duplicate client order id guard",
            reasonCode: "DUPLICATE_CLIENT_ORDER_ID").ConfigureAwait(false);

        return new OrderResult
        {
            Success = false,
            OrderId = orderId,
            ErrorMessage = message
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

    private static string BuildOrderAuditScope(OrderRequest request, string? runId) =>
        BuildOrderAuditScope(request.Symbol, request.StrategyId, runId, includeRunSegment: true);

    private static string BuildOrderAuditScope(OrderState state) =>
        BuildOrderAuditScope(state.Symbol, state.StrategyId, runId: null, includeRunSegment: false);

    private static string BuildOrderAuditScope(
        string? symbolValue,
        string? strategyId,
        string? runId,
        bool includeRunSegment)
    {
        var symbol = string.IsNullOrWhiteSpace(symbolValue) ? "symbol:unknown" : $"symbol:{symbolValue.Trim().ToUpperInvariant()}";
        var strategy = string.IsNullOrWhiteSpace(strategyId) ? "strategy:unknown" : $"strategy:{strategyId.Trim()}";
        if (!includeRunSegment)
        {
            return $"{strategy}/{symbol}";
        }

        var run = string.IsNullOrWhiteSpace(runId) ? "run:unknown" : $"run:{runId.Trim()}";
        return $"{run}/{strategy}/{symbol}";
    }

    private async Task<LiveOrderReadinessDecision> EvaluateLiveOrderReadinessAsync(
        OrderRequest request,
        string brokerName,
        string? runId,
        string? actor,
        string? correlationId,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(runId))
        {
            return LiveOrderReadinessDecision.Rejected(
                "Live order placement requires runId metadata for a W7-approved live run.");
        }

        if (_liveOrderReadinessGate is null)
        {
            return LiveOrderReadinessDecision.Rejected(
                "Live order placement requires a live order readiness gate before broker routing.");
        }

        var decision = await _liveOrderReadinessGate.EvaluateAsync(
            new LiveOrderReadinessRequest(
                RunId: runId.Trim(),
                BrokerName: brokerName,
                Symbol: request.Symbol,
                Side: request.Side,
                OrderType: request.Type,
                Quantity: request.Quantity,
                StrategyId: request.StrategyId,
                Actor: actor,
                CorrelationId: correlationId,
                FundAccountId: request.FundAccountId),
            ct).ConfigureAwait(false);

        if (!decision.IsApproved)
        {
            return string.IsNullOrWhiteSpace(decision.Reason)
                ? decision with { Reason = "Live order readiness gate rejected the order." }
                : decision;
        }

        return string.IsNullOrWhiteSpace(decision.EvidenceReference)
            ? LiveOrderReadinessDecision.Rejected(
                "Live order readiness gate approved the order without a retained evidence reference.")
            : decision with { EvidenceReference = decision.EvidenceReference.Trim() };
    }

    private bool RequiresLiveOrderReadinessGate() =>
        _gatewayExecutionMode is ExecutionMode.Live;

    private static IReadOnlyDictionary<string, string>? BuildOrderSubmittedAuditMetadata(
        ExecutionControlDecision? operatorControlDecision,
        LiveOrderReadinessDecision? liveOrderReadinessDecision)
    {
        var metadata = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (!string.IsNullOrWhiteSpace(operatorControlDecision?.AppliedManualOverrideId))
        {
            metadata["manualOverrideId"] = operatorControlDecision.AppliedManualOverrideId;
            metadata["controlDecision"] = "approved-by-manual-override";
        }

        if (!string.IsNullOrWhiteSpace(liveOrderReadinessDecision?.EvidenceReference))
        {
            metadata["liveReadinessDecision"] = "approved";
            metadata["liveReadinessEvidenceReference"] = liveOrderReadinessDecision.EvidenceReference;
        }

        return metadata.Count == 0 ? null : metadata;
    }

    private static IReadOnlyDictionary<string, string> BuildOrderRejectedByControlAuditMetadata(
        ExecutionControlDecision operatorControlDecision)
    {
        var metadata = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["controlDecision"] = "rejected-by-operator-controls"
        };

        if (!string.IsNullOrWhiteSpace(operatorControlDecision.RejectCode))
        {
            metadata["rejectCode"] = operatorControlDecision.RejectCode;
        }

        return metadata;
    }

    private static IReadOnlyDictionary<string, string> BuildLiveOrderReadinessRejectedAuditMetadata(
        LiveOrderReadinessDecision liveOrderReadinessDecision)
    {
        var metadata = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["liveReadinessDecision"] = "rejected",
            ["rejectCode"] = "LIVE_ORDER_READINESS_REJECTED"
        };

        if (!string.IsNullOrWhiteSpace(liveOrderReadinessDecision.EvidenceReference))
        {
            metadata["liveReadinessEvidenceReference"] = liveOrderReadinessDecision.EvidenceReference;
        }

        return metadata;
    }

    private async Task RecordOrderLifecycleAuditAsync(
        string action,
        string outcome,
        string orderId,
        OrderState? state,
        ExecutionReport? report,
        string? message,
        CancellationToken ct,
        IReadOnlyDictionary<string, string>? metadata = null)
    {
        if (_auditTrail is null)
        {
            return;
        }

        await _auditTrail.RecordAsync(new ExecutionAuditEntry(
            AuditId: Guid.NewGuid().ToString("N"),
            Category: "Order",
            Action: action,
            Outcome: outcome,
            OccurredAt: DateTimeOffset.UtcNow,
            BrokerName: _gateway.GatewayId,
            OrderId: orderId,
            RunId: null,
            Symbol: state?.Symbol ?? report?.Symbol,
            Message: message,
            Reason: report?.RejectReason,
            Scope: state is null ? null : BuildOrderAuditScope(state),
            Metadata: metadata ?? BuildOrderLifecycleAuditMetadata(state, report)), ct).ConfigureAwait(false);
    }

    private static IReadOnlyDictionary<string, string>? BuildOrderLifecycleAuditMetadata(
        OrderState? state,
        ExecutionReport? report)
    {
        if (state is null && report is null)
        {
            return null;
        }

        var metadata = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (state is not null)
        {
            metadata["orderQuantity"] = state.Quantity.ToString("G29");
            metadata["filledQuantity"] = state.FilledQuantity.ToString("G29");
            metadata["orderType"] = state.Type.ToString();
            metadata["side"] = state.Side.ToString();
        }

        if (report is not null)
        {
            metadata["reportType"] = report.ReportType.ToString();
            metadata["reportStatus"] = report.OrderStatus.ToString();
            metadata["gatewayOrderId"] = report.GatewayOrderId ?? string.Empty;
        }

        return metadata;
    }

    private static IReadOnlyDictionary<string, string> BuildOrderModificationAuditMetadata(
        OrderModification modification,
        OrderState state,
        ExecutionReport report)
    {
        var metadata = new Dictionary<string, string>(
            BuildOrderLifecycleAuditMetadata(state, report) ?? new Dictionary<string, string>(),
            StringComparer.OrdinalIgnoreCase);

        metadata["newQuantity"] = modification.NewQuantity?.ToString("G29") ?? string.Empty;
        metadata["newLimitPrice"] = modification.NewLimitPrice?.ToString("G29") ?? string.Empty;
        metadata["newStopPrice"] = modification.NewStopPrice?.ToString("G29") ?? string.Empty;
        metadata["newTrail"] = modification.NewTrail?.ToString("G29") ?? string.Empty;

        return metadata;
    }

    private async Task<OrderResult> RejectOrderAsync(
        string orderId,
        OrderRequest request,
        string? actor,
        string brokerName,
        string? runId,
        string? correlationId,
        string? message,
        string? sessionId,
        CancellationToken ct,
        string rejectionSource,
        string? reasonCode = null,
        IReadOnlyDictionary<string, string>? metadata = null)
    {
        var rejectedState = CreateRejectedState(orderId, request, message);
        // TryAdd, not the indexer: gate rejections run before the order id is registered, so an
        // existing entry under this id belongs to a different order (e.g. a terminal order whose
        // id a rejected submission tried to reuse) and must survive. The rejection is still
        // audit-trailed and returned to the caller.
        if (_orders.TryAdd(orderId, rejectedState))
        {
            TrimRetainedOrdersIfNeeded();
        }

        await RecordSessionOrderUpdateAsync(sessionId, rejectedState, ct).ConfigureAwait(false);
        await RecordOrderRejectionAsync(
            orderId,
            request,
            actor,
            brokerName,
            runId,
            correlationId,
            message,
            ct,
            rejectionSource,
            reasonCode,
            metadata).ConfigureAwait(false);

        return new OrderResult
        {
            Success = false,
            OrderId = orderId,
            ErrorMessage = message,
            OrderState = rejectedState
        };
    }

    private async Task RecordOrderRejectionAsync(
        string orderId,
        OrderRequest request,
        string? actor,
        string brokerName,
        string? runId,
        string? correlationId,
        string? message,
        CancellationToken ct,
        string rejectionSource,
        string? reasonCode = null,
        IReadOnlyDictionary<string, string>? metadata = null)
    {
        _logger.LogWarning(
            "Order {OrderId} for {Symbol} rejected by {RejectionSource}: {Reason}",
            orderId,
            request.Symbol,
            rejectionSource,
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
            Message: message,
            Reason: reasonCode,
            Scope: BuildOrderAuditScope(request, runId),
            Metadata: metadata), ct).ConfigureAwait(false);
    }

    private void TrimRetainedOrdersIfNeeded()
    {
        if (_orders.Count <= _options.ValidatedMaxRetainedOrders)
        {
            return;
        }

        var removableOrderIds = _orders.Values
            .Where(static order => order.Status is
                OrderStatus.Filled or
                OrderStatus.Cancelled or
                OrderStatus.Rejected or
                OrderStatus.Expired)
            .OrderBy(static order => order.LastUpdatedAt ?? order.CreatedAt)
            .Take(_orders.Count - _options.ValidatedMaxRetainedOrders)
            .Select(static order => order.OrderId)
            .ToArray();

        foreach (var removableOrderId in removableOrderIds)
        {
            _orders.TryRemove(removableOrderId, out _);
            _orderSessionIds.TryRemove(removableOrderId, out _);
        }
    }
}

/// <summary>Placeholder attribute for ADR traceability.</summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = true)]
internal sealed class ImplementsAdrAttribute(string adr, string reason) : Attribute
{
    public string Adr { get; } = adr;
    public string Reason { get; } = reason;
}
