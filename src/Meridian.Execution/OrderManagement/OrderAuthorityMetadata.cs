using Meridian.Execution.Sdk;

namespace Meridian.Execution.OrderManagement;

/// <summary>
/// Writes a typed authority's identity into the order's metadata for the OMS components that
/// still read identity from metadata (risk escalation, audit, session persistence). Any value the
/// caller placed under those keys is discarded first, so metadata can never disagree with the
/// authority the order was submitted under.
/// </summary>
internal static class OrderAuthorityMetadata
{
    private static readonly string[] IdentityKeys =
    [
        "actor",
        "runId",
        "run_id",
        "correlationId",
        "sessionId",
        "session_id",
        "orderOrigin"
    ];

    internal static OrderRequest Bind(OrderRequest order, OrderSubmissionAuthority authority)
    {
        ArgumentNullException.ThrowIfNull(order);
        ArgumentNullException.ThrowIfNull(authority);

        var metadata = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (order.Metadata is { } callerMetadata)
        {
            foreach (var (key, value) in callerMetadata)
            {
                if (!IdentityKeys.Any(identity => string.Equals(identity, key, StringComparison.OrdinalIgnoreCase)))
                    metadata[key] = value;
            }
        }

        // Risk escalation records the "actor" as the submitter for segregation of duties, so a
        // release keeps the original submitter there; the approver is carried on the authority.
        metadata["actor"] = Submitter(authority).Actor;
        if (authority.RunId is { } runId)
            metadata["runId"] = runId;
        if (!string.IsNullOrWhiteSpace(authority.CorrelationId))
            metadata["correlationId"] = authority.CorrelationId;
        if (authority.SessionId is { } sessionId)
            metadata["sessionId"] = sessionId;

        return order with
        {
            FundAccountId = order.FundAccountId ?? authority.AccountGrant?.FundAccountId,
            Metadata = metadata
        };
    }

    private static OrderSubmissionAuthority Submitter(OrderSubmissionAuthority authority)
    {
        var current = authority;
        while (current.Origin == OrderCommandOrigin.EscalationRelease && current.ReleasedFrom is { } parked)
            current = parked;
        return current;
    }
}
