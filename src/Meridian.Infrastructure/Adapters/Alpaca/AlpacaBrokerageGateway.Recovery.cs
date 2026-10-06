using System.Net;
using System.Net.Http.Json;
using Meridian.Execution.Sdk;
using OrderSide = Meridian.Execution.Sdk.OrderSide;

namespace Meridian.Infrastructure.Adapters.Alpaca;

public sealed partial class AlpacaBrokerageGateway
{
    public async Task<ExecutionReport?> GetOrderForRecoveryAsync(string clientOrderId, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(clientOrderId);
        EnsureConnected();
        var generation = ConnectionGeneration;
        using var client = CreateHttpClient();
        using var response = await client.GetAsync(
            $"{BaseUrl}/v2/orders:by_client_order_id?client_order_id={Uri.EscapeDataString(clientOrderId)}", ct).ConfigureAwait(false);
        if (response.StatusCode == HttpStatusCode.NotFound)
            return null;
        response.EnsureSuccessStatusCode();
        var order = await response.Content.ReadFromJsonAsync(
            AlpacaBrokerageSerializerContext.Default.AlpacaOrderResponse, ct).ConfigureAwait(false);
        if (order is null || string.IsNullOrWhiteSpace(order.Id) || string.IsNullOrWhiteSpace(order.Symbol)
            || !string.Equals(order.ClientOrderId, clientOrderId, StringComparison.Ordinal)
            || order.Side is not ("buy" or "sell") || generation != ConnectionGeneration
            || order.Status is not ("new" or "accepted" or "pending_new" or "partially_filled" or "filled"
                or "pending_cancel" or "canceled" or "expired" or "rejected"))
            throw new InvalidDataException("Alpaca recovery returned incomplete or inconsistent order identity/state.");
        var quantity = ParseRequiredDecimal(order.Qty, "qty", clientOrderId);
        var filled = ParseRequiredDecimal(order.FilledQty, "filled_qty", clientOrderId);
        var average = ParseNullableDecimal(order.FilledAvgPrice);
        if (quantity <= 0m || filled < 0m || filled > quantity || filled > 0m && average is not > 0m)
            throw new InvalidDataException("Alpaca recovery returned invalid order quantities or fill economics.");
        var status = MapAlpacaStatus(order.Status);
        return new ExecutionReport
        {
            OrderId = clientOrderId,
            ClientOrderId = clientOrderId,
            GatewayOrderId = order.Id,
            Symbol = order.Symbol,
            Side = order.Side == "sell" ? OrderSide.Sell : OrderSide.Buy,
            OrderQuantity = quantity,
            FilledQuantity = filled,
            FillPrice = average,
            AssetClass = order.AssetClass,
            OrderStatus = status,
            ReportType = MapReconciliationReportType(status),
            Timestamp = ResolveReconciliationTimestamp(order, status)
        };
    }
}
