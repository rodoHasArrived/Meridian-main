using Meridian.Contracts.EnvironmentDesign;

namespace Meridian.Contracts.Services;

/// <summary>
/// Previews, publishes, and rolls back versioned environment configurations.
/// </summary>
public interface IEnvironmentPublishService
{
    Task<EnvironmentPublishPreviewDto> PreviewPublishAsync(EnvironmentPublishPlanDto plan, CancellationToken ct = default);

    Task<PublishedEnvironmentVersionDto> PublishAsync(EnvironmentPublishPlanDto plan, CancellationToken ct = default);

    Task<PublishedEnvironmentVersionDto> RollbackAsync(RollbackEnvironmentVersionRequest request, CancellationToken ct = default);
}
