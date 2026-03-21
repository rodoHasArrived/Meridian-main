using System.IO.Compression;
using System.Text.Json;
using Meridian.Application.Logging;
using Parquet;
using Parquet.Data;
using Parquet.Schema;
using Serilog;

namespace Meridian.Storage.Services;

/// <summary>
/// Background service that detects completed trading days' JSONL files
/// and converts them to Parquet format for optimized analytics queries.
/// Only converts files from prior trading days (never the current day's live data).
/// </summary>
public sealed class ParquetConversionService
{
    private readonly ILogger _log = LoggingSetup.ForContext<ParquetConversionService>();
    private readonly StorageOptions _options;
    private readonly string _parquetOutputDir;

    private const int DefaultRowGroupSize = 10_000;

    public ParquetConversionService(StorageOptions options)
    {
        _options = options;
        _parquetOutputDir = Path.Combine(options.RootPath, "_parquet");
    }

    /// <summary>
    /// Scan for JSONL files from completed trading days and convert them to Parquet.
    /// </summary>
    /// <param name="maxAgeDays">Only consider files from the last N days (default: 30).</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Summary of conversion results.</returns>
    public async Task<ConversionSummary> ConvertCompletedDaysAsync(int maxAgeDays = 30, CancellationToken ct = default)
    {
        var summary = new ConversionSummary();
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var cutoff = today.AddDays(-maxAgeDays);

        if (!Directory.Exists(_options.RootPath))
        {
            _log.Warning("Data root {RootPath} does not exist; skipping Parquet conversion", _options.RootPath);
            return summary;
        }

        Directory.CreateDirectory(_parquetOutputDir);

        // Find all JSONL files (excluding today's live data)
        var jsonlFiles = Directory.GetFiles(_options.RootPath, "*.jsonl", SearchOption.AllDirectories)
            .Concat(Directory.GetFiles(_options.RootPath, "*.jsonl.gz", SearchOption.AllDirectories))
            .Where(f => !f.Contains("_wal") && !f.Contains("_archive") && !f.Contains("_parquet"))
            .ToList();

        foreach (var jsonlPath in jsonlFiles)
        {
            ct.ThrowIfCancellationRequested();

            try
            {
                var fileDate = ExtractDateFromPath(jsonlPath);
                if (fileDate == null || fileDate >= today || fileDate < cutoff)
                    continue;

                // Check if Parquet version already exists
                var parquetPath = GetParquetOutputPath(jsonlPath);
                if (File.Exists(parquetPath))
                {
                    summary.SkippedAlreadyConverted++;
                    continue;
                }

                var convertedRecords = await ConvertJsonlFileToParquetAsync(jsonlPath, parquetPath, ct);
                if (convertedRecords == 0)
                {
                    summary.SkippedEmpty++;
                    continue;
                }

                summary.FilesConverted++;
                summary.RecordsConverted += convertedRecords;
                summary.BytesSaved += new FileInfo(jsonlPath).Length - new FileInfo(parquetPath).Length;

                _log.Information("Converted {Source} to Parquet ({RecordCount} records)",
                    Path.GetFileName(jsonlPath), convertedRecords);
            }
            catch (Exception ex)
            {
                summary.Errors++;
                _log.Warning(ex, "Failed to convert {File} to Parquet", jsonlPath);
            }
        }

        _log.Information(
            "Parquet conversion complete: {Converted} files, {Records} records, {Skipped} skipped, {Errors} errors",
            summary.FilesConverted, summary.RecordsConverted, summary.SkippedAlreadyConverted, summary.Errors);

        return summary;
    }

    private DateOnly? ExtractDateFromPath(string path)
    {
        // Try to extract a date from the file path or name
        // Supports patterns: 2026-01-15, 2026/01/15, filename containing date
        var fileName = Path.GetFileNameWithoutExtension(path);
        if (fileName.EndsWith(".jsonl", StringComparison.OrdinalIgnoreCase))
            fileName = Path.GetFileNameWithoutExtension(fileName);

        // Try parsing each segment of the path
        var segments = path.Replace('\\', '/').Split('/');
        foreach (var segment in segments.Reverse())
        {
            if (DateOnly.TryParse(segment, out var date))
                return date;
        }

        // Try extracting date from filename (e.g., AAPL.Trade.2026-01-15)
        var parts = fileName.Split('.');
        foreach (var part in parts)
        {
            if (DateOnly.TryParse(part, out var date))
                return date;
        }

        // Fall back to file modification time
        var fileInfo = new FileInfo(path);
        if (fileInfo.Exists)
        {
            var modDate = DateOnly.FromDateTime(fileInfo.LastWriteTimeUtc);
            var today = DateOnly.FromDateTime(DateTime.UtcNow);
            if (modDate < today)
                return modDate;
        }

        return null;
    }

    private string GetParquetOutputPath(string jsonlPath)
    {
        var relativePath = Path.GetRelativePath(_options.RootPath, jsonlPath);
        var baseName = relativePath
            .Replace(".jsonl.gz", ".parquet")
            .Replace(".jsonl", ".parquet");
        return Path.Combine(_parquetOutputDir, baseName);
    }

    private async Task<long> ConvertJsonlFileToParquetAsync(
        string jsonlPath,
        string parquetPath,
        CancellationToken ct)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(parquetPath)!);

        var schema = await DiscoverSchemaAsync(jsonlPath, ct).ConfigureAwait(false);
        if (schema.Length == 0)
            return 0;

        await using var fileStream = File.Create(parquetPath);
        using var writer = await ParquetWriter.CreateAsync(new ParquetSchema(schema), fileStream, cancellationToken: ct).ConfigureAwait(false);

        long convertedRecords = 0;
        var batch = new List<Dictionary<string, JsonElement>>(DefaultRowGroupSize);

        using var reader = await OpenJsonlReaderAsync(jsonlPath, ct).ConfigureAwait(false);
        while (!reader.EndOfStream && !ct.IsCancellationRequested)
        {
            var line = await reader.ReadLineAsync(ct).ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(line))
                continue;

            if (!TryParseRecord(line, out var record))
                continue;

            batch.Add(record);
            if (batch.Count >= DefaultRowGroupSize)
            {
                await WriteRowGroupAsync(writer, schema, batch, ct).ConfigureAwait(false);
                convertedRecords += batch.Count;
                batch.Clear();
            }
        }

        if (batch.Count > 0)
        {
            await WriteRowGroupAsync(writer, schema, batch, ct).ConfigureAwait(false);
            convertedRecords += batch.Count;
        }

        return convertedRecords;
    }

    private static async Task<DataField[]> DiscoverSchemaAsync(string path, CancellationToken ct)
    {
        var fieldTypes = new Dictionary<string, Type>(StringComparer.Ordinal);

        using var reader = await OpenJsonlReaderAsync(path, ct).ConfigureAwait(false);
        while (!reader.EndOfStream && !ct.IsCancellationRequested)
        {
            var line = await reader.ReadLineAsync(ct).ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(line) || !TryParseRecord(line, out var record))
                continue;

            foreach (var (name, value) in record)
            {
                var inferredType = InferClrType(value);
                if (fieldTypes.TryGetValue(name, out var existingType))
                {
                    fieldTypes[name] = MergeClrTypes(existingType, inferredType);
                }
                else
                {
                    fieldTypes[name] = inferredType;
                }
            }
        }

        return fieldTypes
            .OrderBy(kvp => kvp.Key, StringComparer.Ordinal)
            .Select(kvp => CreateDataField(kvp.Key, kvp.Value))
            .ToArray();
    }

    private static Task<StreamReader> OpenJsonlReaderAsync(string path, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        Stream stream = File.OpenRead(path);
        if (path.EndsWith(".gz", StringComparison.OrdinalIgnoreCase))
            stream = new GZipStream(stream, CompressionMode.Decompress);

        return Task.FromResult(new StreamReader(stream));
    }

    private static bool TryParseRecord(string line, out Dictionary<string, JsonElement> record)
    {
        try
        {
            using var doc = JsonDocument.Parse(line);
            record = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
            foreach (var prop in doc.RootElement.EnumerateObject())
            {
                record[prop.Name] = prop.Value.Clone();
            }

            return true;
        }
        catch
        {
            record = null!;
            return false;
        }
    }

    private static async Task WriteRowGroupAsync(
        ParquetWriter writer,
        IReadOnlyList<DataField> schema,
        IReadOnlyList<Dictionary<string, JsonElement>> records,
        CancellationToken ct)
    {
        if (schema.Count == 0 || records.Count == 0)
            return;

        using var groupWriter = writer.CreateRowGroup();
        foreach (var field in schema)
        {
            await groupWriter.WriteColumnAsync(BuildColumn(field, records), ct).ConfigureAwait(false);
        }
    }

    private static DataColumn BuildColumn(DataField field, IReadOnlyList<Dictionary<string, JsonElement>> records)
    {
        if (field.ClrType == typeof(long))
        {
            var data = records.Select(r =>
                r.TryGetValue(field.Name, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetInt64(out var value)
                    ? value
                    : 0L).ToArray();
            return new DataColumn(field, data);
        }

        if (field.ClrType == typeof(double))
        {
            var data = records.Select(r =>
                r.TryGetValue(field.Name, out var v) && v.ValueKind == JsonValueKind.Number
                    ? v.GetDouble()
                    : 0.0).ToArray();
            return new DataColumn(field, data);
        }

        if (field.ClrType == typeof(bool))
        {
            var data = records.Select(r =>
                r.TryGetValue(field.Name, out var v) &&
                (v.ValueKind == JsonValueKind.True || v.ValueKind == JsonValueKind.False)
                    ? v.GetBoolean()
                    : false).ToArray();
            return new DataColumn(field, data);
        }

        var strings = records.Select(r =>
            r.TryGetValue(field.Name, out var v) ? JsonElementToString(v) : string.Empty).ToArray();
        return new DataColumn(field, strings);
    }

    private static Type InferClrType(JsonElement value)
    {
        return value.ValueKind switch
        {
            JsonValueKind.Number when value.TryGetInt64(out _) => typeof(long),
            JsonValueKind.Number => typeof(double),
            JsonValueKind.True or JsonValueKind.False => typeof(bool),
            _ => typeof(string)
        };
    }

    private static Type MergeClrTypes(Type existing, Type incoming)
    {
        if (existing == incoming)
            return existing;

        if ((existing == typeof(long) && incoming == typeof(double)) ||
            (existing == typeof(double) && incoming == typeof(long)))
        {
            return typeof(double);
        }

        return typeof(string);
    }

    private static DataField CreateDataField(string name, Type clrType)
    {
        if (clrType == typeof(long))
            return new DataField<long>(name);

        if (clrType == typeof(double))
            return new DataField<double>(name);

        if (clrType == typeof(bool))
            return new DataField<bool>(name);

        return new DataField<string>(name);
    }

    private static string JsonElementToString(JsonElement value)
    {
        return value.ValueKind switch
        {
            JsonValueKind.Null => string.Empty,
            JsonValueKind.String => value.GetString() ?? string.Empty,
            _ => value.ToString()
        };
    }
}

/// <summary>
/// Summary of a Parquet conversion batch run.
/// </summary>
public sealed class ConversionSummary
{
    public int FilesConverted { get; set; }
    public long RecordsConverted { get; set; }
    public long BytesSaved { get; set; }
    public int SkippedAlreadyConverted { get; set; }
    public int SkippedEmpty { get; set; }
    public int Errors { get; set; }
}
