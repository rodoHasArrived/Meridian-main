namespace Meridian.Execution.Sdk;

/// <summary>
/// Defines server-owned order metadata keys that callers must not use to choose
/// broker accounts or execution-control state.
/// </summary>
public static class ExecutionOrderMetadataPolicy
{
    private static readonly string[] ClientRejectedKeys =
    [
        "broker_account_id",
        "brokerAccountId",
        "account_id",
        "accountId",
        "alpaca:broker_account_id",
        "asset_class",
        "assetClass",
        "alpaca:asset_class",
        "manualOverrideId",
        "manual_override_id",
        "executionControlOverrideId",
        "execution_control_override_id",
        "liveReadinessEvidenceReference",
        "live_readiness_evidence_reference",
        "livePromotionAuditReference",
        "live_promotion_audit_reference",
        // RiskEscalationQueueService.SubmitterMetadataKey: only the internal chained-release
        // path may stamp the retained submitter. A caller-supplied value would bind an
        // escalation's segregation-of-duties identity to someone other than the real
        // submitter, letting the submitter approve their own escalation.
        "riskSubmitter"
    ];

    private static readonly string[] ServerOwnedRoutingKeys =
    [
        "broker_account_id",
        "brokerAccountId",
        "account_id",
        "accountId",
        "alpaca:broker_account_id",
        "manualOverrideId",
        "manual_override_id",
        "executionControlOverrideId",
        "execution_control_override_id",
        "liveReadinessEvidenceReference",
        "live_readiness_evidence_reference",
        "livePromotionAuditReference",
        "live_promotion_audit_reference"
    ];

    public static bool ContainsClientRejectedServerOwnedKey(IReadOnlyDictionary<string, string>? metadata) =>
        ContainsAny(metadata, ClientRejectedKeys);

    public static IReadOnlyDictionary<string, string>? RemoveClientRejectedServerOwnedKeys(
        IReadOnlyDictionary<string, string>? metadata) =>
        RemoveKeys(metadata, ClientRejectedKeys);

    public static OrderRequest RemoveServerOwnedRoutingKeys(OrderRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        var sanitized = RemoveKeys(request.Metadata, ServerOwnedRoutingKeys);
        return ReferenceEquals(sanitized, request.Metadata)
            ? request
            : request with { Metadata = sanitized };
    }

    private static bool ContainsAny(IReadOnlyDictionary<string, string>? metadata, IReadOnlyList<string> keys)
    {
        if (metadata is null)
        {
            return false;
        }

        // The incoming dictionary's own comparer may be case-sensitive (JSON-deserialized request
        // bags are), while downstream merges copy into ordinal-ignore-case dictionaries — so a
        // differently-cased server-owned key would slip past ContainsKey here yet still be read
        // downstream. Compare keys case-insensitively ourselves.
        foreach (var metadataKey in metadata.Keys)
        {
            foreach (var key in keys)
            {
                if (string.Equals(metadataKey, key, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static IReadOnlyDictionary<string, string>? RemoveKeys(
        IReadOnlyDictionary<string, string>? metadata,
        IReadOnlyList<string> keys)
    {
        if (metadata is null)
        {
            return null;
        }

        var sanitized = new Dictionary<string, string>(metadata, StringComparer.OrdinalIgnoreCase);
        var removed = false;
        foreach (var key in keys)
        {
            removed |= sanitized.Remove(key);
        }

        return removed ? sanitized : metadata;
    }
}
