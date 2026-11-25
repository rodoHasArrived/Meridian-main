using System.Globalization;
using Meridian.Execution.Logging;
using Meridian.Execution.Sdk;
using Meridian.Execution.Services;
using static Meridian.Execution.OrderManagement.OrderAuditMetadata;

namespace Meridian.Execution.OrderManagement;

/// <summary>Writes order lifecycle evidence from explicit snapshots without owning order state.</summary>
internal sealed class OrderExecutionAudit(
    ExecutionAuditTrailService? auditTrail,
    Func<string> gatewayId,
    ILogger logger)
{
    private readonly ExecutionAuditTrailService? _auditTrail = auditTrail;
    private readonly Func<string> _gatewayId = gatewayId;
    private readonly ILogger _logger = logger;

    internal async Task RecordLifecycleAsync(
        string action,
        string outcome,
        string orderId,
        OrderState? state,
        ExecutionReport? report,
        string? message,
        CancellationToken ct,
        IReadOnlyDictionary<string, string>? metadata = null,
        OrderSubmissionAuthority? authority = null,
        string? cancelAllIncidentId = null)
    {
        if (_auditTrail is null)
        {
            return;
        }

        // Authority, when the command carried one, names who acted (a kill-switch sweep's
        // operator or the automated trip) on the record itself rather than leaving it to a
        // separate caller-side entry.
        var lifecycleMetadata = metadata ?? BuildOrderLifecycleAuditMetadata(state, report);
        if (cancelAllIncidentId is not null)
            lifecycleMetadata = new Dictionary<string, string>(lifecycleMetadata ?? new Dictionary<string, string>(), StringComparer.Ordinal)
            {
                // Correlation for privacy projection only. This marker grants no order authority.
                ["cancelAllIncidentId"] = cancelAllIncidentId
            };
        var entry = new ExecutionAuditEntry(
            AuditId: Guid.NewGuid().ToString("N"),
            Category: "Order",
            Action: action,
            Outcome: outcome,
            OccurredAt: DateTimeOffset.UtcNow,
            Actor: authority?.Actor,
            BrokerName: _gatewayId(),
            OrderId: orderId,
            RunId: null,
            Symbol: state?.Symbol ?? report?.Symbol,
            CorrelationId: authority?.CorrelationId,
            Message: message,
            Reason: report?.RejectReason,
            Scope: state is null ? null : BuildOrderAuditScope(state),
            Metadata: authority is null ? lifecycleMetadata : WithCommandAuthority(lifecycleMetadata, authority));
        if (cancelAllIncidentId is null)
            await _auditTrail.RecordAsync(entry, ct).ConfigureAwait(false);
        else
            await _auditTrail.RecordCancelAllEvidenceAsync(entry).ConfigureAwait(false);
    }

    internal async Task<bool> RetainUnresolvedFillAsync(
        string? orderId,
        OrderState? state,
        ExecutionReport report,
        Exception failure)
    {
        if (_auditTrail is null)
        {
            _logger.LogCritical(
                failure,
                "Broker fill for order {OrderId} could not form a durable accounting event and no execution audit store is configured",
                LogSanitizer.Sanitize(orderId ?? report.OrderId));
            return false;
        }

        try
        {
            var metadata = new Dictionary<string, string>(
                BuildOrderLifecycleAuditMetadata(state, report) ?? new Dictionary<string, string>(),
                StringComparer.OrdinalIgnoreCase)
            {
                ["accountingFailureType"] = failure.GetType().Name,
                ["fillPrice"] = report.FillPrice?.ToString(CultureInfo.InvariantCulture) ?? "missing",
                ["fillOccurredAtUtc"] = report.Timestamp.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture)
            };
            await RecordLifecycleAsync(
                    action: "AccountingHandoffUnresolved",
                    outcome: "AttentionRequired",
                    orderId: orderId ?? report.OrderId,
                    state: state,
                    report: report,
                    message: failure.Message,
                    ct: CancellationToken.None,
                    metadata: metadata)
                .ConfigureAwait(false);
            return true;
        }
        catch (Exception auditFailure)
        {
            _logger.LogCritical(
                auditFailure,
                "Broker fill for order {OrderId} could not form a durable accounting event and its audit evidence could not be retained",
                LogSanitizer.Sanitize(orderId ?? report.OrderId));
            return false;
        }
    }

}
