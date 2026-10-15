using System.Collections.Concurrent;
using System.Net.Http.Json;
using System.Text.Json;
using Meridian.Execution.Sdk;
using OrderSide = Meridian.Execution.Sdk.OrderSide;

namespace Meridian.Infrastructure.Adapters.Alpaca;

public sealed partial class AlpacaBrokerageGateway
{
    private readonly ConcurrentDictionary<string, ReplacementAttempt> _replacementAttempts = new(StringComparer.Ordinal);

    private sealed class ReplacementAttempt(AlpacaOrderResponse original)
    {
        public AlpacaOrderResponse Original { get; set; } = original;
        public string? CandidateId { get; set; }
        public volatile ExecutionReport? Resolution;
    }

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
        var existing = await ResolveOrderForCancellationAsync(client, identifier, ct).ConfigureAwait(false)
            ?? throw new InvalidOperationException("Alpaca could not resolve the broker identity required for amendment.");
        ValidateReplacementSnapshot(existing);
        var brokerOrderId = existing.Id!;
        ct.ThrowIfCancellationRequested();
        var attempt = new ReplacementAttempt(existing);
        while (!_replacementAttempts.TryAdd(brokerOrderId, attempt))
        {
            if (!_replacementAttempts.TryGetValue(brokerOrderId, out var inProgress))
                continue;
            if (inProgress.Resolution is null)
                return CreatePendingModification(inProgress, "Alpaca replacement remains unconfirmed; reconcile before retrying.");
            if (_replacementAttempts.TryUpdate(brokerOrderId, attempt, inProgress))
                break;
        }
        var payload = new AlpacaOrderModifyPayload
        {
            Qty = FormatDecimal(modification.NewQuantity),
            LimitPrice = FormatDecimal(modification.NewLimitPrice),
            StopPrice = FormatDecimal(modification.NewStopPrice),
            Trail = FormatDecimal(modification.NewTrail),
        };

        HttpResponseMessage response;
        ct.ThrowIfCancellationRequested();
        try
        {
            response = await client.PatchAsJsonAsync(
                $"{BaseUrl}/v2/orders/{Uri.EscapeDataString(brokerOrderId)}", payload,
                AlpacaBrokerageSerializerContext.Default.AlpacaOrderModifyPayload, ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is HttpRequestException or OperationCanceledException)
        {
            return PublishPendingModification(attempt, "Alpaca amendment acknowledgement was lost; reconcile the replacement before retrying.");
        }
        using var responseLifetime = response;
        if ((int)response.StatusCode >= 500 || (int)response.StatusCode == 408)
            return PublishPendingModification(attempt,
                $"Alpaca amendment outcome is uncertain after HTTP {(int)response.StatusCode}; reconcile before retrying.");
        if (!response.IsSuccessStatusCode)
        {
            var observed = await ReadReplacementSnapshotAsync(client, brokerOrderId, ct).ConfigureAwait(false);
            var refusal = BuildReplacementSnapshotReport(observed ?? existing, existing.ClientOrderId,
                ExecutionReportType.ModificationRejected,
                $"Alpaca amendment failed with HTTP {(int)response.StatusCode}.");
            attempt.Resolution = refusal;
            await _reportChannel.Writer.WriteAsync(refusal, ct).ConfigureAwait(false);
            return refusal;
        }

        // The Trading API explicitly states that PATCH success does not guarantee replacement:
        // the predecessor can still fill and the proposed replacement can then be rejected.
        try
        {
            var candidate = await response.Content.ReadFromJsonAsync(
                AlpacaBrokerageSerializerContext.Default.AlpacaOrderResponse, ct).ConfigureAwait(false);
            if (candidate is null || string.IsNullOrWhiteSpace(candidate.Id)
                || candidate.Symbol != existing.Symbol || candidate.Side != existing.Side
                || candidate.Replaces is { Length: > 0 } && candidate.Replaces != brokerOrderId)
                return PublishPendingModification(attempt, "Alpaca returned an inconsistent provisional replacement; reconcile before retrying.");
            if (candidate.Id != brokerOrderId)
            {
                attempt.CandidateId = candidate.Id;
                _replacementAttempts[candidate.Id] = attempt;
            }
            if (candidate.Status == "rejected")
            {
                var original = await ReadReplacementSnapshotAsync(client, brokerOrderId, ct).ConfigureAwait(false);
                if (original is not null && original.Status != "replaced")
                {
                    attempt.Original = original;
                    var refusal = attempt.Resolution = BuildReplacementSnapshotReport(original, original.ClientOrderId,
                        ExecutionReportType.ModificationRejected, "Alpaca rejected the proposed replacement.");
                    await _reportChannel.Writer.WriteAsync(refusal, ct).ConfigureAwait(false);
                    return refusal;
                }
                return PublishPendingModification(attempt, "Alpaca replacement refusal could not be reconciled with its predecessor.");
            }
            // Only an independent broker observation can commit a new identity and terms.
            var resolution = await ObserveReplacementAsync(client, attempt, ct).ConfigureAwait(false);
            if (resolution is not null)
            {
                await _reportChannel.Writer.WriteAsync(resolution, ct).ConfigureAwait(false);
                return resolution;
            }
        }
        catch (Exception ex) when (ex is HttpRequestException or OperationCanceledException or JsonException or InvalidDataException)
        {
            return PublishPendingModification(attempt, "Alpaca replacement could not be independently confirmed; reconcile before retrying.");
        }
        return attempt.Resolution ?? PublishPendingModification(attempt,
            "Alpaca accepted the replacement request, but replacement is not confirmed; reconcile before retrying.");
    }

    private ExecutionReport PublishPendingModification(ReplacementAttempt attempt, string reason)
    {
        var report = attempt.Resolution ?? CreatePendingModification(attempt, reason);
        _reportChannel.Writer.TryWrite(report);
        return report;
    }

    private static ExecutionReport CreatePendingModification(ReplacementAttempt attempt, string reason) =>
        BuildReplacementSnapshotReport(attempt.Original, attempt.Original.ClientOrderId,
            ExecutionReportType.ModificationPending, reason) with
        { CandidateGatewayOrderId = attempt.CandidateId };

    private async Task<AlpacaOrderResponse?> ReadReplacementSnapshotAsync(HttpClient client, string brokerId, CancellationToken ct)
    {
        var order = await ResolveOrderForCancellationAsync(client,
            new OrderCancellationIdentifier(brokerId, OrderCancellationIdentifierKind.BrokerOrderId), ct).ConfigureAwait(false);
        if (order is not null)
            ValidateReplacementSnapshot(order);
        return order;
    }

    private async Task<ExecutionReport?> ObserveReplacementAsync(HttpClient client, ReplacementAttempt attempt, CancellationToken ct)
    {
        if (attempt.Resolution is not null)
            return attempt.Resolution;
        var original = await ReadReplacementSnapshotAsync(client, attempt.Original.Id!, ct).ConfigureAwait(false);
        if (original is null || original.Symbol != attempt.Original.Symbol || original.Side != attempt.Original.Side)
            return null;
        attempt.Original = original;
        var candidateId = original.ReplacedBy ?? attempt.CandidateId;
        if (candidateId is not { Length: > 0 })
            return null;
        var candidate = await ReadReplacementSnapshotAsync(client, candidateId, ct).ConfigureAwait(false);
        if (candidate is null || candidate.Replaces != original.Id || candidate.Symbol != original.Symbol || candidate.Side != original.Side)
            return null;
        attempt.CandidateId = candidateId;
        _replacementAttempts[candidateId] = attempt;
        if (candidate.Status == "rejected")
        {
            if (original.Status == "replaced")
                return null;
            return attempt.Resolution = BuildReplacementSnapshotReport(original, original.ClientOrderId,
                ExecutionReportType.ModificationRejected,
                "Alpaca rejected the proposed replacement; the predecessor order state was retained.");
        }
        if (original.Status != "replaced" || original.ReplacedBy != candidateId
            || candidate.Status is "pending_new" or "pending_replace" or "accepted_for_bidding")
            return null;
        return attempt.Resolution = BuildReplacementSnapshotReport(candidate, original.ClientOrderId,
            ExecutionReportType.Modified) with
        { ReplacesOrderId = original.Id };
    }

    private static void ValidateReplacementSnapshot(AlpacaOrderResponse order)
    {
        if (string.IsNullOrWhiteSpace(order.Id) || string.IsNullOrWhiteSpace(order.ClientOrderId)
            || string.IsNullOrWhiteSpace(order.Symbol) || order.Side is not ("buy" or "sell")
            || order.Status is not ("new" or "accepted" or "pending_new" or "pending_replace" or "replaced"
                or "partially_filled" or "filled" or "pending_cancel" or "canceled" or "expired" or "rejected"))
            throw new InvalidDataException("Alpaca replacement snapshot lacks consistent order identity/state.");
        var quantity = ParseRequiredDecimal(order.Qty, "qty", order.Id);
        var filled = ParseRequiredDecimal(order.FilledQty, "filled_qty", order.Id);
        if (quantity <= 0m || filled < 0m || filled > quantity
            || filled > 0m && ParseNullableDecimal(order.FilledAvgPrice) is not > 0m)
            throw new InvalidDataException("Alpaca replacement snapshot lacks valid cumulative fill economics.");
    }

    private static ExecutionReport BuildReplacementSnapshotReport(
        AlpacaOrderResponse order, string? clientOrderId, ExecutionReportType type, string? reason = null)
    {
        var filled = ParseDecimal(order.FilledQty);
        var status = order.Status is "pending_replace" or "replaced"
            ? filled > 0m ? OrderStatus.PartiallyFilled : OrderStatus.Accepted
            : MapAlpacaStatus(order.Status);
        return new ExecutionReport
        {
            OrderId = order.Id!,
            GatewayOrderId = order.Id,
            ClientOrderId = clientOrderId,
            ReportType = type,
            Symbol = order.Symbol!,
            Side = order.Side == "sell" ? OrderSide.Sell : OrderSide.Buy,
            OrderStatus = status,
            OrderQuantity = ParseDecimal(order.Qty),
            FilledQuantity = filled,
            FillPrice = ParseNullableDecimal(order.FilledAvgPrice),
            FillPriceIsCumulativeAverage = true,
            HasOrderTerms = order.HasLimitPrice && order.HasStopPrice,
            OrderLimitPrice = ParseNullableDecimal(order.LimitPrice),
            OrderStopPrice = ParseNullableDecimal(order.StopPrice),
            RejectReason = reason,
            Timestamp = order.UpdatedAt ?? DateTimeOffset.UtcNow
        };
    }

    private async Task<ExecutionReport> NormalizeReplacementReportAsync(ExecutionReport report, CancellationToken ct)
    {
        var brokerId = report.GatewayOrderId ?? report.OrderId;
        _replacementAttempts.TryGetValue(brokerId, out var attempt);
        if (attempt is null && report.ReplacesOrderId is { Length: > 0 } predecessor)
            _replacementAttempts.TryGetValue(predecessor, out attempt);
        var status = report.Diagnostics?.BrokerStatus;
        var isCandidate = report.ReplacesOrderId is { Length: > 0 } || attempt?.CandidateId == brokerId;
        var knownPendingCandidate = attempt is { Resolution: null }
            && (attempt.CandidateId == brokerId || report.ReplacesOrderId == attempt.Original.Id);
        if (!isCandidate && status != "replaced")
            return report;
        if (attempt?.Resolution is not null)
            return report;
        using var client = CreateHttpClient();
        if (attempt is null)
        {
            var predecessorId = report.ReplacesOrderId ?? brokerId;
            var original = await ReadReplacementSnapshotAsync(client, predecessorId, ct).ConfigureAwait(false);
            if (original is null)
                throw new InvalidDataException("Alpaca replacement event cannot be bound to its predecessor.");
            attempt = new ReplacementAttempt(original) { CandidateId = isCandidate ? brokerId : report.CandidateGatewayOrderId };
            _replacementAttempts[predecessorId] = attempt;
        }
        if (isCandidate)
        {
            attempt.CandidateId = brokerId;
            _replacementAttempts[brokerId] = attempt;
        }
        if (isCandidate && report.OrderStatus == OrderStatus.Rejected)
        {
            var original = await ReadReplacementSnapshotAsync(client, attempt.Original.Id!, ct).ConfigureAwait(false)
                ?? throw new InvalidDataException("Alpaca replacement refusal requires the predecessor's current state.");
            attempt.Original = original;
            if (original.Status == "replaced")
                throw new InvalidDataException("Alpaca replacement refusal contradicts the confirmed predecessor state; retain the event for reconciliation.");
            return attempt.Resolution = BuildReplacementSnapshotReport(original, original.ClientOrderId,
                ExecutionReportType.ModificationRejected, report.RejectReason ?? "Alpaca rejected the proposed replacement.")
                with
            { Timestamp = report.Timestamp };
        }
        var observed = await ObserveReplacementAsync(client, attempt, ct).ConfigureAwait(false);
        if (report.LastFillQuantity is > 0m || report.OrderStatus is OrderStatus.Filled or OrderStatus.PartiallyFilled)
        {
            if (observed?.ReportType != ExecutionReportType.Modified)
            {
                if (!knownPendingCandidate)
                    throw new InvalidDataException("Alpaca replacement fill has no retained amendment intent; retain the durable event for replay.");
                // Execution is authoritative even while the replacement link is stale. The
                // pending intent supplies its authorization cap; keep the predecessor route
                // and both possible terms until separate replacement evidence resolves them.
                return report with
                {
                    ClientOrderId = attempt.Original.ClientOrderId,
                    GatewayOrderId = attempt.Original.Id,
                    CandidateGatewayOrderId = brokerId,
                    ReportType = ExecutionReportType.ModificationPending,
                    HasOrderTerms = false
                };
            }
            return report with
            {
                ClientOrderId = observed.ClientOrderId,
                ReportType = ExecutionReportType.Modified,
                HasOrderTerms = observed.HasOrderTerms,
                OrderLimitPrice = observed.OrderLimitPrice,
                OrderStopPrice = observed.OrderStopPrice
            };
        }
        return observed ?? CreatePendingModification(attempt,
            "Alpaca replacement event remains provisional until the predecessor and successor are confirmed.");
    }
}
