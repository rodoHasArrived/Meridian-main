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
        var partnersCapital = reportPack.Statements.PartnersCapital is null
            ? null
            : PartnersCapitalStatementLayoutBuilder.Build(reportPack);
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
                    {
                        if (partnersCapital is not null
                            && table.Title == LedgerReportPresentation.PartnersCapitalTableTitle)
                        {
                            RenderPartnersCapital(column, partnersCapital);
                        }
                        else
                        {
                            RenderTable(column, table);
                        }
                    }
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
        var partnersCapital = reportPack.Statements.PartnersCapital is null
            ? null
            : PartnersCapitalStatementLayoutBuilder.Build(reportPack);

        byte[] rendered;
        using (var workbook = new XLWorkbook())
        {
            var usedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var table in tables)
            {
                if (partnersCapital is not null
                    && table.Title == LedgerReportPresentation.PartnersCapitalTableTitle)
                {
                    WritePartnersCapitalSheet(workbook, partnersCapital, usedNames);
                }
                else
                {
                    WriteGenericSheet(workbook, table, usedNames);
                }
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

    private static readonly string[] PartnersCapitalPdfColumns =
    [
        "Partner", "Beginning", "Contributions", "Distributions",
        "Income & Gains", "Expenses", "Fees", "Ending", "Ownership %"
    ];

    private static readonly string[] PartnersCapitalSheetColumns =
    [
        "Partner", "Role", "Beginning", "Contributions", "Distributions",
        "Income & Gains", "Expenses", "Fees", "Allocated Result", "Other", "Ending", "Ownership %"
    ];

    private const string MoneyNumberFormat = "#,##0.00";
    private const string PercentNumberFormat = "0.00%";

    /// <summary>
    /// Bespoke PDF layout for the statement of changes in partners' capital: a NAV (net-assets)
    /// context strip, a role-labelled per-partner roll-forward with right-aligned figures and an
    /// ownership-share column, a bold total row, and a ledger-backed reconciliation footnote. This
    /// replaces the generic table layout for this one statement so the client deliverable reads like a
    /// fund administrator's statement rather than a flat grid.
    /// </summary>
    private static void RenderPartnersCapital(ColumnDescriptor column, PartnersCapitalStatementLayout layout)
    {
        column.Item().Text(LedgerReportPresentation.PartnersCapitalTableTitle)
            .FontSize(11).Bold().FontColor("#102a43");
        column.Item().Text(
                $"Net asset value {Money(layout.NetAssetValue)} {layout.BaseCurrency} · {layout.Lines.Count} capital accounts")
            .FontSize(8).FontColor("#627d98");

        column.Item().Table(grid =>
        {
            grid.ColumnsDefinition(columns =>
            {
                columns.RelativeColumn(2.2f);
                for (var index = 1; index < PartnersCapitalPdfColumns.Length; index++)
                    columns.RelativeColumn();
            });

            grid.Header(headerRow =>
            {
                // Partner heading left-aligned; every economic heading right-aligned to read as figures.
                headerRow.Cell().Background("#d9e2ec").PaddingVertical(3).PaddingHorizontal(4)
                    .Text(PartnersCapitalPdfColumns[0]).SemiBold().FontSize(8);
                for (var index = 1; index < PartnersCapitalPdfColumns.Length; index++)
                {
                    headerRow.Cell().Background("#d9e2ec").AlignRight().PaddingVertical(3).PaddingHorizontal(4)
                        .Text(PartnersCapitalPdfColumns[index]).SemiBold().FontSize(8);
                }
            });

            var alternate = false;
            foreach (var line in layout.Lines)
            {
                RenderPartnersCapitalRow(grid, line, alternate ? "#f0f4f8" : "#ffffff", isTotal: false);
                alternate = !alternate;
            }

            RenderPartnersCapitalRow(grid, layout.Total, "#d9e2ec", isTotal: true);
        });

        var note = layout.TiesToNetAssets
            ? $"Ending partners' capital of {Money(layout.Total.EndingCapital)} {layout.BaseCurrency} reconciles to the fund's ledger-backed net asset value."
            : $"Reconciliation exception: ending partners' capital differs from net assets by {Money(layout.NetAssetVariance)} {layout.BaseCurrency}.";
        column.Item().Text(note).FontSize(7).FontColor(layout.TiesToNetAssets ? "#627d98" : "#b91c1c");
    }

    private static void RenderPartnersCapitalRow(
        TableDescriptor grid,
        PartnersCapitalStatementLine line,
        string background,
        bool isTotal)
    {
        grid.Cell().Background(background).PaddingVertical(2).PaddingHorizontal(4).Column(cell =>
        {
            var name = cell.Item().Text(line.PartnerLabel).FontSize(8);
            if (isTotal)
                name.Bold();
            else
                name.SemiBold();
            if (!isTotal)
                cell.Item().Text(RoleLabel(line.Role)).FontSize(6).FontColor("#829ab1");
        });

        RenderPartnersCapitalNumber(grid, Money(line.BeginningCapital), background, isTotal);
        RenderPartnersCapitalNumber(grid, Money(line.Contributions), background, isTotal);
        RenderPartnersCapitalNumber(grid, Money(line.Distributions), background, isTotal);
        RenderPartnersCapitalNumber(grid, Money(line.IncomeGainAllocations), background, isTotal);
        RenderPartnersCapitalNumber(grid, Money(line.ExpenseAllocations), background, isTotal);
        RenderPartnersCapitalNumber(grid, Money(line.FeeAllocations), background, isTotal);
        RenderPartnersCapitalNumber(grid, Money(line.EndingCapital), background, isTotal);
        RenderPartnersCapitalNumber(grid, Percent(line.OwnershipPercent), background, isTotal);
    }

    private static void RenderPartnersCapitalNumber(
        TableDescriptor grid,
        string value,
        string background,
        bool bold)
    {
        var text = grid.Cell().Background(background).AlignRight().PaddingVertical(2).PaddingHorizontal(4)
            .Text(value).FontSize(8);
        if (bold)
            text.Bold();
    }

    private static void WriteGenericSheet(XLWorkbook workbook, LedgerReportTable table, HashSet<string> usedNames)
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

    /// <summary>
    /// Bespoke XLSX sheet for the partners' capital statement. Unlike the generic sheet (whose every
    /// cell is a pre-formatted string), the money and ownership cells are written as typed numeric
    /// values with accounting/percent number formats, so an operator can SUM, pivot, and recompute the
    /// statement without retyping. A NAV anchor block records the ledger-backed net asset value the
    /// statement ties to.
    /// </summary>
    private static void WritePartnersCapitalSheet(
        XLWorkbook workbook,
        PartnersCapitalStatementLayout layout,
        HashSet<string> usedNames)
    {
        var sheet = workbook.Worksheets.Add(UniqueSheetName("Partners' Capital", usedNames));

        for (var column = 0; column < PartnersCapitalSheetColumns.Length; column++)
        {
            var cell = sheet.Cell(1, column + 1);
            cell.Value = PartnersCapitalSheetColumns[column];
            cell.Style.Font.Bold = true;
            cell.Style.Fill.BackgroundColor = XLColor.FromHtml("#0b7285");
            cell.Style.Font.FontColor = XLColor.White;
        }

        sheet.Row(1).Height = 18;

        var rowNumber = 2;
        foreach (var line in layout.Lines)
        {
            WritePartnersCapitalRow(sheet, rowNumber, line, RoleLabel(line.Role), bold: false);
            rowNumber++;
        }

        var totalRowNumber = rowNumber;
        WritePartnersCapitalRow(sheet, totalRowNumber, layout.Total, "Total", bold: true);

        // Fund-economics anchor: the ledger-backed NAV the statement reconciles to, with an explicit
        // tie flag, so the governed deliverable can prove the ending capital against net assets.
        var navLabelRow = totalRowNumber + 2;
        sheet.Cell(navLabelRow, 1).Value = "Net asset value (NAV base)";
        sheet.Cell(navLabelRow, 1).Style.Font.Bold = true;
        WriteMoneyCell(sheet.Cell(navLabelRow, 3), layout.NetAssetValue);

        var tieRow = navLabelRow + 1;
        sheet.Cell(tieRow, 1).Value = "Ending capital ties to NAV";
        sheet.Cell(tieRow, 3).Value = layout.TiesToNetAssets
            ? "Yes"
            : $"No — variance {layout.NetAssetVariance.ToString(MoneyNumberFormat, CultureInfo.InvariantCulture)}";

        sheet.Columns().AdjustToContents();
        sheet.SheetView.FreezeRows(1);
    }

    private static void WritePartnersCapitalRow(
        IXLWorksheet sheet,
        int rowNumber,
        PartnersCapitalStatementLine line,
        string roleLabel,
        bool bold)
    {
        sheet.Cell(rowNumber, 1).Value = line.PartnerLabel;
        sheet.Cell(rowNumber, 2).Value = roleLabel;
        WriteMoneyCell(sheet.Cell(rowNumber, 3), line.BeginningCapital);
        WriteMoneyCell(sheet.Cell(rowNumber, 4), line.Contributions);
        WriteMoneyCell(sheet.Cell(rowNumber, 5), line.Distributions);
        WriteMoneyCell(sheet.Cell(rowNumber, 6), line.IncomeGainAllocations);
        WriteMoneyCell(sheet.Cell(rowNumber, 7), line.ExpenseAllocations);
        WriteMoneyCell(sheet.Cell(rowNumber, 8), line.FeeAllocations);
        WriteMoneyCell(sheet.Cell(rowNumber, 9), line.AllocatedResult);
        WriteMoneyCell(sheet.Cell(rowNumber, 10), line.OtherMovements);
        WriteMoneyCell(sheet.Cell(rowNumber, 11), line.EndingCapital);

        var ownership = sheet.Cell(rowNumber, 12);
        ownership.Value = line.OwnershipPercent / 100m; // stored as a true fraction so Excel foots to 100%
        ownership.Style.NumberFormat.Format = PercentNumberFormat;

        if (bold)
            sheet.Range(rowNumber, 1, rowNumber, PartnersCapitalSheetColumns.Length).Style.Font.Bold = true;
    }

    private static void WriteMoneyCell(IXLCell cell, decimal value)
    {
        cell.Value = value;
        cell.Style.NumberFormat.Format = MoneyNumberFormat;
    }

    private static string RoleLabel(PartnersCapitalPartnerRole role) => role switch
    {
        PartnersCapitalPartnerRole.LimitedPartner => "Limited partner",
        PartnersCapitalPartnerRole.GeneralPartner => "General partner",
        PartnersCapitalPartnerRole.UndistributedResult => "Undistributed result",
        _ => "Fund capital",
    };

    private static string Money(decimal value) => value.ToString(MoneyNumberFormat, CultureInfo.InvariantCulture);

    private static string Percent(decimal value) => value.ToString("0.00", CultureInfo.InvariantCulture) + "%";

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
