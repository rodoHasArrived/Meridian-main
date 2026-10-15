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
            var pending = new PendingOrderAmendment(original, proposed, brokerId);
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
        return pending;
    }

    private IReadOnlyList<OrderState> GetAmendmentExposureReservations()
    {
        var reservations = new List<OrderState>();
        foreach (var (id, pending) in _pendingModificationOrderIds)
        {
            var current = _orders.GetValueOrDefault(id) ?? pending.Original;
            var quantity = Math.Max(pending.Original.Quantity, pending.Proposed.Quantity);
            if (quantity <= current.FilledQuantity)
                continue;
            decimal? MaxPrice(decimal? first, decimal? second) => first is null ? second : second is null ? first : Math.Max(first.Value, second.Value);
            reservations.Add(current with
            {
                Status = OrderStatus.Accepted,
                Quantity = quantity,
                LimitPrice = MaxPrice(pending.Original.LimitPrice, pending.Proposed.LimitPrice),
                StopPrice = MaxPrice(pending.Original.StopPrice, pending.Proposed.StopPrice),
                RoutedNotional = pending.Original.RoutedNotional is not null || pending.Proposed.RoutedNotional is not null
                    ? MaxPrice(MeasureOrderValue(pending.Original.Quantity, pending.Original.LimitPrice, pending.Original.StopPrice,
                        pending.Original.RoutedNotional, current.ContractMultiplier, current.UsesFaceValuePercentageOfPar),
                        MeasureOrderValue(pending.Proposed.Quantity, pending.Proposed.LimitPrice, pending.Proposed.StopPrice,
                        pending.Proposed.RoutedNotional, current.ContractMultiplier, current.UsesFaceValuePercentageOfPar))
                    : null
            });
        }
        return reservations;
    }
}
