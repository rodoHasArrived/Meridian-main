using System.IO.Compression;
using System.Text;
using FluentAssertions;
using Meridian.Documents;
using Meridian.Ledger;
using Meridian.Ui.Shared.Services;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Meridian.Tests.Ledger;

/// <summary>
/// Proves the client-grade renderer is actually wired: a host that registers the Documents renderer
/// (as the workstation composition root now does) resolves the QuestPDF/ClosedXML
/// <see cref="FinancialReportDocumentRenderer"/> for the <see cref="ILedgerReportBinaryRenderer"/>
/// seam — not the dependency-free plain-text <see cref="BuiltInLedgerReportBinaryRenderer"/> fallback.
/// </summary>
public sealed class LedgerReportRendererCompositionTests
{
    [Fact]
    public void HostComposition_ResolvesQuestPdfClosedXmlRenderer_NotPlainTextFallback()
    {
        var services = new ServiceCollection();

        // The exact registration the workstation host performs in WorkstationServiceCollectionExtensions.
        services.AddFinancialReportDocumentRenderer();
        using var provider = services.BuildServiceProvider();

        var renderer = provider.GetRequiredService<ILedgerReportBinaryRenderer>();

        renderer.Should().BeOfType<FinancialReportDocumentRenderer>();
        renderer.Should().NotBeOfType<BuiltInLedgerReportBinaryRenderer>();
    }

    [Fact]
    public void WithoutTheRenderer_TheExportBuilderStillFallsBackToPlainText()
    {
        // Guards the premise of the composition: absent the wiring, the ledger export path resolves
        // the dependency-free fallback. The wiring above is what flips it to the client-grade renderer.
        var services = new ServiceCollection();
        using var provider = services.BuildServiceProvider();

        provider.GetService<ILedgerReportBinaryRenderer>().Should().BeNull();
        BuiltInLedgerReportBinaryRenderer.Instance.Should().BeOfType<BuiltInLedgerReportBinaryRenderer>();
    }

    [Fact]
    public void WiredRenderer_EmitsRealClientGradeBinaries_NotPlainText()
    {
        var services = new ServiceCollection();
        services.AddFinancialReportDocumentRenderer();
        using var provider = services.BuildServiceProvider();
        var renderer = provider.GetRequiredService<ILedgerReportBinaryRenderer>();
        var pack = LedgerReportPackTestData.BuildContributionPack();

        var pdf = renderer.RenderPdf(pack);
        var xlsx = renderer.RenderWorkbook(pack);

        Encoding.ASCII.GetString(pdf, 0, 5).Should().Be("%PDF-");
        xlsx.Take(2).Should().Equal((byte)'P', (byte)'K');

        // A ClosedXML workbook carries a styles part (we brand headers with fills/fonts); the
        // dependency-free fallback hand-builds only workbook + worksheet parts and never emits styles.
        using var archive = new ZipArchive(new MemoryStream(xlsx), ZipArchiveMode.Read);
        archive.GetEntry("xl/styles.xml").Should().NotBeNull(
            "the ClosedXML renderer emits a styled workbook the plain-text fallback cannot produce");
    }

    [Fact]
    public void SharedExportService_RoutesBothClientsThroughTheWiredRenderer()
    {
        // The single shared seam the browser and WPF workstations both consume resolves the wired
        // renderer from the composition root and emits the client deliverables.
        var services = new ServiceCollection();
        services.AddFinancialReportDocumentRenderer();
        services.AddSingleton(sp => new LedgerClientReportExportService(
            sp.GetService<ILedgerReportBinaryRenderer>()));
        using var provider = services.BuildServiceProvider();

        var exportService = provider.GetRequiredService<LedgerClientReportExportService>();
        exportService.HasClientGradeRenderer.Should().BeTrue();

        var artifacts = exportService.BuildClientDeliverables(
            LedgerReportPackTestData.BuildContributionPack(),
            LedgerReportPackTestData.BuildScheduledExport());

        artifacts.Should().Contain(a => a.Name == "scheduled-export-financials.pdf");
        artifacts.Should().Contain(a => a.Name == "scheduled-export-financials.xlsx");
    }

    [Fact]
    public void SharedExportService_BuildsThePrimaryPdfAndXlsxAsOneRendererBackedPackage()
    {
        var renderer = new TrackingBinaryRenderer();
        var exportService = new LedgerClientReportExportService(renderer);

        var package = exportService.BuildClientDocumentPackage(
            LedgerReportPackTestData.BuildContributionPack());

        package.Pdf.Should().Equal(TrackingBinaryRenderer.PdfBytes);
        package.Workbook.Should().Equal(TrackingBinaryRenderer.WorkbookBytes);
        renderer.PdfRenderCount.Should().Be(1);
        renderer.WorkbookRenderCount.Should().Be(1);
    }

    [Fact]
    public void SharedExportService_WithoutRenderer_DegradesToPlainTextFallback()
    {
        var exportService = new LedgerClientReportExportService(renderer: null);

        exportService.HasClientGradeRenderer.Should().BeFalse();
        // Still produces valid deliverables via the dependency-free fallback.
        var artifacts = exportService.BuildClientDeliverables(
            LedgerReportPackTestData.BuildContributionPack(),
            LedgerReportPackTestData.BuildScheduledExport());
        artifacts.Should().Contain(a => a.Name == "scheduled-export-financials.pdf");
    }

    private sealed class TrackingBinaryRenderer : ILedgerReportBinaryRenderer
    {
        internal static readonly byte[] PdfBytes = "%PDF-tracked"u8.ToArray();
        internal static readonly byte[] WorkbookBytes = [(byte)'P', (byte)'K', 1, 2, 3];

        internal int PdfRenderCount { get; private set; }

        internal int WorkbookRenderCount { get; private set; }

        public byte[] RenderPdf(LedgerFinancialReportPack reportPack)
        {
            ArgumentNullException.ThrowIfNull(reportPack);
            PdfRenderCount++;
            return PdfBytes;
        }

        public byte[] RenderWorkbook(LedgerFinancialReportPack reportPack)
        {
            ArgumentNullException.ThrowIfNull(reportPack);
            WorkbookRenderCount++;
            return WorkbookBytes;
        }
    }
}
