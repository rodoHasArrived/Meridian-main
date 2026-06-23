using System.Collections.Generic;
using Meridian.Contracts.SecurityMaster;

namespace Meridian.Application.SecurityMaster;

/// <summary>
/// No-op implementations for Clearwater-model Security Master services, registered
/// when <c>MERIDIAN_SECURITY_MASTER_CONNECTION_STRING</c> is not configured.
/// </summary>

public sealed class NullSecurityMasterPricingService : ISecurityMasterPricingService
{
    private static readonly IReadOnlyList<SecurityComparisonPriceDto> _empty =
        Array.Empty<SecurityComparisonPriceDto>();

    public Task<SecurityPricingHierarchyDto?> GetPricingHierarchyAsync(
        Guid securityId, string? accountId, CancellationToken ct = default)
        => Task.FromResult<SecurityPricingHierarchyDto?>(null);

    public Task UpsertPricingHierarchyAsync(
        SecurityPricingHierarchyDto hierarchy, CancellationToken ct = default)
        => throw new InvalidOperationException(
            "Security Master is not configured. Set MERIDIAN_SECURITY_MASTER_CONNECTION_STRING to enable pricing hierarchy.");

    public Task RecordRawPriceAsync(RecordRawPriceRequest request, CancellationToken ct = default)
        => throw new InvalidOperationException(
            "Security Master is not configured. Set MERIDIAN_SECURITY_MASTER_CONNECTION_STRING to enable raw price recording.");

    public Task<SecurityPriceGoldenCopyDto?> GetGoldenCopyPriceAsync(
        Guid securityId, string? accountId, CancellationToken ct = default)
        => Task.FromResult<SecurityPriceGoldenCopyDto?>(null);

    public Task<IReadOnlyList<SecurityComparisonPriceDto>> GetComparisonPricesAsync(
        Guid securityId, CancellationToken ct = default)
        => Task.FromResult(_empty);
}

public sealed class NullSecurityMasterCashFlowService : ISecurityMasterCashFlowService
{
    public Task<SecurityCashFlowSourceDto?> GetCashFlowSourceAsync(
        Guid securityId, CancellationToken ct = default)
        => Task.FromResult<SecurityCashFlowSourceDto?>(null);

    public Task UpsertCashFlowSourceAsync(
        UpsertCashFlowSourceRequest request, CancellationToken ct = default)
        => throw new InvalidOperationException(
            "Security Master is not configured. Set MERIDIAN_SECURITY_MASTER_CONNECTION_STRING to enable cash flow source management.");

    public Task<StructuredCashFlowProjectionDto?> GetProjectionAsync(
        Guid securityId, StructuredCashFlowScenario scenario, CancellationToken ct = default)
        => Task.FromResult<StructuredCashFlowProjectionDto?>(null);
}

public sealed class NullDataVendorEntitlementService : IDataVendorEntitlementService
{
    private static readonly IReadOnlyList<DataVendorEntitlementDto> _empty =
        Array.Empty<DataVendorEntitlementDto>();

    public Task<IReadOnlyList<DataVendorEntitlementDto>> GetAllAsync(CancellationToken ct = default)
        => Task.FromResult(_empty);

    public Task<IReadOnlyList<DataVendorEntitlementDto>> GetExpiringAsync(
        int withinDays, CancellationToken ct = default)
        => Task.FromResult(_empty);

    public Task<DataVendorEntitlementDto> UpsertAsync(
        UpsertDataVendorEntitlementRequest request, CancellationToken ct = default)
        => throw new InvalidOperationException(
            "Security Master is not configured. Set MERIDIAN_SECURITY_MASTER_CONNECTION_STRING to enable data vendor entitlement management.");

    public Task DeactivateAsync(Guid entitlementId, string actor, CancellationToken ct = default)
        => throw new InvalidOperationException(
            "Security Master is not configured. Set MERIDIAN_SECURITY_MASTER_CONNECTION_STRING to enable data vendor entitlement management.");
}

public sealed class NullSecurityMasterDataQualityService : ISecurityMasterDataQualityService
{
    public Task<SecurityMasterQualityReportDto> RunQualityChecksAsync(CancellationToken ct = default)
        => throw new InvalidOperationException(
            "Security Master is not configured. Set MERIDIAN_SECURITY_MASTER_CONNECTION_STRING to enable data quality checks.");

    public Task<SecurityMasterQualityReportDto?> GetLatestReportAsync(CancellationToken ct = default)
        => Task.FromResult<SecurityMasterQualityReportDto?>(null);
}
