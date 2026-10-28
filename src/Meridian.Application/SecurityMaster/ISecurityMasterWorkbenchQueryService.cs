using Meridian.Contracts.Workstation;

namespace Meridian.Application.SecurityMaster;

/// <summary>
/// Supplies trust, passport, and operating-model views and bulk conflict resolution for the
/// workbench.
/// </summary>
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
