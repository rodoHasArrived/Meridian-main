using Meridian.Contracts.AssetOperations;

namespace Meridian.Storage.AssetOperations;

public interface IAssetOperationsProjectionStore
{
    Task<AssetOperationsDetailDto?> GetAsync(Guid securityId, CancellationToken ct = default);

    Task UpsertAsync(
        AssetOperationsProjectionDto projection,
        AssetOperationsWriteApprovalDto approval,
        CancellationToken ct = default);
}
