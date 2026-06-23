using System.Collections.Generic;
using Meridian.Contracts.SecurityMaster;

namespace Meridian.Storage.SecurityMaster;

public interface IDataVendorEntitlementStore
{
    Task<IReadOnlyList<DataVendorEntitlementDto>> GetAllAsync(CancellationToken ct = default);
    Task<DataVendorEntitlementDto?> GetByIdAsync(Guid entitlementId, CancellationToken ct = default);
    Task<DataVendorEntitlementDto> UpsertAsync(DataVendorEntitlementDto entitlement, CancellationToken ct = default);
    Task DeactivateAsync(Guid entitlementId, CancellationToken ct = default);
}
