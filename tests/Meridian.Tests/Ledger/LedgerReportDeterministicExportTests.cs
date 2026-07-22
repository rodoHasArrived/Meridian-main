using System.Text;
using FluentAssertions;
using Meridian.Documents;
using Meridian.Ledger;
using Xunit;

namespace Meridian.Tests.Ledger;

/// <summary>
/// Proves the wired client-grade export is deterministic: rendering the same governed report pack
/// twice yields byte-identical PDF and XLSX artifacts with stable content hashes, and the delivery
/// manifest retains a per-artifact hash plus the report-pack provenance signature.
/// </summary>
public sealed class LedgerReportDeterministicExportTests
{
    [Fact]
    public void ClientGradeExport_RendersByteStableForAGivenPack()
    {
        // A single governed pack rendered twice must reproduce the same bytes and hashes. (Two
        // independently built packs are intentionally not compared: the pack's line provenance carries
        // per-posting journal identifiers, so byte-stability is a property of rendering a fixed pack,
        // which is exactly what the deterministic renderer guarantees.)
        var pack = LedgerReportPackTestData.BuildContributionPack();
        var export = LedgerReportPackTestData.BuildScheduledExport();
        var renderer = new FinancialReportDocumentRenderer();

        var first = LedgerScheduledReportExportPackageBuilder.Build(pack, export, renderer);
        var second = LedgerScheduledReportExportPackageBuilder.Build(pack, export, renderer);

        var firstPdf = first.Single(a => a.Name == "scheduled-export-financials.pdf");
        var secondPdf = second.Single(a => a.Name == "scheduled-export-financials.pdf");
        var firstXlsx = first.Single(a => a.Name == "scheduled-export-financials.xlsx");
        var secondXlsx = second.Single(a => a.Name == "scheduled-export-financials.xlsx");

        // Byte-stable binaries.
        firstPdf.GetBytes().Should().Equal(secondPdf.GetBytes());
        firstXlsx.GetBytes().Should().Equal(secondXlsx.GetBytes());

        // Stable content hashes and delivery manifest — the provenance chain reproduces exactly.
        firstPdf.ChecksumSha256.Should().Be(secondPdf.ChecksumSha256);
        firstXlsx.ChecksumSha256.Should().Be(secondXlsx.ChecksumSha256);
        first.Single(a => a.Name == "scheduled-export-manifest.csv").ChecksumSha256
            .Should().Be(second.Single(a => a.Name == "scheduled-export-manifest.csv").ChecksumSha256);
    }

    [Fact]
    public void DeliveryManifest_RetainsPerArtifactHashesAndProvenanceSignature()
    {
        var pack = LedgerReportPackTestData.BuildContributionPack();
        var artifacts = LedgerScheduledReportExportPackageBuilder.Build(
            pack,
            LedgerReportPackTestData.BuildScheduledExport(),
            new FinancialReportDocumentRenderer());

        var manifest = artifacts.Single(a => a.Name == "scheduled-export-manifest.csv");
        var manifestText = Encoding.UTF8.GetString(manifest.GetBytes());

        // The manifest carries the report-pack provenance signature and a per-artifact sha256 row
        // for every artifact retained inside the governed report pack.
        manifestText.Should().Contain(pack.Signature.PayloadChecksumSha256);
        pack.Artifacts.Should().NotBeEmpty();
        foreach (var packArtifact in pack.Artifacts)
        {
            manifestText.Should().Contain(packArtifact.ChecksumSha256);
        }

        // Every delivered artifact carries a non-empty retained hash.
        artifacts.Should().OnlyContain(a => a.ChecksumSha256.Length > 0);
    }

    [Fact]
    public void ClientGradeBinaries_AreRealPdfAndXlsx()
    {
        var pack = LedgerReportPackTestData.BuildContributionPack();
        var renderer = new FinancialReportDocumentRenderer();

        var pdf = renderer.RenderPdf(pack);
        var xlsx = renderer.RenderWorkbook(pack);

        Encoding.ASCII.GetString(pdf, 0, 5).Should().Be("%PDF-");
        xlsx.Take(2).Should().Equal((byte)'P', (byte)'K');
    }
}
