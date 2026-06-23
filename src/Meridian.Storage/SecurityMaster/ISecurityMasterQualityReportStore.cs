using Meridian.Contracts.SecurityMaster;

namespace Meridian.Storage.SecurityMaster;

public interface ISecurityMasterQualityReportStore
{
    Task<SecurityMasterQualityReportDto?> GetLatestAsync(CancellationToken ct = default);
    Task SaveAsync(SecurityMasterQualityReportDto report, CancellationToken ct = default);
}
