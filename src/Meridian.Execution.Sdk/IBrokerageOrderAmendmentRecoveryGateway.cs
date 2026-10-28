namespace Meridian.Execution.Sdk;

/// <summary>Recovers an uncertain amendment using its retained original and candidate identities.</summary>
public interface IBrokerageOrderAmendmentRecoveryGateway
{
    Task<ExecutionReport?> GetAmendmentForRecoveryAsync(
        string clientOrderId,
        OrderState retainedOrder,
        PendingOrderAmendment amendment,
        CancellationToken ct = default);
}

/// <summary>Both possible amendment terms and qualified provider identities.</summary>
public sealed record PendingOrderAmendment(OrderState Original, OrderState Proposed, string? OriginalBrokerOrderId)
{
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public string? CandidateBrokerOrderId { get; init; }
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public string? CandidateClientOrderId { get; init; }

    /// <summary>
    /// Broker ids of replacement-chain members before <see cref="OriginalBrokerOrderId"/>, in no
    /// particular order. Empty means the original is the chain's first member; null means unknown
    /// (retained before this field existed). Recovery reads their final fills authoritatively.
    /// </summary>
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public string[]? PriorChainBrokerOrderIds { get; init; }
    /// <summary>Requested trail amount. Order state carries no trail terms, so a trail-only
    /// amendment has identical original and proposed snapshots and is told apart by this.</summary>
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public decimal? ProposedTrail { get; init; }
}
