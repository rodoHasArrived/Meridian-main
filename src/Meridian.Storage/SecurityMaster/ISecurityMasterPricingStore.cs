using System.Collections.Generic;
using Meridian.Contracts.SecurityMaster;

namespace Meridian.Storage.SecurityMaster;

public interface ISecurityMasterPricingStore
{
    Task<SecurityPricingHierarchyDto?> GetHierarchyAsync(Guid securityId, string? accountId, CancellationToken ct = default);
    Task UpsertHierarchyAsync(SecurityPricingHierarchyDto hierarchy, CancellationToken ct = default);
    Task RecordRawPriceAsync(Guid securityId, string sourceId, decimal price, DateTimeOffset priceAsOf, string recordedBy, CancellationToken ct = default);
    Task<IReadOnlyList<(string SourceId, decimal Price, DateTimeOffset PriceAsOf)>> GetRawPricesAsync(Guid securityId, CancellationToken ct = default);
}
