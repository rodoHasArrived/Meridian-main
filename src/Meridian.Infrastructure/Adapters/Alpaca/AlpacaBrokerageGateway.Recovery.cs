using System.Net;
using System.Net.Http.Json;
using Meridian.Execution.Sdk;

namespace Meridian.Infrastructure.Adapters.Alpaca;

public sealed partial class AlpacaBrokerageGateway
{
    public async Task<ExecutionReport?> GetAmendmentForRecoveryAsync(
        string clientOrderId, OrderState retainedOrder, PendingOrderAmendment amendment, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(clientOrderId);
        ArgumentNullException.ThrowIfNull(retainedOrder);
        ArgumentNullException.ThrowIfNull(amendment);
        EnsureConnected();
        using var client = CreateHttpClient();
        var original = await ResolveOrderForCancellationAsync(client,
            amendment.OriginalBrokerOrderId is { Length: > 0 } originalGatewayOrderId
                ? new OrderCancellationIdentifier(originalGatewayOrderId, OrderCancellationIdentifierKind.BrokerOrderId)
                : new OrderCancellationIdentifier(clientOrderId, OrderCancellationIdentifierKind.ClientOrderId), ct).ConfigureAwait(false);
        if (original is null)
            return null;
        ValidateReplacementSnapshot(original);
        var attempt = new ReplacementAttempt(original) { CandidateId = amendment.CandidateBrokerOrderId };
        _replacementAttempts[original.Id!] = attempt;
        if (amendment.CandidateBrokerOrderId is { Length: > 0 } candidateGatewayOrderId)
            _replacementAttempts[candidateGatewayOrderId] = attempt;
        var observed = await ObserveReplacementAsync(client, attempt, ct).ConfigureAwait(false);
        return (observed ?? CreatePendingModification(attempt,
            "Alpaca amendment recovery has not confirmed replacement or refusal; retain both possible terms."))
            with
        { ClientOrderId = clientOrderId };
    }

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
        if (order is null || !string.Equals(order.ClientOrderId, clientOrderId, StringComparison.Ordinal))
            throw new InvalidDataException("Alpaca recovery returned inconsistent client identity.");
        ValidateReplacementSnapshot(order);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        string? predecessorId = null;
        while (order.ReplacedBy is { Length: > 0 } successor)
        {
            if (!seen.Add(order.Id!) || seen.Count > 32)
                throw new InvalidDataException("Alpaca replacement chain is cyclic or exceeds the recovery bound.");
            var original = order;
            var replacement = await ReadReplacementSnapshotAsync(client, successor, ct).ConfigureAwait(false);
            if (replacement is null || replacement.Id != successor || replacement.Replaces != original.Id
                || replacement.Symbol != original.Symbol || replacement.Side != original.Side)
                throw new InvalidDataException("Alpaca recovery returned an inconsistent replacement chain.");
            var attempt = new ReplacementAttempt(original) { CandidateId = successor };
            _replacementAttempts[original.Id!] = attempt;
            _replacementAttempts[successor] = attempt;
            if (replacement.Status == "rejected")
            {
                if (original.Status == "replaced")
                    return CreatePendingModification(attempt,
                        "Alpaca recovery received contradictory predecessor/replacement state.") with
                    { ClientOrderId = clientOrderId };
                return attempt.Resolution = BuildReplacementSnapshotReport(original, clientOrderId,
                    ExecutionReportType.ModificationRejected, "Alpaca rejected the proposed replacement.");
            }
            if (original.Status != "replaced" || replacement.Status is "pending_new" or "pending_replace")
                return CreatePendingModification(attempt,
                    "Alpaca recovery has not confirmed the proposed replacement.") with
                { ClientOrderId = clientOrderId };
            predecessorId = original.Id;
            order = replacement;
            attempt.Resolution = BuildReplacementSnapshotReport(order, clientOrderId,
                ExecutionReportType.Modified) with
            { ReplacesOrderId = predecessorId };
        }
        if (generation != ConnectionGeneration)
            throw new InvalidDataException("Alpaca connection changed while recovering the order.");
        if (order.Status == "pending_replace"
            || _replacementAttempts.TryGetValue(order.Id!, out var pending) && pending.Resolution is null)
        {
            var attempt = _replacementAttempts.GetOrAdd(order.Id!, _ => new ReplacementAttempt(order));
            attempt.Original = order;
            var observed = await ObserveReplacementAsync(client, attempt, ct).ConfigureAwait(false);
            return (observed ?? CreatePendingModification(attempt,
                "Alpaca recovery has not confirmed the proposed replacement.")) with
            { ClientOrderId = clientOrderId };
        }
        return BuildReplacementSnapshotReport(order, clientOrderId,
            predecessorId is null ? MapReconciliationReportType(MapAlpacaStatus(order.Status)) : ExecutionReportType.Modified)
            with
        { OrderId = clientOrderId, ReplacesOrderId = predecessorId };
    }
}
