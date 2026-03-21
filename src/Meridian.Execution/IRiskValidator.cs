using Meridian.Execution.Sdk;

namespace Meridian.Execution;

/// <summary>
/// Pre-trade risk validation. Called by the OMS before routing orders to the gateway.
/// </summary>
public interface IRiskValidator
{
    /// <summary>Validates an order against risk rules.</summary>
    Task<RiskValidationResult> ValidateOrderAsync(OrderRequest request, CancellationToken ct = default);
}

/// <summary>Result of a risk validation check.</summary>
public sealed record RiskValidationResult
{
    public required bool IsApproved { get; init; }
    public string? RejectReason { get; init; }

    public static RiskValidationResult Approved() => new() { IsApproved = true };
    public static RiskValidationResult Rejected(string reason) => new() { IsApproved = false, RejectReason = reason };
}
