using System.Globalization;
using Apache.Arrow;
using Apache.Arrow.Ipc;
using Apache.Arrow.Types;

namespace Meridian.Storage.Export;

/// <summary>
/// Apache Arrow IPC (Feather v2) format export and type handling.
/// </summary>
public sealed partial class AnalysisExportService
{
    private async Task<List<ExportedFile>> ExportToArrowAsync(
        List<SourceFile> sourceFiles,
        ExportRequest request,
        ExportProfile profile,
        OutputArtifactSnapshot? outputSnapshot,
        CancellationToken ct)
    {
        var exportedFiles = new List<ExportedFile>();

        foreach (var group in GroupBySymbolIfRequired(sourceFiles, profile.SplitBySymbol))
        {
            var symbol = group.Key ?? "combined";
            var outputPath = Path.Combine(
                request.OutputDirectory,
                $"{symbol}_{DateTime.UtcNow:yyyyMMdd}.arrow");
            EnsureExportArtifactMayBeWritten(outputPath, request.OverwriteExisting);
            var stagedPath = CreateExportArtifactStagingPath(outputPath);

            try
            {
                // Collect all records to determine schema and build columnar data
                var records = new List<Dictionary<string, object?>>();

                foreach (var sourceFile in group)
                {
                    await foreach (var record in ReadJsonlRecordsAsync(sourceFile.Path, ct))
                    {
                        records.Add(record);
                    }
                }

                if (records.Count > 0)
                {
                    await WriteArrowFileAsync(
                        stagedPath,
                        records,
                        profile.TimestampSettings,
                        ct);
                }
                else
                {
                    await WriteEmptyArrowFileAsync(stagedPath, ct);
                }

                var sizeBytes = new FileInfo(stagedPath).Length;
                var checksum = await ComputeChecksumAsync(stagedPath, ct);
                ct.ThrowIfCancellationRequested();
                CommitStagedExportArtifact(
                    stagedPath,
                    outputPath,
                    request.OverwriteExisting,
                    outputSnapshot);

                exportedFiles.Add(new ExportedFile
                {
                    Path = outputPath,
                    RelativePath = Path.GetFileName(outputPath),
                    Symbol = symbol,
                    Format = "arrow",
                    SizeBytes = sizeBytes,
                    RecordCount = records.Count,
                    ChecksumSha256 = checksum
                });
            }
            finally
            {
                DeleteStagedExportArtifact(stagedPath);
            }
        }

        return exportedFiles;
    }

    /// <summary>
    /// Writes records to an Apache Arrow IPC (Feather v2) file.
    /// Uses columnar layout for zero-copy reads in PyArrow, R arrow, Julia, and Spark.
    /// </summary>
    private async Task WriteArrowFileAsync(
        string path,
        List<Dictionary<string, object?>> records,
        TimestampSettings timestampSettings,
        CancellationToken ct)
    {
        if (records.Count == 0)
            return;

        var firstRecord = records[0];
        var columns = firstRecord.Keys.ToList();

        // Build Arrow schema
        var schemaBuilder = new Apache.Arrow.Schema.Builder();
        var arrowFields = new List<Apache.Arrow.Field>();
        foreach (var column in columns)
        {
            var sampleValue = records
                .Select(record => record.TryGetValue(column, out var value) ? value : null)
                .FirstOrDefault(static value => value is not null);
            var field = InferArrowField(column, sampleValue, timestampSettings);
            arrowFields.Add(field);
            schemaBuilder.Field(field);
        }

        var schema = schemaBuilder.Build();

        // Build arrays for each column
        var arrays = new List<IArrowArray>();
        for (int colIdx = 0; colIdx < columns.Count; colIdx++)
        {
            var column = columns[colIdx];
            var fieldType = arrowFields[colIdx].DataType;
            var values = records.Select(r => r.TryGetValue(column, out var v) ? v : null).ToList();
            arrays.Add(BuildArrowArray(fieldType, values));
        }

        var batch = new RecordBatch(schema, arrays.ToArray(), records.Count);

        await using var stream = File.Create(path);
        using var writer = new ArrowFileWriter(stream, schema);
        await writer.WriteRecordBatchAsync(batch, ct);
        await writer.WriteEndAsync(ct);

        _log.Debug("Wrote {RecordCount} records to Arrow file: {Path}", records.Count, path);
    }

    /// <summary>
    /// Creates an empty Arrow IPC file with a minimal schema.
    /// </summary>
    private static async Task WriteEmptyArrowFileAsync(string path, CancellationToken ct)
    {
        var schema = new Apache.Arrow.Schema.Builder()
            .Field(new Apache.Arrow.Field("_empty", StringType.Default, nullable: true))
            .Build();

        await using var stream = File.Create(path);
        using var writer = new ArrowFileWriter(stream, schema);
        await writer.WriteRecordBatchAsync(
            new RecordBatch(schema, new IArrowArray[] { new StringArray.Builder().Build() }, 0), ct);
        await writer.WriteEndAsync(ct);
    }

    /// <summary>
    /// Infers the Arrow field type from a sample value.
    /// </summary>
    private static Apache.Arrow.Field InferArrowField(
        string name,
        object? value,
        TimestampSettings timestampSettings)
    {
        var dataType = IsTimestampValue(name, value)
            ? CreateTimestampType(timestampSettings)
            : value switch
            {
                int => Int32Type.Default as IArrowType,
                long => Int64Type.Default,
                float => FloatType.Default,
                double => DoubleType.Default,
                decimal => DoubleType.Default, // Arrow has no native decimal; use double
                bool => BooleanType.Default,
                _ => StringType.Default
            };

        return new Apache.Arrow.Field(name, dataType, nullable: true);
    }

    /// <summary>
    /// Builds an Arrow array from a list of values based on the target data type.
    /// </summary>
    private static IArrowArray BuildArrowArray(IArrowType dataType, List<object?> values)
    {
        switch (dataType)
        {
            case Int32Type:
                {
                    var builder = new Int32Array.Builder();
                    foreach (var v in values)
                    {
                        if (v is null)
                            builder.AppendNull();
                        else
                            builder.Append(Convert.ToInt32(v));
                    }
                    return builder.Build();
                }
            case Int64Type:
                {
                    var builder = new Int64Array.Builder();
                    foreach (var v in values)
                    {
                        if (v is null)
                            builder.AppendNull();
                        else
                            builder.Append(Convert.ToInt64(v));
                    }
                    return builder.Build();
                }
            case FloatType:
                {
                    var builder = new FloatArray.Builder();
                    foreach (var v in values)
                    {
                        if (v is null)
                            builder.AppendNull();
                        else
                            builder.Append(Convert.ToSingle(v));
                    }
                    return builder.Build();
                }
            case DoubleType:
                {
                    var builder = new DoubleArray.Builder();
                    foreach (var v in values)
                    {
                        if (v is null)
                            builder.AppendNull();
                        else
                            builder.Append(Convert.ToDouble(v));
                    }
                    return builder.Build();
                }
            case BooleanType:
                {
                    var builder = new BooleanArray.Builder();
                    foreach (var v in values)
                    {
                        if (v is null)
                            builder.AppendNull();
                        else
                            builder.Append(Convert.ToBoolean(v));
                    }
                    return builder.Build();
                }
            case TimestampType timestampType:
                {
                    var builder = new TimestampArray.Builder(timestampType);
                    foreach (var v in values)
                    {
                        var timestamp = ConvertToArrowTimestamp(v);
                        if (timestamp is null)
                            builder.AppendNull();
                        else
                            builder.Append(timestamp.Value);
                    }
                    return builder.Build();
                }
            default: // StringType
                {
                    var builder = new StringArray.Builder();
                    foreach (var v in values)
                    {
                        if (v is null)
                            builder.AppendNull();
                        else
                            builder.Append(v.ToString() ?? string.Empty);
                    }
                    return builder.Build();
                }
        }
    }

    private static TimestampType CreateTimestampType(TimestampSettings settings)
    {
        var unit = settings.Format switch
        {
            TimestampFormat.UnixSeconds => TimeUnit.Second,
            TimestampFormat.UnixMilliseconds => TimeUnit.Millisecond,
            TimestampFormat.UnixNanoseconds when settings.IncludeNanoseconds => TimeUnit.Nanosecond,
            TimestampFormat.UnixNanoseconds => TimeUnit.Microsecond,
            _ => TimeUnit.Microsecond
        };

        var timezone = string.IsNullOrWhiteSpace(settings.Timezone)
            ? "UTC"
            : settings.Timezone;
        return new TimestampType(unit, timezone);
    }

    private static bool IsTimestampValue(string columnName, object? value)
    {
        if (!LooksLikeTimestampColumn(columnName))
            return false;

        return ConvertToArrowTimestamp(value) is not null;
    }

    private static bool LooksLikeTimestampColumn(string columnName) =>
        columnName.Contains("timestamp", StringComparison.OrdinalIgnoreCase) ||
        columnName.EndsWith("DateTime", StringComparison.OrdinalIgnoreCase) ||
        columnName.EndsWith("Time", StringComparison.OrdinalIgnoreCase) ||
        columnName.EndsWith("At", StringComparison.OrdinalIgnoreCase);

    private static DateTimeOffset? ConvertToArrowTimestamp(object? value) =>
        value switch
        {
            DateTimeOffset timestamp => timestamp.ToUniversalTime(),
            DateTime timestamp when timestamp.Kind == DateTimeKind.Utc =>
                new DateTimeOffset(timestamp),
            DateTime timestamp when timestamp.Kind == DateTimeKind.Local =>
                new DateTimeOffset(timestamp.ToUniversalTime()),
            DateTime timestamp =>
                new DateTimeOffset(DateTime.SpecifyKind(timestamp, DateTimeKind.Utc)),
            string text when DateTimeOffset.TryParse(
                text,
                CultureInfo.InvariantCulture,
                DateTimeStyles.RoundtripKind,
                out var timestamp) => timestamp.ToUniversalTime(),
            _ => null
        };
}
