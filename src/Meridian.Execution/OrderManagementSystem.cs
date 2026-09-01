using System.Collections.Concurrent;
using System.Globalization;
using System.Runtime.ExceptionServices;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Channels;
using Meridian.Application.Pipeline;
using Meridian.Execution.Events;
using Meridian.Execution.Logging;
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
public sealed partial class OrderManagementSystem : IOrderManager, IDisposable, IAsyncDisposable
{
    private readonly ConcurrentDictionary<string, OrderState> _orders = new();
    private readonly IExecutionGateway _gateway;
    private readonly IRiskValidator? _riskValidator;
    private readonly ISecurityMasterGate? _securityMasterGate;
    private readonly ExecutionOperatorControlService? _operatorControls;
    private readonly ILiveOrderReadinessGate? _liveOrderReadinessGate;
    private readonly ExecutionAuditTrailService? _auditTrail;
    private readonly RiskEscalationQueueService? _escalationQueue;
    private readonly Meridian.Execution.Models.IPortfolioState? _portfolioState;
    private readonly PaperSessionPersistenceService? _sessionPersistence;
    private readonly BrokerageConfiguration? _brokerageConfiguration;
    private readonly OrderManagementSystemOptions _options;
    private readonly ExecutionMode _gatewayExecutionMode;
    private readonly INotionalOrderSizingGateway? _notionalSizingGateway;
    private readonly IFaceValueOrderSizingGateway? _faceValueOrderSizingGateway;
    private readonly ILogger<OrderManagementSystem> _logger;
    private readonly Channel<ExecutionReport> _executionChannel;
    private readonly ConcurrentDictionary<string, string> _orderSessionIds = new(StringComparer.OrdinalIgnoreCase);
    // Broker-assigned identifiers are a separate namespace from the client ids that key _orders.
    // Retaining the proven mapping prevents a UUID-shaped client id from ever being treated as a
    // broker id merely because the values happen to collide.
    private readonly ConcurrentDictionary<string, string> _orderBrokerIds = new(StringComparer.Ordinal);
    // Serializes pre-trade risk validation with the registration that reserves the order's
    // exposure. Without it, concurrent submissions each evaluate against the same
    // pre-order book and can collectively breach a ceiling none of them breaches alone.
    private readonly SemaphoreSlim _preTradeReservationGate = new(1, 1);
    // Submissions past the operator-control gate but not yet acknowledged by the gateway, keyed by
    // order id. The kill-switch sweep waits for these before it snapshots the book: an order that
    // passed the gate a moment before a breaker trip would otherwise reach the broker after the
    // sweep had observed an empty book, and be reported as nothing rather than as working.
    private readonly ConcurrentDictionary<string, TaskCompletionSource> _inFlightDispatches = new(StringComparer.Ordinal);
    // Client order ids held by orders parked for governed approval, mapped to the
    // escalation that owns them. The tracked state is terminal, so without this the id
    // would be reclaimable while the approval is still live.
    private readonly ConcurrentDictionary<string, string> _parkedOrderIds = new(StringComparer.OrdinalIgnoreCase);
    private readonly CancellationTokenSource _reportPumpCts = new();
    private readonly Task _reportPumpTask;
    private readonly ITradeEventPublisher? _tradeEventPublisher;
    private readonly ITradeFillHandoffFailureStore? _tradeFillHandoffFailureStore;
    private readonly Task _handoffRecoveryTask;
    private readonly ConcurrentDictionary<string, string> _orderFinancialAccountIds = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<ExecutionReport, FillProcessingProgress> _fillProcessing = new();
    // Reports whose fill must already reserve exposure but whose precise increment is not
    // tracked yet. Held only across the window in which the order goes terminal.
    private readonly ConcurrentDictionary<ExecutionReport, ExecutionReport> _pendingFillReservations = new();
    // Contract multiplier per order id, for derivative fills.
    private readonly ConcurrentDictionary<string, decimal> _orderContractMultipliers = new(StringComparer.OrdinalIgnoreCase);
    // Order ids the active gateway routes as face value priced as a percentage of par, so the
    // fill booking path scales the clean price exactly as the pre-trade rails valued the order.
    private readonly ConcurrentDictionary<string, bool> _orderFaceValueSizing = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentQueue<ExecutionReport> _completedFillReportOrder = new();
    private readonly object _disposeSync = new();
    private long _droppedExecutionReports;
    private Task? _disposeTask;
    private TaskCompletionSource? _operationsDrained;
    private Exception? _terminalReportPumpFailure;
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
        ITradeFillHandoffFailureStore? tradeFillHandoffFailureStore = null,
        RiskEscalationQueueService? escalationQueue = null)
    {
        _gateway = gateway ?? throw new ArgumentNullException(nameof(gateway));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _riskValidator = riskValidator;
        _securityMasterGate = securityMasterGate;
        _operatorControls = operatorControls;

        // The close-only exception needs to see committed reductions, and only the OMS holds the
        // open book. Without this the gate cannot establish that a close is within the position and
        // refuses every one, so a halted desk could not flatten at all.
        if (_operatorControls is not null)
        {
            _operatorControls.WorkingReductionQuantityProbe = ResolveWorkingReductionQuantity;
        }
        _liveOrderReadinessGate = liveOrderReadinessGate;
        _auditTrail = auditTrail;
        _escalationQueue = escalationQueue;
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
            && tradeFillHandoffFailureStore is not null)
        {
            var publisherScopeIdentity = scopedPublisher.ScopeIdentity.Validate();
            var failureStoreScopeIdentity = tradeFillHandoffFailureStore.ScopeIdentity.Validate();
            var scopeIdentityMatches = publisherScopeIdentity.IsExact && failureStoreScopeIdentity.IsExact
                ? publisherScopeIdentity == failureStoreScopeIdentity
                : !publisherScopeIdentity.IsExact
                  && !failureStoreScopeIdentity.IsExact
                  && string.Equals(
                      publisherScopeIdentity.PostingScope,
                      failureStoreScopeIdentity.PostingScope,
                      StringComparison.Ordinal);
            if (!scopeIdentityMatches)
            {
                throw new ArgumentException(
                    $"Accounting publisher scope identity '{publisherScopeIdentity}' does not match handoff-failure store scope identity '{failureStoreScopeIdentity}'.",
                    nameof(tradeFillHandoffFailureStore));
            }
        }
        _options = options ?? new OrderManagementSystemOptions();
        if (_options.RequireProductionSafetyDependencies)
        {
            if (_riskValidator is null)
                throw new InvalidOperationException("Production OMS requires a pre-trade risk validator.");
            if (_portfolioState is null)
                throw new InvalidOperationException("Production OMS requires authoritative portfolio position state.");
            if (_operatorControls is null)
                throw new InvalidOperationException("Production OMS requires durable operator controls.");
        }
        _gatewayExecutionMode = gateway is IExecutionGatewayModeProvider modeProvider
            ? modeProvider.ExecutionMode
            : BrokerageOrderPlacementGate.ResolveExecutionMode(brokerageConfiguration, gateway.GatewayId);
        // Only a gateway that advertises native notional sizing routes the metadata dollars,
        // and it advertises that per order: an adapter can honour it for one asset class and
        // route quantity for another. Everything else routes Quantity, so measuring those
        // orders at the metadata amount hands the rails a number the broker never sees.
        _notionalSizingGateway = gateway as INotionalOrderSizingGateway;
        _faceValueOrderSizingGateway = gateway as IFaceValueOrderSizingGateway;
        // ExecutionReports is a best-effort observer stream: order state, session fill history,
        // and the durable accounting handoff own correctness. The previous FullMode.Wait made a
        // slow (or absent — there is no production reader today) subscriber block WriteAsync on
        // the fill path, stalling the report pump and submit callers once the channel filled.
        // DropOldest keeps fills flowing; drops are counted and logged.
        var executionPolicy = new EventPipelinePolicy(
            Capacity: _options.ValidatedExecutionChannelCapacity,
            FullMode: BoundedChannelFullMode.DropOldest,
            EnableMetrics: false);
        _executionChannel = Channel.CreateBounded<ExecutionReport>(
            executionPolicy.ToBoundedOptions(singleReader: true, singleWriter: false),
            dropped =>
            {
                var totalDropped = Interlocked.Increment(ref _droppedExecutionReports);
                _logger.LogWarning(
                    "Execution-report observer channel is full; dropped oldest report for order {OrderId} ({ReportType}). Total dropped: {DroppedTotal}",
                    dropped.OrderId, dropped.ReportType, totalDropped);
            });

        RehydrateParkedOrderReservations();

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
        // Stamp the generated id before anything downstream retains the request: a parked
        // escalation keeps the exact request it was given, and releasing one whose client
        // order id was still null would route under a second, unrelated id that the
        // submitter, audits, and cancellation lookups would never see.
        request = request.ClientOrderId is null ? request with { ClientOrderId = orderId } : request;
        // Internal risk-probe metadata belongs to the amendment probe alone. A caller that
        // set the evaluation-only flag would get a parked response carrying no escalation
        // id — one no operator could ever resolve — and one that set an incremental
        // notional would declare their own order to add no exposure at all.
        request = StripInternalRiskMetadata(request);
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

        var safeRequest = SanitizeAndSnapshotRequest(request, orderId);

        // A duplicate client order id must never reach the state table or the gateway: every
        // downstream write in this method (including gate rejections) keys on orderId, so a
        // replayed or colliding id would overwrite the tracked state (fills, status history)
        // of the order already working under that id.
        if (request.ClientOrderId is not null
            && ((_orders.TryGetValue(orderId, out var existingOrder) && !IsTerminalStatus(existingOrder.Status))
                || IsReservedByLiveEscalation(orderId, request)))
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

        if (CarriesUnroutableNotionalMetadata(safeRequest))
        {
            return await RejectOrderAsync(
                orderId,
                safeRequest,
                actor,
                brokerName,
                runId,
                correlationId,
                UnroutableNotionalMetadataReason(brokerName),
                sessionId,
                ct,
                rejectionSource: "notional metadata gate")
                .ConfigureAwait(false);
        }

        var usesFaceValuePercentageOfPar = ResolvesFaceValuePercentageOfPar(safeRequest);
        var riskRequest = StampResolvedOrderSizing(safeRequest, usesFaceValuePercentageOfPar);

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

        IReadOnlyList<string>? riskWarnings = null;
        string? consumedApprovalId = null;

        // Capacity a stateful rule reserved to admit this order, owned by this method from the
        // moment the gate approves until exactly one settlement below. Null when no validator ran
        // or the order never passed the gate; a non-approved decision releases its own capacity
        // inside the validator, so only the approved path reaches here holding anything.
        RiskValidationResult? riskDecision = null;
        var dispatchAttempted = false;
        OrderState orderState;

        // Held from the moment this order becomes visible in the tracked table until the
        // gateway has acknowledged (or refused) it, so a kill-switch sweep started in between
        // waits for the acknowledgement instead of racing it. Disposed on every exit path.
        using var dispatchLease = new DispatchLease(this);

        // Pre-trade risk check. Validation and the registration that reserves this order's
        // exposure happen under one gate so a concurrent order cannot slip through against
        // the same pre-order snapshot.
        await _preTradeReservationGate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (_riskValidator is not null)
            {
                var riskResult = await _riskValidator.ValidateOrderAsync(riskRequest, ct).ConfigureAwait(false);
                riskDecision = riskResult;
                if (!riskResult.IsApproved)
                {
                    // Settled here rather than relying on CompositeRiskValidator releasing its own
                    // capacity before returning. The public contract says a normal return transfers
                    // ownership and the caller settles every path, so an alternate IRiskValidator
                    // may hand back a rejected result still holding slots. Rolling back is safe
                    // either way: settlement is idempotent, and the built-in validator returns an
                    // empty set on this path.
                    SettleRiskReservations(riskResult, commit: false, orderId);

                    // A parked escalation is not a rejection: the order awaits a governed
                    // approval decision, and the result says so in a typed way instead of
                    // hiding the queue entry inside a rejection string.
                    if (riskResult.RequiresApproval)
                    {
                        return await ParkOrderForApprovalAsync(
                            orderId,
                            safeRequest,
                            actor,
                            brokerName,
                            runId,
                            correlationId,
                            riskResult,
                            sessionId,
                            ct).ConfigureAwait(false);
                    }

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
                        rejectionSource: "risk validator",
                        metadata: BuildRiskDecisionAuditMetadata(riskResult),
                        riskWarnings: riskResult.Warnings.Count > 0 ? riskResult.Warnings : null,
                        riskDecision: riskResult.ToSummary())
                        .ConfigureAwait(false);
                }

                consumedApprovalId = riskResult.ConsumedApprovalId;

                // Non-blocking flags (warning-severity breaches, observe bands) must survive
                // an approved order: carry them on the result and retain them durably.
                if (riskResult.Warnings.Count > 0)
                {
                    riskWarnings = riskResult.Warnings;
                    await RecordRiskWarningsAsync(
                        orderId,
                        safeRequest,
                        actor,
                        brokerName,
                        runId,
                        correlationId,
                        riskWarnings,
                        ct).ConfigureAwait(false);
                }
            }

            // A derivative's contract multiplier must reach both the working-order reserve
            // and the fill: every exposure rail measures the position it opens.
            var orderMultiplier = ResolveContractMultiplier(safeRequest);
            orderState = new OrderState
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
                StrategyId = safeRequest.StrategyId,
                FundAccountId = safeRequest.FundAccountId,
                // Broker-native notional orders route dollars and discard quantity; the
                // exposure reserve for this working order must value what actually routes.
                RoutedNotional = BrokerNotionalMetadata.TryRead(safeRequest.Metadata, safeRequest.Quantity),
                UsesFaceValuePercentageOfPar = usesFaceValuePercentageOfPar,
                // A working option order reserves contract notional, not share notional:
                // 100 contracts at a $5 limit hold back $50k, not $500. The derivative
                // identity travels too, so the reserve and any amendment are valued exactly
                // as the pre-trade gate valued the order.
                ContractMultiplier = orderMultiplier,
                OptionContract = safeRequest.OptionContract,
                // Snapshot the legs. An in-process caller can hand in a mutable list and
                // clear or edit it after submission returns, while the broker keeps working
                // the combination it received — the working-order reserve would then value
                // a different leg count than actually routed. The escalation queue already
                // copies legs for the same reason.
                Legs = safeRequest.Legs is { Count: > 0 } submittedLegs
                    ? [.. submittedLegs]
                    : safeRequest.Legs
            };

            // Taken after validation rather than before the gate: a critical rule can trip the
            // breaker from inside ValidateOrderAsync, and the sweep that trip runs waits on
            // these leases. A lease held by the order still being validated would make that
            // sweep wait on itself until the settle window lapsed.
            dispatchLease.Begin(orderId);

            if (!TryRegisterOrder(orderId, orderState))
            {
                // Lost a race with a concurrent submission that claimed the same client order id
                // after the guard above ran; the winner's state must survive untouched.
                //
                // Nothing from this submission routed, so both the reserved capacity and any
                // governed release it consumed go back. Leaving the approval retired would
                // permanently discard an operator decision over a race the submitter did not cause.
                SettleRiskReservations(riskDecision, commit: false, orderId);
                RestoreConsumedApprovals(consumedApprovalId, "a duplicate client order id race", orderId);
                return await RejectDuplicateClientOrderIdAsync(
                    orderId,
                    safeRequest,
                    actor,
                    brokerName,
                    runId,
                    correlationId,
                    ct).ConfigureAwait(false);
            }

            if (orderMultiplier > 1m)
            {
                _orderContractMultipliers[orderId] = orderMultiplier;
            }
            else
            {
                _orderContractMultipliers.TryRemove(orderId, out _);
            }

            if (usesFaceValuePercentageOfPar)
            {
                _orderFaceValueSizing[orderId] = true;
            }
            else
            {
                // A terminal client-order id may be reused. Do not let a prior bond order's
                // price scaling leak into fills for an equity replacement order.
                _orderFaceValueSizing.TryRemove(orderId, out _);
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

            // The order now holds its own id in the tracked table, so an escalation
            // reservation on it has nothing left to protect.
            ReleaseParkedOrderReservation(orderId);
        }
        catch
        {
            // Everything between the gate approving and the gateway call is bookkeeping that can
            // throw — warning retention, audit writes, a faulting logger provider. Without this,
            // such a failure unwound releasing only the semaphore and left the reserved rate slot
            // held for the process's lifetime, for an order that was never even registered.
            //
            // Rollback, not commit: nothing has reached the gateway at this point, so unlike the
            // ambiguous post-dispatch case there is nothing to over-count for.
            SettleRiskReservations(riskDecision, commit: false, orderId);
            throw;
        }
        finally
        {
            // The order is registered (or the submission has already returned), so its
            // exposure is now visible to the next validation.
            _preTradeReservationGate.Release();
        }

        // The operator controls are consulted again at the point of dispatch, not only at the
        // gate above. Everything between the two -- readiness, risk validation, reservation --
        // takes time, and a breaker opened during that time has already run its cancel-all
        // sweep against a book this order was not yet in. Passing the gate a moment before the
        // trip is not permission to reach the broker a moment after it.
        if (_operatorControls is not null)
        {
            var dispatchControlRequest = requiresLiveOrderReadinessGate ? safeRequest : request;
            var dispatchDecision = _operatorControls.EvaluateOrder(dispatchControlRequest, _portfolioState, runId);
            if (!dispatchDecision.IsApproved)
            {
                // Nothing reached the gateway, so the reserved capacity and any governed release
                // this order consumed go back exactly as they do for a gate rejection.
                SettleRiskReservations(riskDecision, commit: false, orderId);
                RestoreConsumedApprovals(consumedApprovalId, "an operator control that closed before dispatch", orderId);
                return await RejectRegisteredOrderBeforeDispatchAsync(
                    orderId,
                    orderState,
                    safeRequest,
                    actor,
                    brokerName,
                    runId,
                    correlationId,
                    dispatchDecision,
                    sessionId,
                    riskWarnings,
                    riskDecision,
                    ct).ConfigureAwait(false);
            }
        }

        try
        {
            ct.ThrowIfCancellationRequested();
            dispatchAttempted = true;
            var report = await _gateway.SubmitOrderAsync(safeRequest with { ClientOrderId = orderId }, ct)
                .ConfigureAwait(false);
            RememberBrokerOrderId(orderId, report);

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
                LogSanitizer.Sanitize(orderId), LogSanitizer.Sanitize(safeRequest.Symbol), safeRequest.Side, safeRequest.Quantity, updatedState.Status);

            // A bracket/OCO submission spawns broker-side child legs with their own order ids.
            // Registering them here makes their execution reports land on tracked state instead
            // of being dropped as "not tracked", and puts them in the book a kill-switch sweep
            // enumerates.
            RegisterGatewayChildOrders(orderId, updatedState, report);

            // Once the broker has acknowledged a fill, its accounting handoff is authoritative.
            // Caller cancellation, paper-session persistence, or audit failures must never run
            // first and leave a broker fill without durable posting/fallback state.
            if (report.OrderStatus is OrderStatus.Filled or OrderStatus.PartiallyFilled)
            {
                await ProcessFillReportAsync(
                        sessionId,
                        report,
                        previousFilledQuantity)
                    .ConfigureAwait(false);
            }

            try
            {
                await RecordSessionOrderUpdateAsync(sessionId, updatedState, ct).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "Order {OrderId} was accepted by gateway {GatewayId}, but its paper-session order update could not be recorded",
                    LogSanitizer.Sanitize(orderId),
                    _gateway.GatewayId);
            }

            // Record submitted order in the audit trail when connected
            if (_auditTrail is not null)
            {
                try
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
                catch (Exception ex)
                {
                    _logger.LogError(
                        ex,
                        "Order {OrderId} was accepted by gateway {GatewayId}, but its submission audit could not be recorded",
                        LogSanitizer.Sanitize(orderId),
                        _gateway.GatewayId);
                }
            }

            // Judged from the merged state rather than the raw acknowledgement. The report pump
            // can apply a fill before a later rejected ack is processed here, and ApplyReport
            // preserves that terminal Filled state. Reporting failure over a filled order would
            // invite the caller to retry a submission that already executed — and because
            // terminal client order ids are reusable, the retry would be accepted as a second
            // order rather than rejected as a duplicate.
            var routed = updatedState.Status is not OrderStatus.Rejected || updatedState.FilledQuantity > 0m;

            if (!routed)
            {
                // The broker refused it: nothing routed, so consumed approvals must be
                // retryable once the broker-side condition clears. This mirrors the
                // exception path below — a normal rejected report is just as final.
                RestoreConsumedApprovals(consumedApprovalId, "a gateway rejection", orderId);
            }

            SettleRiskReservations(riskDecision, commit: routed, orderId);

            return new OrderResult
            {
                Success = routed,
                OrderId = orderId,
                OrderState = updatedState,
                ErrorMessage = routed ? null : report.RejectReason,
                RiskWarnings = riskWarnings,
                RiskDecision = riskDecision?.ToSummary()
            };
        }
        catch (AccountingHandoffException ex)
        {
            var filledState = _orders.TryGetValue(orderId, out var retainedState)
                ? retainedState
                : orderState;
            SettleRiskReservations(riskDecision, commit: true, orderId);
            _logger.LogCritical(
                ex,
                "Order {OrderId} filled but accounting handoff failed; retained={HandoffRetained}",
                LogSanitizer.Sanitize(orderId),
                ex.WasRetained);
            try
            {
                await RecordOrderLifecycleAuditAsync(
                        action: "AccountingHandoffFailed",
                        outcome: "AttentionRequired",
                        orderId: orderId,
                        state: filledState,
                        report: null,
                        message: ex.Message,
                        ct: CancellationToken.None)
                    .ConfigureAwait(false);
            }
            catch (Exception auditFailure)
            {
                _logger.LogCritical(
                    auditFailure,
                    "Accounting handoff failure for order {OrderId} could not be appended to the execution audit trail",
                    LogSanitizer.Sanitize(orderId));
            }

            return new OrderResult
            {
                Success = false,
                OrderId = orderId,
                ErrorMessage = ex.Message,
                OrderState = filledState,
                RiskWarnings = riskWarnings,
                RiskDecision = riskDecision?.ToSummary()
            };
        }
        catch (Exception ex)
        {
            // Settled before anything fallible runs. A throwing logger provider on this line
            // would otherwise skip settlement entirely and hold the slot for the process's
            // lifetime, eventually blocking every later order.
            //
            // Committed when dispatch was attempted: the submit is ambiguous and the order may
            // still execute, so a rate limiter has to over-count. Under-counting would let a
            // runaway algorithm bypass the ceiling by producing ambiguous submissions, which is
            // the behaviour the ceiling exists to stop. A failure before the gateway call is
            // provably pre-dispatch and releases the slot.
            SettleRiskReservations(riskDecision, commit: dispatchAttempted, orderId);

            _logger.LogError(ex, "Failed to submit order {OrderId} for {Symbol}", LogSanitizer.Sanitize(orderId), LogSanitizer.Sanitize(safeRequest.Symbol));

            // Re-armed only when the failure provably predates dispatch. After dispatch the
            // submission is ambiguous and the order may still execute, so restoring the one-shot
            // release would let a retry route a second approved order against one decision — the
            // same ambiguity that makes the rate slot commit rather than roll back. An operator can
            // re-approve; a duplicate execution cannot be taken back.
            if (!dispatchAttempted)
            {
                RestoreConsumedApprovals(consumedApprovalId, "a submission failure before dispatch", orderId);
            }

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
                    Message: ex.Message,
                    // Stamped only when dispatch was attempted, because only then did the slot
                    // stay consumed. Marking a pre-dispatch failure would tell the status
                    // projection to count capacity the throttle had already released.
                    Reason: dispatchAttempted ? AmbiguousSubmissionReason : null),
                    // CancellationToken.None: a cancelled caller must not erase the record of
                    // capacity the throttle is still holding on their behalf.
                    CancellationToken.None).ConfigureAwait(false);
            }

            return new OrderResult
            {
                Success = false,
                OrderId = orderId,
                ErrorMessage = ex.Message,
                OrderState = rejectedState,
                RiskWarnings = riskWarnings,
                RiskDecision = riskDecision?.ToSummary()
            };
        }
    }

    /// <summary>
    /// Audit reason marking the one <c>OrderRejected</c> entry whose rate slot was <em>not</em>
    /// released: a gateway submission that threw after dispatch was attempted. The order may still
    /// execute, so the throttle over-counts rather than under-counts, and the status projection has
    /// to count this entry when it falls back to reconstructing usage from audit history.
    /// </summary>
    public const string AmbiguousSubmissionReason = "AmbiguousSubmission";

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

        // A quantity increase or any supplied limit/stop price is a new risk decision. Notional
        // increases are not the only dangerous direction: lowering a sell limit or a buy stop can
        // move an accepted order straight through the market. Validation and the amended
        // reservation run under the same gate as placement so neither exposure nor price controls
        // can be bypassed through modification.
        OrderState? speculativeReservation = null;
        RiskValidationResult? amendmentDecision = null;
        var amendmentDispatchAttempted = false;
        IReadOnlyList<string>? amendmentWarnings = null;
        if (RequiresRiskRevalidation(state, modification))
        {
            var gate = await ReserveAmendedExposureAsync(orderId, state, modification, ct).ConfigureAwait(false);
            amendmentWarnings = gate.Warnings;
            if (gate.Refusal is not null)
            {
                await RecordOrderLifecycleAuditAsync(
                    action: "OrderModifyRejected",
                    outcome: "Rejected",
                    orderId: orderId,
                    state: state,
                    report: null,
                    message: gate.Refusal,
                    metadata: BuildOrderModificationAuditMetadata(modification, state, report: null, amendmentWarnings, gate.RiskDecision),
                    ct: ct).ConfigureAwait(false);

                return new OrderResult
                {
                    Success = false,
                    OrderId = orderId,
                    OrderState = state,
                    ErrorMessage = gate.Refusal,
                    RiskWarnings = amendmentWarnings
                };
            }

            speculativeReservation = gate.Reservation;
            amendmentDecision = gate.RiskDecision;
        }

        ExecutionReport report;
        try
        {
            ct.ThrowIfCancellationRequested();
            amendmentDispatchAttempted = true;
            report = await _gateway.ModifyOrderAsync(orderId, modification, ct).ConfigureAwait(false);
        }
        catch
        {
            // The gateway never accepted the amendment: the speculative reservation must
            // not outlive the attempt, or the order table and every exposure snapshot
            // would keep reserving a size the broker does not hold.
            //
            // The rate slot follows the placement path's rule rather than the state
            // reservation's: a modify that threw after dispatch may still have reached the venue,
            // so it is committed. Over-counting a rate window is the safe direction.
            // Cancellation after the gateway call began is ambiguous, not pre-dispatch. Gateways
            // that send and then await acknowledgement on the same token — the brokerage adapter
            // does — cancel while the venue already holds the amended exposure. The only proof of
            // pre-dispatch is the check before the call, which throws before the flag is set.
            SettleRiskReservations(amendmentDecision, commit: amendmentDispatchAttempted, orderId);
            RollBackSpeculativeReservation(orderId, speculativeReservation, state);
            throw;
        }

        if (report.OrderStatus is OrderStatus.Rejected)
        {
            // The broker refused the amendment outright, so nothing routed.
            SettleRiskReservations(amendmentDecision, commit: false, orderId);
            RollBackSpeculativeReservation(orderId, speculativeReservation, state);
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
                metadata: BuildOrderModificationAuditMetadata(modification, state, report, amendmentWarnings),
                ct: ct).ConfigureAwait(false);

            return new OrderResult
            {
                Success = false,
                OrderId = orderId,
                OrderState = state,
                ErrorMessage = report.RejectReason ?? "Modify request rejected"
            };
        }

        // The amendment reached the venue and was accepted, so its capacity is spent.
        SettleRiskReservations(amendmentDecision, commit: true, orderId);

        // The gateway report stream is not an authorization channel. Only this locally
        // initiated modification may change the quantity cap, and it may do so only to
        // the quantity the caller requested; never to a gateway-supplied quantity.
        var updated = _orders.AddOrUpdate(
            orderId,
            _ => ApplyReport(state, report, modification.NewQuantity),
            (_, existing) => ApplyReport(existing, report, modification.NewQuantity));
        await RecordSessionOrderUpdateAsync(ResolveSessionId(orderId), updated, ct).ConfigureAwait(false);
        await RecordOrderLifecycleAuditAsync(
            action: "OrderModified",
            outcome: updated.Status.ToString(),
            orderId: orderId,
            state: updated,
            report: report,
            message: report.RejectReason,
            metadata: BuildOrderModificationAuditMetadata(modification, updated, report, amendmentWarnings),
            ct: ct).ConfigureAwait(false);

        // Warnings raised while approving the amendment describe the exposure the caller
        // now holds, so they travel with the accepted result — not only with a refusal.
        return new OrderResult
        {
            Success = true,
            OrderId = orderId,
            OrderState = updated,
            RiskWarnings = amendmentWarnings
        };
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
    /// <summary>
    /// Compatibility bridge for callers that cannot yet use <c>await using</c>. Shutdown is
    /// executed on the thread pool so a single-threaded synchronization context cannot prevent
    /// asynchronous pump continuations from completing.
    /// </summary>
    public void Dispose()
        => Task.Run(
                async () => await DisposeAsync().ConfigureAwait(false),
                CancellationToken.None)
            .GetAwaiter()
            .GetResult();

    /// <summary>
    /// Stops report intake and awaits both the broker-report and retained-handoff pumps before
    /// returning. Dependency injection can therefore dispose the accounting publisher and
    /// failure store only after no OMS task can use them.
    /// </summary>
    public ValueTask DisposeAsync()
    {
        TaskCompletionSource? startGate = null;
        Task disposeTask;
        lock (_disposeSync)
        {
            Interlocked.Exchange(ref _disposeStarted, 1);
            if (_disposeTask is not null)
                return new ValueTask(_disposeTask);

            var operationsDrained = _activeOperations == 0
                ? Task.CompletedTask
                : (_operationsDrained ??= new TaskCompletionSource(
                    TaskCreationOptions.RunContinuationsAsynchronously)).Task;
            startGate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            disposeTask = DisposeCoreAsync(startGate.Task, operationsDrained);
            _disposeTask = disposeTask;
        }

        // Start the asynchronous shutdown only after releasing _disposeSync. This retains the
        // no-context-capture guarantee without invoking cancellation callbacks under the lock.
        startGate!.SetResult();
        return new ValueTask(disposeTask);
    }

    private async Task DisposeCoreAsync(Task startGate, Task operationsDrained)
    {
        // Do not cancel report intake until every operation admitted before disposal has
        // completed. In particular, a broker submit may return a fill whose accounting
        // handoff still needs to reach the primary publisher or durable fallback.
        await startGate.ConfigureAwait(false);
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

        // Every public operation admitted before shutdown and every report already dequeued by
        // the gateway pump has now completed its authoritative side effects. Only now complete
        // subscriber writers so consumers can drain their accepted reports. An abandoned reader
        // is finalized through its bounded recovery/failure handler rather than silently settled.
        try
        {
            await CloseLosslessExecutionReportSubscriptionsAsync().ConfigureAwait(false);
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

        // A consumer can fail after it accepted a channel item, outside the OMS gateway-pump
        // stack. Preserve that terminal failure for the lifecycle owner even if no later broker
        // report arrived to make the internal pump observe it directly.
        if (shutdownFailure is null
            && Volatile.Read(ref _terminalReportPumpFailure) is { } terminalReportFailure)
        {
            shutdownFailure = terminalReportFailure;
        }

        if (shutdownFailure is not null)
            ExceptionDispatchInfo.Capture(shutdownFailure).Throw();
    }

    /// <summary>
    /// Provides a read-only view of fill and partial-fill execution reports for consumption
    /// by diagnostics and compatibility observers. Reports are published as each order
    /// transitions to <see cref="OrderStatus.Filled"/> or <see cref="OrderStatus.PartiallyFilled"/>,
    /// with <see cref="ExecutionReport.FilledQuantity"/> normalised to the fill increment
    /// (gateways report cumulative quantities). This is a lossy observer stream: when a
    /// subscriber lags behind <see cref="OrderManagementSystemOptions.ExecutionChannelCapacity"/>,
    /// the oldest unread report is dropped (logged and counted via
    /// <see cref="DroppedExecutionReports"/>) rather than blocking the fill path. Order state,
    /// session fill history, and the durable accounting handoff remain authoritative and lossless.
    /// Consumers that drive strategy state must use
    /// <see cref="SubscribeLosslessExecutionReports()"/> instead of sharing this reader.
    /// </summary>
    public ChannelReader<ExecutionReport> ExecutionReports => _executionChannel.Reader;

    /// <summary>
    /// Total execution reports dropped from the <see cref="ExecutionReports"/> observer channel
    /// because no subscriber drained it in time. Fills themselves are never lost — order state
    /// and the accounting handoff do not flow through this channel.
    /// </summary>
    public long DroppedExecutionReports => Interlocked.Read(ref _droppedExecutionReports);

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

            if (Volatile.Read(ref _terminalReportPumpFailure) is { } reportPumpFailure)
            {
                throw new InvalidOperationException(
                    "The authoritative broker-report pump failed and the OMS is closed to new operations.",
                    reportPumpFailure);
            }

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

    private void MarkTerminalReportPumpFailure(Exception failure, string message)
    {
        if (Interlocked.CompareExchange(ref _terminalReportPumpFailure, failure, null) is not null)
        {
            return;
        }

        _logger.LogCritical(
            failure,
            "{Message} The OMS is closed to new operations because future accepted fills would have no authoritative consumer.",
            message);
    }

    private string GenerateOrderId()
    {
        var seq = Interlocked.Increment(ref _orderSequence);
        return $"MDN-{DateTimeOffset.UtcNow:yyyyMMdd}-{seq:D6}";
    }

    private static OrderState ApplyReport(
        OrderState current,
        ExecutionReport report,
        decimal? locallyAuthorizedModifiedQuantity = null)
    {
        // Once the OMS has reached a terminal state, late or malicious stream reports
        // must not reopen, resize, or otherwise mutate the completed local order.
        if (IsTerminal(current.Status))
        {
            return current;
        }

        // Gateway-streamed modifications are not trusted to authorize a quantity change.
        // A quantity may change only while applying the response to a locally initiated
        // modification, and is capped to the local request rather than report data.
        var authorizedQuantity = report.ReportType is ExecutionReportType.Modified
            && report.OrderStatus is OrderStatus.Accepted
            && locallyAuthorizedModifiedQuantity is > 0m
            ? Math.Max(locallyAuthorizedModifiedQuantity.Value, current.FilledQuantity)
            : current.Quantity;

        return current with
        {
            Status = report.OrderStatus,
            Quantity = authorizedQuantity,
            FilledQuantity = Math.Min(authorizedQuantity, Math.Max(report.FilledQuantity, current.FilledQuantity)),
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

        // A terminal client-order id may be reused. Any broker UUID retained for the
        // previous incarnation is no longer evidence for the newly registered order;
        // the submit acknowledgement (or a later broker report) will establish a fresh
        // mapping before the kill switch relies on it.
        _orderBrokerIds.TryRemove(orderId, out _);

        return true;
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
            catch (ExecutionReportDeliveryException ex)
            {
                MarkTerminalReportPumpFailure(
                    ex,
                    $"Execution report delivery from gateway '{_gateway.GatewayId}' failed without durable accounting; the OMS report pump is stopping.");
                throw;
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

    private async Task ProcessGatewayReportAsync(ExecutionReport report, CancellationToken ct)
    {
        var orderId = report.ClientOrderId ?? report.OrderId;
        if (!string.IsNullOrWhiteSpace(orderId))
        {
            RememberBrokerOrderId(orderId, report);
        }

        var isFillReport = report.OrderStatus is OrderStatus.Filled or OrderStatus.PartiallyFilled
            || HasCumulativeFillEvidence(report);

        // Publish the fill's exposure reservation BEFORE the tracked order can go terminal.
        // A filled order leaves the open book the instant ApplyReport lands, while the
        // portfolio only receives the fill inside ProcessFillReportAsync; in between, a
        // concurrent validation would find the exposure in neither book and could admit an
        // order that breaches the rails. The placeholder reserves the whole reported fill
        // and is retired as soon as the precise increment is tracked.
        if (isFillReport)
        {
            _pendingFillReservations[report] = report;
        }

        try
        {
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

            if (updatedState is null)
            {
                // A report for an order this process never registered: a fill is adopted and
                // booked as the broker's own executed increment, anything else is logged and
                // audited. See OrderManagementSystem.UntrackedFills.cs for the policy.
                (updatedState, previousFilledQuantity) = await ResolveUntrackedReportAsync(
                    orderId,
                    report,
                    isFillReport,
                    ct).ConfigureAwait(false);
            }
            else
            {
                // Gateways that acknowledge asynchronously deliver bracket child legs on the
                // report stream rather than on the submit return; register them from here too so
                // both delivery shapes end with the children tracked. TryRegisterOrder makes a
                // second sighting of the same child a no-op.
                RegisterGatewayChildOrders(orderId!, updatedState, report);
            }

            if (isFillReport)
            {
                var sessionId = string.IsNullOrWhiteSpace(orderId) ? null : ResolveSessionId(orderId);
                await ProcessFillReportAsync(
                        sessionId,
                        report,
                        previousFilledQuantity)
                    .ConfigureAwait(false);
            }

            if (updatedState is not null)
            {
                await RecordSessionOrderUpdateAsync(ResolveSessionId(orderId!), updatedState, ct).ConfigureAwait(false);
            }
        }
        finally
        {
            // Whatever happened, the placeholder must not outlive this report: either
            // _fillProcessing now carries the precise increment, or the fill was a
            // duplicate that reserves nothing.
            if (isFillReport)
            {
                _pendingFillReservations.TryRemove(report, out _);
            }
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
        decimal previousFilledQuantity)
    {
        var orderId = report.ClientOrderId ?? report.OrderId;
        if (!_fillProcessing.TryGetValue(report, out var progress))
        {
            // Gateways report FilledQuantity cumulatively (e.g. IB CumulativeQuantity,
            // Alpaca filled_qty) while fill consumers treat each report as a discrete
            // trade. The locally accepted quantity is authoritative when an amended
            // order caps a broker report above the accepted order quantity.
            var acceptedFilledQuantity = report.FilledQuantity;
            if (!string.IsNullOrWhiteSpace(orderId)
                && _orders.TryGetValue(orderId, out var currentOrder))
            {
                acceptedFilledQuantity = currentOrder.FilledQuantity;
            }

            var incrementQuantity = acceptedFilledQuantity - previousFilledQuantity;
            if (incrementQuantity <= 0m)
                return;

            var fillIncrement = incrementQuantity == report.FilledQuantity
                ? report
                : report with { FilledQuantity = incrementQuantity };
            if (fillIncrement.ReportType is not (ExecutionReportType.Fill or ExecutionReportType.PartialFill))
            {
                var isCompleteFill = report.OrderQuantity > 0m
                    && acceptedFilledQuantity >= report.OrderQuantity;
                fillIncrement = fillIncrement with
                {
                    ReportType = isCompleteFill ? ExecutionReportType.Fill : ExecutionReportType.PartialFill,
                    OrderStatus = isCompleteFill ? OrderStatus.Filled : OrderStatus.PartiallyFilled
                };
            }

            // Stamp the gateway-resolved sizing semantics onto the increment itself: the paper
            // book, the accounting event, and the durable session record all consume this one
            // report, and the session record is replayed after a restart when the sidecar
            // dictionary no longer exists. The flag is not part of the canonical fill identity,
            // so a replayed broker report still resolves to the same FillId.
            if (!string.IsNullOrWhiteSpace(orderId)
                && _orderFaceValueSizing.ContainsKey(orderId)
                && !fillIncrement.UsesFaceValuePercentageOfPar)
            {
                fillIncrement = fillIncrement with { UsesFaceValuePercentageOfPar = true };
            }

            progress = _fillProcessing.GetOrAdd(
                report,
                _ => new FillProcessingProgress(
                    fillIncrement,
                    acceptedFilledQuantity,
                    !string.IsNullOrWhiteSpace(orderId) && _orders.ContainsKey(orderId)));
        }

        // A broker-accepted fill may not be abandoned because the caller or report-pump token
        // was cancelled after dequeue. Every post-accept side effect in this funnel is therefore
        // non-cancellable; shutdown awaits the admitted operation/report instead of truncating it.
        await progress.Gate.WaitAsync(CancellationToken.None).ConfigureAwait(false);
        try
        {
            if (progress.IsComplete)
                return;

            var fillIncrement = progress.FillIncrement;

            // Gateway-resolved sizing semantics for this order: quantity routed as face value,
            // price quoted as a percentage of par. Stamped onto the increment when its
            // processing state was created, so the paper book, the accounting event, and the
            // durable session record all read the same classification.
            var usesFaceValuePercentageOfPar = fillIncrement.UsesFaceValuePercentageOfPar;

            if (!progress.PortfolioApplied)
            {
                var realisedPnlBefore = _portfolioState?.RealisedPnl ?? 0m;

                // Only fills for orders this OMS placed may mutate the paper portfolio;
                // stream reports for external/untracked orders are still published below.
                if (_portfolioState is PaperTradingPortfolio paperPortfolio
                    && progress.IsTrackedOrder)
                {
                    // The fill carries which fund owns it and, for a derivative, what one
                    // contract is worth. Without both, the shared execution book reports a
                    // position no fund can be shown to own and an option position measured
                    // as if each contract were a single share.
                    var fillOrderId = fillIncrement.ClientOrderId ?? fillIncrement.OrderId;
                    paperPortfolio.ApplyFill(
                        fillIncrement,
                        ownerAccountId: fillOrderId is not null
                            && _orderFinancialAccountIds.TryGetValue(fillOrderId, out var owningFund)
                            ? owningFund
                            : null,
                        contractMultiplier: fillOrderId is not null
                            && _orderContractMultipliers.TryGetValue(fillOrderId, out var multiplier)
                            ? multiplier
                            : 1m,
                        usesFaceValuePercentageOfPar: usesFaceValuePercentageOfPar);
                    progress.RealizedPnl = paperPortfolio.RealisedPnl - realisedPnlBefore;
                }

                progress.NewCash = _portfolioState?.Cash ?? 0m;
                progress.PortfolioApplied = true;
            }

            if (!progress.TradeEventPublished)
            {
                // Materialise the deterministic identity even when no accounting publisher is
                // configured. Paper-session durability uses the same FillId as the accounting
                // handoff rather than inventing a second replay identity.
                if (progress.IsTrackedOrder
                    && (_tradeEventPublisher is not null || !string.IsNullOrWhiteSpace(sessionId)))
                {
                    progress.TradeEvent ??= CreateTradeExecutedEvent(
                        fillIncrement,
                        progress.CumulativeFilledQuantity,
                        progress.RealizedPnl,
                        progress.NewCash,
                        ResolveFinancialAccountId(orderId),
                        usesFaceValuePercentageOfPar);
                }

                if (_tradeEventPublisher is not null && progress.IsTrackedOrder)
                {
                    try
                    {
                        // Awaited, never the blocking Publish bridge: acceptance applies storage
                        // backpressure and can wait unboundedly for the posting consumer to free
                        // channel capacity. Blocking a pool thread there starves the very
                        // consumer that has to drain it, so the fill path would stall instead of
                        // failing closed.
                        await _tradeEventPublisher.PublishAsync(progress.TradeEvent!).ConfigureAwait(false);
                    }
                    catch (Exception ex)
                    {
                        var wasRetained = await RetainAccountingHandoffFailureAsync(
                                progress.TradeEvent!,
                                ex,
                                CancellationToken.None)
                            .ConfigureAwait(false);
                        throw new AccountingHandoffException(progress.TradeEvent!, wasRetained, ex);
                    }
                }

                progress.TradeEventPublished = true;
            }

            if (!progress.SessionRecorded)
            {
                if (string.IsNullOrWhiteSpace(sessionId))
                {
                    progress.SessionRecorded = true;
                }

                try
                {
                    if (!progress.SessionRecorded)
                    {
                        await RecordSessionFillAsync(
                                sessionId,
                                progress.TradeEvent!.FillId,
                                fillIncrement,
                                CancellationToken.None)
                            .ConfigureAwait(false);
                        progress.SessionRecorded = true;
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogCritical(
                        ex,
                        "Accounting accepted fill {FillId}, but paper-session fill history could not be recorded; fill completion remains unacknowledged and will fail closed",
                        progress.TradeEvent?.FillId);
                    throw;
                }
            }

            if (!progress.ExecutionReportPublished)
            {
                if (!progress.LosslessSubscribersPublished)
                {
                    await PublishToLosslessExecutionReportSubscribersAsync(progress).ConfigureAwait(false);
                }

                // The shared compatibility reader is deliberately best effort. DropOldest
                // handles observer saturation, and a completed writer during shutdown is not an
                // authoritative delivery failure.
                if (!_executionChannel.Writer.TryWrite(fillIncrement))
                {
                    _logger.LogDebug(
                        "Best-effort execution-report observer was closed before fill {FillId} could be published",
                        progress.TradeEvent?.FillId);
                }

                progress.ExecutionReportPublished = true;
            }

            if (progress.SessionRecorded
                && progress.LosslessSubscribersPublished
                && progress.ExecutionReportPublished)
            {
                progress.IsComplete = true;
                TrackCompletedFill(report);
            }
        }
        catch (AccountingHandoffException)
        {
            throw;
        }
        catch (ExecutionReportDeliveryException)
        {
            // The subscription already attempted its explicit durable recovery/fail-closed seam.
            // Do not relabel a strategy-delivery failure as an accounting-publisher failure.
            throw;
        }
        catch (Exception ex)
        {
            var evidenceRetained = await TryRetainUnresolvedFillEvidenceAsync(
                    orderId,
                    report,
                    ex)
                .ConfigureAwait(false);
            throw new AccountingHandoffException(orderId ?? report.OrderId, evidenceRetained, ex);
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

    private async Task<bool> TryRetainUnresolvedFillEvidenceAsync(
        string? orderId,
        ExecutionReport report,
        Exception failure)
    {
        if (_auditTrail is null)
        {
            _logger.LogCritical(
                failure,
                "Broker fill for order {OrderId} could not form a durable accounting event and no execution audit store is configured",
                orderId ?? report.OrderId);
            return false;
        }

        try
        {
            _orders.TryGetValue(orderId ?? string.Empty, out var state);
            var metadata = new Dictionary<string, string>(
                BuildOrderLifecycleAuditMetadata(state, report) ?? new Dictionary<string, string>(),
                StringComparer.OrdinalIgnoreCase)
            {
                ["accountingFailureType"] = failure.GetType().Name,
                ["fillPrice"] = report.FillPrice?.ToString(CultureInfo.InvariantCulture) ?? "missing",
                ["fillOccurredAtUtc"] = report.Timestamp.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture)
            };
            await RecordOrderLifecycleAuditAsync(
                    action: "AccountingHandoffUnresolved",
                    outcome: "AttentionRequired",
                    orderId: orderId ?? report.OrderId,
                    state: state,
                    report: report,
                    message: failure.Message,
                    ct: CancellationToken.None,
                    metadata: metadata)
                .ConfigureAwait(false);
            return true;
        }
        catch (Exception auditFailure)
        {
            _logger.LogCritical(
                auditFailure,
                "Broker fill for order {OrderId} could not form a durable accounting event and its audit evidence could not be retained",
                orderId ?? report.OrderId);
            return false;
        }
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
        Guid fillId,
        ExecutionReport report,
        CancellationToken ct)
    {
        if (_sessionPersistence is null || string.IsNullOrWhiteSpace(sessionId))
        {
            return;
        }

        await _sessionPersistence.RecordFillAsync(sessionId, fillId, report, ct).ConfigureAwait(false);
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
        IReadOnlyDictionary<string, string>? metadata = null,
        IReadOnlyList<string>? riskWarnings = null,
        RiskDecisionSummary? riskDecision = null)
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
            OrderState = rejectedState,
            RiskWarnings = riskWarnings,
            RiskDecision = riskDecision
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
            LogSanitizer.Sanitize(orderId),
            LogSanitizer.Sanitize(request.Symbol),
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

        // A fill that has left the order book but not yet reached the portfolio still needs
        // its tracked state and its sidecars. ProcessFillReportAsync reads the contract
        // multiplier from _orderContractMultipliers, so evicting an option order in that
        // window makes the fill fall back to 1 and books a standard contract at a hundredth
        // of its exposure; losing the order entirely can also leave the report untracked.
        var pendingFillOrderIds = _pendingFillReservations.Keys
            .Select(static report => report.ClientOrderId ?? report.OrderId)
            .Where(static id => !string.IsNullOrWhiteSpace(id))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var removableOrderIds = _orders.Values
            .Where(order => order.Status is
                OrderStatus.Filled or
                OrderStatus.Cancelled or
                OrderStatus.Rejected or
                OrderStatus.Expired)
            // A parked order is recorded Rejected but is not finished: its escalation is
            // still live in the durable queue and can still route. Evicting its tracked
            // state makes CancelOrderAsync answer "order not found", stranding an approval
            // the submitter can no longer withdraw. Retain it until the escalation
            // resolves, which is exactly when its reservation is dropped.
            .Where(order => !_parkedOrderIds.ContainsKey(order.OrderId))
            .Where(order => !pendingFillOrderIds.Contains(order.OrderId))
            .OrderBy(static order => order.LastUpdatedAt ?? order.CreatedAt)
            .Take(_orders.Count - _options.ValidatedMaxRetainedOrders)
            .Select(static order => order.OrderId)
            .ToArray();

        foreach (var removableOrderId in removableOrderIds)
        {
            _orders.TryRemove(removableOrderId, out _);
            _orderBrokerIds.TryRemove(removableOrderId, out _);
            _orderSessionIds.TryRemove(removableOrderId, out _);
            _orderFinancialAccountIds.TryRemove(removableOrderId, out _);
            _orderContractMultipliers.TryRemove(removableOrderId, out _);
            _orderFaceValueSizing.TryRemove(removableOrderId, out _);
        }
    }

    private sealed class OperationLease(OrderManagementSystem owner) : IDisposable
    {
        private OrderManagementSystem? _owner = owner;

        public void Dispose()
            => Interlocked.Exchange(ref _owner, null)?.ExitOperation();
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

        public AccountingHandoffException(
            string orderId,
            bool evidenceRetained,
            Exception innerException)
            : base(
                $"Execution fill for order '{orderId}' was accepted by the broker but could not form a safe accounting event"
                + (evidenceRetained
                    ? "; durable execution-audit evidence requires operator reconciliation."
                    : "; no durable reconciliation evidence was available and the order result is fail-closed."),
                innerException)
        {
            WasRetained = evidenceRetained;
        }

        public bool WasRetained { get; }
    }
}
