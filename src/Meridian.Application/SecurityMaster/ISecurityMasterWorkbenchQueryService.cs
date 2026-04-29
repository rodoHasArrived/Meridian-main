using Meridian.Contracts.Workstation;

namespace Meridian.Application.SecurityMaster;

public interface ISecurityMasterWorkbenchQueryService
{
    Task<SecurityMasterTrustSnapshotDto?> GetTrustSnapshotAsync(
        Guid securityId,
        string? fundProfileId,
        CancellationToken ct = default);

    Task<BulkResolveSecurityMasterConflictsResult> BulkResolveConflictsAsync(
        BulkResolveSecurityMasterConflictsRequest request,
        CancellationToken ct = default);
}
