using Meridian.Contracts.SecurityMaster;

namespace Meridian.Contracts.Services;

/// <summary>
/// Workflow-facing Security Master validation gate.
/// </summary>
public interface ISecurityValidationGateService
{
    /// <summary>
    /// Validates Security Master identity and policy posture for a symbol-scoped workflow.
    /// </summary>
    Task<SecurityValidationGateResultDto> ValidateSymbolAsync(
        string symbol,
        SecurityValidationWorkflowDto workflow,
        string? workflowReference = null,
        string? actor = null,
        bool persistSnapshot = false,
        CancellationToken ct = default);

    /// <summary>
    /// Validates Security Master identity and policy posture for a known security.
    /// </summary>
    Task<SecurityValidationGateResultDto> ValidateSecurityAsync(
        Guid securityId,
        SecurityValidationWorkflowDto workflow,
        string? workflowReference = null,
        string? actor = null,
        bool persistSnapshot = false,
        string? symbol = null,
        CancellationToken ct = default);
}
