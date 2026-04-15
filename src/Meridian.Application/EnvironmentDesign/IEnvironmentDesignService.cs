using Meridian.Contracts.EnvironmentDesign;

namespace Meridian.Application.EnvironmentDesign;

public interface IEnvironmentDesignService
{
    Task<IReadOnlyList<EnvironmentDraftDto>> ListDraftsAsync(CancellationToken ct = default);

    Task<EnvironmentDraftDto?> GetDraftAsync(Guid draftId, CancellationToken ct = default);

    Task<EnvironmentDraftDto> CreateDraftAsync(CreateEnvironmentDraftRequest request, CancellationToken ct = default);

    Task<EnvironmentDraftDto> SaveDraftAsync(EnvironmentDraftDto draft, CancellationToken ct = default);

    Task DeleteDraftAsync(Guid draftId, CancellationToken ct = default);

    Task<IReadOnlyList<PublishedEnvironmentVersionDto>> ListPublishedVersionsAsync(
        Guid? organizationId = null,
        CancellationToken ct = default);

    Task<PublishedEnvironmentVersionDto?> GetPublishedVersionAsync(Guid versionId, CancellationToken ct = default);

    Task<PublishedEnvironmentVersionDto?> GetCurrentPublishedVersionAsync(Guid? organizationId = null, CancellationToken ct = default);
}
