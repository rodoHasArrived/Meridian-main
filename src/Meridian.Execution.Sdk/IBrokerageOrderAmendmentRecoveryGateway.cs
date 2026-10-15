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
}
