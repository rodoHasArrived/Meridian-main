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
        "execution_control_override_id"
    ];

    private static readonly string[] BrokerAccountAndOverrideKeys =
    [
        "broker_account_id",
        "brokerAccountId",
        "account_id",
        "accountId",
        "alpaca:broker_account_id",
        "manualOverrideId",
        "manual_override_id",
        "executionControlOverrideId",
        "execution_control_override_id"
    ];

    public static bool ContainsClientRejectedServerOwnedKey(IReadOnlyDictionary<string, string>? metadata) =>
        ContainsAny(metadata, ClientRejectedKeys);

    public static IReadOnlyDictionary<string, string>? RemoveClientRejectedServerOwnedKeys(
        IReadOnlyDictionary<string, string>? metadata) =>
        RemoveKeys(metadata, ClientRejectedKeys);

    public static OrderRequest RemoveBrokerAccountAndOverrideKeys(OrderRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        var sanitized = RemoveKeys(request.Metadata, BrokerAccountAndOverrideKeys);
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

        foreach (var key in keys)
        {
            if (metadata.ContainsKey(key))
            {
                return true;
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
