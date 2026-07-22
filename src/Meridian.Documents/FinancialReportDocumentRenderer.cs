using System.Globalization;
using System.Text;
using ClosedXML.Excel;
using Meridian.Ledger;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace Meridian.Documents;

/// <summary>
/// Client-grade renderer producing branded, tabular PDF and multi-sheet XLSX documents from a
/// ledger financial report pack using QuestPDF and ClosedXML. Implements the ledger's
/// <see cref="ILedgerReportBinaryRenderer"/> seam so it drops into the existing signed export
/// pipeline in place of the dependency-free fallback. Output is made deterministic (fixed document
/// metadata, fixed timestamps, canonical zip ordering) so re-rendering the same pack reproduces the
/// same bytes for audit verification.
/// </summary>
public sealed class FinancialReportDocumentRenderer : ILedgerReportBinaryRenderer
{
    static FinancialReportDocumentRenderer() => DeterministicDocumentPackaging.ConfigureQuestPdf();

    public byte[] RenderPdf(LedgerFinancialReportPack reportPack)
    {
        ArgumentNullException.ThrowIfNull(reportPack);
        var tables = LedgerReportPresentation.BuildTables(reportPack);
        var request = reportPack.Request;

        var document = Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Size(PageSizes.A4);
                page.Margin(36);
                page.DefaultTextStyle(style => style.FontSize(9).FontColor("#1f2933"));

                page.Header().Column(header =>
                {
                    header.Item().Text("Meridian Fund Operations").FontSize(16).Bold().FontColor("#0b7285");
                    header.Item().Text($"{request.FundId} — {request.PeriodId}").FontSize(11).SemiBold();
                    header.Item().Text($"As of {request.AsOf:yyyy-MM-dd} · {request.BaseCurrency}").FontSize(8).FontColor("#627d98");
                    header.Item().PaddingTop(4).LineHorizontal(1).LineColor("#0b7285");
                });

                page.Content().PaddingVertical(6).Column(column =>
                {
                    column.Spacing(14);
                    foreach (var table in tables)
                        RenderTable(column, table);
                });

                page.Footer().Row(row =>
                {
                    row.RelativeItem().Text($"Signature {Shorten(reportPack.Signature.PayloadChecksumSha256)}")
                        .FontSize(7).FontColor("#9aa5b1");
                    row.ConstantItem(120).AlignRight().Text(text =>
                    {
                        text.DefaultTextStyle(style => style.FontSize(7).FontColor("#9aa5b1"));
                        text.Span("Page ");
                        text.CurrentPageNumber();
                        text.Span(" / ");
                        text.TotalPages();
                    });
                });
            });
        });

        document.WithMetadata(new DocumentMetadata
        {
            Title = $"{request.FundId} {request.PeriodId}",
            Author = "Meridian",
            Subject = request.ReportId,
            Creator = "Meridian",
            Producer = "Meridian",
            CreationDate = new DateTimeOffset(DeterministicDocumentPackaging.FixedTimestamp, TimeSpan.Zero),
            ModifiedDate = new DateTimeOffset(DeterministicDocumentPackaging.FixedTimestamp, TimeSpan.Zero),
        });

        return document.GeneratePdf();
    }

    public byte[] RenderWorkbook(LedgerFinancialReportPack reportPack)
    {
        ArgumentNullException.ThrowIfNull(reportPack);
        var tables = LedgerReportPresentation.BuildTables(reportPack);

        byte[] rendered;
        using (var workbook = new XLWorkbook())
        {
            var usedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var table in tables)
            {
                var sheet = workbook.Worksheets.Add(UniqueSheetName(table.Title, usedNames));
                var headerRow = sheet.Row(1);
                for (var column = 0; column < table.Headers.Count; column++)
                {
                    var cell = sheet.Cell(1, column + 1);
                    cell.Value = table.Headers[column];
                    cell.Style.Font.Bold = true;
                    cell.Style.Fill.BackgroundColor = XLColor.FromHtml("#0b7285");
                    cell.Style.Font.FontColor = XLColor.White;
                }

                headerRow.Height = 18;
                var rowNumber = 2;
                foreach (var row in table.Rows)
                {
                    for (var column = 0; column < row.Count; column++)
                        sheet.Cell(rowNumber, column + 1).Value = row[column];
                    rowNumber++;
                }

                sheet.Columns().AdjustToContents();
                sheet.SheetView.FreezeRows(1);
            }

            workbook.Properties.Author = "Meridian";
            workbook.Properties.Title = $"{reportPack.Request.FundId} {reportPack.Request.PeriodId}";
            workbook.Properties.Created = DeterministicDocumentPackaging.FixedTimestamp;
            workbook.Properties.Modified = DeterministicDocumentPackaging.FixedTimestamp;
            workbook.Properties.LastModifiedBy = "Meridian";

            using var buffer = new MemoryStream();
            workbook.SaveAs(buffer);
            rendered = buffer.ToArray();
        }

        return DeterministicDocumentPackaging.Canonicalize(rendered);
    }

    private static void RenderTable(ColumnDescriptor column, LedgerReportTable table)
    {
        column.Item().Text(table.Title).FontSize(11).Bold().FontColor("#102a43");
        column.Item().Table(grid =>
        {
            grid.ColumnsDefinition(columns =>
            {
                columns.RelativeColumn(2);
                for (var index = 1; index < table.Headers.Count; index++)
                    columns.RelativeColumn();
            });

            grid.Header(headerRow =>
            {
                foreach (var header in table.Headers)
                {
                    headerRow.Cell().Background("#d9e2ec").PaddingVertical(3).PaddingHorizontal(4)
                        .Text(header).SemiBold().FontSize(8);
                }
            });

            var alternate = false;
            foreach (var row in table.Rows)
            {
                var background = alternate ? "#f0f4f8" : "#ffffff";
                alternate = !alternate;
                for (var index = 0; index < table.Headers.Count; index++)
                {
                    var value = index < row.Count ? row[index] : string.Empty;
                    grid.Cell().Background(background).PaddingVertical(2).PaddingHorizontal(4)
                        .Text(value).FontSize(8);
                }
            }
        });
    }

    private static string UniqueSheetName(string title, HashSet<string> used)
    {
        var baseName = SanitizeSheetName(title);
        var candidate = baseName;
        var suffix = 2;
        while (!used.Add(candidate))
        {
            // Trim to 25 so even a long suffix keeps the name within Excel's 31-char limit.
            var trimmed = baseName.Length > 25 ? baseName[..25] : baseName;
            candidate = $"{trimmed} {suffix++}";
        }

        return candidate;
    }

    private static string SanitizeSheetName(string title)
    {
        var builder = new StringBuilder(title.Length);
        foreach (var character in title)
            builder.Append(character is '\\' or '/' or '?' or '*' or '[' or ']' or ':' ? ' ' : character);
        var cleaned = builder.ToString().Trim();
        if (cleaned.Length == 0)
            cleaned = "Sheet";
        return cleaned.Length > 31 ? cleaned[..31] : cleaned;
    }

    private static string Shorten(string value) => value.Length <= 16 ? value : value[..16];
}
