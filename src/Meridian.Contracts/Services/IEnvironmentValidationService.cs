using Meridian.Contracts.EnvironmentDesign;

namespace Meridian.Contracts.Services;

public interface IEnvironmentValidationService
{
    Task<EnvironmentValidationResultDto> ValidateAsync(
        EnvironmentDraftDto draft,
        EnvironmentPublishPlanDto? publishPlan = null,
        CancellationToken ct = default);
}
