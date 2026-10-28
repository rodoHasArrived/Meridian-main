using System.Collections.Generic;

namespace Meridian.Contracts.SecurityMaster;

/// <summary>
/// Queries, updates, and deactivates data vendor entitlements and identifies approaching expirations.
/// </summary>
public interface IDataVendorEntitlementService
{
    Task<IReadOnlyList<DataVendorEntitlementDto>> GetAllAsync(CancellationToken ct = default);
    Task<IReadOnlyList<DataVendorEntitlementDto>> GetExpiringAsync(int withinDays, CancellationToken ct = default);
    Task<DataVendorEntitlementDto> UpsertAsync(UpsertDataVendorEntitlementRequest request, CancellationToken ct = default);
    Task DeactivateAsync(Guid entitlementId, string actor, CancellationToken ct = default);
}
