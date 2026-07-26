using System.Globalization;
using Meridian.Documents;
using Meridian.Reporting;

namespace Meridian.Ui.Shared.Services;

/// <summary>
/// Renders the primary PDF/XLSX artifact of a governed reporting run. This is the seam that lets the
/// certified artifact producer stay the single orchestrator (declarations, hashing, retained
/// manifest, provenance) while the client-grade document bytes are produced by
/// <c>Meridian.Documents</c>. Implementations must be deterministic: identical manifests must yield
/// identical bytes, because artifacts are hash-verified on read-back.
/// </summary>
public interface IReportingPrimaryDocumentRenderer
{
    byte[] RenderPdf(ReportingOutputManifest manifest);

    byte[] RenderWorkbook(ReportingOutputManifest manifest);
}

/// <summary>
/// Client-grade primary-document renderer backed by <see cref="ClientGradeReportRenderer"/>
/// (QuestPDF/ClosedXML). It maps the certified reporting manifest into the presentation-neutral
/// <see cref="ReportDocumentModel"/> and preserves the exact certified figures — only the
/// presentation changes relative to the built-in plain-text output.
/// </summary>
public sealed class DocumentsReportingPrimaryDocumentRenderer : IReportingPrimaryDocumentRenderer
{
    private readonly ClientGradeReportRenderer _renderer = new();

    public byte[] RenderPdf(ReportingOutputManifest manifest)
        => _renderer.RenderPdf(BuildModel(manifest));

    public byte[] RenderWorkbook(ReportingOutputManifest manifest)
        => _renderer.RenderWorkbook(BuildModel(manifest));

    private static ReportDocumentModel BuildModel(ReportingOutputManifest manifest)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        var template = manifest.ResolvedTemplate!;
        var rowHash = DeterministicReportingCertifiedArtifactProducer.ComputeCertifiedRowsHash(
            manifest.CertifiedDatasetRows);

        if (manifest.CertifiedPartnersCapital is { } partnersCapital)
        {
            return BuildPartnersCapitalModel(manifest, partnersCapital, rowHash);
        }

        var headerFields = new List<ReportDocumentField>
        {
            new("Run", manifest.RunId),
            new("Template", $"{template.Name} v{template.Version.ToString(CultureInfo.InvariantCulture)}"),
            new("As of", manifest.AsOfDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)),
            new("Source checkpoint", manifest.AuthoritativeSource!.CheckpointId),
            new("Snapshot", manifest.CertifiedSnapshot!.SnapshotId),
            new("Certified rows", manifest.CertifiedDatasetRows.Length.ToString(CultureInfo.InvariantCulture)),
            new("Certified row hash", rowHash),
        };

        var tables = new List<ReportDocumentTable>();
        var columns = DeterministicReportingCertifiedArtifactProducer.ResolveCertifiedColumns(
            manifest.CertifiedDatasetRows);
        if (columns.Length > 0)
        {
            var rows = manifest.CertifiedDatasetRows
                .Select(row => (IReadOnlyList<string>)Array.ConvertAll(
                    columns,
                    column => row.TryGetValue(column, out var value) ? value : string.Empty))
                .ToArray();
            tables.Add(new ReportDocumentTable("Certified dataset", columns, rows));
        }

        return new ReportDocumentModel(
            Title: template.Name,
            HeaderFields: headerFields,
            Tables: tables,
            Subtitle: $"Governed report · run {manifest.RunId}",
            FooterNote: $"Certified row hash {(rowHash.Length <= 16 ? rowHash : rowHash[..16])}");
    }

    // Bespoke Capital Account Statement layout: a per-partner + Total roll-forward table
    // (opening -> contributions -> distributions -> allocated -> other -> ending) sourced from the
    // certified partners-capital projection. Figures are pre-formatted culture-invariant for
    // deterministic bytes.
    private static ReportDocumentModel BuildPartnersCapitalModel(
        ReportingOutputManifest manifest,
        CertifiedPartnersCapitalProjection projection,
        string rowHash)
    {
        var template = manifest.ResolvedTemplate!;
        var headerFields = new List<ReportDocumentField>
        {
            new("Run", manifest.RunId),
            new("Template", $"{template.Name} v{template.Version.ToString(CultureInfo.InvariantCulture)}"),
            new("Period", $"{projection.PeriodStart.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)} to {projection.AsOf.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)}"),
            new("As of", manifest.AsOfDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)),
            new("Source checkpoint", manifest.AuthoritativeSource!.CheckpointId),
            new("Snapshot", manifest.CertifiedSnapshot!.SnapshotId),
            new("Reconciled", projection.IsReconciled
                ? "Yes"
                : $"No (variance {FormatMoney(projection.ReconciliationVariance)})"),
        };

        var headers = new[]
        {
            "Partner", "Beginning", "Contributions", "Distributions", "Allocated", "Other", "Ending",
        };
        var rows = new List<IReadOnlyList<string>>();
        foreach (var account in projection.Accounts)
        {
            rows.Add(new[]
            {
                PartnerLabel(account),
                FormatMoney(account.BeginningCapital),
                FormatMoney(account.Contributions),
                FormatMoney(account.Distributions),
                FormatMoney(account.AllocatedResult),
                FormatMoney(account.OtherMovements),
                FormatMoney(account.EndingCapital),
            });
        }

        rows.Add(new[]
        {
            "Total",
            FormatMoney(projection.BeginningCapital),
            FormatMoney(projection.Contributions),
            FormatMoney(projection.Distributions),
            FormatMoney(projection.AllocatedResult),
            FormatMoney(projection.OtherMovements),
            FormatMoney(projection.EndingCapital),
        });

        var tables = new List<ReportDocumentTable>
        {
            new("Statement of Changes in Partners' Capital", headers, rows),
        };

        return new ReportDocumentModel(
            Title: template.Name,
            HeaderFields: headerFields,
            Tables: tables,
            Subtitle: $"Partners' capital roll-forward - run {manifest.RunId}",
            FooterNote: $"Certified row hash {(rowHash.Length <= 16 ? rowHash : rowHash[..16])}");
    }

    private static string PartnerLabel(CertifiedPartnersCapitalAccount account)
        => string.IsNullOrWhiteSpace(account.InvestorId)
            ? account.CapitalAccountName
            : $"{account.CapitalAccountName} ({account.InvestorId})";

    private static string FormatMoney(decimal value)
        => value.ToString("N2", CultureInfo.InvariantCulture);
}
