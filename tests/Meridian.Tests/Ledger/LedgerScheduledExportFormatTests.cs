using System.IO.Compression;
using System.Text;
using FluentAssertions;
using Meridian.Ledger;
using Xunit;

namespace Meridian.Tests.Ledger;

public sealed class LedgerScheduledExportFormatTests
{
    private static (LedgerFinancialReportPack Pack, LedgerReportScheduledExport Export) BuildFixture(
        params LedgerReportExportFormat[] formats)
    {
        var chart = new ChartOfAccounts();
        var cash = chart.Register("Assets:Cash", LedgerAccountType.Asset);
        var capital = chart.Register("Equity:Capital", LedgerAccountType.Equity);
        var ledger = new Meridian.Ledger.Ledger();
        ledger.PostLines(
            new DateTimeOffset(2026, 5, 15, 12, 0, 0, TimeSpan.Zero),
            "capital contribution",
            [(cash, 1_000_000m, 0m), (capital, 0m, 1_000_000m)]);

        var schedule = new LedgerReportSchedule(
            "reg-pack",
            "fund-alpha",
            "Monthly export",
            LedgerReportScheduleFrequency.Monthly,
            new DateOnly(2026, 5, 1),
            dueDaysAfterPeriodEnd: 5,
            "usd",
            formats,
            ["controller@example.com"],
            "controller",
            DateTimeOffset.Parse("2026-04-15T12:00:00Z"));
        var scheduledExport = LedgerReportSchedulePlanner.Project(schedule, occurrenceCount: 1).Single();
        var request = scheduledExport.ToReportPackRequest("controller", DateTimeOffset.Parse("2026-06-01T13:00:00Z"));
        var reportPack = LedgerReportPackBuilder.Build(ledger, request, chart);
        return (reportPack, scheduledExport);
    }

    [Fact]
    public void DeclaredXlsx_ProducesValidWorkbookBytes()
    {
        var (pack, export) = BuildFixture(LedgerReportExportFormat.Xlsx);

        var artifacts = LedgerScheduledReportExportPackageBuilder.Build(pack, export);

        var xlsx = artifacts.Single(artifact => artifact.Name == "scheduled-export-financials.xlsx");
        xlsx.IsBinary.Should().BeTrue();
        xlsx.ContentType.Should().Be("application/vnd.openxmlformats-officedocument.spreadsheetml.sheet");
        // Valid OOXML: a readable zip containing the workbook part.
        using var archive = new ZipArchive(new MemoryStream(xlsx.GetBytes()), ZipArchiveMode.Read);
        archive.GetEntry("xl/workbook.xml").Should().NotBeNull();
        archive.GetEntry("[Content_Types].xml").Should().NotBeNull();
    }

    [Fact]
    public void DeclaredPdf_ProducesValidPdfBytes()
    {
        var (pack, export) = BuildFixture(LedgerReportExportFormat.Pdf);

        var artifacts = LedgerScheduledReportExportPackageBuilder.Build(pack, export);

        var pdf = artifacts.Single(artifact => artifact.Name == "scheduled-export-financials.pdf");
        pdf.IsBinary.Should().BeTrue();
        pdf.ContentType.Should().Be("application/pdf");
        Encoding.ASCII.GetString(pdf.GetBytes(), 0, 8).Should().StartWith("%PDF-1.");
        Encoding.ASCII.GetString(pdf.GetBytes()).Should().Contain("%%EOF");
    }

    [Fact]
    public void EveryDeclaredFormat_YieldsAnArtifact()
    {
        var (pack, export) = BuildFixture(
            LedgerReportExportFormat.Csv,
            LedgerReportExportFormat.Json,
            LedgerReportExportFormat.Xlsx,
            LedgerReportExportFormat.Pdf,
            LedgerReportExportFormat.RegulatoryXml);

        var artifacts = LedgerScheduledReportExportPackageBuilder.Build(pack, export);

        artifacts.Should().Contain(artifact => artifact.Name == "scheduled-export-financials.csv");
        artifacts.Should().Contain(artifact => artifact.Name == "scheduled-export-financials.json");
        artifacts.Should().Contain(artifact => artifact.Name == "scheduled-export-financials.xlsx");
        artifacts.Should().Contain(artifact => artifact.Name == "scheduled-export-financials.pdf");
        artifacts.Should().Contain(artifact => artifact.Name == "regulatory-summary.xml");
        artifacts.Should().OnlyContain(artifact => artifact.ChecksumSha256.Length == 64);
    }

    [Fact]
    public void BinaryChecksum_CoversRenderedBytes()
    {
        var (pack, export) = BuildFixture(LedgerReportExportFormat.Xlsx);

        var xlsx = LedgerScheduledReportExportPackageBuilder.Build(pack, export)
            .Single(artifact => artifact.Name == "scheduled-export-financials.xlsx");

        var expected = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(xlsx.GetBytes())).ToLowerInvariant();
        xlsx.ChecksumSha256.Should().Be(expected);
    }
}
