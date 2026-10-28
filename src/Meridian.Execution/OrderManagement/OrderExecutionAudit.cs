using System.Globalization;
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
            BrokerName: _gatewayId(),
            OrderId: orderId,
            RunId: null,
            Symbol: state?.Symbol ?? report?.Symbol,
            Message: message,
            Reason: report?.RejectReason,
            Scope: state is null ? null : BuildOrderAuditScope(state),
            Metadata: metadata ?? BuildOrderLifecycleAuditMetadata(state, report)), ct).ConfigureAwait(false);
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
                orderId ?? report.OrderId);
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
                orderId ?? report.OrderId);
            return false;
        }
    }

}
