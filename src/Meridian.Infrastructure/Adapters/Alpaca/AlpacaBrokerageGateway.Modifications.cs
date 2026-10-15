using System.Net.Http.Json;
using Meridian.Execution.Sdk;
using OrderSide = Meridian.Execution.Sdk.OrderSide;

namespace Meridian.Infrastructure.Adapters.Alpaca;

public sealed partial class AlpacaBrokerageGateway
{
    /// <inheritdoc />
    public Task<ExecutionReport> ModifyOrderAsync(
        string orderId, OrderModification modification, CancellationToken ct = default) =>
        ModifyOrderAsync(new OrderCancellationIdentifier(orderId, OrderCancellationIdentifierKind.BrokerOrderId), modification, ct);

    /// <inheritdoc />
    public async Task<ExecutionReport> ModifyOrderAsync(
        OrderCancellationIdentifier identifier, OrderModification modification, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(identifier.Value);
        ArgumentNullException.ThrowIfNull(modification);
        ObjectDisposedException.ThrowIf(_disposed, this);
        EnsureConnected();

        using var client = CreateHttpClient();
        var existing = identifier.Kind == OrderCancellationIdentifierKind.ClientOrderId
            ? await ResolveOrderForCancellationAsync(client, identifier, ct).ConfigureAwait(false)
            : null;
        var brokerOrderId = identifier.Kind switch
        {
            OrderCancellationIdentifierKind.BrokerOrderId => identifier.Value,
            OrderCancellationIdentifierKind.ClientOrderId => existing?.Id
                ?? throw new InvalidOperationException("Alpaca could not resolve the broker identity required for amendment."),
            _ => throw new ArgumentOutOfRangeException(nameof(identifier))
        };

        var payload = new AlpacaOrderModifyPayload
        {
            Qty = FormatDecimal(modification.NewQuantity),
            LimitPrice = FormatDecimal(modification.NewLimitPrice),
            StopPrice = FormatDecimal(modification.NewStopPrice),
            Trail = FormatDecimal(modification.NewTrail),
        };

        HttpResponseMessage response;
        // Cancellation observed here is still provably before the network attempt.
        ct.ThrowIfCancellationRequested();
        try
        {
            response = await client.PatchAsJsonAsync(
                $"{BaseUrl}/v2/orders/{Uri.EscapeDataString(brokerOrderId)}", payload,
                AlpacaBrokerageSerializerContext.Default.AlpacaOrderModifyPayload, ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is HttpRequestException or OperationCanceledException)
        {
            var pending = CreatePendingModification(brokerOrderId, existing,
                "Alpaca amendment acknowledgement was lost; reconcile the replacement before retrying.");
            _reportChannel.Writer.TryWrite(pending);
            return pending;
        }
        using var responseLifetime = response;

        if ((int)response.StatusCode >= 500 || (int)response.StatusCode == 408)
        {
            var pending = CreatePendingModification(brokerOrderId, existing,
                $"Alpaca amendment outcome is uncertain after HTTP {(int)response.StatusCode}; reconcile before retrying.");
            _reportChannel.Writer.TryWrite(pending);
            return pending;
        }

        if (!response.IsSuccessStatusCode)
        {
            // A refusal belongs to the amendment command. Re-read the original order because
            // executions can race the PATCH, and never turn this refusal into order rejection.
            existing = await ResolveOrderForCancellationAsync(client,
                new OrderCancellationIdentifier(brokerOrderId, OrderCancellationIdentifierKind.BrokerOrderId), ct).ConfigureAwait(false)
                ?? existing;
            var failure = new ExecutionReport
            {
                OrderId = brokerOrderId,
                GatewayOrderId = brokerOrderId,
                ClientOrderId = existing?.ClientOrderId,
                ReportType = ExecutionReportType.ModificationRejected,
                Symbol = existing?.Symbol ?? string.Empty,
                Side = existing?.Side == "sell" ? OrderSide.Sell : OrderSide.Buy,
                OrderStatus = existing is null ? OrderStatus.PendingNew : MapAlpacaStatus(existing.Status),
                OrderQuantity = ParseDecimal(existing?.Qty),
                FilledQuantity = ParseDecimal(existing?.FilledQty),
                FillPrice = ParseNullableDecimal(existing?.FilledAvgPrice),
                FillPriceIsCumulativeAverage = true,
                RejectReason = $"Alpaca amendment failed with HTTP {(int)response.StatusCode}.",
                Timestamp = DateTimeOffset.UtcNow
            };
            await _reportChannel.Writer.WriteAsync(failure, ct).ConfigureAwait(false);
            return failure;
        }

        var order = await response.Content.ReadFromJsonAsync(
            AlpacaBrokerageSerializerContext.Default.AlpacaOrderResponse, ct).ConfigureAwait(false);
        if (order?.Status == "rejected")
        {
            var original = await ResolveOrderForCancellationAsync(client,
                new OrderCancellationIdentifier(brokerOrderId, OrderCancellationIdentifierKind.BrokerOrderId), ct).ConfigureAwait(false);
            if (original is null)
                throw new InvalidDataException("Alpaca replacement was rejected, but the original order state could not be verified.");
            var refused = new ExecutionReport
            {
                OrderId = brokerOrderId,
                GatewayOrderId = brokerOrderId,
                ClientOrderId = original.ClientOrderId,
                ReportType = ExecutionReportType.ModificationRejected,
                Symbol = original.Symbol ?? string.Empty,
                Side = original.Side == "sell" ? OrderSide.Sell : OrderSide.Buy,
                OrderStatus = MapAlpacaStatus(original.Status),
                OrderQuantity = ParseDecimal(original.Qty),
                FilledQuantity = ParseDecimal(original.FilledQty),
                FillPrice = ParseNullableDecimal(original.FilledAvgPrice),
                FillPriceIsCumulativeAverage = true,
                RejectReason = "Alpaca rejected the replacement order; the original order state was retained.",
                Timestamp = original.UpdatedAt ?? DateTimeOffset.UtcNow
            };
            await _reportChannel.Writer.WriteAsync(refused, ct).ConfigureAwait(false);
            return refused;
        }
        if (order is null || string.IsNullOrWhiteSpace(order.Id) || string.IsNullOrWhiteSpace(order.ClientOrderId)
            || string.IsNullOrWhiteSpace(order.Symbol) || order.Side is not ("buy" or "sell")
            || existing is not null && (!string.Equals(order.Symbol, existing.Symbol, StringComparison.OrdinalIgnoreCase)
                || order.Side != existing.Side)
            || order.Status is not ("new" or "accepted" or "pending_new" or "pending_replace" or "partially_filled"
                or "filled" or "pending_cancel" or "canceled" or "expired" or "rejected")
            || order.Replaces is { Length: > 0 } && !string.Equals(order.Replaces, brokerOrderId, StringComparison.Ordinal))
            throw new InvalidDataException("Alpaca amendment acknowledgement lacks consistent replacement identity/state.");
        var quantity = ParseRequiredDecimal(order.Qty, "qty", order.Id);
        var filled = ParseRequiredDecimal(order.FilledQty, "filled_qty", order.Id);
        var average = ParseNullableDecimal(order.FilledAvgPrice);
        if (quantity <= 0m || filled < 0m || filled > quantity || filled > 0m && average is not > 0m)
            throw new InvalidDataException("Alpaca amendment acknowledgement lacks valid cumulative fill economics.");

        var report = new ExecutionReport
        {
            OrderId = order.Id,
            GatewayOrderId = order.Id,
            ClientOrderId = existing?.ClientOrderId ?? order.ClientOrderId,
            ReplacesOrderId = brokerOrderId,
            ReportType = order.Status == "rejected" ? ExecutionReportType.ModificationRejected : ExecutionReportType.Modified,
            Symbol = order.Symbol,
            Side = order.Side == "sell" ? OrderSide.Sell : OrderSide.Buy,
            OrderStatus = MapAlpacaStatus(order.Status),
            OrderQuantity = quantity,
            FilledQuantity = filled,
            FillPrice = average,
            FillPriceIsCumulativeAverage = true,
            Timestamp = order.UpdatedAt ?? DateTimeOffset.UtcNow,
        };
        await _reportChannel.Writer.WriteAsync(report, ct).ConfigureAwait(false);
        return report;
    }

    private static ExecutionReport CreatePendingModification(string brokerOrderId, AlpacaOrderResponse? existing, string reason) => new()
    {
        OrderId = brokerOrderId,
        GatewayOrderId = brokerOrderId,
        ClientOrderId = existing?.ClientOrderId,
        ReportType = ExecutionReportType.ModificationPending,
        Symbol = existing?.Symbol ?? string.Empty,
        Side = existing?.Side == "sell" ? OrderSide.Sell : OrderSide.Buy,
        OrderStatus = existing is null ? OrderStatus.PendingNew : MapAlpacaStatus(existing.Status),
        OrderQuantity = ParseDecimal(existing?.Qty),
        FilledQuantity = ParseDecimal(existing?.FilledQty),
        FillPrice = ParseNullableDecimal(existing?.FilledAvgPrice),
        FillPriceIsCumulativeAverage = true,
        RejectReason = reason,
        Timestamp = DateTimeOffset.UtcNow
    };

}
