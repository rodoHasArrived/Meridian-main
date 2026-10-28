using Meridian.Contracts.EnvironmentDesign;

namespace Meridian.Contracts.Services;

/// <summary>
/// Validates an environment design and returns its validation findings.
/// </summary>
public interface IEnvironmentValidationService
{
    Task<EnvironmentValidationResultDto> ValidateAsync(
        EnvironmentDraftDto draft,
        EnvironmentPublishPlanDto? publishPlan = null,
        CancellationToken ct = default);
}
