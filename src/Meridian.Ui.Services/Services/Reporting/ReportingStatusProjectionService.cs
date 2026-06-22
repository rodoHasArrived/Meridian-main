using Meridian.Reporting;

namespace Meridian.Ui.Services.Services.Reporting;

public sealed record ReportingLineageProjection(
    string SectionId,
    string DatasetSnapshotId,
    string DatasetSnapshotHash,
    string ReconciliationCheckpointId);

public sealed record ReportingTemplateProjection(
    string TemplateId,
    string Family,
    string Name,
    string Version,
    IReadOnlyList<string> Sections);

public sealed record ReportingStatusProjection(
    string RunId,
    string TemplateId,
    string Family,
    string Status,
    int AttemptCount,
    string Trigger,
    int SectionCount,
    int LineageLinkedSections,
    IReadOnlyList<ReportingLineageProjection> Lineage,
    IReadOnlyList<string> Artifacts,
    IReadOnlyList<ReportingRunAuditEntry> AuditTrail,
    string? FailureReason);

public interface IReportingStatusProjectionService
{
    ReportingStatusProjection Project(ReportingOutputManifest manifest, IReadOnlyList<ReportingRunAuditEntry> audit);
    IReadOnlyList<ReportingTemplateProjection> ProjectTemplates(IReportingTemplateCatalog catalog);
}

public sealed class ReportingStatusProjectionService : IReportingStatusProjectionService
{
    public ReportingStatusProjection Project(ReportingOutputManifest manifest, IReadOnlyList<ReportingRunAuditEntry> audit)
    {
        var lineage = manifest.Sections
            .Where(static s => !string.IsNullOrWhiteSpace(s.DatasetSnapshotId)
                && !string.IsNullOrWhiteSpace(s.ReconciliationCheckpointId))
            .Select(static s => new ReportingLineageProjection(
                s.SectionId,
                s.DatasetSnapshotId,
                s.Lineage.DatasetSnapshotHash,
                s.ReconciliationCheckpointId))
            .ToArray();

        return new ReportingStatusProjection(
            manifest.RunId,
            manifest.TemplateId,
            ResolveFamily(manifest.TemplateId),
            manifest.Status.ToString(),
            manifest.AttemptCount,
            manifest.Trigger.ToString(),
            manifest.Sections.Length,
            lineage.Length,
            lineage,
            manifest.Artifacts.ToArray(),
            audit.ToArray(),
            manifest.FailureReason);
    }

    public IReadOnlyList<ReportingTemplateProjection> ProjectTemplates(IReportingTemplateCatalog catalog)
        => catalog.ListTemplates()
            .Select(static template => new ReportingTemplateProjection(
                template.TemplateId,
                template.Family.ToString(),
                template.Name,
                template.Version,
                template.Sections.ToArray()))
            .ToArray();

    private static string ResolveFamily(string templateId)
    {
        if (templateId.StartsWith("investor", StringComparison.OrdinalIgnoreCase))
            return ReportingTemplateFamily.InvestorStatement.ToString();
        if (templateId.StartsWith("sec", StringComparison.OrdinalIgnoreCase))
            return ReportingTemplateFamily.SecFilingPacket.ToString();
        if (templateId.StartsWith("shadow", StringComparison.OrdinalIgnoreCase))
            return ReportingTemplateFamily.ShadowNavPack.ToString();
        return "Unknown";
    }
}
