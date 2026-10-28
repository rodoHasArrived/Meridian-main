using Meridian.Contracts.SecurityMaster;

namespace Meridian.Storage.SecurityMaster;

/// <summary>
/// Retains Security Master quality reports and retrieves the latest report.
/// </summary>
public interface ISecurityMasterQualityReportStore
{
    Task<SecurityMasterQualityReportDto?> GetLatestAsync(CancellationToken ct = default);
    Task SaveAsync(SecurityMasterQualityReportDto report, CancellationToken ct = default);
}
