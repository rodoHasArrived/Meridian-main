using Meridian.Contracts.EnvironmentDesign;

namespace Meridian.Contracts.Services;

public interface IEnvironmentPublishService
{
    Task<EnvironmentPublishPreviewDto> PreviewPublishAsync(EnvironmentPublishPlanDto plan, CancellationToken ct = default);

    Task<PublishedEnvironmentVersionDto> PublishAsync(EnvironmentPublishPlanDto plan, CancellationToken ct = default);

    Task<PublishedEnvironmentVersionDto> RollbackAsync(RollbackEnvironmentVersionRequest request, CancellationToken ct = default);
}
