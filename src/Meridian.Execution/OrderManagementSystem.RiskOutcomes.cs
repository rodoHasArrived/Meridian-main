using Meridian.Execution.Logging;
using Meridian.Execution.Sdk;
using Meridian.Execution.Services;
using Microsoft.Extensions.Logging;

namespace Meridian.Execution;

/// <summary>
/// Risk-outcome handling for the OMS pre-trade gate: the typed parked outcome for
/// governed-approval escalations and the durable retention of non-blocking risk warning
/// flags on both approved and rejected orders.
/// </summary>
public sealed partial class OrderManagementSystem
{
    /// <summary>
    /// Terminal handling for an order a risk escalation parked for governed approval: the
    /// order does not route, its tracked state mirrors a rejection (nothing is live at the
    /// broker), but the audit action and typed result distinguish "awaiting approval" from
    /// "rejected" so operators and downstream status surfaces do not count it as a breach.
    /// </summary>
    private async Task<OrderResult> ParkOrderForApprovalAsync(
        string orderId,
        OrderRequest request,
        string? actor,
        string brokerName,
        string? runId,
        string? correlationId,
        RiskValidationResult riskResult,
        string? sessionId,
        CancellationToken ct)
    {
        var parkedState = CreateRejectedState(orderId, request, riskResult.RejectReason);
        if (_orders.TryAdd(orderId, parkedState))
        {
            TrimRetainedOrdersIfNeeded();
        }

        await RecordSessionOrderUpdateAsync(sessionId, parkedState, ct).ConfigureAwait(false);

        _logger.LogWarning(
            "Order {OrderId} parked for governed risk approval ({EscalationId})",
            LogSanitizer.Sanitize(orderId),
            LogSanitizer.Sanitize(riskResult.EscalationId));

        if (_auditTrail is not null)
        {
            var metadata = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["escalationId"] = riskResult.EscalationId ?? string.Empty
            };
            AppendRiskWarningsMetadata(metadata, riskResult.Warnings);

            await _auditTrail.RecordAsync(new ExecutionAuditEntry(
                AuditId: Guid.NewGuid().ToString("N"),
                Category: "Risk",
                Action: "OrderParkedForApproval",
                Outcome: "Parked",
                OccurredAt: DateTimeOffset.UtcNow,
                Actor: actor,
                BrokerName: brokerName,
                OrderId: orderId,
                RunId: runId,
                Symbol: request.Symbol,
                CorrelationId: correlationId,
                Message: riskResult.RejectReason,
                Reason: "RISK_ESCALATION_PARKED",
                Scope: BuildOrderAuditScope(request, runId),
                Metadata: metadata), ct).ConfigureAwait(false);
        }

        return new OrderResult
        {
            Success = false,
            OrderId = orderId,
            ErrorMessage = riskResult.RejectReason,
            OrderState = parkedState,
            RequiresApproval = true,
            EscalationId = riskResult.EscalationId,
            RiskWarnings = riskResult.Warnings.Count > 0 ? riskResult.Warnings : null
        };
    }

    private static IReadOnlyDictionary<string, string>? BuildRiskWarningsAuditMetadata(
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

    private static void AppendRiskWarningsMetadata(
        IDictionary<string, string> metadata,
        IReadOnlyList<string> warnings)
    {
        for (var i = 0; i < warnings.Count; i++)
        {
            metadata[$"warning{i + 1}"] = warnings[i];
        }
    }

    private async Task RecordRiskWarningsAsync(
        string orderId,
        OrderRequest request,
        string? actor,
        string brokerName,
        string? runId,
        string? correlationId,
        IReadOnlyList<string> warnings,
        CancellationToken ct)
    {
        _logger.LogWarning(
            "Order {OrderId} approved with {WarningCount} non-blocking risk flag(s)",
            LogSanitizer.Sanitize(orderId),
            warnings.Count);

        if (_auditTrail is null)
        {
            return;
        }

        try
        {
            var metadata = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            AppendRiskWarningsMetadata(metadata, warnings);

            await _auditTrail.RecordAsync(new ExecutionAuditEntry(
                AuditId: Guid.NewGuid().ToString("N"),
                Category: "Risk",
                Action: "RiskWarningsFlagged",
                Outcome: "Approved",
                OccurredAt: DateTimeOffset.UtcNow,
                Actor: actor,
                BrokerName: brokerName,
                OrderId: orderId,
                RunId: runId,
                Symbol: request.Symbol,
                CorrelationId: correlationId,
                Message: $"Order approved with {warnings.Count} non-blocking risk flag(s).",
                Scope: BuildOrderAuditScope(request, runId),
                Metadata: metadata), ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Order {OrderId} risk warnings could not be recorded to the audit trail",
                LogSanitizer.Sanitize(orderId));
        }
    }
}
