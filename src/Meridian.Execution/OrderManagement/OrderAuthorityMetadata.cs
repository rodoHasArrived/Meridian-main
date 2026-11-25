using Meridian.Execution.Sdk;
using Meridian.Execution.Services;

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
        "orderOrigin",
        RiskEscalationQueueService.SubmitterMetadataKey
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

        // "actor" is the operator issuing this command. For a release that is the approver: the
        // queue records it as ReleasedBy and refuses a release by the order's own submitter. The
        // original submitter travels under the submitter key, so a rule that escalates again
        // during the release re-parks under the submitter's identity (the convention the
        // untyped release path already follows).
        metadata["actor"] = authority.Actor;
        if (authority.Origin == OrderCommandOrigin.EscalationRelease)
            metadata[RiskEscalationQueueService.SubmitterMetadataKey] = Submitter(authority).Actor;
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
