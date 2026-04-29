using System.Globalization;
using System.IO.Compression;
using System.Text;

namespace Meridian.Storage.Export;

/// <summary>
/// Minimal OpenXML workbook writer used by local-first governance artifacts.
/// </summary>
public static class XlsxWorkbookWriter
{
    public static byte[] CreateWorkbook(IReadOnlyList<XlsxWorksheet> worksheets, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(worksheets);

        var normalized = worksheets.Count == 0
            ? [new XlsxWorksheet("Report", [], [])]
            : worksheets;
        var sheetNames = BuildSheetNames(normalized);
        var sharedStrings = new List<string>();
        var sharedStringIndex = new Dictionary<string, int>(StringComparer.Ordinal);

        using var output = new MemoryStream();
        using (var archive = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: true))
        {
            WriteTextEntry(archive, "[Content_Types].xml", GetContentTypesXml(normalized.Count));
            WriteTextEntry(archive, "_rels/.rels", GetRelsXml());
            WriteTextEntry(archive, "xl/workbook.xml", GetWorkbookXml(sheetNames));
            WriteTextEntry(archive, "xl/_rels/workbook.xml.rels", GetWorkbookRelsXml(normalized.Count));
            WriteTextEntry(archive, "xl/styles.xml", GetStylesXml());

            for (var index = 0; index < normalized.Count; index++)
            {
                ct.ThrowIfCancellationRequested();
                WriteTextEntry(
                    archive,
                    $"xl/worksheets/sheet{index + 1}.xml",
                    BuildWorksheetXml(normalized[index], sharedStrings, sharedStringIndex, ct));
            }

            WriteTextEntry(archive, "xl/sharedStrings.xml", GetSharedStringsXml(sharedStrings));
        }

        return output.ToArray();
    }

    private static string BuildWorksheetXml(
        XlsxWorksheet worksheet,
        List<string> sharedStrings,
        Dictionary<string, int> sharedStringIndex,
        CancellationToken ct)
    {
        var xml = new StringBuilder();
        xml.AppendLine("""<?xml version="1.0" encoding="UTF-8" standalone="yes"?>""");
        xml.AppendLine("""<worksheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main">""");
        xml.AppendLine("<sheetData>");

        if (worksheet.Headers.Count > 0)
        {
            xml.AppendLine("""<row r="1">""");
            for (var col = 0; col < worksheet.Headers.Count; col++)
            {
                ct.ThrowIfCancellationRequested();
                xml.Append(GetStringCellXml(GetCellReference(col, 0), worksheet.Headers[col], sharedStrings, sharedStringIndex));
            }

            xml.AppendLine("</row>");
        }

        for (var rowIndex = 0; rowIndex < worksheet.Rows.Count; rowIndex++)
        {
            ct.ThrowIfCancellationRequested();
            var row = worksheet.Rows[rowIndex];
            xml.AppendLine($"<row r=\"{rowIndex + 2}\">");
            for (var col = 0; col < worksheet.Headers.Count && col < row.Count; col++)
            {
                if (row[col] is null)
                {
                    continue;
                }

                xml.Append(GetCellXml(GetCellReference(col, rowIndex + 1), row[col]!, sharedStrings, sharedStringIndex));
            }

            xml.AppendLine("</row>");
        }

        xml.AppendLine("</sheetData>");
        xml.AppendLine("</worksheet>");
        return xml.ToString();
    }

    private static string GetCellXml(
        string cellRef,
        object value,
        List<string> sharedStrings,
        Dictionary<string, int> sharedStringIndex)
    {
        return value switch
        {
            byte or sbyte or short or ushort or int or uint or long or ulong or float or double or decimal =>
                $"<c r=\"{cellRef}\"><v>{Convert.ToString(value, CultureInfo.InvariantCulture)}</v></c>",
            bool boolean => $"<c r=\"{cellRef}\" t=\"b\"><v>{(boolean ? "1" : "0")}</v></c>",
            DateTime dateTime => $"<c r=\"{cellRef}\" s=\"1\"><v>{dateTime.ToOADate().ToString(CultureInfo.InvariantCulture)}</v></c>",
            DateTimeOffset dateTimeOffset => $"<c r=\"{cellRef}\" s=\"1\"><v>{dateTimeOffset.UtcDateTime.ToOADate().ToString(CultureInfo.InvariantCulture)}</v></c>",
            DateOnly date => $"<c r=\"{cellRef}\" s=\"1\"><v>{date.ToDateTime(TimeOnly.MinValue).ToOADate().ToString(CultureInfo.InvariantCulture)}</v></c>",
            _ => GetStringCellXml(cellRef, value.ToString() ?? string.Empty, sharedStrings, sharedStringIndex)
        };
    }

    private static string GetStringCellXml(
        string cellRef,
        string value,
        List<string> sharedStrings,
        Dictionary<string, int> sharedStringIndex)
    {
        if (!sharedStringIndex.TryGetValue(value, out var index))
        {
            index = sharedStrings.Count;
            sharedStringIndex[value] = index;
            sharedStrings.Add(value);
        }

        return $"<c r=\"{cellRef}\" t=\"s\"><v>{index}</v></c>";
    }

    private static IReadOnlyList<string> BuildSheetNames(IReadOnlyList<XlsxWorksheet> worksheets)
    {
        var names = new List<string>(worksheets.Count);
        var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var worksheet in worksheets)
        {
            var baseName = SanitizeSheetName(worksheet.Name);
            var candidate = baseName;
            var suffix = 2;
            while (!used.Add(candidate))
            {
                var marker = $" {suffix++}";
                candidate = baseName.Length + marker.Length > 31
                    ? baseName[..(31 - marker.Length)] + marker
                    : baseName + marker;
            }

            names.Add(candidate);
        }

        return names;
    }

    private static string SanitizeSheetName(string value)
    {
        var trimmed = string.IsNullOrWhiteSpace(value) ? "Sheet" : value.Trim();
        var invalid = new HashSet<char> { '[', ']', ':', '*', '?', '/', '\\' };
        var buffer = new StringBuilder(trimmed.Length);
        foreach (var character in trimmed)
        {
            buffer.Append(invalid.Contains(character) ? ' ' : character);
        }

        var sanitized = buffer.ToString().Trim();
        if (sanitized.Length == 0)
        {
            sanitized = "Sheet";
        }

        return sanitized.Length <= 31 ? sanitized : sanitized[..31];
    }

    private static string GetCellReference(int col, int row)
    {
        var colName = new StringBuilder();
        var colNum = col;
        while (colNum >= 0)
        {
            colName.Insert(0, (char)('A' + (colNum % 26)));
            colNum = colNum / 26 - 1;
        }

        return $"{colName}{row + 1}";
    }

    private static void WriteTextEntry(ZipArchive archive, string path, string content)
    {
        var entry = archive.CreateEntry(path);
        using var stream = entry.Open();
        using var writer = new StreamWriter(stream, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        writer.Write(content);
    }

    private static string GetContentTypesXml(int sheetCount)
    {
        var overrides = new StringBuilder();
        for (var index = 1; index <= sheetCount; index++)
        {
            overrides.AppendLine($"    <Override PartName=\"/xl/worksheets/sheet{index}.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml\"/>");
        }

        return $$"""
            <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
            <Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types">
                <Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/>
                <Default Extension="xml" ContentType="application/xml"/>
                <Override PartName="/xl/workbook.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml"/>
            {{overrides}}    <Override PartName="/xl/styles.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.styles+xml"/>
                <Override PartName="/xl/sharedStrings.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.sharedStrings+xml"/>
            </Types>
            """;
    }

    private static string GetRelsXml() => """
        <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
        <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
            <Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument" Target="xl/workbook.xml"/>
        </Relationships>
        """;

    private static string GetWorkbookXml(IReadOnlyList<string> sheetNames)
    {
        var sheets = new StringBuilder();
        for (var index = 0; index < sheetNames.Count; index++)
        {
            sheets.AppendLine($"        <sheet name=\"{EscapeXml(sheetNames[index])}\" sheetId=\"{index + 1}\" r:id=\"rId{index + 1}\"/>");
        }

        return $$"""
            <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
            <workbook xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main" xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships">
                <sheets>
            {{sheets}}    </sheets>
            </workbook>
            """;
    }

    private static string GetWorkbookRelsXml(int sheetCount)
    {
        var rels = new StringBuilder();
        for (var index = 1; index <= sheetCount; index++)
        {
            rels.AppendLine($"    <Relationship Id=\"rId{index}\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet\" Target=\"worksheets/sheet{index}.xml\"/>");
        }

        rels.AppendLine($"    <Relationship Id=\"rId{sheetCount + 1}\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/styles\" Target=\"styles.xml\"/>");
        rels.AppendLine($"    <Relationship Id=\"rId{sheetCount + 2}\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/sharedStrings\" Target=\"sharedStrings.xml\"/>");

        return $$"""
            <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
            <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
            {{rels}}</Relationships>
            """;
    }

    private static string GetStylesXml() => """
        <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
        <styleSheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main">
            <numFmts count="1"><numFmt numFmtId="164" formatCode="yyyy-mm-dd hh:mm:ss"/></numFmts>
            <fonts count="1"><font><sz val="11"/><name val="Calibri"/></font></fonts>
            <fills count="1"><fill><patternFill patternType="none"/></fill></fills>
            <borders count="1"><border><left/><right/><top/><bottom/></border></borders>
            <cellXfs count="2"><xf numFmtId="0" fontId="0" fillId="0" borderId="0"/><xf numFmtId="164" fontId="0" fillId="0" borderId="0" applyNumberFormat="1"/></cellXfs>
        </styleSheet>
        """;

    private static string GetSharedStringsXml(List<string> strings)
    {
        var values = string.Join(
            Environment.NewLine,
            strings.Select(static value => $"<si><t>{EscapeXml(value)}</t></si>"));

        return $$"""
            <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
            <sst xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main" count="{{strings.Count}}" uniqueCount="{{strings.Count}}">
            {{values}}
            </sst>
            """;
    }

    private static string EscapeXml(string value) =>
        value
            .Replace("&", "&amp;", StringComparison.Ordinal)
            .Replace("<", "&lt;", StringComparison.Ordinal)
            .Replace(">", "&gt;", StringComparison.Ordinal)
            .Replace("\"", "&quot;", StringComparison.Ordinal)
            .Replace("'", "&apos;", StringComparison.Ordinal);
}

public sealed record XlsxWorksheet(
    string Name,
    IReadOnlyList<string> Headers,
    IReadOnlyList<IReadOnlyList<object?>> Rows);
