using Meridian.Contracts.EnvironmentDesign;

namespace Meridian.Application.EnvironmentDesign;

public interface IEnvironmentValidationService
{
    Task<EnvironmentValidationResultDto> ValidateAsync(
        EnvironmentDraftDto draft,
        EnvironmentPublishPlanDto? publishPlan = null,
        CancellationToken ct = default);
}
