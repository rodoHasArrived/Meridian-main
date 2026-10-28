using System.Collections.Concurrent;
using System.Runtime.ExceptionServices;
using System.Threading.Channels;
using Meridian.Execution.Events;
using Meridian.Execution.Logging;
using Meridian.Execution.OrderManagement;
using Meridian.Execution.Sdk;
using Meridian.Execution.Services;
using Microsoft.Extensions.Logging;

using static Meridian.Execution.OrderManagement.OrderAuditMetadata;
using static Meridian.Execution.OrderManagement.OrderRiskSizingPolicy;

namespace Meridian.Execution;

/// <summary>
/// Central Order Management System (OMS). Coordinates order lifecycle between
/// strategies, risk checks, and execution gateways. Uses bounded channels
/// for backpressure-aware execution event processing.
/// </summary>
[ImplementsAdr("ADR-013", "Uses bounded channels for execution event pipeline")]
public sealed partial class OrderManagementSystem : IOrderManager, IOrderManagementLifecycle, IDisposable, IAsyncDisposable
{
    private readonly ConcurrentDictionary<string, OrderState> _orders = new();
    private readonly IExecutionGateway _gateway;
    private readonly OrderFillProcessor _fillProcessor;
    private readonly OrderExecutionAudit _executionAudit;
    private readonly AccountingHandoffCoordinator _accountingHandoff;
    private readonly ExecutionReportHub _executionReportHub;
    private readonly ExecutionFailureState _executionFailureState;
    private readonly OrderAdmissionPolicy _admissionPolicy;
    private readonly BrokerageOrderRecoveryJournal _recoveryJournal;
    private readonly OrderBrokerIdentityIndex _brokerIdentities = new();
    private readonly IRiskValidator? _riskValidator;
    private readonly ExecutionOperatorControlService? _operatorControls;
    private readonly ExecutionAuditTrailService? _auditTrail;
    private readonly RiskEscalationQueueService? _escalationQueue;
    private readonly Meridian.Execution.Models.IPortfolioState? _portfolioState;
    private readonly PaperSessionPersistenceService? _sessionPersistence;
    private readonly BrokerageConfiguration? _brokerageConfiguration;
    private readonly OrderManagementSystemOptions _options;
    private readonly ILogger<OrderManagementSystem> _logger;
    private readonly ConcurrentDictionary<string, string> _orderSessionIds = new(StringComparer.OrdinalIgnoreCase);
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
    private Task _reportPumpTask = Task.CompletedTask;
    private Task _handoffRecoveryTask = Task.CompletedTask;
    private readonly ConcurrentDictionary<string, string> _orderFinancialAccountIds = new(StringComparer.OrdinalIgnoreCase);
    // Contract multiplier per order id, for derivative fills.
    private readonly ConcurrentDictionary<string, decimal> _orderContractMultipliers = new(StringComparer.OrdinalIgnoreCase);
    // Order ids the active gateway routes as face value priced as a percentage of par, so the
    // fill booking path scales the clean price exactly as the pre-trade rails valued the order.
    private readonly ConcurrentDictionary<string, bool> _orderFaceValueSizing = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _disposeSync = new();
    private Task? _disposeTask;
    private TaskCompletionSource? _operationsDrained;
    private int _activeOperations;
    private int _disposeStarted;

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
        RiskEscalationQueueService? escalationQueue = null,
        FileBrokerageOrderRecoveryStore? recoveryStore = null)
    {
        _gateway = gateway ?? throw new ArgumentNullException(nameof(gateway));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _riskValidator = riskValidator;
        _operatorControls = operatorControls;

        _auditTrail = auditTrail;
        _escalationQueue = escalationQueue;
        _portfolioState = portfolioState;
        _sessionPersistence = sessionPersistence;
        _recoveryJournal = new BrokerageOrderRecoveryJournal(recoveryStore);
        _brokerageConfiguration = brokerageConfiguration;
        _accountingHandoff = new AccountingHandoffCoordinator(
            tradeEventPublisher, tradeFillHandoffFailureStore, logger);
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
        _admissionPolicy = new OrderAdmissionPolicy(
            gateway, logger, brokerageConfiguration, securityMasterGate,
            operatorControls, liveOrderReadinessGate, portfolioState);
        _executionFailureState = new ExecutionFailureState(logger);
        _executionReportHub = new ExecutionReportHub(_options, () => gateway.GatewayId, auditTrail, logger, _executionFailureState);
        _executionAudit = new OrderExecutionAudit(auditTrail, () => gateway.GatewayId, logger);
        _fillProcessor = new OrderFillProcessor(
            new OrderFillContext(_orders, _orderFinancialAccountIds, _orderContractMultipliers, _orderFaceValueSizing),
            portfolioState, sessionPersistence, _accountingHandoff, _executionReportHub, _executionAudit, logger);

        RestoreBrokerageOrders();
        RehydrateParkedOrderReservations();

        // Construction restores identity evidence but performs no background work.
        // The lifecycle owner starts report intake before admitting order operations.
    }

    /// <inheritdoc />
    public async Task<OrderResult> PlaceOrderAsync(OrderRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        using var operation = EnterOperation();

        var orderId = request.ClientOrderId ?? GenerateOrderId();
        var prepared = _admissionPolicy.Prepare(request, orderId);
        request = prepared.Request;
        var safeRequest = prepared.SafeRequest;
        var brokerName = prepared.BrokerName;
        var actor = prepared.Actor;
        var correlationId = prepared.CorrelationId;
        var runId = prepared.RunId;
        var sessionId = prepared.SessionId;

        // A duplicate client order id must never reach the state table or the gateway: every
        // downstream write in this method (including gate rejections) keys on orderId, so a
        // replayed or colliding id would overwrite the tracked state (fills, status history)
        // of the order already working under that id.
        if (request.ClientOrderId is not null
            && (_recoveryJournal.IsDispatched(orderId)
                || (_orders.TryGetValue(orderId, out var existingOrder) && !IsTerminalStatus(existingOrder.Status))
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

        var admission = await _admissionPolicy.EvaluatePreflightAsync(prepared, ct).ConfigureAwait(false);
        if (admission.Refusal is { } refusal)
        {
            return await RejectOrderAsync(
                orderId, safeRequest, actor, brokerName, runId, correlationId,
                refusal.Reason, sessionId, ct,
                rejectionSource: refusal.Source,
                reasonCode: refusal.ReasonCode,
                metadata: refusal.Metadata).ConfigureAwait(false);
        }
        var usesFaceValuePercentageOfPar = admission.UsesFaceValuePercentageOfPar;
        var riskRequest = admission.RiskRequest;
        var liveOrderReadinessDecision = admission.LiveReadinessDecision;
        var operatorControlDecision = admission.OperatorControlDecision;

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
            var brokerageLimitRefusal = await EvaluateBrokerageAdmissionLimitsAsync(riskRequest, ct).ConfigureAwait(false);
            if (brokerageLimitRefusal is not null)
            {
                return await RejectOrderAsync(
                    orderId, safeRequest, actor, brokerName, runId, correlationId,
                    brokerageLimitRefusal, sessionId, ct,
                    rejectionSource: "brokerage admission limits",
                    reasonCode: "BROKERAGE_LIMIT_REJECTED").ConfigureAwait(false);
            }

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
                RunId = runId,
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

            // The operator controls are consulted again here, at the point the order is about to
            // become visible and be dispatched, not only at the gate above. Everything between
            // the two -- readiness, risk validation, reservation -- takes time, and a breaker
            // opened during that time has already run its cancel-all sweep against a book this
            // order was not yet in. Passing the gate a moment before the trip is not permission
            // to reach the broker a moment after it. Evaluated before registration so that the
            // close-only exception measures the desk's other working reductions and not this
            // order's own quantity twice.
            if (_operatorControls is not null)
            {
                var dispatchDecision = _admissionPolicy.EvaluateDispatchControls(prepared)!;
                // The submission audit describes the controls that actually authorized routing,
                // which are these, not the gate's: an override consumed at the gate can have
                // expired by now with the breaker since closed, and the evidence must not say
                // an override was applied to a dispatch that needed none.
                operatorControlDecision = dispatchDecision;
                if (!dispatchDecision.IsApproved)
                {
                    // Nothing reached the gateway, so the reserved capacity and any governed
                    // release this order consumed go back exactly as they do for a gate rejection.
                    SettleRiskReservations(riskDecision, commit: false, orderId);
                    RestoreConsumedApprovals(consumedApprovalId, "an operator control that closed before dispatch", orderId);
                    return await RejectOrderAsync(
                        orderId,
                        safeRequest,
                        actor,
                        brokerName,
                        runId,
                        correlationId,
                        dispatchDecision.RejectReason,
                        sessionId,
                        ct,
                        rejectionSource: "operator controls at dispatch",
                        reasonCode: dispatchDecision.RejectCode ?? "OPERATOR_CONTROL_REJECTED",
                        metadata: BuildOrderRejectedByControlAuditMetadata(dispatchDecision),
                        riskWarnings: riskWarnings,
                        riskDecision: riskDecision?.ToSummary())
                        .ConfigureAwait(false);
                }
            }

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

        using var retentionLease = TrackBrokerageSubmissionForRetention(orderId);
        try
        {
            ct.ThrowIfCancellationRequested();
            // Retain the identity before the network boundary. A lost acknowledgement or
            // process exit must never make an already accepted client id available again.
            if (_gateway is IBrokerageGateway || _recoveryJournal.HasStore)
                RetainBrokerageDispatch(orderState);
            var recoveryVersion = _recoveryJournal.CaptureVersion(orderId);
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

            // The gateway has answered and the answer is in the tracked table: the sweep has
            // what it waits for. Settled here rather than at method exit so the fill handoff,
            // session persistence, and audit writes below never hold a kill switch open.
            dispatchLease.Settle();

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
                await _fillProcessor.ProcessAsync(
                        sessionId,
                        report,
                        previousFilledQuantity)
                    .ConfigureAwait(false);
            }

            RetainProcessedBrokerageOrder(orderId, report, recoveryVersion);

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
                await _executionAudit.RecordLifecycleAsync(
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
            if (dispatchAttempted)
                _recoveryJournal.RequireRecovery(orderId);

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

            // Dispatch failure is an unknown outcome, not a broker rejection. In particular,
            // the report pump may already have confirmed fills before the response was lost.
            var rejectedState = _orders.AddOrUpdate(orderId,
                _ => orderState with { Status = dispatchAttempted ? OrderStatus.PendingNew : OrderStatus.Rejected },
                (_, current) => dispatchAttempted
                    ? current
                    : current with { Status = OrderStatus.Rejected, LastUpdatedAt = DateTimeOffset.UtcNow });
            TrimRetainedOrdersIfNeeded();
            await RecordSessionOrderUpdateAsync(sessionId, rejectedState, CancellationToken.None).ConfigureAwait(false);

            if (_auditTrail is not null)
            {
                await _auditTrail.RecordAsync(new ExecutionAuditEntry(
                    AuditId: Guid.NewGuid().ToString("N"),
                    Category: "Order",
                    Action: dispatchAttempted ? "OrderSubmissionUncertain" : "OrderRejected",
                    Outcome: dispatchAttempted ? "RecoveryRequired" : "Rejected",
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
                    Reason: dispatchAttempted ? AmbiguousSubmissionReason : null,
                    Scope: BuildOrderAuditScope(safeRequest, runId)),
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
    public IReadOnlyList<OrderState> GetOpenOrders()
    {
        return _orders.Values
            .Where(o => o.Status is OrderStatus.PendingNew or OrderStatus.Accepted or OrderStatus.PartiallyFilled or OrderStatus.PendingCancel)
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
            _executionReportHub.BeginClose();
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
            await _executionReportHub.CompleteSubscribersAsync().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            shutdownFailure = shutdownFailure is null
                ? ex
                : new AggregateException(shutdownFailure, ex);
        }
        finally
        {
            _executionReportHub.CompleteObserver();
            _reportPumpCts.Dispose();
            if (_operatorControls?.WorkingReductionQuantityProbe == ResolveWorkingReductionQuantity)
                _operatorControls.WorkingReductionQuantityProbe = null;
        }

        // A consumer can fail after it accepted a channel item, outside the OMS gateway-pump
        // stack. Preserve that terminal failure for the lifecycle owner even if no later broker
        // report arrived to make the internal pump observe it directly.
        if (shutdownFailure is null
            && _executionFailureState.Failure is { } terminalReportFailure)
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
    public ChannelReader<ExecutionReport> ExecutionReports => _executionReportHub.Observer;

    /// <summary>
    /// Total execution reports dropped from the <see cref="ExecutionReports"/> observer channel
    /// because no subscriber drained it in time. Fills themselves are never lost — order state
    /// and the accounting handoff do not flow through this channel.
    /// </summary>
    public long DroppedExecutionReports => _executionReportHub.DroppedReports;

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
        return await _accountingHandoff.GetPendingAsync(ct).ConfigureAwait(false);
    }

    private OperationLease EnterOperation()
    {
        lock (_disposeSync)
        {
            if (_disposeStarted != 0)
                throw new ObjectDisposedException(nameof(OrderManagementSystem));

            if (!_started)
                throw new InvalidOperationException("The OMS must be started before accepting operations. Call StartAsync first.");

            if (_executionFailureState.Failure is { } reportPumpFailure)
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
        => _executionFailureState.Mark(failure, message);

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
        if (_recoveryJournal.IsDispatched(orderId))
            return false;
        while (!_orders.TryAdd(orderId, orderState))
        {
            if (!_orders.TryGetValue(orderId, out var existing))
            {
                continue; // Entry was trimmed between TryAdd and TryGetValue; retry.
            }

            if (!IsTerminalStatus(existing.Status) || _recoveryJournal.IsDispatched(orderId))
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
        _brokerIdentities.ForgetActiveId(orderId);
        _adoptedFillGaps.TryRemove(orderId, out _);

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

    private async Task ProcessGatewayReportCoreAsync(ExecutionReport report, CancellationToken ct, decimal? authorizedQuantity = null)
    {
        report = NormalizeBrokerOrderIdentity(report);
        var orderId = report.ClientOrderId ?? report.OrderId;
        if (IsCompactedBrokerageReport(report))
            return;
        if (report.ReportType is ExecutionReportType.ModificationPending && !_pendingModificationOrderIds.ContainsKey(orderId))
            return;
        var resolvedAmendment = PrepareAmendmentReport(orderId, ref report, authorizedQuantity);
        var recoveryVersion = string.IsNullOrWhiteSpace(orderId) ? 0L : _recoveryJournal.CaptureVersion(orderId);
        if (!string.IsNullOrWhiteSpace(orderId))
        {
            RememberBrokerOrderId(orderId, report);
        }

        // An operation refusal without a matching broker snapshot has no authority to
        // change lifecycle state. It can still be returned to the initiating caller.
        if (report.ReportType is ExecutionReportType.CancelRejected or ExecutionReportType.ModificationRejected
            && (!_orders.TryGetValue(orderId, out var refusedState)
                || !string.Equals(refusedState.Symbol, report.Symbol, StringComparison.OrdinalIgnoreCase)
                || refusedState.Side != report.Side))
            return;

        if (report.FillPriceIsCumulativeAverage && report.FilledQuantity > 0m
            && _orders.TryGetValue(orderId, out var prior) && report.FilledQuantity > prior.FilledQuantity
            && prior.FilledQuantity > 0m)
        {
            if (report.FillPrice is not > 0m || prior.AverageFillPrice is not > 0m)
                throw new InvalidDataException("Broker cumulative fill economics are incomplete.");
            var price = (report.FilledQuantity * report.FillPrice.Value
                - prior.FilledQuantity * prior.AverageFillPrice.Value) / (report.FilledQuantity - prior.FilledQuantity);
            if (price <= 0m)
                throw new InvalidDataException("Broker cumulative fill economics contradict prior executions.");
            report = report with { FillPrice = price, FillPriceIsCumulativeAverage = false };
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
            _fillProcessor.ReservePending(report);
        }

        try
        {
            OrderState? updatedState = null;
            var previousFilledQuantity = 0m;
            while (!string.IsNullOrWhiteSpace(orderId) && _orders.TryGetValue(orderId, out var existing))
            {
                var executionLimit = GetPendingAmendmentForRecovery(orderId) is { } pendingAmendment
                    ? Math.Max(pendingAmendment.Original.Quantity, pendingAmendment.Proposed.Quantity) : (decimal?)null;
                var merged = ApplyReport(existing, report, authorizedQuantity, executionLimit);
                if (_orders.TryUpdate(orderId, merged, existing))
                {
                    previousFilledQuantity = existing.FilledQuantity;
                    updatedState = merged;
                    break;
                }
            }

            var adoptedUntrackedFill = false;
            var lateAdoptedIncrement = false;
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
                adoptedUntrackedFill = updatedState is not null;
            }
            else
            {
                // Gateways that acknowledge asynchronously deliver bracket child legs on the
                // report stream rather than on the submit return; register them from here too so
                // both delivery shapes end with the children tracked. TryRegisterOrder makes a
                // second sighting of the same child a no-op.
                RegisterGatewayChildOrders(orderId!, updatedState, report);

                if (isFillReport && updatedState.FilledQuantity <= previousFilledQuantity)
                {
                    // The cumulative books nothing, which for an adopted order may mean an
                    // earlier event of it delivered after the one that adopted it. Its own
                    // quantity is then claimed against the adoption gap rather than dropped.
                    var claimedFrom = await TryClaimLateAdoptedIncrementAsync(orderId!, updatedState, report, ct)
                        .ConfigureAwait(false);
                    if (claimedFrom is { } bookFrom)
                    {
                        previousFilledQuantity = bookFrom;
                        adoptedUntrackedFill = true;
                        lateAdoptedIncrement = true;
                    }
                }
            }

            // A refused untracked fill still enters the funnel, which for an order this OMS does
            // not track publishes the report to observers and lossless subscribers only: the
            // paper book, the accounting handoff, and session persistence are each gated on the
            // order being tracked, so nothing is booked, exactly as external fills have always
            // been surfaced without being booked.
            if (isFillReport)
            {
                var sessionId = string.IsNullOrWhiteSpace(orderId) ? null : ResolveSessionId(orderId);
                try
                {
                    await _fillProcessor.ProcessAsync(
                            sessionId,
                            report,
                            previousFilledQuantity)
                        .ConfigureAwait(false);
                }
                catch (Exception handoffFailure) when (adoptedUntrackedFill)
                {
                    // The adoption audit must describe what happened, not what was attempted:
                    // an entry saying the fill was booked, written before the handoff ran,
                    // would stand as evidence over a ledger that never accepted it.
                    await RecordUntrackedFillOutcomeAsync(orderId!, updatedState!, report, handoffFailure, ct, lateAdoptedIncrement)
                        .ConfigureAwait(false);
                    throw;
                }

                if (adoptedUntrackedFill)
                {
                    await RecordUntrackedFillOutcomeAsync(orderId!, updatedState!, report, null, ct, lateAdoptedIncrement)
                        .ConfigureAwait(false);
                }
            }

            if (updatedState is not null)
            {
                if (resolvedAmendment is not null)
                    _pendingModificationOrderIds.TryRemove(new KeyValuePair<string, PendingOrderAmendment>(orderId!, resolvedAmendment));
                RetainProcessedBrokerageOrder(orderId!, report, recoveryVersion);
                await RecordSessionOrderUpdateAsync(ResolveSessionId(orderId!), updatedState, ct).ConfigureAwait(false);
            }
        }
        finally
        {
            // Whatever happened, the placeholder must not outlive this report: either
            // the fill processor now carries the precise increment, or the fill was a
            // duplicate that reserves nothing.
            if (isFillReport)
            {
                _fillProcessor.ReleasePending(report);
            }
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

    private string? ResolveSessionId(string orderId) =>
        _orderSessionIds.TryGetValue(orderId, out var sessionId) ? sessionId : null;

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

    private sealed class OperationLease(OrderManagementSystem owner) : IDisposable
    {
        private OrderManagementSystem? _owner = owner;

        public void Dispose()
            => Interlocked.Exchange(ref _owner, null)?.ExitOperation();
    }



}
