namespace Meridian.Contracts.SecurityMaster;

/// <summary>
/// Runs Security Master quality checks and retrieves the latest retained quality report.
/// </summary>
public interface ISecurityMasterDataQualityService
{
    Task<SecurityMasterQualityReportDto> RunQualityChecksAsync(CancellationToken ct = default);
    Task<SecurityMasterQualityReportDto?> GetLatestReportAsync(CancellationToken ct = default);
}
