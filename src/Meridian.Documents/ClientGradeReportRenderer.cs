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

    public byte[] RenderPdf(ReportDocumentModel model) =>
        RenderPdf(model, PageSizes.A4, null, null, null);

    /// <summary>Renders the first table as a grid and flows any following tables as supporting detail.</summary>
    public byte[] RenderGridPdf(
        ReportDocumentModel model,
        IReadOnlyList<bool> numericColumns,
        string? firmName,
        string? primaryColor)
    {
        ArgumentNullException.ThrowIfNull(model);
        var table = model.Tables[0];
        var widths = table.Headers.Select((header, index) =>
        {
            var longestCell = table.Rows.Select(row => row[index].Length).DefaultIfEmpty().Max();
            var contentWidth = numericColumns[index] ? Math.Min(longestCell, 48) * 6f : Math.Min(longestCell, 24) * 5f;
            var headerWidth = Math.Min(header.Length, 24) * 5f;
            return Math.Max(54f, Math.Max(headerWidth, contentWidth) + 10f);
        }).ToArray();
        var size = widths.Sum() > PageSizes.A4.Width - 72 ? PageSizes.A4.Landscape() : PageSizes.A4;
        var bands = BuildColumnBands(table, widths, numericColumns, size.Width - 72);
        return RenderPdf(model, size, bands, firmName, primaryColor);
    }

    private static byte[] RenderPdf(
        ReportDocumentModel model,
        PageSize pageSize,
        IReadOnlyList<GridBand>? gridBands,
        string? firmName,
        string? primaryColor)
    {
        ArgumentNullException.ThrowIfNull(model);
        var accent = primaryColor ?? "#0b7285";

        var document = Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Size(pageSize);
                page.Margin(36);
                page.DefaultTextStyle(style => style.FontSize(9).FontColor("#1f2933"));

                page.Header().Column(header =>
                {
                    header.Item().Text(firmName ?? "Meridian Fund Operations").FontSize(16).Bold().FontColor(accent);
                    header.Item().Text(model.Title).FontSize(12).SemiBold();
                    if (!string.IsNullOrWhiteSpace(model.Subtitle))
                        header.Item().Text(model.Subtitle).FontSize(9).FontColor("#627d98");
                    foreach (var field in model.HeaderFields)
                        header.Item().Text($"{field.Label}: {field.Value}").FontSize(8).FontColor("#627d98");
                    header.Item().PaddingTop(4).LineHorizontal(1).LineColor(accent);
                });

                page.Content().PaddingVertical(6).Column(column =>
                {
                    column.Spacing(14);
                    if (model.Tables.Count == 0)
                        column.Item().Text("No certified rows for this report.").FontSize(9).FontColor("#627d98");
                    if (gridBands is not null)
                    {
                        for (var index = 0; index < gridBands.Count; index++)
                        {
                            if (index > 0)
                                column.Item().PageBreak();
                            var band = gridBands[index];
                            RenderTable(column, band.Table, band.Widths, band.NumericColumns);
                        }
                    }
                    foreach (var table in model.Tables.Skip(gridBands is null ? 0 : 1))
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

    private static IReadOnlyList<GridBand> BuildColumnBands(
        ReportDocumentTable table,
        float[] widths,
        IReadOnlyList<bool> numericColumns,
        float availableWidth)
    {
        if (widths.Sum() <= availableWidth)
            return [new(table, widths, numericColumns)];

        // Repeat a unique dimension when the grid has one. Otherwise repeat the row number
        // so readers can join bands even when dimension labels are shared across rows.
        var identity = Enumerable.Range(0, widths.Length)
            .FirstOrDefault(index => !numericColumns[index]
                && table.Rows.Select(row => row[index]).Distinct(StringComparer.Ordinal).Count() == table.Rows.Count, -1);
        var identityWidth = identity >= 0 ? widths[identity] : 38f;
        var groups = new List<List<int>>();
        var group = new List<int>();
        var usedWidth = identityWidth;
        for (var index = 0; index < widths.Length; index++)
        {
            if (index == identity)
                continue;
            if (group.Count > 0 && usedWidth + widths[index] > availableWidth)
            {
                groups.Add(group);
                group = [];
                usedWidth = identityWidth;
            }
            group.Add(index);
            usedWidth += widths[index];
        }
        groups.Add(group);

        return groups.Select((indexes, band) => new GridBand(
            new ReportDocumentTable(
                $"{table.Title} · columns {band + 1} of {groups.Count}",
                [identity >= 0 ? table.Headers[identity] : "Row", .. indexes.Select(index => table.Headers[index])],
                table.Rows.Select((row, rowIndex) => (IReadOnlyList<string>)
                    [identity >= 0 ? row[identity] : (rowIndex + 1).ToString(System.Globalization.CultureInfo.InvariantCulture),
                     .. indexes.Select(index => row[index])]).ToArray()),
            [identityWidth, .. indexes.Select(index => widths[index])],
            [false, .. indexes.Select(index => numericColumns[index])])).ToArray();
    }

    private sealed record GridBand(ReportDocumentTable Table, IReadOnlyList<float> Widths, IReadOnlyList<bool> NumericColumns);

    private static void RenderTable(
        QuestPDF.Fluent.ColumnDescriptor column,
        ReportDocumentTable table,
        IReadOnlyList<float>? widths = null,
        IReadOnlyList<bool>? numericColumns = null)
    {
        column.Item().Text(table.Title).FontSize(11).Bold().FontColor("#102a43");
        if (table.Headers.Count == 0)
            return;

        column.Item().Table(grid =>
        {
            grid.ColumnsDefinition(columns =>
            {
                if (widths is not null)
                {
                    foreach (var width in widths)
                        columns.ConstantColumn(width);
                    return;
                }
                columns.RelativeColumn(2);
                for (var index = 1; index < table.Headers.Count; index++)
                    columns.RelativeColumn();
            });

            grid.Header(headerRow =>
            {
                for (var index = 0; index < table.Headers.Count; index++)
                {
                    var cell = headerRow.Cell().Background("#d9e2ec").PaddingVertical(3).PaddingHorizontal(4);
                    if (numericColumns?[index] == true)
                        cell = cell.AlignRight();
                    cell.Text(table.Headers[index]).SemiBold().FontSize(8);
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
                    var cell = grid.Cell().Background(background).PaddingVertical(2).PaddingHorizontal(4);
                    if (numericColumns?[index] == true)
                        cell = cell.AlignRight();
                    cell.Text(value).FontSize(8);
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
