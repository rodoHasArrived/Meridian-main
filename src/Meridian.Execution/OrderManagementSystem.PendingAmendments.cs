using Meridian.Execution.Sdk;
using Meridian.Execution.Services;

namespace Meridian.Execution;

public sealed partial class OrderManagementSystem
{
    internal PendingOrderAmendment? GetPendingAmendmentForRecovery(string orderId) =>
        _pendingModificationOrderIds.GetValueOrDefault(orderId);

    private async Task<PendingOrderAmendment> BeginOrderAmendmentAsync(
        string orderId, OrderState original, OrderModification modification, CancellationToken ct)
    {
        await _brokerageRecoveryReportGate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var proposed = original with
            {
                Quantity = modification.NewQuantity ?? original.Quantity,
                LimitPrice = modification.NewLimitPrice ?? original.LimitPrice,
                StopPrice = modification.NewStopPrice ?? original.StopPrice,
                RoutedNotional = ResolveAmendedRoutedNotional(original, modification)
            };
            _orderBrokerIds.TryGetValue(orderId, out var brokerId);
            var pending = new PendingOrderAmendment(original, proposed, brokerId) { ProposedTrail = modification.NewTrail };
            if (!_pendingModificationOrderIds.TryAdd(orderId, pending))
                throw new InvalidOperationException("An earlier amendment requires broker reconciliation.");
            var current = _orders[orderId];
            _orders[orderId] = ApplyAmendmentTerms(current, original);
            try
            {
                if (IsTerminal(current.Status))
                    throw new InvalidOperationException("Order became terminal before amendment dispatch.");
                SavePendingAmendment(orderId);
            }
            catch
            {
                _pendingModificationOrderIds.TryRemove(orderId, out _);
                throw;
            }
            return pending;
        }
        finally
        {
            _brokerageRecoveryReportGate.Release();
        }
    }

    private async Task AbandonOrderAmendmentAsync(string orderId, PendingOrderAmendment amendment)
    {
        await _brokerageRecoveryReportGate.WaitAsync().ConfigureAwait(false);
        try
        {
            if (_pendingModificationOrderIds.TryRemove(new KeyValuePair<string, PendingOrderAmendment>(orderId, amendment)))
            {
                _orders[orderId] = ApplyAmendmentTerms(_orders[orderId], amendment.Original);
                SavePendingAmendment(orderId);
            }
        }
        finally
        {
            _brokerageRecoveryReportGate.Release();
        }
    }

    /// <summary>
    /// Applies a gateway's pending amendment report atomically with the check that the amendment
    /// is still unresolved. An authoritative Modified/ModificationRejected report on the live
    /// stream may resolve it at any moment; checking and marking recovery outside the report gate
    /// would tell the caller a confirmed amendment is uncertain and invite a duplicate retry.
    /// </summary>
    /// <returns>null while the outcome remains pending; true when it resolved to the proposed
    /// terms; false when it resolved to the original terms.</returns>
    private async Task<bool?> ProcessPendingAmendmentReportAsync(
        string orderId, PendingOrderAmendment amendment, ExecutionReport report)
    {
        await _brokerageRecoveryReportGate.WaitAsync().ConfigureAwait(false);
        try
        {
            if (IsSamePendingAmendment(orderId, amendment))
            {
                await ProcessGatewayReportCoreAsync(report, CancellationToken.None).ConfigureAwait(false);
                if (IsSamePendingAmendment(orderId, amendment))
                {
                    MarkBrokerageRecoveryRequired(orderId);
                    return null;
                }
            }

            // Report the outcome the broker actually established. Comparing terms cannot tell a
            // trail-only amendment's acceptance from its rejection: both snapshots are identical.
            if (_amendmentOutcomes.TryGetValue(amendment.Proposed, out var outcome))
                return outcome.Value;
            var resolved = _orders[orderId];
            return resolved.LimitPrice == amendment.Proposed.LimitPrice
                && resolved.StopPrice == amendment.Proposed.StopPrice
                && resolved.Quantity == Math.Max(amendment.Proposed.Quantity, resolved.FilledQuantity);
        }
        finally
        {
            _brokerageRecoveryReportGate.Release();
            TrimRetainedOrdersIfNeeded();
        }
    }

    private bool IsSamePendingAmendment(string orderId, PendingOrderAmendment amendment) =>
        _pendingModificationOrderIds.TryGetValue(orderId, out var current)
        && ReferenceEquals(current.Original, amendment.Original)
        && ReferenceEquals(current.Proposed, amendment.Proposed);

    private void SavePendingAmendment(string orderId)
    {
        if (!_dispatchedOrderIds.ContainsKey(orderId))
            return;
        var pending = GetPendingAmendmentForRecovery(orderId);
        _orderBrokerIds.TryGetValue(orderId, out var brokerId);
        _recoveryStore?.Save(new RetainedBrokerageOrder(_orders[orderId], brokerId, pending is not null)
        {
            BrokerOrderAliases = GetBrokerOrderAliases(orderId),
            RequiresAmendmentRecovery = pending is not null,
            PendingAmendment = pending
        });
        if (pending is not null)
            _recoveryOrderIds[orderId] = 0;
        else
            _recoveryOrderIds.TryRemove(orderId, out _);
    }

    private static OrderState ApplyAmendmentTerms(OrderState current, OrderState terms) => current with
    {
        Quantity = Math.Max(terms.Quantity, current.FilledQuantity),
        LimitPrice = terms.LimitPrice,
        StopPrice = terms.StopPrice,
        RoutedNotional = terms.RoutedNotional
    };

    private static bool MatchesAmendmentTerms(ExecutionReport report, OrderState terms) =>
        report.OrderQuantity == terms.Quantity && (!report.HasOrderTerms
            || report.OrderLimitPrice == terms.LimitPrice && report.OrderStopPrice == terms.StopPrice);

    private static OrderState SelectAmendmentTerms(
        ExecutionReport report, PendingOrderAmendment pending, bool locallyConfirmed = false)
    {
        if (report.ReportType is ExecutionReportType.ModificationPending)
            throw new InvalidDataException("Broker amendment outcome remains pending.");
        var brokerId = report.GatewayOrderId ?? report.OrderId;
        var successor = !string.Equals(brokerId, pending.OriginalBrokerOrderId, StringComparison.Ordinal)
            && (report.ReplacesOrderId == pending.OriginalBrokerOrderId || brokerId == pending.CandidateBrokerOrderId);
        if (brokerId == pending.OriginalBrokerOrderId
            && (pending.CandidateBrokerOrderId is not null || pending.CandidateClientOrderId is not null)
            && IsTerminal(report.OrderStatus)
            && report.ReportType is not ExecutionReportType.ModificationRejected)
            throw new InvalidDataException("A terminal predecessor does not establish the candidate replacement outcome.");
        if (locallyConfirmed && report.ReportType is ExecutionReportType.Modified)
            return pending.Proposed;
        if (report.ReportType is ExecutionReportType.ModificationRejected && !successor)
            return pending.Original;
        if (!report.HasOrderTerms
            && (pending.Original.LimitPrice != pending.Proposed.LimitPrice || pending.Original.StopPrice != pending.Proposed.StopPrice))
            throw new InvalidDataException("Broker recovery lacks the prices needed to distinguish amendment outcomes.");
        if (successor && MatchesAmendmentTerms(report, pending.Proposed))
            return pending.Proposed;
        var original = MatchesAmendmentTerms(report, pending.Original);
        var proposed = MatchesAmendmentTerms(report, pending.Proposed);
        if (original && proposed)
            // Indistinguishable terms (a trail-only amendment): the report type is the evidence.
            return report.ReportType is ExecutionReportType.Modified ? pending.Proposed : pending.Original;
        if (original)
            return pending.Original;
        if (proposed)
            return pending.Proposed;
        throw new InvalidDataException("Broker recovery matches neither retained amendment outcome.");
    }

    private PendingOrderAmendment? PrepareAmendmentReport(
        string orderId, ref ExecutionReport report, decimal? locallyAuthorizedQuantity)
    {
        if (!_pendingModificationOrderIds.TryGetValue(orderId, out var pending))
            return null;
        if (report.CandidateGatewayOrderId is { Length: > 0 } candidate)
        {
            pending = pending with { CandidateBrokerOrderId = candidate };
            _pendingModificationOrderIds[orderId] = pending;
        }
        if (report.CandidateClientOrderId is { Length: > 0 } candidateClient)
        {
            pending = pending with { CandidateClientOrderId = candidateClient };
            _pendingModificationOrderIds[orderId] = pending;
        }
        var current = _orders[orderId];
        if (report.ReportType is ExecutionReportType.Modified
            && report.GatewayOrderId is { Length: > 0 } confirmedCandidate
            && confirmedCandidate != pending.OriginalBrokerOrderId)
        {
            pending = pending with { CandidateBrokerOrderId = confirmedCandidate };
            _pendingModificationOrderIds[orderId] = pending;
        }
        if (report.ReportType is ExecutionReportType.ModificationPending)
        {
            var status = current.Status;
            if (!IsTerminal(status) && report.FillPrice is > 0m && report.FilledQuantity > current.FilledQuantity)
                status = report.FilledQuantity >= Math.Max(pending.Original.Quantity, pending.Proposed.Quantity)
                    ? OrderStatus.Filled : OrderStatus.PartiallyFilled;
            report = report with { OrderStatus = status, OrderQuantity = current.Quantity };
            return null;
        }
        if (report.ReportType is not (ExecutionReportType.Modified or ExecutionReportType.ModificationRejected))
            return null;
        OrderState terms;
        try
        {
            terms = SelectAmendmentTerms(report, pending, locallyAuthorizedQuantity is not null);
        }
        catch (InvalidDataException)
        {
            report = report with
            {
                ReportType = ExecutionReportType.ModificationPending,
                OrderStatus = current.Status,
                OrderQuantity = current.Quantity
            };
            return null;
        }
        // A late working acknowledgement cannot reopen or resize a terminal order. Its
        // still-possible exposure remains separately reserved until authoritative recovery.
        if (IsTerminal(current.Status)
            && (!IsTerminal(report.OrderStatus) || terms.Quantity != current.Quantity))
        {
            report = report with
            {
                ReportType = ExecutionReportType.ModificationPending,
                OrderStatus = current.Status,
                OrderQuantity = current.Quantity
            };
            return null;
        }
        _orders[orderId] = ApplyAmendmentTerms(current, terms);
        RecordAmendmentOutcome(pending, ReferenceEquals(terms, pending.Proposed));
        return pending;
    }

    // Keyed by the proposed snapshot's identity, which every `with` copy of the amendment shares.
    private readonly System.Runtime.CompilerServices.ConditionalWeakTable<OrderState, System.Runtime.CompilerServices.StrongBox<bool>> _amendmentOutcomes = new();

    private void RecordAmendmentOutcome(PendingOrderAmendment pending, bool accepted) =>
        _amendmentOutcomes.AddOrUpdate(pending.Proposed, new System.Runtime.CompilerServices.StrongBox<bool>(accepted));

    private IReadOnlyList<OrderState> GetAmendmentExposureReservations()
    {
        var reservations = new List<OrderState>();
        foreach (var (id, pending) in _pendingModificationOrderIds)
        {
            var current = _orders.GetValueOrDefault(id) ?? pending.Original;
            if (Math.Max(pending.Original.Quantity, pending.Proposed.Quantity) <= current.FilledQuantity)
                continue;
            // Reserve one real outcome, never independent maxima: 100 shares at $1 amended to
            // 1 share at $100 is $100 either way, not a synthetic 100 shares at $100. Exposure
            // providers multiply quantity by price, so the terms must stay correlated.
            var outcome = SelectLargerAmendmentOutcome(pending, current);
            if (outcome.Quantity <= current.FilledQuantity)
                outcome = outcome == pending.Original ? pending.Proposed : pending.Original;
            reservations.Add(current with
            {
                Status = OrderStatus.Accepted,
                Quantity = outcome.Quantity,
                LimitPrice = outcome.LimitPrice,
                StopPrice = outcome.StopPrice,
                RoutedNotional = outcome.RoutedNotional
            });
        }
        return reservations;
    }

    private static OrderState SelectLargerAmendmentOutcome(PendingOrderAmendment pending, OrderState current)
    {
        var original = MeasureOrderValue(pending.Original.Quantity, pending.Original.LimitPrice, pending.Original.StopPrice,
            pending.Original.RoutedNotional, current.ContractMultiplier, current.UsesFaceValuePercentageOfPar);
        var proposed = MeasureOrderValue(pending.Proposed.Quantity, pending.Proposed.LimitPrice, pending.Proposed.StopPrice,
            pending.Proposed.RoutedNotional, current.ContractMultiplier, current.UsesFaceValuePercentageOfPar);
        // Unpriced outcomes (market orders) are valued at the mark by the exposure provider, so
        // the larger quantity is the larger exposure.
        if (original is null || proposed is null)
            return pending.Proposed.Quantity > pending.Original.Quantity ? pending.Proposed : pending.Original;
        if (proposed.Value != original.Value)
            return proposed.Value > original.Value ? pending.Proposed : pending.Original;
        return pending.Proposed.Quantity > pending.Original.Quantity ? pending.Proposed : pending.Original;
    }
}
