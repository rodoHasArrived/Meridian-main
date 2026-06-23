using System.Collections.Generic;
using Meridian.Contracts.SecurityMaster;

namespace Meridian.Storage.SecurityMaster;

public interface IDataVendorEntitlementStore
{
    Task<IReadOnlyList<DataVendorEntitlementDto>> GetAllAsync(CancellationToken ct = default);

    /// <summary>
    /// Returns active (non-expired) entitlements whose effective-to date falls on or before
    /// <paramref name="cutoffUtc"/>, filtered at the storage layer.
    /// </summary>
    Task<IReadOnlyList<DataVendorEntitlementDto>> GetExpiringAsync(DateTimeOffset cutoffUtc, CancellationToken ct = default);

    Task<DataVendorEntitlementDto?> GetByIdAsync(Guid entitlementId, CancellationToken ct = default);
    Task<DataVendorEntitlementDto> UpsertAsync(DataVendorEntitlementDto entitlement, CancellationToken ct = default);
    Task DeactivateAsync(Guid entitlementId, CancellationToken ct = default);
}
