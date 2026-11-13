using System.Text.Json;
using Meridian.Contracts.Integrity;
using Meridian.Execution.Sdk;
using Meridian.Execution.Serialization;
using Meridian.Execution.Services;

namespace Meridian.Execution;

public sealed partial class OrderManagementSystem
{
    internal bool IsClientOrderIdUnavailable(string id) => _recoveryJournal.IsDispatched(id)
        || _orders.ContainsKey(id);

    private string JournalScope => _gateway is IBrokerageConnectionState connection
        ? connection.ScopeIdentity ?? throw new InvalidOperationException("Verified broker account/environment is required for journal retention.")
        : $"local-paper:{_gateway.GatewayId}";

    private static string JournalEventId(string eventId, Guid? attemptId) =>
        attemptId is { } attempt ? $"{eventId}:{attempt:N}" : eventId;

    private async Task RecordJournalIntentAsync(OrderRequest request, string kind, Guid? attemptId, CancellationToken ct)
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
            ReportingAssetClass: reportingAssetClass, AttemptId: attemptId), ct).ConfigureAwait(false);
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
        OrderModification? modification = null, string? detail = null) => _journal is null ? Task.CompletedTask
        : _journal.AppendAsync(new(1, $"operation:{Guid.NewGuid():N}", JournalScope, state.FundAccountId,
            state.OrderId, kind, DateTimeOffset.UtcNow, Detail: detail, Modification: modification, AttemptId: state.JournalAttemptId), ct);
}
