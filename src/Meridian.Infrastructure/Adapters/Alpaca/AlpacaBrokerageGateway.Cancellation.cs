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

        // While a replacement is unconfirmed the OMS keeps routing cancellation to the predecessor,
        // but the candidate successor may already be live. Cancel every possible successor first,
        // or an operator or kill-switch cancel can leave that candidate working.
        var successorFailure = await CancelUnconfirmedSuccessorAsync(client, brokerOrderId, ct).ConfigureAwait(false);

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
        if (successorFailure is not null && verifiedStatus is OrderStatus.Cancelled)
        {
            // The predecessor is cancelled but a possible successor is not proven inactive. Stay
            // nonterminal so the OMS keeps the order in future cancellation sweeps.
            verifiedStatus = OrderStatus.PendingCancel;
            verificationFailure = successorFailure;
        }

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
            FillPriceIsCumulativeAverage = true,
            RejectReason = verificationFailure,
            GatewayOrderId = brokerOrderId,
            Timestamp = ResolveReconciliationTimestamp(authoritativeOrder, verifiedStatus),
        };
        await _reportChannel.Writer.WriteAsync(report, ct).ConfigureAwait(false);
        return report;
    }

    /// <summary>
    /// Cancels the candidate successor of an unresolved replacement attempt. Returns null when no
    /// candidate exists or it is proven inactive, otherwise the reason it may still be working.
    /// </summary>
    private async Task<string?> CancelUnconfirmedSuccessorAsync(
        HttpClient client, string predecessorId, CancellationToken ct)
    {
        if (!_replacementAttempts.TryGetValue(predecessorId, out var attempt)
            || attempt.Resolution is not null
            || attempt.CandidateId is not { Length: > 0 } candidateId
            || string.Equals(candidateId, predecessorId, StringComparison.Ordinal))
            return null;

        using var candidateDelete = await client.DeleteAsync(
            $"{BaseUrl}/v2/orders/{Uri.EscapeDataString(candidateId)}", ct).ConfigureAwait(false);
        if (candidateDelete.StatusCode is HttpStatusCode.NotFound)
            return null;

        // 422 means Alpaca will not cancel it (often already terminal); the re-read decides.
        var (candidateOrder, candidateStatus, _) = await VerifyCancellationAgainstBrokerAsync(client, candidateId, ct).ConfigureAwait(false);
        if (candidateStatus is not (OrderStatus.Cancelled or OrderStatus.Filled or OrderStatus.Rejected or OrderStatus.Expired))
            return $"Alpaca cancelled the predecessor, but its unconfirmed replacement {candidateId} remains {candidateStatus}.";
        if (ParseDecimal(candidateOrder?.FilledQty) <= 0m)
            return null;
        // A terminal candidate that executed proves the replacement took effect. Publish its
        // cumulative fills through the replacement chain before the predecessor's cancel report,
        // which describes only the predecessor; otherwise those fills depend on a delayed stream.
        var resolution = await ObserveReplacementAsync(client, attempt, ct).ConfigureAwait(false);
        if (resolution is not { ReportType: ExecutionReportType.Modified })
            return $"Alpaca replacement {candidateId} executed before cancellation but its replacement chain is unconfirmed; recovery must book its fills.";
        await _reportChannel.Writer.WriteAsync(resolution, ct).ConfigureAwait(false);
        return null;
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
