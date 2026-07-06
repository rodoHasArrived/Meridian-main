using Meridian.Execution.Sdk;

namespace Meridian.Execution.Services;

/// <summary>
/// Validates that a live broker order belongs to a W7-approved, audit-retained
/// live operation before the OMS submits it to an external broker gateway.
/// </summary>
public interface ILiveOrderReadinessGate
{
    Task<LiveOrderReadinessDecision> EvaluateAsync(
        LiveOrderReadinessRequest request,
        CancellationToken ct = default);
}

public sealed record LiveOrderReadinessRequest(
    string RunId,
    string BrokerName,
    string Symbol,
    OrderSide Side,
    OrderType OrderType,
    decimal Quantity,
    string? StrategyId = null,
    string? Actor = null,
    string? CorrelationId = null,
    Guid? FundAccountId = null);

public sealed record LiveOrderReadinessDecision(
    bool IsApproved,
    string? Reason = null,
    string? EvidenceReference = null)
{
    public static LiveOrderReadinessDecision Approved(string evidenceReference) =>
        new(true, EvidenceReference: evidenceReference);

    public static LiveOrderReadinessDecision Rejected(string reason) =>
        new(false, Reason: reason);
}
