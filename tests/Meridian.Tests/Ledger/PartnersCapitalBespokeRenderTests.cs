using System;
using System.IO;
using System.Linq;
using System.Text;

using ClosedXML.Excel;
using FluentAssertions;
using Meridian.Documents;
using Meridian.Ledger;
using Xunit;

namespace Meridian.Tests.Ledger;

/// <summary>
/// Proves the client-grade renderer emits the bespoke partners' capital layout: a dedicated XLSX
/// sheet whose money and ownership cells are typed numbers — so an operator can SUM/pivot without
/// retyping, the exact program-review gap ("every cell is text") — plus a ledger-backed NAV anchor
/// block, and a valid PDF. Both render deterministically for governed hash verification.
/// </summary>
public sealed class PartnersCapitalBespokeRenderTests
{
    [Fact]
    public void Workbook_HasDedicatedPartnersCapitalSheet_WithTypedNumericCells()
    {
        var pack = LedgerReportPackTestData.BuildContributionPack();

        var xlsx = new FinancialReportDocumentRenderer().RenderWorkbook(pack);

        using var workbook = new XLWorkbook(new MemoryStream(xlsx));
        workbook.Worksheets.Any(sheet => sheet.Name == "Partners' Capital").Should().BeTrue();
        var sheet = workbook.Worksheet("Partners' Capital");

        // Bespoke column set, distinct from the generic 8-column table.
        sheet.Cell(1, 1).GetString().Should().Be("Partner");
        sheet.Cell(1, 2).GetString().Should().Be("Role");

        // Every data row's money cells are typed numbers with an accounting format — not text.
        var beginning = sheet.Cell(2, 3);
        beginning.DataType.Should().Be(XLDataType.Number);
        beginning.Style.NumberFormat.Format.Should().Be("#,##0.00");
        sheet.Cell(2, 11).DataType.Should().Be(XLDataType.Number);

        // Ownership is a true percentage fraction so the column foots to 100% in Excel.
        var ownership = sheet.Cell(2, 12);
        ownership.DataType.Should().Be(XLDataType.Number);
        ownership.Style.NumberFormat.Format.Should().Be("0.00%");
    }

    [Fact]
    public void Workbook_PartnersCapitalSheet_CarriesLedgerBackedNavAnchor()
    {
        var pack = LedgerReportPackTestData.BuildContributionPack();

        var xlsx = new FinancialReportDocumentRenderer().RenderWorkbook(pack);

        using var workbook = new XLWorkbook(new MemoryStream(xlsx));
        var cells = workbook.Worksheet("Partners' Capital").RangeUsed()!.CellsUsed().ToList();

        cells.Should().Contain(cell => cell.GetString() == "Net asset value (NAV base)");
        cells.Should().Contain(cell => cell.GetString() == "Ending capital ties to NAV");

        // The NAV value is carried as a typed number equal to the pack's ledger-backed net assets.
        var netAssets = (double)pack.Statements.EndingEquity;
        cells.Should().Contain(cell => cell.DataType == XLDataType.Number && cell.GetDouble() == netAssets);
    }

    [Fact]
    public void Renderer_BespokePath_IsDeterministicAndProducesValidPdf()
    {
        var pack = LedgerReportPackTestData.BuildContributionPack();
        var renderer = new FinancialReportDocumentRenderer();

        var firstPdf = renderer.RenderPdf(pack);
        var secondPdf = renderer.RenderPdf(pack);
        var firstXlsx = renderer.RenderWorkbook(pack);
        var secondXlsx = renderer.RenderWorkbook(pack);

        Encoding.ASCII.GetString(firstPdf, 0, 5).Should().Be("%PDF-");
        firstPdf.Should().Equal(secondPdf);
        firstXlsx.Should().Equal(secondXlsx);
    }
}
