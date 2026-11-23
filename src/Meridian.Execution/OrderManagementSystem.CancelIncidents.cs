using Meridian.Execution.OrderManagement;
using Meridian.Execution.Sdk;

namespace Meridian.Execution;

public sealed partial class OrderManagementSystem
{
    private CancelAllIncidentRecorder CreateCancelAllIncident(OrderSubmissionAuthority? authority)
    {
        var paperScope = _options.PaperIntentScope;
        var account = paperScope?.FundAccountId;
        if (account is null && _portfolioState is Services.PaperTradingPortfolio paper
            && Guid.TryParse(paper.PrimaryAccountId, out var paperAccount))
            account = paperAccount;
        return new CancelAllIncidentRecorder(new CancelAllIncident
        {
            IncidentId = $"cancel-{Guid.NewGuid():N}",
            StartedAt = DateTimeOffset.UtcNow,
            Actor = authority?.Actor,
            CorrelationId = authority?.CorrelationId,
            Authority = authority?.ToRecord(),
            GatewayId = _gateway.GatewayId,
            ExecutionMode = _gateway is IExecutionGatewayModeProvider mode ? mode.ExecutionMode
                : BrokerageOrderPlacementGate.ResolveExecutionMode(_brokerageConfiguration, _gateway.GatewayId),
            BrokerScopeIdentity = (_gateway as IBrokerageConnectionState)?.ScopeIdentity,
            TenantId = paperScope?.TenantId,
            CompanyId = paperScope?.CompanyId,
            FundAccountId = account
        }, _auditTrail, _logger);
    }

    private async Task<CancelAllOrderOutcome> ObserveTrackedCancellationAsync(CancelAllIncidentRecorder incident, string orderId,
        OrderState? initial, bool attempted, string? brokerOrderId = null, string? detail = null,
        bool uncertain = false, bool withdrawn = false, BrokerOrder? brokerObservation = null)
    {
        // Observation is separate from the order's accounting/recovery state. It must never apply
        // an execution report or infer that an unconfirmed successor disappeared with its parent.
        // Never wait for report/accounting work merely to capture evidence. Immutable snapshots
        // are safe to read; if an amendment changes across the read, keep either unresolved attempt.
        var before = GetPendingAmendmentForRecovery(orderId);
        var state = _orders.GetValueOrDefault(orderId) ?? initial;
        var after = GetPendingAmendmentForRecovery(orderId);
        var pending = after ?? before;
        uncertain |= !ReferenceEquals(before, after);
        if (brokerOrderId is null)
            _brokerIdentities.TryGetActiveId(orderId, out brokerOrderId);
        var observedQuantity = brokerObservation?.Quantity ?? state?.Quantity;
        // Broker-native notional orders and absent broker quantity are not measured shares.
        var quantity = observedQuantity is > 0m ? observedQuantity : null;
        var filled = brokerObservation?.FilledQuantity ?? state?.FilledQuantity;
        var status = brokerObservation?.Status ?? state?.Status;
        var unresolved = uncertain || pending is not null || _dispatchRegistry.IsUnsettled(orderId);
        var terminal = status is { } known && OrderStatusFacts.IsTerminal(known);
        var disposition = unresolved ? CancelAllOrderDisposition.Unknown
            : withdrawn ? CancelAllOrderDisposition.WithdrawnBeforeDispatch
            : status == OrderStatus.Cancelled ? CancelAllOrderDisposition.ConfirmedCancelled
            : status == OrderStatus.Filled ? CancelAllOrderDisposition.FilledDuringCancel
            : terminal ? CancelAllOrderDisposition.AlreadyTerminal
            : attempted || brokerObservation is not null ? CancelAllOrderDisposition.StillWorking
            : CancelAllOrderDisposition.Unknown;
        var remaining = state?.RoutedNotional is not > 0m && quantity is { } qty && filled is >= 0m && filled <= qty
            ? qty - filled.Value : (decimal?)null;
        if (terminal && !unresolved || withdrawn)
            remaining = 0m;
        if (pending is not null)
            remaining = pending.Original.RoutedNotional is not > 0m && pending.Proposed.RoutedNotional is not > 0m
                && pending.Original.Quantity > 0m && pending.Proposed.Quantity > 0m
                ? Math.Max(remaining ?? 0m,
                    Math.Max(0m, Math.Max(pending.Original.Quantity, pending.Proposed.Quantity) - (filled ?? 0m)))
                : null;

        var observation = new CancelAllOrderOutcome
        {
            ObservationKey = $"local:{orderId}", OrderId = orderId, BrokerOrderId = brokerOrderId,
            FundAccountId = state?.FundAccountId, BrokerAccountId = state?.BrokerAccountId,
            Symbol = state?.Symbol ?? brokerObservation?.Symbol, Side = state?.Side ?? brokerObservation?.Side,
            Operation = withdrawn ? "WithdrawEscalation" : "Cancel",
            Disposition = disposition, InitialStatus = initial?.Status ?? status,
            InitialQuantity = initial?.Quantity ?? quantity, InitialFilledQuantity = initial?.FilledQuantity ?? filled,
            Status = status, Quantity = quantity, FilledQuantity = filled, RemainingQuantity = remaining,
            CancellationAttempted = attempted, RequiresReconciliation = unresolved || disposition == CancelAllOrderDisposition.Unknown,
            HasUnconfirmedReplacement = pending is not null, CandidateBrokerOrderId = pending?.CandidateBrokerOrderId,
            CandidateClientOrderId = pending?.CandidateClientOrderId, ObservedAt = DateTimeOffset.UtcNow, Detail = detail
        };
        observation = attempted || brokerObservation is not null || withdrawn
            ? await ValueCancellationObservationAsync(observation, state).ConfigureAwait(false)
            : observation with { ValuationCoverage = "NotEvaluatedBeforeCancellation" };
        await incident.ObserveAsync(observation).ConfigureAwait(false);
        return observation;
    }

    private Task ObserveBrokerCancellationAsync(CancelAllIncidentRecorder incident, BrokerOrder original,
        bool attempted, ExecutionReport? report = null, string? detail = null, bool uncertain = false,
        BrokerOrder? verified = null)
    {
        var status = verified?.Status ?? report?.OrderStatus ?? original.Status;
        var observedQuantity = verified?.Quantity ?? (report?.OrderQuantity > 0m ? report.OrderQuantity : original.Quantity);
        decimal? quantity = observedQuantity > 0m ? observedQuantity : null;
        var filled = verified?.FilledQuantity ?? report?.FilledQuantity ?? original.FilledQuantity;
        var terminal = OrderStatusFacts.IsTerminal(status);
        var disposition = uncertain ? CancelAllOrderDisposition.Unknown
            : status == OrderStatus.Cancelled ? CancelAllOrderDisposition.ConfirmedCancelled
            : status == OrderStatus.Filled ? CancelAllOrderDisposition.FilledDuringCancel
            : terminal ? CancelAllOrderDisposition.AlreadyTerminal
            : attempted || verified is not null ? CancelAllOrderDisposition.StillWorking : CancelAllOrderDisposition.Unknown;
        var remaining = terminal && !uncertain ? 0m
            : quantity is { } qty && filled >= 0m && filled <= qty ? qty - filled : (decimal?)null;
        return incident.ObserveAsync(new CancelAllOrderOutcome
        {
            ObservationKey = $"broker:{original.OrderId}", OrderId = original.ClientOrderId ?? original.OrderId,
            BrokerOrderId = original.OrderId, Symbol = original.Symbol, Side = original.Side,
            // A broker row has no fund-account attribution. Never borrow it from another order or
            // the account selected by the reader; account projections omit these unowned rows.
            InitialStatus = original.Status, InitialQuantity = original.Quantity, InitialFilledQuantity = original.FilledQuantity,
            Status = status, Quantity = quantity, FilledQuantity = filled, RemainingQuantity = remaining,
            Disposition = disposition, CancellationAttempted = attempted,
            RequiresReconciliation = uncertain || disposition == CancelAllOrderDisposition.Unknown,
            ResidualNotional = remaining == 0m && !uncertain ? 0m : null,
            ValuationCoverage = remaining == 0m && !uncertain ? "NoRemainingOrderQuantity" : "Unavailable: broker-only instrument and account basis",
            ObservedAt = DateTimeOffset.UtcNow, Detail = detail
        });
    }

    private async Task<CancelAllOrderOutcome> ValueCancellationObservationAsync(CancelAllOrderOutcome outcome, OrderState? state)
    {
        if (outcome.RemainingQuantity == 0m && !outcome.RequiresReconciliation)
            return outcome with { ResidualNotional = 0m, ValuationCoverage = "NoRemainingOrderQuantity" };
        // The first valuation envelope is measured USD equity shares with a fresh executable-side
        // quote. Unknown, dollar-sized, debt, derivative and pending successor bases stay unknown.
        if (state is null || outcome.RemainingQuantity is null || outcome.RequiresReconciliation
            || state.RoutedNotional is > 0m || state.UsesFaceValuePercentageOfPar || state.ContractMultiplier != 1m
            || state.OptionContract is not null || state.Legs is { Count: > 0 } || _instrumentCoverage is null)
            return outcome with { ValuationCoverage = "Unavailable: unverified order sizing or successor exposure" };
        try
        {
            using var deadline = new CancellationTokenSource(TimeSpan.FromMilliseconds(250));
            var token = deadline.Token;
            var request = new OrderRequest { Symbol = state.Symbol, Side = state.Side, Type = state.Type,
                Quantity = state.Quantity, FundAccountId = state.FundAccountId };
            return await Task.Run(async () =>
            {
                if (!await _instrumentCoverage.IsUsdEquityAsync(request, token).ConfigureAwait(false))
                    return outcome with { ValuationCoverage = "Unavailable: instrument or currency basis" };
                var reference = _marketReferences?.GetReference(state.Symbol, state.Side);
                var now = DateTimeOffset.UtcNow;
                if (reference is not { Price: > 0m } || reference.ObservedAt > now || now - reference.ObservedAt >= TimeSpan.FromSeconds(30))
                    return outcome with { Currency = "USD", ValuationCoverage = "Unavailable: fresh executable-side quote" };
                return outcome with { ResidualNotional = outcome.RemainingQuantity.Value * reference.Price,
                    Currency = "USD", ValuationReference = reference, ValuationCoverage = "ObservedUsdEquityQuote" };
            }, token).WaitAsync(token).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            return outcome with { ValuationCoverage = $"Unavailable: valuation failed ({exception.GetType().Name})" };
        }
    }
}
