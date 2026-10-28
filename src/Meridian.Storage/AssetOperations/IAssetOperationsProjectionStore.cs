using Meridian.Contracts.AssetOperations;

namespace Meridian.Storage.AssetOperations;

/// <summary>
/// Reads and updates asset operations projections with their write approval.
/// </summary>
public interface IAssetOperationsProjectionStore
{
    Task<AssetOperationsDetailDto?> GetAsync(Guid securityId, CancellationToken ct = default);

    Task UpsertAsync(
        AssetOperationsProjectionDto projection,
        AssetOperationsWriteApprovalDto approval,
        CancellationToken ct = default);
}
