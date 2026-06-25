using System.IO.Compression;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Xml.Linq;
using Meridian.Contracts.Etl;

namespace Meridian.Infrastructure.Etl;

public sealed class CsvPartnerFileParser : IPartnerFileParser
{
    private static readonly XNamespace Spreadsheet = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
    private static readonly XNamespace Relationships = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
    private static readonly XNamespace PackageRelationships = "http://schemas.openxmlformats.org/package/2006/relationships";

    private readonly IPartnerSchemaRegistry _schemas;

    public CsvPartnerFileParser(IPartnerSchemaRegistry schemas)
    {
        _schemas = schemas;
    }

    public string SchemaId => "partner.trades.csv.v1";

    public bool CanParse(EtlStagedFile file)
    {
        var extension = Path.GetExtension(file.FileName);
        return extension.Equals(".csv", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".xlsx", StringComparison.OrdinalIgnoreCase);
    }

    public async IAsyncEnumerable<PartnerRecordEnvelope> ParseAsync(
        EtlStagedFile file,
        EtlCheckpointToken? checkpoint,
        string? schemaId = null,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        var resolvedSchemaId = string.IsNullOrWhiteSpace(schemaId) ? SchemaId : schemaId!;
        var schema = _schemas.GetCsvSchema(resolvedSchemaId);
        var extension = Path.GetExtension(file.FileName);

        if (extension.Equals(".xlsx", StringComparison.OrdinalIgnoreCase))
        {
            foreach (var row in ParseWorkbook(file, schema, resolvedSchemaId, checkpoint))
            {
                ct.ThrowIfCancellationRequested();
                yield return row;
            }

            yield break;
        }

        await foreach (var row in ParseCsvAsync(file, schema, resolvedSchemaId, checkpoint, ct).ConfigureAwait(false))
        {
            yield return row;
        }
    }

    private static async IAsyncEnumerable<PartnerRecordEnvelope> ParseCsvAsync(
        EtlStagedFile file,
        CsvSchemaDefinition schema,
        string schemaId,
        EtlCheckpointToken? checkpoint,
        [EnumeratorCancellation] CancellationToken ct)
    {
        using var reader = new StreamReader(file.StagedPath);
        string[]? headers = null;
        long recordIndex = 0;

        if (schema.HasHeaderRow)
        {
            var headerLine = await reader.ReadLineAsync(ct).ConfigureAwait(false);
            if (headerLine is null)
                yield break;
            headers = SplitCsvLine(headerLine, schema.Delimiter).ToArray();
        }

        string? line;
        while ((line = await reader.ReadLineAsync(ct).ConfigureAwait(false)) is not null)
        {
            recordIndex++;
            if (ShouldSkip(checkpoint, file.ChecksumSha256, recordIndex))
                continue;

            var values = SplitCsvLine(line, schema.Delimiter).ToArray();
            headers ??= schema.Columns.Keys.ToArray();
            yield return BuildEnvelope(file, schemaId, recordIndex, headers, values, line, normalizeExcelTimestampSerials: false);
        }
    }

    private static IEnumerable<PartnerRecordEnvelope> ParseWorkbook(
        EtlStagedFile file,
        CsvSchemaDefinition schema,
        string schemaId,
        EtlCheckpointToken? checkpoint)
    {
        using var archive = ZipFile.OpenRead(file.StagedPath);
        var sharedStrings = ReadSharedStrings(archive);
        var sheetEntry = ResolveFirstWorksheetEntry(archive)
            ?? throw new InvalidOperationException($"Excel workbook '{file.FileName}' does not contain a readable worksheet.");

        using var sheetStream = sheetEntry.Open();
        var sheet = XDocument.Load(sheetStream);
        string[]? headers = null;
        long recordIndex = 0;

        foreach (var row in sheet.Descendants(Spreadsheet + "sheetData").Elements(Spreadsheet + "row"))
        {
            var values = ReadRowValues(row, sharedStrings).ToArray();
            if (values.Length == 0 || values.All(string.IsNullOrWhiteSpace))
                continue;

            if (schema.HasHeaderRow && headers is null)
            {
                headers = values;
                continue;
            }

            recordIndex++;
            if (ShouldSkip(checkpoint, file.ChecksumSha256, recordIndex))
                continue;

            headers ??= schema.Columns.Keys.ToArray();
            yield return BuildEnvelope(file, schemaId, recordIndex, headers, values, string.Join(',', values.Select(EscapeCsvCell)), normalizeExcelTimestampSerials: true);
        }
    }

    private static PartnerRecordEnvelope BuildEnvelope(
        EtlStagedFile file,
        string schemaId,
        long recordIndex,
        IReadOnlyList<string> headers,
        IReadOnlyList<string> values,
        string? rawLine,
        bool normalizeExcelTimestampSerials)
    {
        var fields = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < headers.Count; i++)
        {
            var value = i < values.Count ? values[i] : null;
            if (normalizeExcelTimestampSerials && IsTimestampHeader(headers[i]))
            {
                value = NormalizeExcelTimestampSerial(value);
            }

            fields[headers[i]] = value;
        }

        return new PartnerRecordEnvelope
        {
            PartnerSchemaId = schemaId,
            SourceFileName = file.FileName,
            SourceFileChecksum = file.ChecksumSha256,
            RecordIndex = recordIndex,
            Fields = fields,
            RawLine = rawLine
        };
    }

    private static bool ShouldSkip(EtlCheckpointToken? checkpoint, string checksum, long recordIndex)
        => checkpoint?.CurrentFileChecksum == checksum
           && checkpoint.CurrentRecordIndex.HasValue
           && recordIndex <= checkpoint.CurrentRecordIndex.Value;

    private static ZipArchiveEntry? ResolveFirstWorksheetEntry(ZipArchive archive)
    {
        var workbookEntry = archive.GetEntry("xl/workbook.xml");
        var relsEntry = archive.GetEntry("xl/_rels/workbook.xml.rels");
        if (workbookEntry is null || relsEntry is null)
            return archive.GetEntry("xl/worksheets/sheet1.xml");

        using var workbookStream = workbookEntry.Open();
        using var relsStream = relsEntry.Open();
        var workbook = XDocument.Load(workbookStream);
        var rels = XDocument.Load(relsStream);
        var firstSheet = workbook.Descendants(Spreadsheet + "sheet").FirstOrDefault();
        var relationshipId = firstSheet?.Attribute(Relationships + "id")?.Value;
        if (string.IsNullOrWhiteSpace(relationshipId))
            return archive.GetEntry("xl/worksheets/sheet1.xml");

        var target = rels.Descendants(PackageRelationships + "Relationship")
            .FirstOrDefault(x => string.Equals(x.Attribute("Id")?.Value, relationshipId, StringComparison.Ordinal))
            ?.Attribute("Target")?.Value;
        if (string.IsNullOrWhiteSpace(target))
            return archive.GetEntry("xl/worksheets/sheet1.xml");

        var normalized = NormalizeWorkbookRelationshipTarget(target);
        return normalized is null ? null : archive.GetEntry(normalized);
    }

    private static string? NormalizeWorkbookRelationshipTarget(string target)
    {
        var trimmed = target.Trim();
        if (trimmed.Length == 0 ||
            trimmed.Contains('\\', StringComparison.Ordinal) ||
            Uri.TryCreate(trimmed, UriKind.Absolute, out _) ||
            (trimmed.Length >= 2 && char.IsLetter(trimmed[0]) && trimmed[1] == ':'))
        {
            return null;
        }

        var relative = trimmed.StartsWith("/", StringComparison.Ordinal)
            ? trimmed.TrimStart('/')
            : trimmed.StartsWith("xl/", StringComparison.OrdinalIgnoreCase)
                ? trimmed
                : $"xl/{trimmed}";

        var segments = relative.Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (segments.Length == 0 || segments.Any(segment => segment is "." or ".."))
            return null;

        return string.Join('/', segments);
    }

    private static string[] ReadSharedStrings(ZipArchive archive)
    {
        var entry = archive.GetEntry("xl/sharedStrings.xml");
        if (entry is null)
            return [];

        using var stream = entry.Open();
        var document = XDocument.Load(stream);
        return document.Descendants(Spreadsheet + "si")
            .Select(si => string.Concat(si.Descendants(Spreadsheet + "t").Select(t => t.Value)))
            .ToArray();
    }

    private static IEnumerable<string> ReadRowValues(XElement row, IReadOnlyList<string> sharedStrings)
    {
        var cells = row.Elements(Spreadsheet + "c").ToList();
        var nextColumn = 1;
        foreach (var cell in cells)
        {
            var column = GetColumnIndex(cell.Attribute("r")?.Value) ?? nextColumn;
            while (nextColumn < column)
            {
                yield return string.Empty;
                nextColumn++;
            }

            yield return ReadCellValue(cell, sharedStrings);
            nextColumn = column + 1;
        }
    }

    private static string ReadCellValue(XElement cell, IReadOnlyList<string> sharedStrings)
    {
        var value = cell.Element(Spreadsheet + "v")?.Value ?? cell.Element(Spreadsheet + "is")?.Value ?? string.Empty;
        var type = cell.Attribute("t")?.Value;
        if (type == "s" && int.TryParse(value, out var sharedStringIndex) && sharedStringIndex >= 0 && sharedStringIndex < sharedStrings.Count)
            return sharedStrings[sharedStringIndex];
        return value;
    }

    private static bool IsTimestampHeader(string header)
        => string.Equals(header.Trim(), "timestamp", StringComparison.OrdinalIgnoreCase);

    private static string? NormalizeExcelTimestampSerial(string? value)
    {
        if (string.IsNullOrWhiteSpace(value) ||
            !double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var serial) ||
            serial <= 0)
        {
            return value;
        }

        try
        {
            var utc = DateTime.SpecifyKind(DateTime.FromOADate(serial), DateTimeKind.Utc);
            return utc.ToString("O", CultureInfo.InvariantCulture);
        }
        catch (ArgumentException)
        {
            return value;
        }
    }

    private static int? GetColumnIndex(string? cellReference)
    {
        if (string.IsNullOrWhiteSpace(cellReference))
            return null;

        var index = 0;
        foreach (var ch in cellReference)
        {
            if (!char.IsLetter(ch))
                break;
            index = (index * 26) + char.ToUpperInvariant(ch) - 'A' + 1;
        }

        return index == 0 ? null : index;
    }

    private static IEnumerable<string> SplitCsvLine(string line, char delimiter)
    {
        var values = new List<string>();
        var current = new System.Text.StringBuilder();
        var inQuotes = false;
        for (var i = 0; i < line.Length; i++)
        {
            var ch = line[i];
            if (ch == '"')
            {
                if (inQuotes && i + 1 < line.Length && line[i + 1] == '"')
                {
                    current.Append('"');
                    i++;
                }
                else
                {
                    inQuotes = !inQuotes;
                }
            }
            else if (ch == delimiter && !inQuotes)
            {
                values.Add(current.ToString());
                current.Clear();
            }
            else
            {
                current.Append(ch);
            }
        }

        values.Add(current.ToString());
        return values;
    }

    private static string EscapeCsvCell(string value)
        => value.Contains(',') || value.Contains('"') || value.Contains('\n')
            ? $"\"{value.Replace("\"", "\"\"")}\""
            : value;
}
