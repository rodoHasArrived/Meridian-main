using Meridian.Application.Reporting;

namespace Meridian.Ui.Services.Services.Reporting;

public sealed record ReportingStatusProjection(
    string RunId,
    string TemplateId,
    string Family,
    string Status,
    int SectionCount,
    int LineageLinkedSections,
    IReadOnlyList<ReportingRunAuditEntry> AuditTrail);

public interface IReportingStatusProjectionService
{
    ReportingStatusProjection Project(ReportingOutputManifest manifest, IReadOnlyList<ReportingRunAuditEntry> audit);
}

public sealed class ReportingStatusProjectionService : IReportingStatusProjectionService
{
    public ReportingStatusProjection Project(ReportingOutputManifest manifest, IReadOnlyList<ReportingRunAuditEntry> audit)
    {
        var lineageLinkedSections = manifest.Sections.Count(s => !string.IsNullOrWhiteSpace(s.DatasetSnapshotId)
            && !string.IsNullOrWhiteSpace(s.ReconciliationCheckpointId));

        return new ReportingStatusProjection(
            manifest.RunId,
            manifest.TemplateId,
            ResolveFamily(manifest.TemplateId),
            manifest.Status.ToString(),
            manifest.Sections.Length,
            lineageLinkedSections,
            audit);
    }

    private static string ResolveFamily(string templateId)
    {
        if (templateId.StartsWith("investor", StringComparison.OrdinalIgnoreCase)) return ReportingTemplateFamily.InvestorStatement.ToString();
        if (templateId.StartsWith("sec", StringComparison.OrdinalIgnoreCase)) return ReportingTemplateFamily.SecFilingPacket.ToString();
        if (templateId.StartsWith("shadow", StringComparison.OrdinalIgnoreCase)) return ReportingTemplateFamily.ShadowNavPack.ToString();
        return "Unknown";
    }
}
