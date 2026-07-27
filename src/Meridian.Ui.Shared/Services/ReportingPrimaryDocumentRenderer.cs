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
        bool explicitlyRequired,
        ReportingOutputFormatDto outputFormat) =>
        outputFormat == ReportingOutputFormatDto.ClientPackage
        && (explicitlyRequired
            || string.Equals(
                templateId,
                CapitalAccountTemplateId,
                StringComparison.OrdinalIgnoreCase));

    internal static bool IsRequired(
        ReportingAuthoritativeSourceCaptureIntent? intent,
        ReportingOutputFormatDto outputFormat) =>
        IsRequired(
            intent?.TemplateId,
            intent?.RequiresCertifiedLedgerPresentation == true,
            outputFormat);

    internal static bool IsRequired(ReportingOutputManifest manifest) =>
        manifest.ResolvedParameters is { } parameters
        && parameters.OutputFormat == ReportingOutputFormatDto.ClientPackage
        && (manifest.CertifiedPartnersCapital is not null
            || IsRequired(
                manifest.TemplateId,
                manifest.CertifiedSnapshot?.RequiresCertifiedLedgerPresentation == true,
                parameters.OutputFormat));

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
/// Extended primary-document contract for renderers that can consume the exact checkpoint-bound
/// ledger report pack supplied by the certified artifact producer. The complete PDF/XLSX pair is
/// returned from one call so callers cannot retain a half-rendered client package.
/// </summary>
public interface IReportingPrimaryDocumentRendererWithLedgerReportPack :
    IReportingPrimaryDocumentRenderer
{
    LedgerClientReportDocumentPackage RenderClientPackage(
        ReportingOutputManifest manifest,
        LedgerFinancialReportPack certifiedLedgerReportPack);
}

/// <summary>
/// Reporting adapter over the existing Documents authority. Generic report outputs retain the
/// presentation-neutral <see cref="ClientGradeReportRenderer"/> path, while a checkpoint-bound
/// ledger client package is passed intact to <see cref="LedgerClientReportExportService"/>. The
/// latter resolves <see cref="FinancialReportDocumentRenderer"/> from composition and renders both
/// PDF and XLSX without rebuilding partners-capital figures or creating another renderer.
/// </summary>
public sealed class DocumentsReportingPrimaryDocumentRenderer :
    IReportingPrimaryDocumentRendererWithLedgerReportPack
{
    private readonly ClientGradeReportRenderer _genericRenderer = new();
    private readonly LedgerClientReportExportService _clientReportExportService;

    public byte[] RenderPdf(ReportingOutputManifest manifest)
        => _genericRenderer.RenderPdf(BuildModel(manifest));

    public byte[] RenderWorkbook(ReportingOutputManifest manifest)
        => _genericRenderer.RenderWorkbook(BuildModel(manifest));

    /// <summary>
    /// Convenience construction for dependency-light callers and focused tests. Production
    /// composition supplies the shared export service explicitly.
    /// </summary>
    public DocumentsReportingPrimaryDocumentRenderer()
        : this(new LedgerClientReportExportService(new FinancialReportDocumentRenderer()))
    {
    }

    public DocumentsReportingPrimaryDocumentRenderer(
        LedgerClientReportExportService clientReportExportService)
    {
        ArgumentNullException.ThrowIfNull(clientReportExportService);
        _clientReportExportService = clientReportExportService;
    }

    public LedgerClientReportDocumentPackage RenderClientPackage(
        ReportingOutputManifest manifest,
        LedgerFinancialReportPack certifiedLedgerReportPack)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        ArgumentNullException.ThrowIfNull(certifiedLedgerReportPack);
        if (!ReportingCertifiedLedgerPresentationBinding.IsRequired(manifest))
        {
            throw new ReportingGovernanceException(
                $"Reporting run '{manifest.RunId}' is not bound to a certified ledger client package.");
        }

        return _clientReportExportService.BuildClientDocumentPackage(certifiedLedgerReportPack);
    }

    /// <summary>
    /// Projects a generic certified manifest into the presentation-neutral client-grade document
    /// model. The manifest's retained journal rows do not carry the beginning-balance and
    /// period-classification state required to reconstruct a ledger client package, so a manifest
    /// that requires that package is rejected and must use <see cref="RenderClientPackage"/>.
    /// </summary>
    internal static ReportDocumentModel BuildModel(ReportingOutputManifest manifest)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        var template = manifest.ResolvedTemplate!;
        var rowHash = DeterministicReportingCertifiedArtifactProducer.ComputeCertifiedRowsHash(
            manifest.CertifiedDatasetRows);

        if (ReportingCertifiedLedgerPresentationBinding.IsRequired(manifest))
        {
            throw new ReportingGovernanceException(
                $"Capital-account client package '{manifest.RunId}' requires the exact checkpoint-bound canonical ledger presentation and cannot use a checkpoint-unbound partners-capital projection.");
        }

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
