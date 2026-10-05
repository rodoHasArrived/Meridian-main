using System.Globalization;
using Meridian.Execution.Sdk;
using Microsoft.Extensions.Logging;
using static Meridian.Contracts.Text.TextPrimitives;

namespace Meridian.Execution.Services;

/// <summary>
/// Compares active broker order state against the local OMS view before operators rely on
/// broker-backed live execution evidence.
/// </summary>
public sealed class BrokerageExecutionReconciliationService
{
    private readonly ILogger<BrokerageExecutionReconciliationService> _logger;

    public BrokerageExecutionReconciliationService(ILogger<BrokerageExecutionReconciliationService> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Resolves retained dispatch uncertainty using a broker lookup by the original client id.
    /// Missing broker evidence remains unresolved; no recovery path submits an order.
    /// </summary>
    public async Task RecoverOrdersAsync(
        IBrokerageGateway gateway,
        OrderManagementSystem orderManager,
        Guid fundAccountId,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(gateway);
        ArgumentNullException.ThrowIfNull(orderManager);
        ct.ThrowIfCancellationRequested();
        if (gateway is not IBrokerageOrderRecoveryGateway recoveryGateway)
            return;
        orderManager.EnsureBrokerageRecoveryScope(gateway);
        // Accepted or terminal acknowledgements can precede a late fill lost during an outage.
        // Re-query every retained order with possible remaining execution on explicit sync;
        // cancellation and expiry do not prove that no earlier execution arrived late.
        var recoveryOrders = orderManager.GetRecoveryOrders(fundAccountId)
            .Concat(orderManager.GetRetainedBrokerageOrders(fundAccountId)
                .Where(static order => order.Status is OrderStatus.PendingNew or OrderStatus.Accepted
                    or OrderStatus.PartiallyFilled or OrderStatus.PendingCancel
                    or OrderStatus.Cancelled or OrderStatus.Expired))
            .DistinctBy(static order => order.OrderId, StringComparer.Ordinal)
            .ToArray();
        if (recoveryOrders.Length == 0)
            return;
        foreach (var order in recoveryOrders)
        {
            ct.ThrowIfCancellationRequested();
            var report = await recoveryGateway.GetOrderForRecoveryAsync(order.OrderId, ct).ConfigureAwait(false);
            if (report is null)
            {
                // A terminal local order is absent from both open books, so a missing lookup
                // cannot be represented by open-order discrepancies. Keep synchronization
                // explicitly blocked until the broker can prove its cumulative executions.
                if (order.Status is OrderStatus.Cancelled or OrderStatus.Expired or OrderStatus.Rejected or OrderStatus.Filled)
                    throw new InvalidDataException("Broker could not verify a retained terminal order; late-fill recovery remains unresolved.");
                continue;
            }

            // The report pump may have advanced the order while the broker query was in flight.
            // Compare against the current retained state before passing evidence to the OMS.
            var current = orderManager.GetOrder(order.OrderId);
            if (current is null || current.FundAccountId != fundAccountId
                || !string.Equals(report.ClientOrderId ?? report.OrderId, order.OrderId, StringComparison.Ordinal)
                || !string.Equals(report.Symbol, current.Symbol, StringComparison.OrdinalIgnoreCase)
                || report.Side != current.Side
                || report.OrderQuantity != current.Quantity
                || report.FilledQuantity < current.FilledQuantity
                || report.FilledQuantity < 0m
                || report.FilledQuantity > current.Quantity
                || (report.OrderStatus == OrderStatus.Filled && report.FilledQuantity != current.Quantity)
                || (report.OrderStatus == OrderStatus.PartiallyFilled
                    && (report.FilledQuantity <= 0m || report.FilledQuantity >= current.Quantity))
                || (report.FilledQuantity > current.FilledQuantity && report.FillPrice is not > 0m))
            {
                throw new InvalidDataException("Broker recovery evidence is incomplete or does not match the retained account order.");
            }

            await orderManager.ReconcileRecoveryOrderAsync(report, ct).ConfigureAwait(false);
        }
    }

    public Task<BrokerageExecutionReconciliationReport> ReconcileOpenOrdersAsync(
        IBrokerageGateway gateway,
        IOrderManager orderManager,
        CancellationToken ct = default) =>
        ReconcileOpenOrdersAsync(gateway, orderManager, fundAccountId: null, ct);

    /// <summary>
    /// Reconciles the exposure-reserving OMS book for one account against a gateway already
    /// bound to that account. Reconciliation reports evidence; it never submits or updates orders.
    /// </summary>
    public async Task<BrokerageExecutionReconciliationReport> ReconcileOpenOrdersAsync(
        IBrokerageGateway gateway,
        IOrderManager orderManager,
        Guid? fundAccountId,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(gateway);
        ArgumentNullException.ThrowIfNull(orderManager);

        var reconciledAt = DateTimeOffset.UtcNow;
        var health = await CheckHealthAsync(gateway, ct).ConfigureAwait(false);
        var breaks = new List<BrokerageExecutionReconciliationBreak>();
        var exposureOrders = orderManager.GetExposureReservingOrders();
        var localGroups = exposureOrders
            .Where(order => !fundAccountId.HasValue || order.FundAccountId == fundAccountId)
            .GroupBy(static order => order.OrderId, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var localOrders = localGroups.Select(static group => group.First()).ToArray();
        var pendingExposureIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var group in localGroups.Where(static group => group.Count() > 1))
        {
            // The exposure book can include an open order and unapplied fill reservations
            // under the same id. Do not collapse that handoff window into clean evidence.
            pendingExposureIds.Add(group.Key);
            breaks.Add(new BrokerageExecutionReconciliationBreak(
                BrokerageExecutionReconciliationBreakKind.PendingLocalExposure,
                LocalOrderId: group.Key,
                BrokerOrderId: null,
                ClientOrderId: group.Key,
                Symbol: group.First().Symbol,
                Description: "OMS retains additional exposure for this order while fill handoff is pending.",
                LocalValue: string.Join("; ", group.Select(DescribeOrder)),
                BrokerValue: null));
        }
        IReadOnlyList<BrokerOrder> brokerOrders;

        try
        {
            brokerOrders = await gateway.GetOpenOrdersAsync(ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Broker open-order reconciliation query failed for gateway {GatewayId}", gateway.GatewayId);
            breaks.Add(new BrokerageExecutionReconciliationBreak(
                BrokerageExecutionReconciliationBreakKind.BrokerOpenOrderQueryFailed,
                LocalOrderId: null,
                BrokerOrderId: null,
                ClientOrderId: null,
                Symbol: null,
                Description: "Broker open-order query failed.",
                LocalValue: null,
                BrokerValue: ex.Message));

            return BuildReport(gateway, health, [], breaks, reconciledAt, fundAccountId, localOrders.Length, null);
        }

        var traceableBrokerOrders = new List<TraceableBrokerOrder>(brokerOrders.Count);
        var matches = new List<BrokerageExecutionOrderMatch>();

        foreach (var brokerOrder in brokerOrders)
        {
            ct.ThrowIfCancellationRequested();
            var clientOrderId = NormalizeOptional(brokerOrder.ClientOrderId);
            if (clientOrderId is null)
            {
                breaks.Add(CreateBreak(
                    BrokerageExecutionReconciliationBreakKind.BrokerOrderMissingClientOrderId,
                    null,
                    brokerOrder,
                    "Broker open order has no client order id, so it cannot be tied to the OMS order ledger."));
                continue;
            }

            traceableBrokerOrders.Add(new TraceableBrokerOrder(clientOrderId, brokerOrder));
        }

        var duplicateClientIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var group in traceableBrokerOrders
            .GroupBy(static order => order.ClientOrderId, StringComparer.OrdinalIgnoreCase)
            .Where(static group => group.Count() > 1))
        {
            duplicateClientIds.Add(group.Key);
            breaks.Add(new BrokerageExecutionReconciliationBreak(
                BrokerageExecutionReconciliationBreakKind.DuplicateBrokerClientOrderId,
                LocalOrderId: localOrders.FirstOrDefault(order =>
                    string.Equals(order.OrderId, group.Key, StringComparison.OrdinalIgnoreCase))?.OrderId,
                BrokerOrderId: group.First().Order.OrderId,
                ClientOrderId: group.Key,
                Symbol: group.First().Order.Symbol,
                Description: "Broker reports multiple open orders with the same client order id.",
                LocalValue: null,
                BrokerValue: string.Join(", ", group.Select(static order => order.Order.OrderId))));
        }

        if (fundAccountId.HasValue)
        {
            traceableBrokerOrders = traceableBrokerOrders.Where(brokerOrder =>
            {
                // Include retained terminal orders in the ownership check: a late broker
                // callback must never attach another account's order to this account.
                var knownLocal = exposureOrders.FirstOrDefault(order =>
                    string.Equals(order.OrderId, brokerOrder.ClientOrderId, StringComparison.OrdinalIgnoreCase))
                    ?? orderManager.GetOrder(brokerOrder.ClientOrderId);
                if (knownLocal is null || knownLocal.FundAccountId == fundAccountId)
                    return true;

                breaks.Add(new BrokerageExecutionReconciliationBreak(
                    BrokerageExecutionReconciliationBreakKind.AccountScopeMismatch,
                    LocalOrderId: knownLocal.OrderId,
                    BrokerOrderId: brokerOrder.Order.OrderId,
                    ClientOrderId: brokerOrder.ClientOrderId,
                    Symbol: brokerOrder.Order.Symbol,
                    Description: "Broker order matches a retained OMS order outside the requested account scope.",
                    LocalValue: knownLocal.FundAccountId?.ToString() ?? "Unscoped",
                    BrokerValue: fundAccountId.Value.ToString()));
                return false;
            }).ToList();
        }

        var comparison = ReconciliationSetComparer.Compare(
            localOrders,
            traceableBrokerOrders,
            static order => order.OrderId,
            static brokerOrder => brokerOrder.ClientOrderId,
            StringComparer.OrdinalIgnoreCase);

        foreach (var brokerOrder in comparison.MissingLocal)
        {
            breaks.Add(CreateBreak(
                BrokerageExecutionReconciliationBreakKind.MissingInOrderManager,
                null,
                brokerOrder.Order,
                "Broker reports an open order that the OMS is not tracking."));
        }

        foreach (var match in comparison.Matches)
        {
            var localOrder = match.Local;
            var brokerOrder = match.External.Order;
            var beforeCount = breaks.Count;
            CompareOrder(localOrder, brokerOrder, breaks);

            if (breaks.Count == beforeCount &&
                !duplicateClientIds.Contains(localOrder.OrderId) &&
                !pendingExposureIds.Contains(localOrder.OrderId))
            {
                matches.Add(new BrokerageExecutionOrderMatch(
                    localOrder.OrderId,
                    brokerOrder.OrderId,
                    localOrder.Symbol,
                    localOrder.Status));
            }
        }

        foreach (var localOrder in comparison.MissingExternal)
        {
            breaks.Add(new BrokerageExecutionReconciliationBreak(
                BrokerageExecutionReconciliationBreakKind.MissingInBrokerage,
                LocalOrderId: localOrder.OrderId,
                BrokerOrderId: null,
                ClientOrderId: localOrder.OrderId,
                Symbol: localOrder.Symbol,
                Description: "OMS tracks an open order that the broker did not report.",
                LocalValue: DescribeOrder(localOrder),
                BrokerValue: null));
        }

        if (breaks.Count > 0)
        {
            _logger.LogWarning(
                "Broker open-order reconciliation found {BreakCount} break(s) for gateway {GatewayId}",
                breaks.Count,
                gateway.GatewayId);
        }

        return BuildReport(gateway, health, matches, breaks, reconciledAt, fundAccountId, localOrders.Length, brokerOrders.Count);
    }

    private async Task<BrokerHealthStatus> CheckHealthAsync(IBrokerageGateway gateway, CancellationToken ct)
    {
        try
        {
            return await gateway.CheckHealthAsync(ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Broker health check failed during open-order reconciliation for gateway {GatewayId}", gateway.GatewayId);
            return BrokerHealthStatus.Unhealthy(ex.Message);
        }
    }

    private static BrokerageExecutionReconciliationReport BuildReport(
        IBrokerageGateway gateway,
        BrokerHealthStatus health,
        IReadOnlyList<BrokerageExecutionOrderMatch> matches,
        IReadOnlyList<BrokerageExecutionReconciliationBreak> breaks,
        DateTimeOffset reconciledAt,
        Guid? fundAccountId,
        int localOpenOrderCount,
        int? brokerOpenOrderCount) => new(
            GatewayId: gateway.GatewayId,
            BrokerDisplayName: gateway.BrokerDisplayName,
            Health: health,
            MatchedOpenOrders: matches,
            Breaks: breaks,
            ReconciledAt: reconciledAt)
        {
            FundAccountId = fundAccountId,
            LocalOpenOrderCount = localOpenOrderCount,
            BrokerOpenOrderCount = brokerOpenOrderCount
        };

    private static void CompareOrder(
        OrderState localOrder,
        BrokerOrder brokerOrder,
        List<BrokerageExecutionReconciliationBreak> breaks)
    {
        if (!string.Equals(localOrder.Symbol, brokerOrder.Symbol, StringComparison.OrdinalIgnoreCase))
        {
            breaks.Add(CreateMismatch(
                BrokerageExecutionReconciliationBreakKind.SymbolMismatch,
                localOrder,
                brokerOrder,
                "Symbol differs between OMS and broker.",
                localOrder.Symbol,
                brokerOrder.Symbol));
        }

        if (localOrder.Side != brokerOrder.Side)
        {
            breaks.Add(CreateMismatch(
                BrokerageExecutionReconciliationBreakKind.SideMismatch,
                localOrder,
                brokerOrder,
                "Order side differs between OMS and broker.",
                localOrder.Side.ToString(),
                brokerOrder.Side.ToString()));
        }

        if (localOrder.Type != brokerOrder.Type)
        {
            breaks.Add(CreateMismatch(
                BrokerageExecutionReconciliationBreakKind.TypeMismatch,
                localOrder,
                brokerOrder,
                "Order type differs between OMS and broker.",
                localOrder.Type.ToString(),
                brokerOrder.Type.ToString()));
        }

        if (localOrder.Quantity != brokerOrder.Quantity)
        {
            breaks.Add(CreateMismatch(
                BrokerageExecutionReconciliationBreakKind.QuantityMismatch,
                localOrder,
                brokerOrder,
                "Order quantity differs between OMS and broker.",
                FormatDecimal(localOrder.Quantity),
                FormatDecimal(brokerOrder.Quantity)));
        }

        if (localOrder.FilledQuantity != brokerOrder.FilledQuantity)
        {
            breaks.Add(CreateMismatch(
                BrokerageExecutionReconciliationBreakKind.FilledQuantityMismatch,
                localOrder,
                brokerOrder,
                "Filled quantity differs between OMS and broker.",
                FormatDecimal(localOrder.FilledQuantity),
                FormatDecimal(brokerOrder.FilledQuantity)));
        }

        if (localOrder.Status != brokerOrder.Status)
        {
            breaks.Add(CreateMismatch(
                BrokerageExecutionReconciliationBreakKind.StatusMismatch,
                localOrder,
                brokerOrder,
                "Order status differs between OMS and broker.",
                localOrder.Status.ToString(),
                brokerOrder.Status.ToString()));
        }
    }

    private static BrokerageExecutionReconciliationBreak CreateMismatch(
        BrokerageExecutionReconciliationBreakKind kind,
        OrderState localOrder,
        BrokerOrder brokerOrder,
        string description,
        string localValue,
        string brokerValue) => new(
            kind,
            LocalOrderId: localOrder.OrderId,
            BrokerOrderId: brokerOrder.OrderId,
            ClientOrderId: brokerOrder.ClientOrderId,
            Symbol: localOrder.Symbol,
            Description: description,
            LocalValue: localValue,
            BrokerValue: brokerValue);

    private static BrokerageExecutionReconciliationBreak CreateBreak(
        BrokerageExecutionReconciliationBreakKind kind,
        OrderState? localOrder,
        BrokerOrder brokerOrder,
        string description) => new(
            kind,
            LocalOrderId: localOrder?.OrderId,
            BrokerOrderId: brokerOrder.OrderId,
            ClientOrderId: brokerOrder.ClientOrderId,
            Symbol: localOrder?.Symbol ?? brokerOrder.Symbol,
            Description: description,
            LocalValue: localOrder is null ? null : DescribeOrder(localOrder),
            BrokerValue: DescribeOrder(brokerOrder));

    private static string DescribeOrder(OrderState order) =>
        $"{order.Status} {order.Side} {FormatDecimal(order.Quantity)} {order.Symbol}";

    private static string DescribeOrder(BrokerOrder order) =>
        $"{order.Status} {order.Side} {FormatDecimal(order.Quantity)} {order.Symbol}";

    private static string FormatDecimal(decimal value) =>
        value.ToString("G29", CultureInfo.InvariantCulture);

    private sealed record TraceableBrokerOrder(string ClientOrderId, BrokerOrder Order);
}

public sealed record BrokerageExecutionReconciliationReport(
    string GatewayId,
    string BrokerDisplayName,
    BrokerHealthStatus Health,
    IReadOnlyList<BrokerageExecutionOrderMatch> MatchedOpenOrders,
    IReadOnlyList<BrokerageExecutionReconciliationBreak> Breaks,
    DateTimeOffset ReconciledAt)
{
    public Guid? FundAccountId { get; init; }

    /// <summary>Distinct OMS orders whose exposure is still reserved in the reconciled scope.</summary>
    public int LocalOpenOrderCount { get; init; }

    /// <summary>Broker rows returned, including duplicates; unknown when the broker query fails.</summary>
    public int? BrokerOpenOrderCount { get; init; }
    public bool IsClean => Health.IsHealthy && Breaks.Count == 0;
}

public sealed record BrokerageExecutionOrderMatch(
    string LocalOrderId,
    string BrokerOrderId,
    string Symbol,
    OrderStatus Status);

public sealed record BrokerageExecutionReconciliationBreak(
    BrokerageExecutionReconciliationBreakKind Kind,
    string? LocalOrderId,
    string? BrokerOrderId,
    string? ClientOrderId,
    string? Symbol,
    string Description,
    string? LocalValue,
    string? BrokerValue);

public enum BrokerageExecutionReconciliationBreakKind
{
    BrokerOpenOrderQueryFailed,
    BrokerOrderMissingClientOrderId,
    MissingInOrderManager,
    MissingInBrokerage,
    SymbolMismatch,
    SideMismatch,
    TypeMismatch,
    QuantityMismatch,
    FilledQuantityMismatch,
    StatusMismatch,
    AccountScopeMismatch,
    DuplicateBrokerClientOrderId,
    PendingLocalExposure
}
