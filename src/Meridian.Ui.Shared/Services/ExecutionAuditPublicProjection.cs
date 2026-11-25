using Meridian.Execution.Services;

namespace Meridian.Ui.Shared.Services;

/// <summary>
/// Removes account details from gateway-wide cancellation evidence before public audit browsing,
/// searching or export. The retained WAL record remains the authority for the original action.
/// </summary>
internal static class ExecutionAuditPublicProjection
{
    private static readonly HashSet<string> SweepActions = new(StringComparer.OrdinalIgnoreCase)
    {
        "KillSwitchSweep",
        "CircuitBreakerCancelAll",
        "CancelAllIncidentStarted",
        "CancelAllIncidentOrder",
        "CancelAllIncidentCompleted",
        "CancelAllIncidentInterrupted"
    };

    internal static ExecutionAuditEntry Project(ExecutionAuditEntry entry)
    {
        var isSweep = entry.CancelAllEvidence is not null || SweepActions.Contains(entry.Action)
            || entry.Metadata?.Any(pair =>
                pair.Key.Equals("commandOrigin", StringComparison.OrdinalIgnoreCase)
                    && string.Equals(pair.Value, "KillSwitch", StringComparison.OrdinalIgnoreCase)
                || pair.Key.Equals("cancelAllIncidentId", StringComparison.OrdinalIgnoreCase)
                    && !string.IsNullOrWhiteSpace(pair.Value)) == true;
        if (!isSweep)
            return entry;

        // Legacy sweep summaries name several accounts in prose, and individual cancellation
        // entries repeat broker/client identifiers in metadata. Removing only the typed payload
        // would still disclose those details through the generic feed or its search predicates.
        return entry with
        {
            OrderId = null,
            RunId = null,
            Symbol = null,
            Scope = null,
            Reason = null,
            Message = "Cancel-all action recorded. Select an execution account to read its authorized incident evidence.",
            Metadata = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["publicProjection"] = "AccountScopedCancelIncidentSummary"
            },
            CancelAllEvidence = null
        };
    }
}
