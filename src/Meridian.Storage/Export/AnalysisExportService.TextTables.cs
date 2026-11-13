using System.Globalization;
using System.IO.Compression;
using System.Text;
using System.Xml.Linq;

namespace Meridian.Storage.Export;

public sealed partial class AnalysisExportService
{
    /// <summary>
    /// Writes a schema-preserving table of already formatted text cells. Domain projections own
    /// their interpretation and formatting; this primitive owns file formats and checksums only.
    /// Empty tables retain the supplied column schema, and spreadsheet formulas remain text.
    /// </summary>
    public async Task<ExportedFile> WriteInvariantTextTableAsync(
        string path,
        ExportFormat format,
        IReadOnlyList<string> columns,
        IReadOnlyList<Dictionary<string, object?>> rows,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(columns);
        ArgumentNullException.ThrowIfNull(rows);
        var names = columns.ToArray();
        if (names.Length == 0 || names.Any(string.IsNullOrWhiteSpace) ||
            names.Distinct(StringComparer.Ordinal).Count() != names.Length)
            throw new ArgumentException("A text table requires unique, nonempty column names.", nameof(columns));
        if (rows.Any(row => row is null || row.Count != names.Length ||
                names.Any(name => !row.ContainsKey(name)) || row.Values.Any(value => value is not null and not string)))
            throw new ArgumentException("Every text-table row must have the supplied columns and text-only values.", nameof(rows));
        if (format is not (ExportFormat.Csv or ExportFormat.Xlsx or ExportFormat.Parquet or ExportFormat.Arrow))
            throw new NotSupportedException("Text tables support CSV, XLSX, Parquet and Arrow.");
        ct.ThrowIfCancellationRequested();
        // Freeze caller-owned dictionaries in schema order before any asynchronous file writes.
        var orderedRows = rows.Select(row => names.ToDictionary(name => name, name => row[name], StringComparer.Ordinal)).ToList();
        switch (format)
        {
            case ExportFormat.Csv:
                await WriteTextCsvAsync(path, names, orderedRows, ct).ConfigureAwait(false);
                break;
            case ExportFormat.Xlsx:
                if (orderedRows.Count >= 1_048_576)
                    throw new InvalidOperationException("Worksheet exceeds Excel's row limit; use a columnar format.");
                await WriteTextWorkbookAsync(path, names, orderedRows, ct).ConfigureAwait(false);
                break;
            case ExportFormat.Parquet:
                if (orderedRows.Count == 0)
                    await WriteEmptyTextParquetAsync(path, names, ct).ConfigureAwait(false);
                else
                    await WriteParquetFileAsync(path, orderedRows, ct).ConfigureAwait(false);
                break;
            case ExportFormat.Arrow:
                if (orderedRows.Count == 0)
                    await WriteEmptyTextArrowAsync(path, names, ct).ConfigureAwait(false);
                else
                    await WriteArrowFileAsync(path, orderedRows, new TimestampSettings(), ct).ConfigureAwait(false);
                break;
        }
        return new ExportedFile
        {
            Path = path,
            RelativePath = Path.GetFileName(path),
            Format = format.ToString().ToLowerInvariant(),
            RecordCount = orderedRows.Count,
            SizeBytes = new FileInfo(path).Length,
            ChecksumSha256 = await ComputeChecksumAsync(path, ct).ConfigureAwait(false)
        };
    }

    private static async Task WriteEmptyTextParquetAsync(string path, string[] columns, CancellationToken ct)
    {
        var fields = columns.Select(column => new Parquet.Schema.DataField<string>(column)).ToArray();
        using var stream = File.Create(path);
        using var writer = await Parquet.ParquetWriter.CreateAsync(new Parquet.Schema.ParquetSchema(fields), stream, cancellationToken: ct);
        using var group = writer.CreateRowGroup();
        foreach (var field in fields)
            await group.WriteColumnAsync(new Parquet.Data.DataColumn(field, Array.Empty<string>()), ct);
    }

    private static async Task WriteEmptyTextArrowAsync(string path, string[] columns, CancellationToken ct)
    {
        var schema = new Apache.Arrow.Schema(columns.Select(column => new Apache.Arrow.Field(column,
            Apache.Arrow.Types.StringType.Default, nullable: true)), null);
        var arrays = columns.Select(_ => (Apache.Arrow.IArrowArray)new Apache.Arrow.StringArray.Builder().Build()).ToArray();
        using var batch = new Apache.Arrow.RecordBatch(schema, arrays, 0);
        await using var stream = File.Create(path);
        using var writer = new Apache.Arrow.Ipc.ArrowFileWriter(stream, schema);
        await writer.WriteRecordBatchAsync(batch, ct);
        await writer.WriteEndAsync(ct);
    }

    private static async Task WriteTextCsvAsync(string path, string[] columns, List<Dictionary<string, object?>> rows, CancellationToken ct)
    {
        await using var writer = new StreamWriter(path, false, new UTF8Encoding(false));
        static string Quote(object? value) => EscapeCsvValue(Convert.ToString(value, CultureInfo.InvariantCulture) ?? "");
        await writer.WriteLineAsync(string.Join(",", columns.Select(Quote)).AsMemory(), ct);
        foreach (var row in rows)
            await writer.WriteLineAsync(string.Join(",", row.Values.Select(Quote)).AsMemory(), ct);
    }

    private static async Task WriteTextWorkbookAsync(string path, string[] columns, List<Dictionary<string, object?>> rows, CancellationToken ct)
    {
        XNamespace ns = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        var data = new XElement(ns + "sheetData");
        IEnumerable<IEnumerable<object?>> values = new[] { columns.Cast<object?>() }
            .Concat(rows.Select(row => row.Values.AsEnumerable()));
        foreach (var row in values)
        {
            ct.ThrowIfCancellationRequested();
            data.Add(new XElement(ns + "row", row.Select(value => new XElement(ns + "c",
                new XAttribute("t", "inlineStr"), new XElement(ns + "is", new XElement(ns + "t",
                    new XAttribute(XNamespace.Xml + "space", "preserve"), value ?? ""))))));
        }
        using var archive = ZipFile.Open(path, ZipArchiveMode.Create);
        var parts = new Dictionary<string, string>
        {
            ["[Content_Types].xml"] = GetContentTypesXml(),
            ["_rels/.rels"] = GetRelsXml(),
            ["xl/workbook.xml"] = GetWorkbookXml(),
            ["xl/_rels/workbook.xml.rels"] = GetWorkbookRelsXml(),
            ["xl/styles.xml"] = GetStylesXml(),
            ["xl/sharedStrings.xml"] = GetSharedStringsXml([]),
            ["xl/worksheets/sheet1.xml"] = new XElement(ns + "worksheet", data).ToString(SaveOptions.DisableFormatting)
        };
        foreach (var (name, content) in parts)
        {
            await using var stream = archive.CreateEntry(name).Open();
            await using var writer = new StreamWriter(stream, new UTF8Encoding(false));
            await writer.WriteAsync(content.AsMemory(), ct);
        }
    }
}
