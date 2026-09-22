using System.Collections.Generic;

namespace Meridian.Contracts.SecurityMaster;

public interface ISecurityMasterPricingService
{
    Task<SecurityPricingHierarchyDto?> GetPricingHierarchyAsync(Guid securityId, string? accountId, CancellationToken ct = default);
    Task UpsertPricingHierarchyAsync(SecurityPricingHierarchyDto hierarchy, CancellationToken ct = default);
    Task RecordRawPriceAsync(RecordRawPriceRequest request, CancellationToken ct = default);
    Task<SecurityPriceGoldenCopyDto?> GetGoldenCopyPriceAsync(Guid securityId, string? accountId, CancellationToken ct = default);
    Task<SecurityPriceGoldenCopyDto?> GetGoldenCopyPriceAsOfAsync(Guid securityId, string? accountId, DateTimeOffset asOf, CancellationToken ct = default, DateTimeOffset? knownAt = null);
    Task<IReadOnlyList<SecurityComparisonPriceDto>> GetComparisonPricesAsync(Guid securityId, CancellationToken ct = default);
}
