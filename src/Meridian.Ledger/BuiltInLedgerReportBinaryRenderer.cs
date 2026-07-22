using System.Globalization;
using System.IO.Compression;
using System.Text;

namespace Meridian.Ledger;

/// <summary>
/// Dependency-free deterministic renderer producing valid — if plain — XLSX and PDF documents from
/// a report pack. Used so the pure ledger export path honors every declared format standalone;
/// production hosts inject a client-grade <see cref="ILedgerReportBinaryRenderer"/> instead.
/// </summary>
public sealed class BuiltInLedgerReportBinaryRenderer : ILedgerReportBinaryRenderer
{
    public static BuiltInLedgerReportBinaryRenderer Instance { get; } = new();

    public byte[] RenderWorkbook(LedgerFinancialReportPack reportPack)
    {
        var tables = LedgerReportPresentation.BuildTables(reportPack);
        using var output = new MemoryStream();
        using (var archive = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: true, Encoding.UTF8))
        {
            var sheetNames = BuildUniqueSheetNames(tables);
            AddEntry(archive, "[Content_Types].xml", BuildContentTypes(sheetNames.Count));
            AddEntry(archive, "_rels/.rels", RootRelationships);
            AddEntry(archive, "xl/workbook.xml", BuildWorkbook(sheetNames));
            AddEntry(archive, "xl/_rels/workbook.xml.rels", BuildWorkbookRelationships(sheetNames.Count));
            for (var index = 0; index < tables.Count; index++)
                AddEntry(archive, $"xl/worksheets/sheet{index + 1}.xml", BuildSheet(tables[index]));
        }

        return output.ToArray();
    }

    public byte[] RenderPdf(LedgerFinancialReportPack reportPack)
    {
        var tables = LedgerReportPresentation.BuildTables(reportPack);
        var lines = new List<string> { $"Meridian financial report - {reportPack.Request.FundId}" };
        foreach (var table in tables)
        {
            lines.Add(string.Empty);
            lines.Add(table.Title);
            lines.Add(string.Join(" | ", table.Headers));
            foreach (var row in table.Rows)
                lines.Add(string.Join(" | ", row));
        }

        return BuildPdf(lines);
    }

    private static IReadOnlyList<string> BuildUniqueSheetNames(IReadOnlyList<LedgerReportTable> tables)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var names = new List<string>(tables.Count);
        for (var index = 0; index < tables.Count; index++)
        {
            var baseName = SanitizeSheetName(tables[index].Title);
            var candidate = baseName;
            var suffix = 2;
            while (!seen.Add(candidate))
            {
                // Trim to 25 so even a long suffix keeps the name within Excel's 31-char limit.
                var trimmed = baseName.Length > 25 ? baseName[..25] : baseName;
                candidate = $"{trimmed} {suffix++}";
            }

            names.Add(candidate);
        }

        return names;
    }

    private static string SanitizeSheetName(string title)
    {
        var builder = new StringBuilder(title.Length);
        foreach (var character in title)
        {
            builder.Append(character is '\\' or '/' or '?' or '*' or '[' or ']' or ':' ? ' ' : character);
        }

        var cleaned = builder.ToString().Trim();
        if (cleaned.Length == 0)
            cleaned = "Sheet";
        return cleaned.Length > 31 ? cleaned[..31] : cleaned;
    }

    private const string RootRelationships =
        "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
        "<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\">" +
        "<Relationship Id=\"rId1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument\" Target=\"xl/workbook.xml\"/>" +
        "</Relationships>";

    private static string BuildContentTypes(int sheetCount)
    {
        var builder = new StringBuilder();
        builder.Append("<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>");
        builder.Append("<Types xmlns=\"http://schemas.openxmlformats.org/package/2006/content-types\">");
        builder.Append("<Default Extension=\"rels\" ContentType=\"application/vnd.openxmlformats-package.relationships+xml\"/>");
        builder.Append("<Default Extension=\"xml\" ContentType=\"application/xml\"/>");
        builder.Append("<Override PartName=\"/xl/workbook.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml\"/>");
        for (var index = 1; index <= sheetCount; index++)
        {
            builder.Append(CultureInfo.InvariantCulture, $"<Override PartName=\"/xl/worksheets/sheet{index}.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml\"/>");
        }

        builder.Append("</Types>");
        return builder.ToString();
    }

    private static string BuildWorkbook(IReadOnlyList<string> sheetNames)
    {
        var builder = new StringBuilder();
        builder.Append("<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>");
        builder.Append("<workbook xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\" xmlns:r=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships\"><sheets>");
        for (var index = 0; index < sheetNames.Count; index++)
        {
            builder.Append(CultureInfo.InvariantCulture, $"<sheet name=\"{EscapeXml(sheetNames[index])}\" sheetId=\"{index + 1}\" r:id=\"rId{index + 1}\"/>");
        }

        builder.Append("</sheets></workbook>");
        return builder.ToString();
    }

    private static string BuildWorkbookRelationships(int sheetCount)
    {
        var builder = new StringBuilder();
        builder.Append("<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>");
        builder.Append("<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\">");
        for (var index = 1; index <= sheetCount; index++)
        {
            builder.Append(CultureInfo.InvariantCulture, $"<Relationship Id=\"rId{index}\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet\" Target=\"worksheets/sheet{index}.xml\"/>");
        }

        builder.Append("</Relationships>");
        return builder.ToString();
    }

    private static string BuildSheet(LedgerReportTable table)
    {
        var builder = new StringBuilder();
        builder.Append("<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>");
        builder.Append("<worksheet xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\"><sheetData>");
        AppendRow(builder, 1, table.Headers);
        var rowNumber = 2;
        foreach (var row in table.Rows)
            AppendRow(builder, rowNumber++, row);
        builder.Append("</sheetData></worksheet>");
        return builder.ToString();
    }

    private static void AppendRow(StringBuilder builder, int rowNumber, IReadOnlyList<string> values)
    {
        builder.Append(CultureInfo.InvariantCulture, $"<row r=\"{rowNumber}\">");
        for (var index = 0; index < values.Count; index++)
        {
            var cellRef = $"{ColumnName(index + 1)}{rowNumber.ToString(CultureInfo.InvariantCulture)}";
            builder.Append(CultureInfo.InvariantCulture, $"<c r=\"{cellRef}\" t=\"inlineStr\"><is><t xml:space=\"preserve\">{EscapeXml(values[index])}</t></is></c>");
        }

        builder.Append("</row>");
    }

    private static string ColumnName(int ordinal)
    {
        var builder = new StringBuilder();
        var value = ordinal;
        while (value > 0)
        {
            value--;
            builder.Insert(0, (char)('A' + value % 26));
            value /= 26;
        }

        return builder.ToString();
    }

    private static void AddEntry(ZipArchive archive, string name, string content)
    {
        var entry = archive.CreateEntry(name, CompressionLevel.NoCompression);
        entry.LastWriteTime = new DateTimeOffset(1980, 1, 1, 0, 0, 0, TimeSpan.Zero);
        using var stream = entry.Open();
        var bytes = Encoding.UTF8.GetBytes(content);
        stream.Write(bytes, 0, bytes.Length);
    }

    private static byte[] BuildPdf(IReadOnlyList<string> textLines)
    {
        const int charactersPerLine = 95;
        const int linesPerPage = 50;
        var wrapped = textLines.SelectMany(line => WrapLine(line, charactersPerLine)).ToArray();
        var pages = wrapped.Chunk(linesPerPage).ToArray();
        if (pages.Length == 0)
            pages = [[string.Empty]];

        const int catalogObjectId = 1;
        const int pagesObjectId = 2;
        const int fontObjectId = 3;
        var objects = new List<string>
        {
            $"<< /Type /Catalog /Pages {pagesObjectId} 0 R >>",
            string.Empty,
            "<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica >>",
        };
        var pageObjectIds = new List<int>(pages.Length);
        foreach (var page in pages)
        {
            var pageObjectId = objects.Count + 1;
            var contentObjectId = pageObjectId + 1;
            pageObjectIds.Add(pageObjectId);
            objects.Add($"<< /Type /Page /Parent {pagesObjectId} 0 R /MediaBox [0 0 612 792] /Resources << /Font << /F1 {fontObjectId} 0 R >> >> /Contents {contentObjectId} 0 R >>");
            var stream = RenderPage(page);
            objects.Add($"<< /Length {Encoding.ASCII.GetByteCount(stream).ToString(CultureInfo.InvariantCulture)} >>\nstream\n{stream}endstream");
        }

        objects[pagesObjectId - 1] =
            $"<< /Type /Pages /Kids [{string.Join(" ", pageObjectIds.Select(static id => $"{id} 0 R"))}] /Count {pages.Length.ToString(CultureInfo.InvariantCulture)} >>";

        using var output = new MemoryStream();
        WriteAscii(output, "%PDF-1.4\n%Meridian\n");
        var offsets = new List<long> { 0 };
        for (var index = 0; index < objects.Count; index++)
        {
            offsets.Add(output.Position);
            WriteAscii(output, $"{index + 1} 0 obj\n{objects[index]}\nendobj\n");
        }

        var xref = output.Position;
        WriteAscii(output, $"xref\n0 {objects.Count + 1}\n0000000000 65535 f \n");
        foreach (var offset in offsets.Skip(1))
            WriteAscii(output, $"{offset.ToString("D10", CultureInfo.InvariantCulture)} 00000 n \n");
        WriteAscii(output, $"trailer\n<< /Size {objects.Count + 1} /Root {catalogObjectId} 0 R >>\nstartxref\n{xref.ToString(CultureInfo.InvariantCulture)}\n%%EOF\n");
        return output.ToArray();
    }

    private static string RenderPage(IEnumerable<string> lines)
    {
        var content = new StringBuilder("BT /F1 9 Tf 40 752 Td 12 TL\n");
        foreach (var line in lines)
            content.Append('(').Append(EscapePdf(line)).Append(") Tj T*\n");
        return content.Append("ET\n").ToString();
    }

    private static IEnumerable<string> WrapLine(string value, int capacity)
    {
        var normalized = ToAscii(value);
        if (normalized.Length == 0)
        {
            yield return string.Empty;
            yield break;
        }

        while (normalized.Length > capacity)
        {
            var split = normalized.LastIndexOf(' ', Math.Min(capacity, normalized.Length - 1));
            if (split <= 0)
                split = capacity;
            yield return normalized[..split].TrimEnd();
            normalized = normalized[split..].TrimStart();
        }

        yield return normalized;
    }

    private static string ToAscii(string value)
    {
        var builder = new StringBuilder(value.Length);
        foreach (var character in value)
            builder.Append(character is >= ' ' and <= '~' ? character : '?');
        return builder.ToString();
    }

    private static string EscapePdf(string value) =>
        value.Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("(", "\\(", StringComparison.Ordinal)
            .Replace(")", "\\)", StringComparison.Ordinal);

    private static string EscapeXml(string value) =>
        value.Replace("&", "&amp;", StringComparison.Ordinal)
            .Replace("<", "&lt;", StringComparison.Ordinal)
            .Replace(">", "&gt;", StringComparison.Ordinal)
            .Replace("\"", "&quot;", StringComparison.Ordinal);

    private static void WriteAscii(Stream stream, string value)
    {
        var bytes = Encoding.ASCII.GetBytes(value);
        stream.Write(bytes, 0, bytes.Length);
    }
}
