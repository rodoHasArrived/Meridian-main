using System.Text.Json;
using Meridian.Contracts.Integrity;
using Meridian.Execution.Sdk;
using Meridian.Execution.Serialization;
using Meridian.Execution.Services;

namespace Meridian.Execution;

public sealed partial class OrderManagementSystem
{
    private async Task<OrderResult?> ClaimPaperTicketAsync(
        OrderRequest request, Guid attemptId, OrderSubmissionAuthorityRecord authority, CancellationToken ct)
    {
        var id = request.ClientOrderId!;
        var retained = await ClaimPaperIntentAsync(request, attemptId, authority, ct).ConfigureAwait(false);
        if (retained is null)
            return null;

        var original = retained.FirstOrDefault(entry => entry.Kind == "Intent" &&
            entry.Authority is { Origin: OrderCommandOrigin.OperatorTicket });
        if (original?.Authority is not { } recorded ||
            original.AccountId != request.FundAccountId ||
            recorded.Actor != authority.Actor ||
            recorded.SessionId != authority.SessionId ||
            original.Request is null ||
            !string.Equals(TicketTerms(original.Request), TicketTerms(request), StringComparison.Ordinal))
            return new OrderResult
            {
                Success = false,
                OrderId = id,
                ErrorMessage = "This client order ID is already retained for another intent."
            };

        var attempts = retained.Where(entry => entry.Authority is { } command &&
            ExecutionJournal.OriginalAuthority(command).CorrelationId == recorded.CorrelationId)
            .Select(entry => entry.AttemptId).ToHashSet();
        var state = retained.LastOrDefault(entry => entry.State is not null && attempts.Contains(entry.AttemptId))?.State;
        if (state is { Status: not OrderStatus.PendingNew })
            return new OrderResult
            {
                Success = state.Status != OrderStatus.Rejected,
                OrderId = id,
                OrderState = state,
                ErrorMessage = retained.LastOrDefault(entry => attempts.Contains(entry.AttemptId) &&
                    entry.Report?.RejectReason is not null)?.Report?.RejectReason
                    ?? retained.LastOrDefault(entry => attempts.Contains(entry.AttemptId) && entry.Kind == "Rejected")?.Detail
            };

        if (_approvalReservations.FindParkedEscalationId(id) is { } escalationId &&
            _escalationQueue?.TryGet(escalationId)?.Authority is { } parked &&
            ExecutionJournal.OriginalAuthority(parked).CorrelationId == recorded.CorrelationId)
            return new OrderResult { Success = false, OrderId = id, RequiresApproval = true, EscalationId = escalationId };

        return new OrderResult
        {
            Success = false,
            OrderId = id,
            OrderState = state,
            ErrorMessage = "The retained Paper ticket requires reconciliation before it can be retried.",
            RequiresOrderReconciliation = true
        };
    }

    private Task<IReadOnlyList<ExecutionJournalEntry>?> ClaimPaperIntentAsync(
        OrderRequest request, Guid attemptId, OrderSubmissionAuthorityRecord? authority, CancellationToken ct) =>
        _journal!.ClaimPaperSubmissionAsync(new ExecutionJournalEntry(1, $"intent:{attemptId:N}", JournalScope,
            request.FundAccountId, request.ClientOrderId!, "Intent", DateTimeOffset.UtcNow,
            Request: request, AttemptId: attemptId, Authority: authority), ct);

    private static string TicketTerms(OrderRequest request)
    {
        var terms = new SortedDictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (key, value) in request.Metadata ?? new Dictionary<string, string>())
        {
            if (key.Equals("actor", StringComparison.OrdinalIgnoreCase) ||
                key.Equals("correlationId", StringComparison.OrdinalIgnoreCase) ||
                key.Equals("clientCorrelationId", StringComparison.OrdinalIgnoreCase) ||
                key.Equals("sessionId", StringComparison.OrdinalIgnoreCase))
                continue;
            terms[key] = value;
        }
        return JsonSerializer.Serialize(request with
        {
            Symbol = request.Symbol?.Trim().ToUpperInvariant() ?? string.Empty,
            Metadata = terms
        }, ExecutionJsonContext.Default.OrderRequest);
    }

    internal bool IsClientOrderIdUnavailable(string id) => _recoveryJournal.IsDispatched(id)
        || _orders.ContainsKey(id);

    private string JournalScope => _gateway is IBrokerageConnectionState connection
        ? connection.ScopeIdentity ?? throw new InvalidOperationException("Verified broker account/environment is required for journal retention.")
        : $"local-paper:{_gateway.GatewayId}";

    private static string JournalEventId(string eventId, Guid? attemptId) =>
        attemptId is { } attempt ? $"{eventId}:{attempt:N}" : eventId;

    private async Task RecordJournalIntentAsync(
        OrderRequest request,
        string kind,
        Guid? attemptId,
        CancellationToken ct,
        OrderSubmissionAuthorityRecord? authority = null)
    {
        if (_journal is null)
            return;
        var id = request.ClientOrderId ?? throw new InvalidOperationException("Journal intent requires an assigned client order ID.");
        var hash = Sha256Digest.ComputeUtf8(JsonSerializer.Serialize(request, ExecutionJsonContext.Default.OrderRequest));
        var reportingAssetClass = kind == "Dispatch" && _instrumentCoverage is not null
            && await _instrumentCoverage.IsUsdEquityAsync(request, ct).ConfigureAwait(false) ? ExecutionReportingAssetClasses.UsdUsEquity : null;
        await _journal.AppendAsync(new ExecutionJournalEntry(1, JournalEventId($"{kind}:{id}:{hash}", attemptId), JournalScope,
            request.FundAccountId, id, kind, DateTimeOffset.UtcNow, Request: request,
            Reference: kind == "Dispatch" ? _marketReferences?.GetReference(request.Symbol, request.Side) : null,
            ReportingAssetClass: reportingAssetClass, AttemptId: attemptId, Authority: authority), ct).ConfigureAwait(false);
    }

    // A false result identifies an exact replay already retained for an earlier local attempt.
    private async Task<bool> RecordJournalReportAsync(ExecutionReport report, OrderState? state, CancellationToken ct,
        bool boundToTrackedAttempt = false)
    {
        if (_journal is null)
            return true;
        try
        {
            var id = state?.OrderId ?? report.ClientOrderId ?? report.OrderId;
            var scope = JournalScope;
            var accountId = state?.FundAccountId;
            var attemptId = state?.JournalAttemptId;
            var hash = Sha256Digest.ComputeUtf8(JsonSerializer.Serialize(report, ExecutionJsonContext.Default.ExecutionReport));
            if (!boundToTrackedAttempt && _gateway is not IBrokerageGateway && state is not null
                && attemptId.HasValue && report.Timestamp < state.CreatedAt)
            {
                // A delayed copy of an earlier paper execution must not create a fill for a
                // newer incarnation of the same ID. The synchronous acknowledgement already
                // carries its exact submission's state, so this check only applies to replay.
                var retained = await _journal.ReadAsync(ct: ct).ConfigureAwait(false);
                if (retained.Any(entry => entry.Scope == scope && entry.OrderId == id
                    && entry.AttemptId != attemptId && entry.EventId == JournalEventId($"report:{hash}", entry.AttemptId)))
                    return false;
            }
            if (state is null)
            {
                // A compacted broker order can still receive replayed reports. Dispatch evidence
                // owns its account and attempt; a preceding refused intent does not own either.
                var dispatches = (await _journal.ReadAsync(ct: ct).ConfigureAwait(false))
                    .Where(entry => entry.Scope == scope && entry.OrderId == id && entry.Kind == "Dispatch")
                    .DistinctBy(entry => (entry.AccountId, entry.AttemptId)).Take(2).ToArray();
                if (dispatches.Length == 1)
                {
                    accountId = dispatches[0].AccountId;
                    attemptId = dispatches[0].AttemptId;
                }
            }
            await _journal.AppendAsync(new ExecutionJournalEntry(1, JournalEventId($"report:{hash}", attemptId), scope,
                accountId, id, report.ReportType.ToString(), DateTimeOffset.UtcNow, Report: report,
                AttemptId: attemptId), ct).ConfigureAwait(false);
            return true;
        }
        catch (Exception ex)
        {
            var failure = new ExecutionReportDeliveryException(report, $"Journal retention failed after gateway acknowledgement: {ex.Message}")
            { IsJournalFailure = true };
            _executionFailureState.MarkJournalFailure(failure, "Execution journal retention failed; order admission is halted pending recovery.");
            throw failure;
        }
    }

    private async Task RecordJournalStateAsync(OrderState state, CancellationToken ct)
    {
        if (_journal is null)
            return;
        var hash = Sha256Digest.ComputeUtf8(JsonSerializer.Serialize(state, ExecutionJsonContext.Default.OrderState));
        await _journal.AppendAsync(new(1, $"state:{hash}", JournalScope, state.FundAccountId,
            state.OrderId, "OrderState", DateTimeOffset.UtcNow, State: state, AttemptId: state.JournalAttemptId), ct).ConfigureAwait(false);
    }

    private Task RecordJournalOperationAsync(OrderState state, string kind, CancellationToken ct,
        OrderModification? modification = null, string? detail = null, OrderSubmissionAuthority? authority = null) => _journal is null ? Task.CompletedTask
        : _journal.AppendAsync(new(1, $"operation:{Guid.NewGuid():N}", JournalScope, state.FundAccountId,
            state.OrderId, kind, DateTimeOffset.UtcNow, Detail: detail, Modification: modification, AttemptId: state.JournalAttemptId,
            Authority: authority?.ToRecord()), ct);
}
