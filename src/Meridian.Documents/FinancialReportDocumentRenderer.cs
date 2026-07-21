using System.Globalization;
using System.IO.Compression;
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
    private static readonly DateTime FixedTimestamp = new(2000, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    static FinancialReportDocumentRenderer()
    {
        QuestPDF.Settings.License = LicenseType.Community;
        QuestPDF.Settings.EnableDebugging = false;
        QuestPDF.Settings.CheckIfAllTextGlyphsAreAvailable = false;
    }

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
            CreationDate = new DateTimeOffset(FixedTimestamp, TimeSpan.Zero),
            ModifiedDate = new DateTimeOffset(FixedTimestamp, TimeSpan.Zero),
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
            workbook.Properties.Created = FixedTimestamp;
            workbook.Properties.Modified = FixedTimestamp;
            workbook.Properties.LastModifiedBy = "Meridian";

            using var buffer = new MemoryStream();
            workbook.SaveAs(buffer);
            rendered = buffer.ToArray();
        }

        return Canonicalize(rendered);
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
            var trimmed = baseName.Length > 27 ? baseName[..27] : baseName;
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

    // ClosedXML names the OPC core-properties part with a fresh random GUID each save (and references
    // it by that GUID in _rels/.rels) and reorders zip entries; re-zip deterministically with a fixed
    // core-properties name, fixed timestamps, and sorted entries so re-rendering yields stable bytes.
    private const string CanonicalCorePropertiesPart = "package/services/metadata/core-properties/core.psmdcp";

    private static byte[] Canonicalize(byte[] workbookBytes)
    {
        var parts = new Dictionary<string, byte[]>(StringComparer.Ordinal);
        string? volatileCorePropertiesPart = null;
        using (var source = new ZipArchive(new MemoryStream(workbookBytes), ZipArchiveMode.Read))
        {
            foreach (var entry in source.Entries)
            {
                using var entryStream = entry.Open();
                using var buffer = new MemoryStream();
                entryStream.CopyTo(buffer);
                var name = entry.FullName;
                if (name.StartsWith("package/services/metadata/core-properties/", StringComparison.Ordinal)
                    && name.EndsWith(".psmdcp", StringComparison.Ordinal))
                {
                    volatileCorePropertiesPart = name;
                    name = CanonicalCorePropertiesPart;
                }

                parts[name] = buffer.ToArray();
            }
        }

        // Repoint every reference to the GUID-named core-properties part (_rels/.rels targets it and
        // [Content_Types].xml overrides it by full part name), and pin the wall-clock created/modified
        // timestamps ClosedXML stamps into the core-properties part regardless of Properties.
        if (volatileCorePropertiesPart is not null)
        {
            var oldFileName = volatileCorePropertiesPart[(volatileCorePropertiesPart.LastIndexOf('/') + 1)..];
            foreach (var referencingPart in new[] { "_rels/.rels", "[Content_Types].xml" })
            {
                if (parts.TryGetValue(referencingPart, out var bytes))
                {
                    var rewritten = Encoding.UTF8.GetString(bytes).Replace(oldFileName, "core.psmdcp", StringComparison.Ordinal);
                    if (referencingPart == "_rels/.rels")
                        rewritten = NormalizeRelationshipIds(rewritten);
                    parts[referencingPart] = Encoding.UTF8.GetBytes(rewritten);
                }
            }

            if (parts.TryGetValue(CanonicalCorePropertiesPart, out var coreBytes))
                parts[CanonicalCorePropertiesPart] = Encoding.UTF8.GetBytes(PinCoreTimestamps(Encoding.UTF8.GetString(coreBytes)));
        }

        using var output = new MemoryStream();
        using (var target = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: true, Encoding.UTF8))
        {
            foreach (var part in parts.OrderBy(static item => item.Key, StringComparer.Ordinal))
            {
                var targetEntry = target.CreateEntry(part.Key, CompressionLevel.NoCompression);
                targetEntry.LastWriteTime = new DateTimeOffset(1980, 1, 1, 0, 0, 0, TimeSpan.Zero);
                using var targetStream = targetEntry.Open();
                targetStream.Write(part.Value, 0, part.Value.Length);
            }
        }

        return output.ToArray();
    }

    // Package-level relationships carry random 16-hex ids that no other part references by id;
    // replace them with sequential canonical ids so re-rendering is byte-stable.
    private static string NormalizeRelationshipIds(string rels)
    {
        var counter = 0;
        return System.Text.RegularExpressions.Regex.Replace(
            rels,
            "Id=\"R[0-9a-fA-F]{16}\"",
            _ => $"Id=\"Rc{++counter}\"");
    }

    private static string PinCoreTimestamps(string coreProperties)
        => System.Text.RegularExpressions.Regex.Replace(
            coreProperties,
            @"(<dcterms:(?:created|modified)[^>]*>)[^<]*(</dcterms:(?:created|modified)>)",
            "${1}2000-01-01T00:00:00Z${2}");

    private static string Shorten(string value) => value.Length <= 16 ? value : value[..16];
}
