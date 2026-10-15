using System.Collections.Generic;

namespace Meridian.Contracts.SecurityMaster;

public interface ISecurityMasterPricingService
{
    Task<SecurityPricingHierarchyDto?> GetPricingHierarchyAsync(Guid securityId, string? accountId, CancellationToken ct = default);
    Task UpsertPricingHierarchyAsync(SecurityPricingHierarchyDto hierarchy, CancellationToken ct = default);
    Task RecordRawPriceAsync(RecordRawPriceRequest request, CancellationToken ct = default);
    Task<SecurityPriceGoldenCopyDto?> GetGoldenCopyPriceAsync(Guid securityId, string? accountId, CancellationToken ct = default);
    Task<SecurityPriceGoldenCopyDto?> GetGoldenCopyPriceAsOfAsync(Guid securityId, string? accountId, DateTimeOffset asOf, CancellationToken ct = default, DateTimeOffset? knownAt = null);
    /// <summary>Replays an exact retained selection; absent or differently scoped receipts return null.</summary>
    Task<SecurityPriceGoldenCopyDto?> GetGoldenCopySelectionAsync(Guid securityId, string? accountId, Guid receiptId, CancellationToken ct = default);
    Task<IReadOnlyList<SecurityComparisonPriceDto>> GetComparisonPricesAsync(Guid securityId, CancellationToken ct = default);
    Task<SecurityClosePriceSetDto> CreateClosePriceSetAsync(CreateSecurityClosePriceSetRequest request, CancellationToken ct = default);
    Task<SecurityClosePriceSetDto> ApproveClosePriceSetAsync(ApproveSecurityClosePriceSetRequest request, CancellationToken ct = default);
    /// <summary>Returns only the retained version in the exact account/book/date scope.</summary>
    Task<SecurityClosePriceSetDto?> GetClosePriceSetAsync(string accountId, string bookId, DateOnly valuationDate, Guid snapshotId, CancellationToken ct = default);
    /// <summary>Returns every retained version and approval in exact account/book/date scope, ordered by ascending version.</summary>
    Task<IReadOnlyList<SecurityClosePriceSetDto>> GetClosePriceSetsAsync(string accountId, string bookId, DateOnly valuationDate, CancellationToken ct = default);
}
