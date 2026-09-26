using System.Collections.Generic;
using Meridian.Contracts.SecurityMaster;

namespace Meridian.Storage.SecurityMaster;

public interface ISecurityMasterPricingStore
{
    Task<SecurityPricingHierarchyDto?> GetHierarchyAsync(Guid securityId, string? accountId, CancellationToken ct = default);
    Task<SecurityPricingHierarchyDto?> GetHierarchyAsOfAsync(Guid securityId, string? accountId, DateTimeOffset asOf, CancellationToken ct = default, DateTimeOffset? knownAt = null);
    Task UpsertHierarchyAsync(SecurityPricingHierarchyDto hierarchy, CancellationToken ct = default);
    Task RecordRawPriceAsync(RecordRawPriceRequest request, CancellationToken ct = default);
    Task RetainPriceSelectionAsync(SecurityPriceGoldenCopyDto selection, string? accountId, CancellationToken ct = default);
    Task<SecurityPriceGoldenCopyDto?> GetPriceSelectionAsync(Guid securityId, string? accountId, Guid receiptId, CancellationToken ct = default);
    Task<IReadOnlyList<SecurityRawPriceDto>> GetRawPricesAsync(Guid securityId, DateTimeOffset asOf, CancellationToken ct = default, DateTimeOffset? knownAt = null);
}
