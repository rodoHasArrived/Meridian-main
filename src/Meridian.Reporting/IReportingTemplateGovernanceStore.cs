using Meridian.Contracts.Workstation;

namespace Meridian.Reporting;

/// <summary>
/// Loads and saves versioned template governance records without prescribing a storage adapter.
/// </summary>
public interface IReportingTemplateGovernanceStore
{
    IReadOnlyList<ReportTemplateGovernanceRecordDto> Load();

    void Save(IReadOnlyList<ReportTemplateGovernanceRecordDto> records);
}
