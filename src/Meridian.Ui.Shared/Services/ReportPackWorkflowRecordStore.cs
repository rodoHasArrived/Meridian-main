using Meridian.Contracts.Workstation;

namespace Meridian.Ui.Shared.Services;

public sealed record ReportPackWorkflowRecordStoreOptions(string SnapshotPath);

/// <summary>
/// Compatibility-only snapshot seam. Production composition does not register this store;
/// canonical report-pack workflow authority is reporting governance.
/// </summary>
public interface IReportPackWorkflowRecordStore
{
    IReadOnlyList<ReportPackWorkflowRecordDto> Load();

    void Save(IReadOnlyList<ReportPackWorkflowRecordDto> records);
}
