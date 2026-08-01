using System.Globalization;
using Meridian.Execution.Sdk;
using Meridian.Execution.Services;

namespace Meridian.Execution;

/// <summary>
/// Audit and rejection recording for <see cref="OrderManagementSystem"/>.
/// <para>
/// Everything here answers "write down what happened": the metadata builders that flatten a
/// decision into audit fields, the lifecycle audit append, and the rejection path that registers
/// the rejected state, records it, and returns the result. None of it routes an order or talks to
/// a gateway. Splitting it out keeps the submission and cancellation lifecycle in the main file
/// readable, and keeps that file under the repository's file-size ratchet.
/// </para>
/// </summary>
public sealed partial class OrderManagementSystem
{
    private static IReadOnlyDictionary<string, string>? BuildOrderSubmittedAuditMetadata(
        ExecutionControlDecision? operatorControlDecision,
        LiveOrderReadinessDecision? liveOrderReadinessDecision)
    {
        var metadata = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (!string.IsNullOrWhiteSpace(operatorControlDecision?.AppliedManualOverrideId))
        {
            metadata["manualOverrideId"] = operatorControlDecision.AppliedManualOverrideId;
            metadata["controlDecision"] = "approved-by-manual-override";
        }

        if (!string.IsNullOrWhiteSpace(liveOrderReadinessDecision?.EvidenceReference))
        {
            metadata["liveReadinessDecision"] = "approved";
            metadata["liveReadinessEvidenceReference"] = liveOrderReadinessDecision.EvidenceReference;
        }

        return metadata.Count == 0 ? null : metadata;
    }

    private static IReadOnlyDictionary<string, string> BuildOrderRejectedByControlAuditMetadata(
        ExecutionControlDecision operatorControlDecision)
    {
        var metadata = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["controlDecision"] = "rejected-by-operator-controls"
        };

        if (!string.IsNullOrWhiteSpace(operatorControlDecision.RejectCode))
        {
            metadata["rejectCode"] = operatorControlDecision.RejectCode;
        }

        return metadata;
    }

    private static IReadOnlyDictionary<string, string> BuildLiveOrderReadinessRejectedAuditMetadata(
        LiveOrderReadinessDecision liveOrderReadinessDecision)
    {
        var metadata = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["liveReadinessDecision"] = "rejected",
            ["rejectCode"] = "LIVE_ORDER_READINESS_REJECTED"
        };

        if (!string.IsNullOrWhiteSpace(liveOrderReadinessDecision.EvidenceReference))
        {
            metadata["liveReadinessEvidenceReference"] = liveOrderReadinessDecision.EvidenceReference;
        }

        return metadata;
    }

    private async Task RecordOrderLifecycleAuditAsync(
        string action,
        string outcome,
        string orderId,
        OrderState? state,
        ExecutionReport? report,
        string? message,
        CancellationToken ct,
        IReadOnlyDictionary<string, string>? metadata = null)
    {
        if (_auditTrail is null)
        {
            return;
        }

        await _auditTrail.RecordAsync(new ExecutionAuditEntry(
            AuditId: Guid.NewGuid().ToString("N"),
            Category: "Order",
            Action: action,
            Outcome: outcome,
            OccurredAt: DateTimeOffset.UtcNow,
            BrokerName: _gateway.GatewayId,
            OrderId: orderId,
            RunId: null,
            Symbol: state?.Symbol ?? report?.Symbol,
            Message: message,
            Reason: report?.RejectReason,
            Scope: state is null ? null : BuildOrderAuditScope(state),
            Metadata: metadata ?? BuildOrderLifecycleAuditMetadata(state, report)), ct).ConfigureAwait(false);
    }

    private static IReadOnlyDictionary<string, string>? BuildOrderLifecycleAuditMetadata(
        OrderState? state,
        ExecutionReport? report)
    {
        if (state is null && report is null)
        {
            return null;
        }

        var metadata = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (state is not null)
        {
            metadata["orderQuantity"] = state.Quantity.ToString("G29");
            metadata["filledQuantity"] = state.FilledQuantity.ToString("G29");
            metadata["orderType"] = state.Type.ToString();
            metadata["side"] = state.Side.ToString();
        }

        if (report is not null)
        {
            metadata["reportType"] = report.ReportType.ToString();
            metadata["reportStatus"] = report.OrderStatus.ToString();
            metadata["gatewayOrderId"] = report.GatewayOrderId ?? string.Empty;
        }

        return metadata;
    }

    private static IReadOnlyDictionary<string, string> BuildOrderModificationAuditMetadata(
        OrderModification modification,
        OrderState state,
        ExecutionReport report)
    {
        var metadata = new Dictionary<string, string>(
            BuildOrderLifecycleAuditMetadata(state, report) ?? new Dictionary<string, string>(),
            StringComparer.OrdinalIgnoreCase);

        metadata["newQuantity"] = modification.NewQuantity?.ToString("G29") ?? string.Empty;
        metadata["newLimitPrice"] = modification.NewLimitPrice?.ToString("G29") ?? string.Empty;
        metadata["newStopPrice"] = modification.NewStopPrice?.ToString("G29") ?? string.Empty;
        metadata["newTrail"] = modification.NewTrail?.ToString("G29") ?? string.Empty;

        return metadata;
    }

    private async Task<OrderResult> RejectOrderAsync(
        string orderId,
        OrderRequest request,
        string? actor,
        string brokerName,
        string? runId,
        string? correlationId,
        string? message,
        string? sessionId,
        CancellationToken ct,
        string rejectionSource,
        string? reasonCode = null,
        IReadOnlyDictionary<string, string>? metadata = null,
        RiskDecisionSummary? riskSummary = null)
    {
        var rejectedState = CreateRejectedState(orderId, request, message);
        // TryAdd, not the indexer: gate rejections run before the order id is registered, so an
        // existing entry under this id belongs to a different order (e.g. a terminal order whose
        // id a rejected submission tried to reuse) and must survive. The rejection is still
        // audit-trailed and returned to the caller.
        if (_orders.TryAdd(orderId, rejectedState))
        {
            TrimRetainedOrdersIfNeeded();
        }

        await RecordSessionOrderUpdateAsync(sessionId, rejectedState, ct).ConfigureAwait(false);
        await RecordOrderRejectionAsync(
            orderId,
            request,
            actor,
            brokerName,
            runId,
            correlationId,
            message,
            ct,
            rejectionSource,
            reasonCode,
            metadata).ConfigureAwait(false);

        return new OrderResult
        {
            Success = false,
            OrderId = orderId,
            ErrorMessage = message,
            OrderState = rejectedState,
            RiskDecision = riskSummary
        };
    }

    /// <summary>
    /// Flattens a risk decision into audit metadata. Numeric values use the invariant culture so a
    /// WAL written under one locale still parses under another.
    /// </summary>
    private static IReadOnlyDictionary<string, string> BuildRiskRejectedAuditMetadata(
        RiskValidationResult result)
    {
        var metadata = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            // OrderRejected is the shared audit action for every gate, so the read projection needs
            // a discriminator to tell risk decisions from readiness, operator-control,
            // security-master, and duplicate-id rejections.
            ["decisionSource"] = "risk",
            ["decision"] = result.Decision.ToString(),
            ["violation.count"] = result.Violations.Count.ToString(CultureInfo.InvariantCulture)
        };

        for (var i = 0; i < result.Violations.Count; i++)
        {
            var violation = result.Violations[i];
            var prefix = string.Create(CultureInfo.InvariantCulture, $"violation.{i}.");
            metadata[prefix + "rule"] = violation.RuleName;
            metadata[prefix + "severity"] = violation.Severity.ToString();
            metadata[prefix + "code"] = violation.Code;
            metadata[prefix + "message"] = violation.Message;
            metadata[prefix + "requiresAcknowledgement"] =
                violation.RequiresAcknowledgement ? "true" : "false";

            if (violation.ObservedValue is { } observed)
            {
                metadata[prefix + "observed"] = observed.ToString("G29", CultureInfo.InvariantCulture);
            }

            if (violation.LimitValue is { } limit)
            {
                metadata[prefix + "limit"] = limit.ToString("G29", CultureInfo.InvariantCulture);
            }
        }

        return metadata;
    }
}
