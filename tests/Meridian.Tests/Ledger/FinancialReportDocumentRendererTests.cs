using System.IO.Compression;
using System.Text;
using FluentAssertions;
using Meridian.Documents;
using Meridian.Ledger;
using Xunit;

namespace Meridian.Tests.Ledger;

public sealed class FinancialReportDocumentRendererTests
{
    private static LedgerFinancialReportPack BuildPack()
    {
        var chart = new ChartOfAccounts();
        var cash = chart.Register("Assets:Cash", LedgerAccountType.Asset);
        var capital = chart.Register("Equity:Capital", LedgerAccountType.Equity);
        var ledger = new Meridian.Ledger.Ledger();
        ledger.PostLines(
            new DateTimeOffset(2026, 5, 15, 12, 0, 0, TimeSpan.Zero),
            "capital contribution",
            [(cash, 2_500_000m, 0m), (capital, 0m, 2_500_000m)]);
        var request = new LedgerReportPackRequest(
            "monthly-report",
            "fund-alpha",
            "2026-05",
            new DateTimeOffset(2026, 5, 1, 0, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2026, 5, 31, 23, 59, 59, TimeSpan.Zero),
            new DateTimeOffset(2026, 5, 31, 0, 0, 0, TimeSpan.Zero),
            "usd",
            "controller",
            DateTimeOffset.Parse("2026-06-01T00:00:00Z"));
        return LedgerReportPackBuilder.Build(ledger, request, chart);
    }

    [Fact]
    public void RenderPdf_ProducesValidPdf()
    {
        var renderer = new FinancialReportDocumentRenderer();

        var pdf = renderer.RenderPdf(BuildPack());

        pdf.Should().NotBeEmpty();
        Encoding.ASCII.GetString(pdf, 0, 5).Should().Be("%PDF-");
    }

    [Fact]
    public void RenderPdf_IsDeterministicAcrossCalls()
    {
        var renderer = new FinancialReportDocumentRenderer();
        var pack = BuildPack();

        var first = renderer.RenderPdf(pack);
        var second = renderer.RenderPdf(pack);

        first.Should().Equal(second);
    }

    [Fact]
    public void RenderWorkbook_ProducesMultiSheetWorkbook()
    {
        var renderer = new FinancialReportDocumentRenderer();

        var xlsx = renderer.RenderWorkbook(BuildPack());

        using var archive = new ZipArchive(new MemoryStream(xlsx), ZipArchiveMode.Read);
        archive.GetEntry("xl/workbook.xml").Should().NotBeNull();
        archive.Entries.Count(entry => entry.FullName.StartsWith("xl/worksheets/sheet", StringComparison.Ordinal))
            .Should().BeGreaterThan(1);
    }

    [Fact]
    public void RenderWorkbook_IsDeterministicAcrossCalls()
    {
        var renderer = new FinancialReportDocumentRenderer();
        var pack = BuildPack();

        var first = renderer.RenderWorkbook(pack);
        var second = renderer.RenderWorkbook(pack);

        first.Should().Equal(second);
    }

    [Fact]
    public void DocumentRenderer_HonorsScheduledExportSeam()
    {
        var chart = new ChartOfAccounts();
        var cash = chart.Register("Assets:Cash", LedgerAccountType.Asset);
        var capital = chart.Register("Equity:Capital", LedgerAccountType.Equity);
        var ledger = new Meridian.Ledger.Ledger();
        ledger.PostLines(new DateTimeOffset(2026, 5, 15, 12, 0, 0, TimeSpan.Zero), "contribution",
            [(cash, 1_000_000m, 0m), (capital, 0m, 1_000_000m)]);
        var schedule = new LedgerReportSchedule(
            "reg-pack", "fund-alpha", "Monthly export", LedgerReportScheduleFrequency.Monthly,
            new DateOnly(2026, 5, 1), 5, "usd",
            [LedgerReportExportFormat.Pdf, LedgerReportExportFormat.Xlsx],
            ["controller@example.com"], "controller", DateTimeOffset.Parse("2026-04-15T12:00:00Z"));
        var export = LedgerReportSchedulePlanner.Project(schedule, 1).Single();
        var pack = LedgerReportPackBuilder.Build(ledger, export.ToReportPackRequest("controller", DateTimeOffset.Parse("2026-06-01T13:00:00Z")), chart);

        var artifacts = LedgerScheduledReportExportPackageBuilder.Build(pack, export, new FinancialReportDocumentRenderer());

        var pdf = artifacts.Single(artifact => artifact.Name == "scheduled-export-financials.pdf");
        Encoding.ASCII.GetString(pdf.GetBytes(), 0, 5).Should().Be("%PDF-");
        artifacts.Single(artifact => artifact.Name == "scheduled-export-financials.xlsx").GetBytes().Should().NotBeEmpty();
    }
}
