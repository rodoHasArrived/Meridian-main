using Meridian.Contracts.EnvironmentDesign;

namespace Meridian.Contracts.Services;

/// <summary>
/// Retrieves the runtime projection of the current or a specified published environment version.
/// </summary>
public interface IEnvironmentRuntimeProjectionService
{
    Task<PublishedEnvironmentRuntimeDto?> GetCurrentRuntimeAsync(Guid? organizationId = null, CancellationToken ct = default);

    Task<PublishedEnvironmentRuntimeDto?> GetRuntimeForVersionAsync(Guid versionId, CancellationToken ct = default);
}
