using Meridian.Application.SecurityMaster;
using Meridian.Contracts.Workstation;

namespace Meridian.Ui.Shared.Services;

public interface IMultiAssetCoverageReadService
{
    Task<MultiAssetCoverageSummaryDto> GetCoverageAsync(
        string? fundAccountId,
        string? entity,
        string? assetClass,
        CancellationToken ct = default);
}

public sealed class MultiAssetCoverageReadService : IMultiAssetCoverageReadService
{
    private readonly ISecurityMasterOperationalReadinessService _readinessService;

    public MultiAssetCoverageReadService(ISecurityMasterOperationalReadinessService readinessService)
    {
        _readinessService = readinessService;
    }

    public Task<MultiAssetCoverageSummaryDto> GetCoverageAsync(
        string? fundAccountId,
        string? entity,
        string? assetClass,
        CancellationToken ct = default)
        => _readinessService.GetReadinessAsync(
            new SecurityMasterOperationalReadinessRequest(fundAccountId, entity, assetClass),
            ct);
}
