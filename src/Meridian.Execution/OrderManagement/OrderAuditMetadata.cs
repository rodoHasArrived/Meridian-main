using System.Globalization;
using Meridian.Execution.Sdk;
using Meridian.Execution.Services;

namespace Meridian.Execution.OrderManagement;

/// <summary>
/// Audit-metadata projections for the OMS order lifecycle: the shared per-order fields
/// every lifecycle entry carries, and the amendment-specific additions.
/// </summary>
internal static class OrderAuditMetadata
{
    internal static IReadOnlyDictionary<string, string>? BuildOrderLifecycleAuditMetadata(
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

    internal static IReadOnlyDictionary<string, string> BuildOrderModificationAuditMetadata(
        OrderModification modification,
        OrderState state,
        ExecutionReport? report,
        IReadOnlyList<string>? riskWarnings = null,
        RiskValidationResult? riskDecision = null)
    {
        var metadata = new Dictionary<string, string>(
            BuildOrderLifecycleAuditMetadata(state, report) ?? new Dictionary<string, string>(),
            StringComparer.OrdinalIgnoreCase);

        // Structured violations, when the caller has a decision to attribute. A composite rejection
        // headlines the highest-severity rule and keeps the others in Violations, so without these
        // an amendment refused partly on a fat-finger breach records no trace of it and the rule
        // reports Healthy while actively rejecting amendments. Placement already does this; the
        // amendment path is the same claim about the same rules.
        if (riskDecision is { } decision && BuildRiskDecisionAuditMetadata(decision) is { } riskMetadata)
        {
            foreach (var (key, value) in riskMetadata)
            {
                metadata[key] = value;
            }
        }

        metadata["newQuantity"] = modification.NewQuantity?.ToString("G29") ?? string.Empty;
        metadata["newLimitPrice"] = modification.NewLimitPrice?.ToString("G29") ?? string.Empty;
        metadata["newStopPrice"] = modification.NewStopPrice?.ToString("G29") ?? string.Empty;
        metadata["newTrail"] = modification.NewTrail?.ToString("G29") ?? string.Empty;

        if (riskWarnings is { Count: > 0 })
        {
            metadata["riskWarnings"] = string.Join("; ", riskWarnings);
        }

        return metadata;
    }

    internal static IReadOnlyDictionary<string, string>? BuildOrderSubmittedAuditMetadata(
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

    internal static IReadOnlyDictionary<string, string> BuildOrderRejectedByControlAuditMetadata(
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

    internal static IReadOnlyDictionary<string, string> BuildLiveOrderReadinessRejectedAuditMetadata(
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
    internal static string BuildOrderAuditScope(OrderRequest request, string? runId) =>
        BuildOrderAuditScope(request.Symbol, request.StrategyId, runId, includeRunSegment: true);

    internal static string BuildOrderAuditScope(OrderState state) =>
        BuildOrderAuditScope(state.Symbol, state.StrategyId, runId: null, includeRunSegment: false);

    internal static string BuildOrderAuditScope(
        string? symbolValue,
        string? strategyId,
        string? runId,
        bool includeRunSegment)
    {
        var symbol = string.IsNullOrWhiteSpace(symbolValue) ? "symbol:unknown" : $"symbol:{symbolValue.Trim().ToUpperInvariant()}";
        var strategy = string.IsNullOrWhiteSpace(strategyId) ? "strategy:unknown" : $"strategy:{strategyId.Trim()}";
        if (!includeRunSegment)
        {
            return $"{strategy}/{symbol}";
        }

        var run = string.IsNullOrWhiteSpace(runId) ? "run:unknown" : $"run:{runId.Trim()}";
        return $"{run}/{strategy}/{symbol}";
    }

    internal static IReadOnlyDictionary<string, string>? BuildRiskWarningsAuditMetadata(
        IReadOnlyList<string> warnings)
    {
        if (warnings.Count == 0)
        {
            return null;
        }

        var metadata = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        AppendRiskWarningsMetadata(metadata, warnings);
        return metadata;
    }

    /// <summary>
    /// Serializes the whole decision into the rejection audit: the headline names one breach, but
    /// every rule is evaluated before a decision is taken, so a rejection can carry several.
    /// Without these fields a breach behind the headline exists only on the transient
    /// <see cref="OrderResult"/> and never reaches rule status or history.
    /// </summary>
    internal static IReadOnlyDictionary<string, string>? BuildRiskDecisionAuditMetadata(
        RiskValidationResult decision)
    {
        if (decision.Violations.Count == 0 && decision.Warnings.Count == 0)
        {
            return null;
        }

        var metadata = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["decisionSource"] = "risk",
            ["decision"] = decision.Decision.ToString(),
        };

        AppendRiskWarningsMetadata(metadata, decision.Warnings);

        if (decision.Violations.Count == 0)
        {
            return metadata;
        }

        // Invariant culture on the numerics: a WAL written under one locale has to parse under
        // another, and these are read back by the status projection.
        metadata["violation.count"] = decision.Violations.Count.ToString(CultureInfo.InvariantCulture);
        for (var i = 0; i < decision.Violations.Count; i++)
        {
            var violation = decision.Violations[i];
            var prefix = string.Create(CultureInfo.InvariantCulture, $"violation.{i}");
            metadata[$"{prefix}.rule"] = violation.RuleName;
            metadata[$"{prefix}.code"] = violation.Code;
            metadata[$"{prefix}.message"] = violation.Message;
            metadata[$"{prefix}.severity"] = violation.Severity.ToString();

            if (violation.ObservedValue is { } observed)
            {
                metadata[$"{prefix}.observed"] = observed.ToString(CultureInfo.InvariantCulture);
            }

            if (violation.LimitValue is { } limit)
            {
                metadata[$"{prefix}.limit"] = limit.ToString(CultureInfo.InvariantCulture);
            }
        }

        return metadata;
    }

    internal static void AppendRiskWarningsMetadata(
        IDictionary<string, string> metadata,
        IReadOnlyList<string> warnings)
    {
        for (var i = 0; i < warnings.Count; i++)
        {
            metadata[$"warning{i + 1}"] = warnings[i];
        }
    }

}
