using Meridian.Execution.Sdk;

namespace Meridian.Infrastructure.Adapters.InteractiveBrokers;

public sealed partial class IBBrokerageGateway
{
    // Incremented on every connect and disconnect; recovery evidence read across a change is
    // from another session and is refused rather than applied to the retained order.
    private int _connectionEpoch;

    /// <inheritdoc />
    public Task<ExecutionReport?> GetOrderForRecoveryAsync(string clientOrderId, CancellationToken ct = default)
        => RecoverFromOpenOrdersAsync(clientOrderId, null, ct);

    /// <inheritdoc />
    public Task<ExecutionReport?> GetAmendmentForRecoveryAsync(
        string clientOrderId, OrderState retainedOrder, PendingOrderAmendment amendment, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(retainedOrder);
        ArgumentNullException.ThrowIfNull(amendment);
        return RecoverFromOpenOrdersAsync(clientOrderId, amendment, ct);
    }

    /// <summary>
    /// Recovers a retained order from IB's authoritative open-order snapshot, matched by the
    /// orderRef Meridian submits as the client order id. IB amends in place, so the working order
    /// keeps its id and its echoed terms select the amendment outcome. A completed order is absent
    /// from the snapshot; absence proves nothing, so it and any ambiguous match stay unresolved.
    /// </summary>
    private async Task<ExecutionReport?> RecoverFromOpenOrdersAsync(
        string clientOrderId, PendingOrderAmendment? amendment, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(clientOrderId);
        ObjectDisposedException.ThrowIf(_disposed, this);
        EnsureConnected();
        var epoch = Volatile.Read(ref _connectionEpoch);

        var open = await GetOpenOrdersAsync(ct).ConfigureAwait(false);
        var matches = open.Where(order => string.Equals(order.ClientOrderId, clientOrderId, StringComparison.Ordinal)).ToArray();
        if (matches.Length != 1 || !int.TryParse(matches[0].OrderId, out var gatewayOrderId))
            return Current(null);
        var order = matches[0];
        if (amendment?.OriginalBrokerOrderId is { Length: > 0 } brokerId
            && !string.Equals(brokerId, order.OrderId, StringComparison.Ordinal))
            return Current(null);
        // Rebuilding a derivative context from the snapshot would lose contract detail that a
        // later amendment or cancellation needs; leave those for manual reconciliation.
        if (!_openOrderSecurityTypes.TryGetValue(gatewayOrderId, out var securityType)
            || securityType.Length > 0 && !string.Equals(securityType, "STK", StringComparison.OrdinalIgnoreCase))
            return Current(null);

        _latestOrderReports.TryGetValue(gatewayOrderId, out var latest);
        var filled = Math.Max(order.FilledQuantity, latest?.FilledQuantity ?? 0m);
        var fillPrice = latest?.FillPrice;
        if (filled < 0m || filled > order.Quantity || filled > 0m && fillPrice is not > 0m)
            return Current(null);

        var reportType = order.Status == OrderStatus.PartiallyFilled ? ExecutionReportType.PartialFill : ExecutionReportType.New;
        if (amendment is not null)
        {
            if (MatchesTerms(order, amendment.Proposed))
                reportType = ExecutionReportType.Modified;
            else if (MatchesTerms(order, amendment.Original))
                reportType = ExecutionReportType.ModificationRejected;
            else
                return Current(null);
        }

        // Restore enough context to route cancellation. Time-in-force is not in the snapshot, so
        // the context is marked recovered and amendment is refused rather than guessed.
        var context = new SubmittedOrderContext(gatewayOrderId, clientOrderId, clientOrderId, order.Symbol, order.Side,
            order.Type, order.Quantity, order.LimitPrice, order.StopPrice, TimeInForce.Day, null, null, null, null,
            order.CreatedAt, RecoveredFromSnapshot: true);
        _submittedOrders.TryAdd(gatewayOrderId, context);
        _gatewayOrderIdsByExternalId[clientOrderId] = gatewayOrderId;
        _gatewayOrderIdsByExternalId[gatewayOrderId.ToString()] = gatewayOrderId;

        var report = BuildExecutionReport(gatewayOrderId, reportType, order.Status, context, order.Symbol, order.Side,
            order.Quantity, filled, fillPrice, order.Status.ToString(), null, null, DateTimeOffset.UtcNow,
            fillPriceIsCumulativeAverage: true) with
        {
            OrderId = clientOrderId,
            HasOrderTerms = true,
            OrderLimitPrice = order.LimitPrice,
            OrderStopPrice = order.StopPrice
        };
        return Current(report);

        ExecutionReport? Current(ExecutionReport? result) => Volatile.Read(ref _connectionEpoch) == epoch
            ? result
            : throw new InvalidOperationException(
                "The IB connection changed during order recovery; evidence from the previous session was discarded.");
    }

    private static bool MatchesTerms(BrokerOrder order, OrderState terms)
        => order.Quantity == terms.Quantity && order.LimitPrice == terms.LimitPrice && order.StopPrice == terms.StopPrice;
}
