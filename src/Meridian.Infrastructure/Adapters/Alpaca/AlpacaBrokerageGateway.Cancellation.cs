using System.Net;
using System.Net.Http.Json;
using Meridian.Execution.Sdk;
using Microsoft.Extensions.Logging;
using OrderSide = Meridian.Execution.Sdk.OrderSide;
using OrderStatus = Meridian.Execution.Sdk.OrderStatus;

namespace Meridian.Infrastructure.Adapters.Alpaca;

public sealed partial class AlpacaBrokerageGateway
{
    /// <inheritdoc />
    public Task<ExecutionReport> CancelOrderAsync(string orderId, CancellationToken ct = default) =>
        CancelOrderAsync(
            new OrderCancellationIdentifier(orderId, OrderCancellationIdentifierKind.BrokerOrderId),
            ct);

    /// <inheritdoc />
    public async Task<ExecutionReport> CancelOrderAsync(
        OrderCancellationIdentifier identifier,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(identifier.Value);
        ObjectDisposedException.ThrowIf(_disposed, this);
        EnsureConnected();

        using var client = CreateHttpClient();

        // Alpaca's DELETE route accepts only its broker UUID. Client and broker identifiers are
        // separate namespaces, and shape is not evidence of which one the caller supplied: a
        // UUID-shaped client id must never resolve through the direct broker-id route first.
        var existing = await ResolveOrderForCancellationAsync(client, identifier, ct).ConfigureAwait(false);
        if (existing?.Id is not { Length: > 0 } brokerOrderId)
        {
            var unresolvedReport = new ExecutionReport
            {
                OrderId = identifier.Value,
                ClientOrderId = identifier.Kind is OrderCancellationIdentifierKind.ClientOrderId
                    ? identifier.Value
                    : null,
                GatewayOrderId = identifier.Kind is OrderCancellationIdentifierKind.BrokerOrderId
                    ? identifier.Value
                    : null,
                ReportType = ExecutionReportType.Rejected,
                Symbol = existing?.Symbol ?? string.Empty,
                Side = existing?.Side == "sell" ? OrderSide.Sell : OrderSide.Buy,
                // Failure to resolve an identifier is a rejected cancellation command, not
                // authoritative evidence that Alpaca rejected the still-potentially-live order.
                // Keep the state nonterminal so the OMS retains it in future kill-switch sweeps.
                OrderStatus = OrderStatus.PendingCancel,
                RejectReason = "Alpaca could not resolve the broker order ID required for cancellation.",
                Timestamp = DateTimeOffset.UtcNow,
            };
            await _reportChannel.Writer.WriteAsync(unresolvedReport, ct).ConfigureAwait(false);
            return unresolvedReport;
        }

        using var deleteResponse = await client.DeleteAsync(
            $"{BaseUrl}/v2/orders/{Uri.EscapeDataString(brokerOrderId)}",
            ct).ConfigureAwait(false);

        if (!deleteResponse.IsSuccessStatusCode)
        {
            var errorBody = await deleteResponse.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            _logger.LogWarning("Alpaca cancel failed with status {StatusCode}: {Body}", deleteResponse.StatusCode, errorBody);
        }

        var (verifiedOrder, verifiedStatus, verificationFailure) = deleteResponse.IsSuccessStatusCode
            ? await VerifyCancellationAgainstBrokerAsync(client, brokerOrderId, ct).ConfigureAwait(false)
            : (
                existing,
                MapAlpacaStatus(existing.Status),
                $"Cancel request failed with HTTP {(int)deleteResponse.StatusCode}.");

        // A 204 acknowledges only the cancel request. The re-read is the authoritative transition:
        // if the order filled in that race, publish the broker's cumulative fill evidence rather
        // than the pre-delete snapshot (which can still say zero filled).
        var authoritativeOrder = verifiedOrder ?? existing;

        var report = new ExecutionReport
        {
            OrderId = brokerOrderId,
            ClientOrderId = authoritativeOrder.ClientOrderId ?? existing.ClientOrderId,
            ReportType = verifiedStatus switch
            {
                OrderStatus.Cancelled => ExecutionReportType.Cancelled,
                OrderStatus.Filled => ExecutionReportType.Fill,
                OrderStatus.PartiallyFilled => ExecutionReportType.PartialFill,
                OrderStatus.Expired => ExecutionReportType.Expired,
                _ => ExecutionReportType.Rejected
            },
            Symbol = authoritativeOrder.Symbol ?? string.Empty,
            Side = authoritativeOrder.Side == "sell" ? OrderSide.Sell : OrderSide.Buy,
            OrderStatus = verifiedStatus,
            OrderQuantity = ParseDecimal(authoritativeOrder.Qty),
            FilledQuantity = ParseDecimal(authoritativeOrder.FilledQty),
            FillPrice = ParseNullableDecimal(authoritativeOrder.FilledAvgPrice),
            RejectReason = verificationFailure,
            GatewayOrderId = brokerOrderId,
            Timestamp = ResolveReconciliationTimestamp(authoritativeOrder, verifiedStatus),
        };
        await _reportChannel.Writer.WriteAsync(report, ct).ConfigureAwait(false);
        return report;
    }

    private async Task<AlpacaOrderResponse?> ResolveOrderForCancellationAsync(
        HttpClient client,
        OrderCancellationIdentifier identifier,
        CancellationToken ct)
    {
        if (identifier.Kind is OrderCancellationIdentifierKind.BrokerOrderId)
        {
            using var directResponse = await client.GetAsync(
                $"{BaseUrl}/v2/orders/{Uri.EscapeDataString(identifier.Value)}",
                ct).ConfigureAwait(false);
            if (!directResponse.IsSuccessStatusCode)
            {
                _logger.LogWarning(
                    "Alpaca could not resolve the broker order ID before cancellation; direct lookup returned {StatusCode}",
                    directResponse.StatusCode);
                return null;
            }

            var directOrder = await directResponse.Content.ReadFromJsonAsync(
                AlpacaBrokerageSerializerContext.Default.AlpacaOrderResponse, ct).ConfigureAwait(false);
            return string.Equals(directOrder?.Id, identifier.Value, StringComparison.Ordinal)
                ? directOrder
                : null;
        }

        if (identifier.Kind is not OrderCancellationIdentifierKind.ClientOrderId)
        {
            throw new ArgumentOutOfRangeException(
                nameof(identifier),
                identifier.Kind,
                "Unknown order-cancellation identifier namespace.");
        }

        using var clientIdResponse = await client.GetAsync(
            $"{BaseUrl}/v2/orders:by_client_order_id?client_order_id={Uri.EscapeDataString(identifier.Value)}",
            ct).ConfigureAwait(false);
        if (!clientIdResponse.IsSuccessStatusCode)
        {
            _logger.LogWarning(
                "Alpaca could not resolve the broker order ID before cancellation; client-order lookup returned {StatusCode}",
                clientIdResponse.StatusCode);
            return null;
        }

        var clientOrder = await clientIdResponse.Content.ReadFromJsonAsync(
            AlpacaBrokerageSerializerContext.Default.AlpacaOrderResponse, ct).ConfigureAwait(false);
        return clientOrder?.Id is { Length: > 0 }
            && string.Equals(clientOrder.ClientOrderId, identifier.Value, StringComparison.Ordinal)
                ? clientOrder
                : null;
    }

    private async Task<(AlpacaOrderResponse? Order, OrderStatus Status, string? FailureReason)>
        VerifyCancellationAgainstBrokerAsync(
        HttpClient client,
        string brokerOrderId,
        CancellationToken ct)
    {
        // HTTP 204 means Alpaca accepted the request, not that the order is already terminal.
        // Re-read the exact broker UUID (including for a bracket child) so pagination cannot hide
        // a still-fillable order, and report Cancelled only after Alpaca says so or returns 404.
        using var verificationResponse = await client.GetAsync(
            $"{BaseUrl}/v2/orders/{Uri.EscapeDataString(brokerOrderId)}",
            ct).ConfigureAwait(false);
        if (verificationResponse.StatusCode is HttpStatusCode.NotFound)
        {
            return (null, OrderStatus.Cancelled, null);
        }

        if (!verificationResponse.IsSuccessStatusCode)
        {
            return (
                null,
                OrderStatus.PendingCancel,
                $"Alpaca accepted the cancellation, but broker-order verification failed with HTTP {(int)verificationResponse.StatusCode}.");
        }

        var verifiedOrder = await verificationResponse.Content.ReadFromJsonAsync(
            AlpacaBrokerageSerializerContext.Default.AlpacaOrderResponse, ct).ConfigureAwait(false);
        if (verifiedOrder?.Id is not { Length: > 0 } verifiedOrderId
            || !string.Equals(verifiedOrderId, brokerOrderId, StringComparison.Ordinal))
        {
            return (
                null,
                OrderStatus.PendingCancel,
                "Alpaca accepted the cancellation, but broker-order verification returned no matching order state.");
        }

        var verifiedStatus = MapAlpacaStatus(verifiedOrder.Status);
        if (verifiedStatus is OrderStatus.Cancelled)
        {
            return (verifiedOrder, OrderStatus.Cancelled, null);
        }

        if (verifiedStatus is OrderStatus.Filled or OrderStatus.Rejected or OrderStatus.Expired)
        {
            return (
                verifiedOrder,
                verifiedStatus,
                $"Alpaca accepted the cancellation, but broker order became terminal as {verifiedStatus} rather than Cancelled.");
        }

        return (
            verifiedOrder,
            verifiedStatus,
            $"Alpaca accepted the cancellation, but broker order remains {verifiedStatus} after broker-order verification.");
    }
}
