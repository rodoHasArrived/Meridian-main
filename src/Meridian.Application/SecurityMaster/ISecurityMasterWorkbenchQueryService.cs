using Meridian.Contracts.Workstation;

namespace Meridian.Application.SecurityMaster;

public interface ISecurityMasterWorkbenchQueryService
{
    Task<SecurityMasterTrustSnapshotDto?> GetTrustSnapshotAsync(
        Guid securityId,
        string? fundProfileId,
        CancellationToken ct = default);

    Task<InstrumentPassportDto?> GetInstrumentPassportAsync(
        Guid securityId,
        string? fundProfileId,
        CancellationToken ct = default);

    Task<SecurityMasterOperatingModelDto?> GetOperatingModelAsync(
        Guid securityId,
        string? fundProfileId,
        CancellationToken ct = default);

    Task<BulkResolveSecurityMasterConflictsResult> BulkResolveConflictsAsync(
        BulkResolveSecurityMasterConflictsRequest request,
        CancellationToken ct = default);
}
