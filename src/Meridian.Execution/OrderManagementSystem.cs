using System.Collections.Concurrent;
using System.Globalization;
using System.Runtime.ExceptionServices;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Channels;
using Meridian.Application.Pipeline;
using Meridian.Execution.Events;
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
public sealed class OrderManagementSystem : IOrderManager, IDisposable, IAsyncDisposable
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
    private readonly CancellationTokenSource _reportPumpCts = new();
    private readonly Task _reportPumpTask;
    private readonly ITradeEventPublisher? _tradeEventPublisher;
    private readonly ITradeFillHandoffFailureStore? _tradeFillHandoffFailureStore;
    private readonly Task _handoffRecoveryTask;
    private readonly ConcurrentDictionary<string, string> _orderFinancialAccountIds = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<ExecutionReport, FillProcessingProgress> _fillProcessing = new();
    private readonly ConcurrentQueue<ExecutionReport> _completedFillReportOrder = new();
    private readonly object _disposeSync = new();
    private Task? _disposeTask;
    private TaskCompletionSource? _operationsDrained;
    private int _orderSequence;
    private int _activeOperations;
    private int _disposeStarted;

    private const int MaxTrackedFillReports = 4096;
    private static readonly TimeSpan InitialReportStreamRetryDelay = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan MaxReportStreamRetryDelay = TimeSpan.FromSeconds(60);

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
        OrderManagementSystemOptions? options = null,
        ITradeEventPublisher? tradeEventPublisher = null,
        ITradeFillHandoffFailureStore? tradeFillHandoffFailureStore = null)
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
        _tradeEventPublisher = tradeEventPublisher;
        _tradeFillHandoffFailureStore = tradeFillHandoffFailureStore;
        if (tradeFillHandoffFailureStore is not null
            && tradeEventPublisher is not IScopedTradeEventPublisher)
        {
            throw new ArgumentException(
                "A handoff-failure store requires a scope-bound accounting publisher.",
                nameof(tradeEventPublisher));
        }
        if (tradeEventPublisher is IScopedTradeEventPublisher scopedPublisher
            && tradeFillHandoffFailureStore is not null
            && !string.Equals(
                scopedPublisher.PostingScope,
                tradeFillHandoffFailureStore.PostingScope,
                StringComparison.Ordinal))
        {
            throw new ArgumentException(
                $"Accounting publisher scope '{scopedPublisher.PostingScope}' does not match handoff-failure store scope '{tradeFillHandoffFailureStore.PostingScope}'.",
                nameof(tradeFillHandoffFailureStore));
        }
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

        // Consume the gateway's asynchronous execution report stream so partial fills,
        // rejects, and cancels that arrive after the synchronous submit ack still reach
        // order state, session persistence, and downstream fill consumers.
        _reportPumpTask = Task.Run(() => PumpGatewayExecutionReportsAsync(_reportPumpCts.Token));
        _handoffRecoveryTask = _tradeEventPublisher is not null && _tradeFillHandoffFailureStore is not null
            ? Task.Run(() => ReplayRetainedAccountingHandoffsAsync(_reportPumpCts.Token))
            : Task.CompletedTask;
    }

    /// <inheritdoc />
    public async Task<OrderResult> PlaceOrderAsync(OrderRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        using var operation = EnterOperation();

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

        // Reject a duplicate client order id that is still open before anything writes _orders[orderId]:
        // that dictionary is keyed by order id, so placing over an existing open order would clobber
        // its tracked state and — once the gateway rejects the duplicate — mark the still-live original
        // as rejected. Orders in a terminal state may reuse their id.
        if (_orders.TryGetValue(orderId, out var existingOpenOrder)
            && existingOpenOrder.Status is not (OrderStatus.Filled or OrderStatus.Cancelled
                or OrderStatus.Rejected or OrderStatus.Expired))
        {
            var duplicateReason = $"An order with client order id '{orderId}' is already open.";
            await RecordOrderLifecycleAuditAsync(
                action: "OrderPlaceRejected",
                outcome: "Rejected",
                orderId: orderId,
                state: existingOpenOrder,
                report: null,
                message: duplicateReason,
                ct: ct).ConfigureAwait(false);

            return new OrderResult
            {
                Success = false,
                OrderId = orderId,
                OrderState = existingOpenOrder,
                ErrorMessage = duplicateReason
            };
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

        // Atomic reservation closes the concurrent-duplicate race the early guard cannot: if two
        // placements with the same id race past that read-only guard, only one wins this slot. A
        // placement that finds an already-open order here rejects instead of overwriting it; a
        // terminal order at the key is a legitimate id reuse and is replaced.
        var reserved = _orders.AddOrUpdate(
            orderId,
            orderState,
            (_, existing) => existing.Status is OrderStatus.Filled or OrderStatus.Cancelled
                or OrderStatus.Rejected or OrderStatus.Expired
                ? orderState
                : existing);
        if (!ReferenceEquals(reserved, orderState))
        {
            var duplicateReason = $"An order with client order id '{orderId}' is already open.";
            await RecordOrderLifecycleAuditAsync(
                action: "OrderPlaceRejected",
                outcome: "Rejected",
                orderId: orderId,
                state: reserved,
                report: null,
                message: duplicateReason,
                ct: ct).ConfigureAwait(false);

            return new OrderResult
            {
                Success = false,
                OrderId = orderId,
                OrderState = reserved,
                ErrorMessage = duplicateReason
            };
        }

        if (safeRequest.FundAccountId is { } fundAccountId)
        {
            _orderFinancialAccountIds[orderId] = fundAccountId.ToString("D");
        }
        else
        {
            // A terminal client-order id may be reused. Do not let the prior order's
            // accounting scope leak into fills for an unscoped replacement order.
            _orderFinancialAccountIds.TryRemove(orderId, out _);
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

            // Merge against the latest tracked state: the async report pump may already
            // have applied a fill for this order before the submit ack is processed here.
            var previousFilledQuantity = 0m;
            var updatedState = _orders.AddOrUpdate(
                orderId,
                _ =>
                {
                    previousFilledQuantity = orderState.FilledQuantity;
                    return ApplyReport(orderState, report);
                },
                (_, existing) =>
                {
                    previousFilledQuantity = existing.FilledQuantity;
                    return ApplyReport(existing, report);
                });

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
                await ProcessFillReportAsync(sessionId, report, previousFilledQuantity, ct).ConfigureAwait(false);
            }

            return new OrderResult
            {
                Success = report.OrderStatus is not OrderStatus.Rejected,
                OrderId = orderId,
                OrderState = updatedState,
                ErrorMessage = report.RejectReason
            };
        }
        catch (AccountingHandoffException ex)
        {
            var filledState = _orders.TryGetValue(orderId, out var retainedState)
                ? retainedState
                : orderState;
            _logger.LogCritical(
                ex,
                "Order {OrderId} filled but accounting handoff failed; retained={HandoffRetained}",
                orderId,
                ex.WasRetained);
            await RecordOrderLifecycleAuditAsync(
                    action: "AccountingHandoffFailed",
                    outcome: "AttentionRequired",
                    orderId: orderId,
                    state: filledState,
                    report: null,
                    message: ex.Message,
                    ct: ct)
                .ConfigureAwait(false);

            return new OrderResult
            {
                Success = false,
                OrderId = orderId,
                ErrorMessage = ex.Message,
                OrderState = filledState
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
        using var operation = EnterOperation();
        return await CancelOrderCoreAsync(orderId, ct).ConfigureAwait(false);
    }

    private async Task<OrderResult> CancelOrderCoreAsync(string orderId, CancellationToken ct)
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

        var updated = _orders.AddOrUpdate(
            orderId,
            _ => ApplyReport(state, report),
            (_, existing) => ApplyReport(existing, report));
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
        using var operation = EnterOperation();

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
        if (report.OrderStatus is OrderStatus.Rejected)
        {
            // Do not apply a rejected modify to order state: ApplyReport would let the terminal
            // Rejected overwrite a completed Filled/Cancelled order, and returning Success would
            // misreport that overwrite as a successful modify. Mirror the cancel path and fail.
            await RecordOrderLifecycleAuditAsync(
                action: "OrderModifyRejected",
                outcome: report.OrderStatus.ToString(),
                orderId: orderId,
                state: state,
                report: report,
                message: report.RejectReason ?? "Modify request rejected",
                metadata: BuildOrderModificationAuditMetadata(modification, state, report),
                ct: ct).ConfigureAwait(false);

            return new OrderResult
            {
                Success = false,
                OrderId = orderId,
                OrderState = state,
                ErrorMessage = report.RejectReason ?? "Modify request rejected"
            };
        }

        var updated = _orders.AddOrUpdate(
            orderId,
            _ => ApplyReport(state, report),
            (_, existing) => ApplyReport(existing, report));
        await RecordSessionOrderUpdateAsync(ResolveSessionId(orderId), updated, ct).ConfigureAwait(false);
        await RecordOrderLifecycleAuditAsync(
            action: "OrderModified",
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
        using var operation = EnterOperation();

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
                await CancelOrderCoreAsync(order.OrderId, token).ConfigureAwait(false);
            }).ConfigureAwait(false);
    }

    public void Dispose()
        => DisposeAsync().AsTask().GetAwaiter().GetResult();

    /// <summary>
    /// Stops report intake and awaits both the broker-report and retained-handoff pumps before
    /// returning. Dependency injection can therefore dispose the accounting publisher and
    /// failure store only after no OMS task can use them.
    /// </summary>
    public ValueTask DisposeAsync()
    {
        lock (_disposeSync)
        {
            Interlocked.Exchange(ref _disposeStarted, 1);
            if (_disposeTask is not null)
                return new ValueTask(_disposeTask);

            var operationsDrained = _activeOperations == 0
                ? Task.CompletedTask
                : (_operationsDrained ??= new TaskCompletionSource(
                    TaskCreationOptions.RunContinuationsAsynchronously)).Task;
            _disposeTask = DisposeCoreAsync(operationsDrained);
            return new ValueTask(_disposeTask);
        }
    }

    private async Task DisposeCoreAsync(Task operationsDrained)
    {
        // Do not cancel report intake until every operation admitted before disposal has
        // completed. In particular, a broker submit may return a fill whose accounting
        // handoff still needs to reach the primary publisher or durable fallback.
        await Task.Yield();
        await operationsDrained.ConfigureAwait(false);

        Exception? shutdownFailure = null;
        try
        {
            await _reportPumpCts.CancelAsync().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            shutdownFailure = ex;
        }

        try
        {
            await Task.WhenAll(_reportPumpTask, _handoffRecoveryTask).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (_reportPumpCts.IsCancellationRequested)
        {
            // Expected when shutdown interrupts a gateway read or retained-handoff load.
        }
        catch (Exception ex)
        {
            shutdownFailure = shutdownFailure is null
                ? ex
                : new AggregateException(shutdownFailure, ex);
        }
        finally
        {
            _executionChannel.Writer.TryComplete();
            _reportPumpCts.Dispose();
        }

        if (shutdownFailure is not null)
            ExceptionDispatchInfo.Capture(shutdownFailure).Throw();
    }

    /// <summary>
    /// Provides a read-only view of fill and partial-fill execution reports for consumption
    /// by portfolio trackers and audit subscribers.  Reports are published as each order
    /// transitions to <see cref="OrderStatus.Filled"/> or <see cref="OrderStatus.PartiallyFilled"/>,
    /// with <see cref="ExecutionReport.FilledQuantity"/> normalised to the fill increment
    /// (gateways report cumulative quantities). Consumers must drain this reader promptly
    /// to avoid backpressure.
    /// </summary>
    public ChannelReader<ExecutionReport> ExecutionReports => _executionChannel.Reader;

    /// <summary>
    /// Returns OMS-level accounting handoff failures that could not enter the primary publisher.
    /// These records survive process restart when a failure store is composed.
    /// </summary>
    public Task<IReadOnlyList<RetainedTradeFillHandoffFailure>> GetAccountingHandoffFailuresAsync(
        CancellationToken ct = default)
        => GetAccountingHandoffFailuresCoreAsync(ct);

    private async Task<IReadOnlyList<RetainedTradeFillHandoffFailure>> GetAccountingHandoffFailuresCoreAsync(
        CancellationToken ct)
    {
        using var operation = EnterOperation();
        return _tradeFillHandoffFailureStore is null
            ? []
            : await _tradeFillHandoffFailureStore.LoadPendingAsync(ct).ConfigureAwait(false);
    }

    private OperationLease EnterOperation()
    {
        lock (_disposeSync)
        {
            if (_disposeStarted != 0)
                throw new ObjectDisposedException(nameof(OrderManagementSystem));

            checked
            {
                _activeOperations++;
            }

            return new OperationLease(this);
        }
    }

    private void ExitOperation()
    {
        lock (_disposeSync)
        {
            _activeOperations--;
            if (_activeOperations == 0)
                _operationsDrained?.TrySetResult();
        }
    }

    private string GenerateOrderId()
    {
        var seq = Interlocked.Increment(ref _orderSequence);
        return $"MDN-{DateTimeOffset.UtcNow:yyyyMMdd}-{seq:D6}";
    }

    private static OrderState ApplyReport(OrderState current, ExecutionReport report)
    {
        // A replayed or late non-terminal report (e.g. the submit ack racing the async
        // report stream) must not regress an order that already reached a terminal status.
        var status = IsTerminal(current.Status) && !IsTerminal(report.OrderStatus)
            ? current.Status
            : report.OrderStatus;

        return current with
        {
            Status = status,
            FilledQuantity = Math.Max(report.FilledQuantity, current.FilledQuantity),
            AverageFillPrice = report.FillPrice ?? current.AverageFillPrice,
            LastUpdatedAt = report.Timestamp
        };
    }

    private static bool IsTerminal(OrderStatus status) =>
        status is OrderStatus.Filled or OrderStatus.Cancelled or OrderStatus.Rejected or OrderStatus.Expired;

    /// <summary>
    /// Long-running consumer of <see cref="IExecutionGateway.StreamExecutionReportsAsync"/>.
    /// Applies asynchronous reports (partial fills, rejects, cancels from a live broker) to
    /// tracked order state and routes fills through the same funnel as the synchronous
    /// submit path. Retries with capped backoff if the stream faults; stops when the stream
    /// completes normally or the OMS is disposed.
    /// </summary>
    private async Task PumpGatewayExecutionReportsAsync(CancellationToken ct)
    {
        var retryDelay = InitialReportStreamRetryDelay;

        while (!ct.IsCancellationRequested)
        {
            try
            {
                await foreach (var report in _gateway.StreamExecutionReportsAsync(ct).ConfigureAwait(false))
                {
                    retryDelay = InitialReportStreamRetryDelay;
                    await ProcessGatewayReportAsync(report, ct).ConfigureAwait(false);
                }

                // Stream completed normally: the gateway has no more asynchronous reports
                // (synchronous-only gateways complete immediately; live gateways complete
                // on their own disposal).
                return;
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "Execution report stream from gateway {GatewayId} faulted; retrying in {RetryDelay}",
                    _gateway.GatewayId, retryDelay);

                try
                {
                    await Task.Delay(retryDelay, ct).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    return;
                }

                retryDelay = retryDelay >= MaxReportStreamRetryDelay
                    ? MaxReportStreamRetryDelay
                    : TimeSpan.FromTicks(Math.Min(retryDelay.Ticks * 2, MaxReportStreamRetryDelay.Ticks));
            }
        }
    }

    private async Task ReplayRetainedAccountingHandoffsAsync(CancellationToken ct)
    {
        if (_tradeEventPublisher is null || _tradeFillHandoffFailureStore is null)
            return;

        IReadOnlyList<RetainedTradeFillHandoffFailure> retained;
        try
        {
            retained = await _tradeFillHandoffFailureStore.LoadPendingAsync(ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            return;
        }
        catch (Exception ex)
        {
            _logger.LogCritical(ex, "Could not load retained accounting handoff failures");
            return;
        }

        foreach (var failure in retained)
        {
            if (ct.IsCancellationRequested)
                return;
            try
            {
                await _tradeEventPublisher.PublishAsync(failure.TradeEvent).ConfigureAwait(false);
                await _tradeFillHandoffFailureStore
                    .MarkReplayedAsync(failure.TradeEvent.FillId, CancellationToken.None)
                    .ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogCritical(
                    ex,
                    "Retained accounting handoff replay failed for fill {FillId}; the durable failure record remains pending",
                    failure.TradeEvent.FillId);
                try
                {
                    await _tradeFillHandoffFailureStore
                        .RetainAsync(failure.TradeEvent, ex.Message, CancellationToken.None)
                        .ConfigureAwait(false);
                }
                catch (Exception retentionException)
                {
                    _logger.LogCritical(
                        retentionException,
                        "Could not update retained accounting handoff failure for fill {FillId}",
                        failure.TradeEvent.FillId);
                }
            }
        }
    }

    private async Task<bool> RetainAccountingHandoffFailureAsync(
        TradeExecutedEvent tradeEvent,
        Exception publisherFailure,
        CancellationToken ct)
    {
        if (_tradeFillHandoffFailureStore is null)
        {
            _logger.LogCritical(
                publisherFailure,
                "Accounting publisher rejected fill {FillId} and no durable OMS handoff-failure store is configured",
                tradeEvent.FillId);
            return false;
        }

        try
        {
            await _tradeFillHandoffFailureStore
                .RetainAsync(tradeEvent, publisherFailure.Message, ct)
                .ConfigureAwait(false);
            return true;
        }
        catch (Exception retentionFailure)
        {
            _logger.LogCritical(
                retentionFailure,
                "Accounting publisher and OMS failure-store retention both failed for fill {FillId}; the order path will fail closed",
                tradeEvent.FillId);
            return false;
        }
    }

    private async Task ProcessGatewayReportAsync(ExecutionReport report, CancellationToken ct)
    {
        var orderId = report.ClientOrderId ?? report.OrderId;

        OrderState? updatedState = null;
        var previousFilledQuantity = 0m;
        while (!string.IsNullOrWhiteSpace(orderId) && _orders.TryGetValue(orderId, out var existing))
        {
            var merged = ApplyReport(existing, report);
            if (_orders.TryUpdate(orderId, merged, existing))
            {
                previousFilledQuantity = existing.FilledQuantity;
                updatedState = merged;
                break;
            }
        }

        if (updatedState is not null)
        {
            await RecordSessionOrderUpdateAsync(ResolveSessionId(orderId!), updatedState, ct).ConfigureAwait(false);
        }
        else
        {
            _logger.LogWarning(
                "Received execution report for order {OrderId} ({ReportType}, {Status}) not tracked by this OMS",
                report.OrderId, report.ReportType, report.OrderStatus);
        }

        if (report.OrderStatus is OrderStatus.Filled or OrderStatus.PartiallyFilled)
        {
            var sessionId = string.IsNullOrWhiteSpace(orderId) ? null : ResolveSessionId(orderId);
            await ProcessFillReportAsync(sessionId, report, previousFilledQuantity, ct).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Single funnel for fill side-effects, shared by the synchronous submit path and the
    /// asynchronous report pump. Gateways derived from <c>BaseBrokerageGateway</c> publish
    /// the submit ack on the report stream as well, so fills are deduplicated to avoid
    /// double-applying them to the portfolio.
    /// </summary>
    private async Task ProcessFillReportAsync(
        string? sessionId,
        ExecutionReport report,
        decimal previousFilledQuantity,
        CancellationToken ct)
    {
        var orderId = report.ClientOrderId ?? report.OrderId;
        if (!_fillProcessing.TryGetValue(report, out var progress))
        {
            // Gateways report FilledQuantity cumulatively (e.g. IB CumulativeQuantity,
            // Alpaca filled_qty) while fill consumers treat each report as a discrete
            // trade, so only the increment since the last tracked fill may be forwarded.
            var incrementQuantity = report.FilledQuantity - previousFilledQuantity;
            if (incrementQuantity <= 0m)
                return;

            var fillIncrement = incrementQuantity == report.FilledQuantity
                ? report
                : report with { FilledQuantity = incrementQuantity };
            progress = _fillProcessing.GetOrAdd(
                report,
                _ => new FillProcessingProgress(
                    fillIncrement,
                    report.FilledQuantity,
                    !string.IsNullOrWhiteSpace(orderId) && _orders.ContainsKey(orderId)));
        }

        await progress.Gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (progress.IsComplete)
                return;

            var fillIncrement = progress.FillIncrement;

            if (!progress.PortfolioApplied)
            {
                var realisedPnlBefore = _portfolioState?.RealisedPnl ?? 0m;

                // Only fills for orders this OMS placed may mutate the paper portfolio;
                // stream reports for external/untracked orders are still published below.
                if (_portfolioState is PaperTradingPortfolio paperPortfolio
                    && progress.IsTrackedOrder)
                {
                    paperPortfolio.ApplyFill(fillIncrement);
                    progress.RealizedPnl = paperPortfolio.RealisedPnl - realisedPnlBefore;
                }

                progress.NewCash = _portfolioState?.Cash ?? 0m;
                progress.PortfolioApplied = true;
            }

            if (!progress.TradeEventPublished)
            {
                if (_tradeEventPublisher is not null && progress.IsTrackedOrder)
                {
                    progress.TradeEvent ??= CreateTradeExecutedEvent(
                        fillIncrement,
                        progress.CumulativeFilledQuantity,
                        progress.RealizedPnl,
                        progress.NewCash,
                        ResolveFinancialAccountId(orderId));
                    try
                    {
                        await _tradeEventPublisher.PublishAsync(progress.TradeEvent).ConfigureAwait(false);
                    }
                    catch (Exception ex)
                    {
                        var wasRetained = await RetainAccountingHandoffFailureAsync(
                                progress.TradeEvent,
                                ex,
                                CancellationToken.None)
                            .ConfigureAwait(false);
                        throw new AccountingHandoffException(progress.TradeEvent, wasRetained, ex);
                    }
                }

                progress.TradeEventPublished = true;
            }

            if (!progress.SessionRecorded)
            {
                await RecordSessionFillAsync(sessionId, fillIncrement, ct).ConfigureAwait(false);
                progress.SessionRecorded = true;
            }

            if (!progress.ExecutionReportPublished)
            {
                // FullMode.Wait must be observed asynchronously. TryWrite here silently lost
                // accepted fills whenever subscribers lagged behind the configured capacity.
                await _executionChannel.Writer.WriteAsync(fillIncrement, ct).ConfigureAwait(false);
                progress.ExecutionReportPublished = true;
            }

            progress.IsComplete = true;
            TrackCompletedFill(report);
        }
        catch (AccountingHandoffException)
        {
            throw;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            // Preserve the per-side-effect progress object. An identical gateway replay can
            // resume at the failed step without applying portfolio/session/publication twice.
            _logger.LogError(
                ex,
                "Fill processing paused for order {OrderId} ({Symbol} {FilledQuantity} @ {FillPrice}); a replay will resume the unfinished side effects",
                progress.FillIncrement.OrderId,
                progress.FillIncrement.Symbol,
                progress.FillIncrement.FilledQuantity,
                progress.FillIncrement.FillPrice);
        }
        finally
        {
            progress.Gate.Release();
        }
    }

    private void TrackCompletedFill(ExecutionReport report)
    {
        _completedFillReportOrder.Enqueue(report);
        while (_completedFillReportOrder.Count > MaxTrackedFillReports
            && _completedFillReportOrder.TryDequeue(out var oldest))
        {
            if (_fillProcessing.TryGetValue(oldest, out var progress) && progress.IsComplete)
                _fillProcessing.TryRemove(oldest, out _);
        }
    }

    private string? ResolveFinancialAccountId(string? orderId)
        => !string.IsNullOrWhiteSpace(orderId)
            && _orderFinancialAccountIds.TryGetValue(orderId, out var accountId)
                ? accountId
                : null;

    private static TradeExecutedEvent CreateTradeExecutedEvent(
        ExecutionReport fillIncrement,
        decimal cumulativeFilledQuantity,
        decimal realizedPnl,
        decimal newCash,
        string? financialAccountId)
    {
        if (fillIncrement.FillPrice is not { } fillPrice)
        {
            throw new InvalidOperationException(
                $"Fill report '{fillIncrement.OrderId}' for '{fillIncrement.Symbol}' has no execution price.");
        }

        // STABILITY CONTRACT: the deterministic fillId below is derived from this exact field
        // list, order, and encoding. Ledger entries already posted for a fill are keyed by it,
        // so changing any part of the identity (adding/removing/reordering fields, formats)
        // silently changes fill identity and re-posts fills after a restart. Do not modify
        // without a migration plan for previously posted ledger entries.
        var canonicalIdentity = string.Join(
            "|",
            EncodeIdentityPart(fillIncrement.OrderId),
            EncodeIdentityPart(fillIncrement.ClientOrderId),
            EncodeIdentityPart(fillIncrement.GatewayOrderId),
            EncodeIdentityPart(fillIncrement.Symbol),
            ((int)fillIncrement.Side).ToString(CultureInfo.InvariantCulture),
            fillIncrement.FilledQuantity.ToString(CultureInfo.InvariantCulture),
            cumulativeFilledQuantity.ToString(CultureInfo.InvariantCulture),
            fillPrice.ToString(CultureInfo.InvariantCulture),
            (fillIncrement.Commission ?? 0m).ToString(CultureInfo.InvariantCulture),
            fillIncrement.Timestamp.ToUniversalTime().Ticks.ToString(CultureInfo.InvariantCulture),
            EncodeIdentityPart(financialAccountId));
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(canonicalIdentity));
        var fillId = new Guid(hash.AsSpan(0, 16));

        return new TradeExecutedEvent(
            fillId,
            fillIncrement.ClientOrderId ?? fillIncrement.OrderId,
            fillIncrement.Symbol,
            fillIncrement.Side,
            fillIncrement.FilledQuantity,
            fillPrice,
            fillIncrement.Commission ?? 0m,
            realizedPnl,
            newCash,
            fillIncrement.Timestamp,
            financialAccountId);
    }

    private static string EncodeIdentityPart(string? value)
        => value is null
            ? "-"
            : Convert.ToBase64String(Encoding.UTF8.GetBytes(value));

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
        _orders[orderId] = rejectedState;
        TrimRetainedOrdersIfNeeded();
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
            _orderFinancialAccountIds.TryRemove(removableOrderId, out _);
        }
    }

    private sealed class OperationLease(OrderManagementSystem owner) : IDisposable
    {
        private OrderManagementSystem? _owner = owner;

        public void Dispose()
            => Interlocked.Exchange(ref _owner, null)?.ExitOperation();
    }

    private sealed class FillProcessingProgress(
        ExecutionReport fillIncrement,
        decimal cumulativeFilledQuantity,
        bool isTrackedOrder)
    {
        public SemaphoreSlim Gate { get; } = new(1, 1);
        public ExecutionReport FillIncrement { get; } = fillIncrement;
        public decimal CumulativeFilledQuantity { get; } = cumulativeFilledQuantity;
        public bool IsTrackedOrder { get; } = isTrackedOrder;
        public TradeExecutedEvent? TradeEvent { get; set; }
        public decimal RealizedPnl { get; set; }
        public decimal NewCash { get; set; }
        public bool PortfolioApplied { get; set; }
        public bool TradeEventPublished { get; set; }
        public bool SessionRecorded { get; set; }
        public bool ExecutionReportPublished { get; set; }
        public volatile bool IsComplete;
    }

    private sealed class AccountingHandoffException : Exception
    {
        public AccountingHandoffException(
            TradeExecutedEvent tradeEvent,
            bool wasRetained,
            Exception innerException)
            : base(
                $"Execution fill '{tradeEvent.FillId:D}' was accepted by the broker but its accounting handoff failed"
                + (wasRetained
                    ? "; the event is durably retained for restart replay."
                    : "; no durable fallback accepted the event and the order result is fail-closed."),
                innerException)
        {
            WasRetained = wasRetained;
        }

        public bool WasRetained { get; }
    }
}
