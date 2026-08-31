using Meridian.Ledger;

namespace Meridian.Ui.Shared.Services;

/// <summary>
/// The complete binary primary-document pair rendered from one immutable ledger report pack.
/// Construction is all-or-nothing: callers do not receive a package unless both documents were
/// rendered successfully and contain bytes.
/// </summary>
public sealed record LedgerClientReportDocumentPackage(
    byte[] Pdf,
    byte[] Workbook);

/// <summary>
/// Shared, DI-resolved seam that turns a governed ledger report pack into client-grade delivery
/// artifacts (branded PDF + multi-sheet XLSX) plus the retained hash/provenance manifest. It uses the
/// composition root's registered <see cref="ILedgerReportBinaryRenderer"/> — the QuestPDF/ClosedXML
/// renderer once <c>AddFinancialReportDocumentRenderer</c> is wired — so both the browser workstation
/// and the WPF desktop consume one export path rather than each re-rendering deliverables. The
/// renderer is optional so lightweight hosts that never registered it still degrade to the
/// dependency-free plain-text fallback instead of failing to construct.
/// </summary>
public sealed class LedgerClientReportExportService
{
    private readonly ILedgerReportBinaryRenderer? _renderer;

    public LedgerClientReportExportService(ILedgerReportBinaryRenderer? renderer = null)
    {
        _renderer = renderer;
    }

    /// <summary>True when a client-grade binary renderer is wired (not the plain-text fallback).</summary>
    public bool HasClientGradeRenderer => _renderer is not null and not BuiltInLedgerReportBinaryRenderer;

    /// <summary>
    /// Renders the atomic PDF/XLSX primary-document pair for a governed client package from the
    /// exact supplied report pack. Both documents flow through the same composition-root renderer;
    /// if either render fails or returns no bytes, no package is returned.
    /// </summary>
    public LedgerClientReportDocumentPackage BuildClientDocumentPackage(
        LedgerFinancialReportPack reportPack)
    {
        ArgumentNullException.ThrowIfNull(reportPack);
        var renderer = _renderer ?? BuiltInLedgerReportBinaryRenderer.Instance;
        var pdf = renderer.RenderPdf(reportPack);
        var workbook = renderer.RenderWorkbook(reportPack);
        if (pdf.Length == 0 || workbook.Length == 0)
        {
            throw new InvalidOperationException(
                $"Ledger client-document package '{reportPack.Request.ReportId}' did not render both PDF and XLSX bytes.");
        }

        return new LedgerClientReportDocumentPackage(pdf, workbook);
    }

    /// <summary>
    /// Builds the scheduled-export delivery artifacts (manifest + one artifact per declared format)
    /// for <paramref name="reportPack"/>, rendering PDF/XLSX through the wired renderer.
    /// </summary>
    public IReadOnlyList<LedgerReportPackArtifact> BuildClientDeliverables(
        LedgerFinancialReportPack reportPack,
        LedgerReportScheduledExport scheduledExport)
    {
        ArgumentNullException.ThrowIfNull(reportPack);
        ArgumentNullException.ThrowIfNull(scheduledExport);
        return LedgerScheduledReportExportPackageBuilder.Build(reportPack, scheduledExport, _renderer);
    }
}
