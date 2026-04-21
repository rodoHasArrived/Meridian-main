using Meridian.Contracts.EnvironmentDesign;

namespace Meridian.Application.EnvironmentDesign;

public interface IEnvironmentRuntimeProjectionService
{
    Task<PublishedEnvironmentRuntimeDto?> GetCurrentRuntimeAsync(Guid? organizationId = null, CancellationToken ct = default);

    Task<PublishedEnvironmentRuntimeDto?> GetRuntimeForVersionAsync(Guid versionId, CancellationToken ct = default);
}
