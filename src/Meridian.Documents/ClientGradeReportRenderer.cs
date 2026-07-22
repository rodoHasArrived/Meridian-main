using System.Text;
using ClosedXML.Excel;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace Meridian.Documents;

/// <summary>
/// Renders a presentation-neutral <see cref="ReportDocumentModel"/> into branded, client-grade PDF
/// and multi-sheet XLSX documents using QuestPDF and ClosedXML. Output is deterministic (fixed
/// document metadata, fixed timestamps, canonical zip ordering) so re-rendering the same model
/// reproduces the same bytes for governed-reporting hash verification.
/// </summary>
public sealed class ClientGradeReportRenderer
{
    static ClientGradeReportRenderer() => DeterministicDocumentPackaging.ConfigureQuestPdf();

    public byte[] RenderPdf(ReportDocumentModel model)
    {
        ArgumentNullException.ThrowIfNull(model);

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
                    header.Item().Text(model.Title).FontSize(12).SemiBold();
                    if (!string.IsNullOrWhiteSpace(model.Subtitle))
                        header.Item().Text(model.Subtitle).FontSize(9).FontColor("#627d98");
                    foreach (var field in model.HeaderFields)
                        header.Item().Text($"{field.Label}: {field.Value}").FontSize(8).FontColor("#627d98");
                    header.Item().PaddingTop(4).LineHorizontal(1).LineColor("#0b7285");
                });

                page.Content().PaddingVertical(6).Column(column =>
                {
                    column.Spacing(14);
                    if (model.Tables.Count == 0)
                        column.Item().Text("No certified rows for this report.").FontSize(9).FontColor("#627d98");
                    foreach (var table in model.Tables)
                        RenderTable(column, table);
                });

                page.Footer().Row(row =>
                {
                    row.RelativeItem().Text(model.FooterNote ?? string.Empty)
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
            Title = model.Title,
            Author = "Meridian",
            Subject = model.Subtitle ?? model.Title,
            Creator = "Meridian",
            Producer = "Meridian",
            CreationDate = new DateTimeOffset(DeterministicDocumentPackaging.FixedTimestamp, TimeSpan.Zero),
            ModifiedDate = new DateTimeOffset(DeterministicDocumentPackaging.FixedTimestamp, TimeSpan.Zero),
        });

        return document.GeneratePdf();
    }

    public byte[] RenderWorkbook(ReportDocumentModel model)
    {
        ArgumentNullException.ThrowIfNull(model);

        byte[] rendered;
        using (var workbook = new XLWorkbook())
        {
            var usedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            if (model.HeaderFields.Count > 0)
            {
                var summary = workbook.Worksheets.Add(UniqueSheetName("Summary", usedNames));
                WriteHeaderCells(summary, ["Field", "Value"]);
                var summaryRow = 2;
                foreach (var field in model.HeaderFields)
                {
                    summary.Cell(summaryRow, 1).Value = field.Label;
                    summary.Cell(summaryRow, 2).Value = field.Value;
                    summaryRow++;
                }

                summary.Columns().AdjustToContents();
                summary.SheetView.FreezeRows(1);
            }

            foreach (var table in model.Tables)
            {
                var sheet = workbook.Worksheets.Add(UniqueSheetName(table.Title, usedNames));
                WriteHeaderCells(sheet, table.Headers);
                var rowNumber = 2;
                foreach (var row in table.Rows)
                {
                    for (var column = 0; column < row.Count; column++)
                        sheet.Cell(rowNumber, column + 1).Value = row[column];
                    rowNumber++;
                }

                sheet.Columns().AdjustToContents();
                if (table.Headers.Count > 0)
                    sheet.SheetView.FreezeRows(1);
            }

            if (!workbook.Worksheets.Any())
                workbook.Worksheets.Add(UniqueSheetName("Report", usedNames));

            workbook.Properties.Author = "Meridian";
            workbook.Properties.Title = model.Title;
            workbook.Properties.Created = DeterministicDocumentPackaging.FixedTimestamp;
            workbook.Properties.Modified = DeterministicDocumentPackaging.FixedTimestamp;
            workbook.Properties.LastModifiedBy = "Meridian";

            using var buffer = new MemoryStream();
            workbook.SaveAs(buffer);
            rendered = buffer.ToArray();
        }

        return DeterministicDocumentPackaging.Canonicalize(rendered);
    }

    private static void WriteHeaderCells(IXLWorksheet sheet, IReadOnlyList<string> headers)
    {
        if (headers.Count == 0)
            return;

        for (var column = 0; column < headers.Count; column++)
        {
            var cell = sheet.Cell(1, column + 1);
            cell.Value = headers[column];
            cell.Style.Font.Bold = true;
            cell.Style.Fill.BackgroundColor = XLColor.FromHtml("#0b7285");
            cell.Style.Font.FontColor = XLColor.White;
        }

        sheet.Row(1).Height = 18;
    }

    private static void RenderTable(QuestPDF.Fluent.ColumnDescriptor column, ReportDocumentTable table)
    {
        column.Item().Text(table.Title).FontSize(11).Bold().FontColor("#102a43");
        if (table.Headers.Count == 0)
            return;

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
}
