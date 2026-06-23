namespace Meridian.Contracts.SecurityMaster;

public interface ISecurityMasterDataQualityService
{
    Task<SecurityMasterQualityReportDto> RunQualityChecksAsync(CancellationToken ct = default);
    Task<SecurityMasterQualityReportDto?> GetLatestReportAsync(CancellationToken ct = default);
}
