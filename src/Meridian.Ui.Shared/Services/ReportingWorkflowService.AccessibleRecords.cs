using Meridian.Contracts.Workstation;

namespace Meridian.Ui.Shared.Services;

public sealed partial class ReportPackWorkflowService
{
    /// <summary>
    /// The newest report-pack records the caller's access context admits, capped after authorization
    /// rather than before it.
    /// <para>
    /// The cap and the filter do not commute, which is the whole reason this exists beside
    /// <see cref="ListRecords"/>. That one takes the newest records across every tenant, so a caller
    /// whose own packs are older than two hundred other tenants' is handed a page containing none of
    /// its own: an explorer reporting nothing while the records are retained, and a drill-in
    /// answering 404 for a record the host still holds. Filtering first costs one ordering pass over
    /// the full set, which <see cref="ListRecords"/> already pays.
    /// </para>
    /// <para>
    /// A null context is the legacy unbound caller and filters nothing, so this matches
    /// <see cref="ListRecords"/> exactly in that case.
    /// </para>
    /// </summary>
    public IReadOnlyList<ReportPackWorkflowRecordDto> ListAccessibleRecords(
        int limit,
        ReportAccessQueryContext? accessContext) =>
        ReportPackRunReadService
            .FilterWorkflowRecords(OrderedRecordsNewestFirst(), accessContext)
            .Take(Math.Clamp(limit, 1, 200))
            .ToArray();

    private ReportPackWorkflowRecordDto[] OrderedRecordsNewestFirst() =>
        _records.Values
            .OrderByDescending(static x => x.UpdatedAt)
            .ThenByDescending(static x => x.Version)
            .ThenBy(static x => x.ReportId)
            .ToArray();
}
