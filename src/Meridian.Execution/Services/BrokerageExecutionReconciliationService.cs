using System.Globalization;
using Meridian.Execution.Sdk;
using Microsoft.Extensions.Logging;

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

    public async Task<BrokerageExecutionReconciliationReport> ReconcileOpenOrdersAsync(
        IBrokerageGateway gateway,
        IOrderManager orderManager,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(gateway);
        ArgumentNullException.ThrowIfNull(orderManager);

        var reconciledAt = DateTimeOffset.UtcNow;
        var health = await CheckHealthAsync(gateway, ct).ConfigureAwait(false);
        var breaks = new List<BrokerageExecutionReconciliationBreak>();
        IReadOnlyList<BrokerOrder> brokerOrders;

        try
        {
            brokerOrders = await gateway.GetOpenOrdersAsync(ct).ConfigureAwait(false);
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

            return BuildReport(gateway, health, [], breaks, reconciledAt);
        }

        var localOrders = orderManager.GetOpenOrders()
            .ToDictionary(static order => order.OrderId, StringComparer.OrdinalIgnoreCase);
        var matchedLocalIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var matches = new List<BrokerageExecutionOrderMatch>();

        foreach (var brokerOrder in brokerOrders)
        {
            ct.ThrowIfCancellationRequested();
            var clientOrderId = NormalizeId(brokerOrder.ClientOrderId);
            if (clientOrderId is null)
            {
                breaks.Add(CreateBreak(
                    BrokerageExecutionReconciliationBreakKind.BrokerOrderMissingClientOrderId,
                    null,
                    brokerOrder,
                    "Broker open order has no client order id, so it cannot be tied to the OMS order ledger."));
                continue;
            }

            if (!localOrders.TryGetValue(clientOrderId, out var localOrder))
            {
                breaks.Add(CreateBreak(
                    BrokerageExecutionReconciliationBreakKind.MissingInOrderManager,
                    null,
                    brokerOrder,
                    "Broker reports an open order that the OMS is not tracking."));
                continue;
            }

            matchedLocalIds.Add(localOrder.OrderId);
            var beforeCount = breaks.Count;
            CompareOrder(localOrder, brokerOrder, breaks);

            if (breaks.Count == beforeCount)
            {
                matches.Add(new BrokerageExecutionOrderMatch(
                    localOrder.OrderId,
                    brokerOrder.OrderId,
                    localOrder.Symbol,
                    localOrder.Status));
            }
        }

        foreach (var localOrder in localOrders.Values)
        {
            if (!matchedLocalIds.Contains(localOrder.OrderId))
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
        }

        if (breaks.Count > 0)
        {
            _logger.LogWarning(
                "Broker open-order reconciliation found {BreakCount} break(s) for gateway {GatewayId}",
                breaks.Count,
                gateway.GatewayId);
        }

        return BuildReport(gateway, health, matches, breaks, reconciledAt);
    }

    private async Task<BrokerHealthStatus> CheckHealthAsync(IBrokerageGateway gateway, CancellationToken ct)
    {
        try
        {
            return await gateway.CheckHealthAsync(ct).ConfigureAwait(false);
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
        DateTimeOffset reconciledAt) => new(
            GatewayId: gateway.GatewayId,
            BrokerDisplayName: gateway.BrokerDisplayName,
            Health: health,
            MatchedOpenOrders: matches,
            Breaks: breaks,
            ReconciledAt: reconciledAt);

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

    private static string? NormalizeId(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static string DescribeOrder(OrderState order) =>
        $"{order.Status} {order.Side} {FormatDecimal(order.Quantity)} {order.Symbol}";

    private static string DescribeOrder(BrokerOrder order) =>
        $"{order.Status} {order.Side} {FormatDecimal(order.Quantity)} {order.Symbol}";

    private static string FormatDecimal(decimal value) =>
        value.ToString("G29", CultureInfo.InvariantCulture);
}

public sealed record BrokerageExecutionReconciliationReport(
    string GatewayId,
    string BrokerDisplayName,
    BrokerHealthStatus Health,
    IReadOnlyList<BrokerageExecutionOrderMatch> MatchedOpenOrders,
    IReadOnlyList<BrokerageExecutionReconciliationBreak> Breaks,
    DateTimeOffset ReconciledAt)
{
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
    StatusMismatch
}
