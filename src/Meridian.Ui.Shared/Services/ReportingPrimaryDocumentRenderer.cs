using System.Globalization;
using Meridian.Contracts.Workstation;
using Meridian.Documents;
using Meridian.Ledger;
using Meridian.Reporting;

namespace Meridian.Ui.Shared.Services;

/// <summary>
/// Checkpoint-bound input for a canonical ledger presentation. The source implementation must
/// resolve an already-built <see cref="LedgerFinancialReportPack"/> from the exact authoritative
/// checkpoint represented by the reporting manifest. It must not rebuild the report pack from the
/// manifest's display rows because those rows do not prove opening balances or period
/// classifications.
/// </summary>
public sealed record ReportingCertifiedLedgerPresentationInput(
    string SourceCheckpointId,
    string SourceCheckpointHash,
    string CertifiedDatasetHashSha256,
    LedgerFinancialReportPack ReportPack);

/// <summary>
/// Shared binding rules for the one governed report family that requires the complete-history
/// ledger presentation. The authoritative checkpoint remains template-neutral; this separate
/// evidence receipt binds the signed report pack to the certified manifest without making every
/// client package depend on a partners-capital replay.
/// </summary>
internal static class ReportingCertifiedLedgerPresentationBinding
{
    internal const string CapitalAccountTemplateId = "capital-account-statement";
    internal const string EvidencePrefix = "ledger-report-pack:";

    internal static bool IsRequired(
        string? templateId,
        ReportingOutputFormatDto outputFormat) =>
        string.Equals(templateId, CapitalAccountTemplateId, StringComparison.OrdinalIgnoreCase)
        && outputFormat == ReportingOutputFormatDto.ClientPackage;

    internal static bool IsRequired(ReportingOutputManifest manifest) =>
        manifest.ResolvedParameters is { } parameters
        && IsRequired(manifest.TemplateId, parameters.OutputFormat);

    internal static string BuildEvidenceId(LedgerFinancialReportPack reportPack)
    {
        ArgumentNullException.ThrowIfNull(reportPack);
        return $"{EvidencePrefix}{reportPack.Request.ReportId}:{reportPack.Signature.PayloadChecksumSha256}";
    }

    internal static string? GetSingleEvidenceId(ReportingAuthoritativeSourceCheckpoint checkpoint)
    {
        ArgumentNullException.ThrowIfNull(checkpoint);
        if (checkpoint.EvidenceIds.IsDefaultOrEmpty)
        {
            return null;
        }

        var matches = checkpoint.EvidenceIds
            .Where(static evidence =>
                evidence.StartsWith(EvidencePrefix, StringComparison.Ordinal))
            .Take(2)
            .ToArray();
        return matches.Length == 1 ? matches[0] : null;
    }
}

/// <summary>
/// Resolves the canonical ledger report pack whose accounting presentation was certified for an
/// exact reporting checkpoint. Returning <see langword="null"/> means that no authoritative
/// presentation is available and callers that require it must fail closed.
/// </summary>
public interface IReportingCertifiedLedgerPresentationSource
{
    ValueTask<ReportingCertifiedLedgerPresentationInput?> ResolveExactAsync(
        ReportingOutputManifest manifest,
        CancellationToken cancellationToken = default);
}

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
/// Extended primary-document contract for renderers that can consume canonical ledger
/// presentation tables supplied by the certified artifact producer.
/// </summary>
public interface IReportingPrimaryDocumentRendererWithLedgerPresentation :
    IReportingPrimaryDocumentRenderer
{
    byte[] RenderPdf(
        ReportingOutputManifest manifest,
        IReadOnlyList<LedgerReportTable> certifiedLedgerPresentation);

    byte[] RenderWorkbook(
        ReportingOutputManifest manifest,
        IReadOnlyList<LedgerReportTable> certifiedLedgerPresentation);
}

/// <summary>
/// Client-grade primary-document renderer backed by <see cref="ClientGradeReportRenderer"/>
/// (QuestPDF/ClosedXML). It maps the certified reporting manifest into the presentation-neutral
/// <see cref="ReportDocumentModel"/> and preserves the exact certified figures — only the
/// presentation changes relative to the built-in plain-text output.
/// </summary>
public sealed class DocumentsReportingPrimaryDocumentRenderer :
    IReportingPrimaryDocumentRendererWithLedgerPresentation
{
    private const string PartnersCapitalTableTitle = "Statement of Changes in Partners' Capital";

    private readonly ClientGradeReportRenderer _renderer = new();

    public byte[] RenderPdf(ReportingOutputManifest manifest)
        => _renderer.RenderPdf(BuildModel(manifest));

    public byte[] RenderWorkbook(ReportingOutputManifest manifest)
        => _renderer.RenderWorkbook(BuildModel(manifest));

    public byte[] RenderPdf(
        ReportingOutputManifest manifest,
        IReadOnlyList<LedgerReportTable> certifiedLedgerPresentation)
        => _renderer.RenderPdf(BuildModel(manifest, certifiedLedgerPresentation));

    public byte[] RenderWorkbook(
        ReportingOutputManifest manifest,
        IReadOnlyList<LedgerReportTable> certifiedLedgerPresentation)
        => _renderer.RenderWorkbook(BuildModel(manifest, certifiedLedgerPresentation));

    /// <summary>
    /// Projects the certified manifest and, when supplied, canonical ledger presentation tables into
    /// the client-grade document model. The manifest's retained journal rows do not carry the
    /// beginning-balance and period-classification state required to recalculate partners' capital,
    /// so this method never reconstructs that accounting math. A caller that owns the exact
    /// certified ledger report pack may pass the output of
    /// <see cref="LedgerReportPresentation.BuildTables"/>; only its already-calculated partners'
    /// capital table is copied, byte-for-byte at the cell-value boundary.
    /// </summary>
    internal static ReportDocumentModel BuildModel(
        ReportingOutputManifest manifest,
        IReadOnlyList<LedgerReportTable>? certifiedLedgerPresentation = null)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        var template = manifest.ResolvedTemplate!;
        var rowHash = DeterministicReportingCertifiedArtifactProducer.ComputeCertifiedRowsHash(
            manifest.CertifiedDatasetRows);

        if (certifiedLedgerPresentation is null
            && manifest.CertifiedPartnersCapital is { } partnersCapital)
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

        if (CanProjectPartnersCapital(manifest, certifiedLedgerPresentation))
        {
            var partnersCapitalTable = certifiedLedgerPresentation!.First(static table =>
                string.Equals(table.Title, PartnersCapitalTableTitle, StringComparison.Ordinal));
            tables.Add(new ReportDocumentTable(
                partnersCapitalTable.Title,
                partnersCapitalTable.Headers,
                partnersCapitalTable.Rows));
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

    private static bool CanProjectPartnersCapital(
        ReportingOutputManifest manifest,
        IReadOnlyList<LedgerReportTable>? certifiedLedgerPresentation)
        => string.Equals(
                manifest.TemplateId,
                ReportingCertifiedLedgerPresentationBinding.CapitalAccountTemplateId,
                StringComparison.OrdinalIgnoreCase)
            && string.Equals(
                manifest.AuthoritativeSource?.SourceKind,
                "durable-ledger-journal",
                StringComparison.Ordinal)
            && certifiedLedgerPresentation?.Any(static table =>
                string.Equals(table.Title, PartnersCapitalTableTitle, StringComparison.Ordinal)) == true;
}
