using Parquet;
using Parquet.Data;
using Parquet.Schema;

namespace Meridian.Storage.Export;

/// <summary>
/// Parquet format export and type conversion helpers.
/// </summary>
public sealed partial class AnalysisExportService
{
    private async Task<List<ExportedFile>> ExportToParquetAsync(
        List<SourceFile> sourceFiles,
        ExportRequest request,
        ExportProfile profile,
        CancellationToken ct)
    {
        var exportedFiles = new List<ExportedFile>();

        foreach (var group in GroupBySymbolIfRequired(sourceFiles, profile.SplitBySymbol))
        {
            var symbol = group.Key ?? "combined";
            var outputPath = Path.Combine(
                request.OutputDirectory,
                $"{symbol}_{DateTime.UtcNow:yyyyMMdd}.parquet");

            // Collect all records first to determine schema
            var records = new List<Dictionary<string, object?>>();
            long recordCount = 0;

            foreach (var sourceFile in group)
            {
                await foreach (var record in ReadJsonlRecordsAsync(sourceFile.Path, ct))
                {
                    records.Add(record);
                    recordCount++;
                }
            }

            if (records.Count is > 0)
                await WriteParquetFileAsync(outputPath, records, ct);
            else
                await WriteEmptyParquetFileAsync(outputPath, ct);

            var fileInfo = new FileInfo(outputPath);
            exportedFiles.Add(new ExportedFile
            {
                Path = outputPath,
                RelativePath = Path.GetFileName(outputPath),
                Symbol = symbol,
                Format = "parquet",
                SizeBytes = fileInfo.Length,
                RecordCount = recordCount,
                ChecksumSha256 = await ComputeChecksumAsync(outputPath, ct)
            });
        }

        return exportedFiles;
    }

    /// <summary>
    /// Writes records to a Parquet file using columnar storage format.
    /// Dynamically builds schema from record keys and writes data in columnar format
    /// for optimal compression and analytics performance.
    /// </summary>
    private async Task WriteParquetFileAsync(
        string path,
        List<Dictionary<string, object?>> records,
        CancellationToken ct)
    {
        if (records.Count == 0)
            return;

        // Build schema from the first record's keys
        var firstRecord = records[0];
        var columns = firstRecord.Keys.ToList();
        var dataFields = new List<DataField>();

        // Infer schema from first record's values
        foreach (var column in columns)
        {
            var value = firstRecord[column];
            var dataField = InferDataField(column, value);
            dataFields.Add(dataField);
        }

        var schema = new ParquetSchema(dataFields);

        // Prepare columnar data
        var columnData = new Dictionary<string, List<object?>>();
        foreach (var column in columns)
        {
            columnData[column] = new List<object?>(records.Count);
        }

        // Extract data into columns
        foreach (var record in records)
        {
            foreach (var column in columns)
            {
                record.TryGetValue(column, out var value);
                columnData[column].Add(value);
            }
        }

        // Write to Parquet file
        await using var fileStream = File.Create(path);
        using var parquetWriter = await ParquetWriter.CreateAsync(schema, fileStream);
        using var rowGroupWriter = parquetWriter.CreateRowGroup();

        for (int i = 0; i < columns.Count; i++)
        {
            var column = columns[i];
            var dataField = dataFields[i];
            var values = columnData[column];
            var dataColumn = CreateDataColumn(dataField, values);
            await rowGroupWriter.WriteColumnAsync(dataColumn);
        }

        _log.Debug("Wrote {RecordCount} records to Parquet file: {Path}", records.Count, path);
    }

    /// <summary>
    /// Creates an empty Parquet file with a minimal schema.
    /// </summary>
    private async Task WriteEmptyParquetFileAsync(string path, CancellationToken ct)
    {
        var schema = new ParquetSchema(
            new DataField<string>("_empty")
        );

        await using var fileStream = File.Create(path);
        using var parquetWriter = await ParquetWriter.CreateAsync(schema, fileStream);
        using var rowGroupWriter = parquetWriter.CreateRowGroup();
        await rowGroupWriter.WriteColumnAsync(new DataColumn(schema.DataFields[0], System.Array.Empty<string>()));
    }

    /// <summary>
    /// Infers the appropriate Parquet DataField type from a sample value.
    /// </summary>
    private static DataField InferDataField(string columnName, object? sampleValue)
    {
        return sampleValue switch
        {
            int => new DataField<int?>(columnName),
            long => new DataField<long?>(columnName),
            float => new DataField<float?>(columnName),
            double => new DataField<double?>(columnName),
            decimal => new DataField<decimal?>(columnName),
            bool => new DataField<bool?>(columnName),
            DateTime => new DataField<DateTimeOffset?>(columnName),
            DateTimeOffset => new DataField<DateTimeOffset?>(columnName),
            _ => new DataField<string>(columnName) // Default to string for unknown types
        };
    }

    /// <summary>
    /// Creates a DataColumn from a list of values, converting them to the appropriate type.
    /// </summary>
    private static DataColumn CreateDataColumn(DataField dataField, List<object?> values) =>
        dataField.ClrType switch
        {
            // Parquet.Net DataField.ClrType returns the non-nullable base type even for
            // nullable fields (e.g. DataField<double?>.ClrType == typeof(double)).
            var t when t == typeof(int) => new DataColumn(dataField, values.Select(ConvertToInt).ToArray()),
            var t when t == typeof(long) => new DataColumn(dataField, values.Select(ConvertToLong).ToArray()),
            var t when t == typeof(float) => new DataColumn(dataField, values.Select(ConvertToFloat).ToArray()),
            var t when t == typeof(double) => new DataColumn(dataField, values.Select(ConvertToDouble).ToArray()),
            var t when t == typeof(decimal) => new DataColumn(dataField, values.Select(ConvertToDecimal).ToArray()),
            var t when t == typeof(bool) => new DataColumn(dataField, values.Select(ConvertToBool).ToArray()),
            var t when t == typeof(DateTimeOffset) => new DataColumn(dataField, values.Select(ConvertToDateTimeOffset).ToArray()),
            _ => new DataColumn(dataField, values.Select(v => v?.ToString() ?? string.Empty).ToArray())
        };

    private static int? ConvertToInt(object? v) => v switch
    {
        int i => i,
        long l => (int)l,
        double d => (int)d,
        null => null,
        _ => Convert.ToInt32(v)
    };

    private static long? ConvertToLong(object? v) => v switch
    {
        long l => l,
        int i => i,
        double d => (long)d,
        null => null,
        _ => Convert.ToInt64(v)
    };

    private static float? ConvertToFloat(object? v) => v switch
    {
        float f => f,
        double d => (float)d,
        int i => i,
        null => null,
        _ => Convert.ToSingle(v)
    };

    private static double? ConvertToDouble(object? v) => v switch
    {
        double d => d,
        float f => f,
        int i => i,
        long l => l,
        null => null,
        _ => Convert.ToDouble(v)
    };

    private static decimal? ConvertToDecimal(object? v) => v switch
    {
        decimal dec => dec,
        double d => Convert.ToDecimal(d),
        float f => Convert.ToDecimal(f),
        int i => i,
        long l => l,
        null => null,
        _ => Convert.ToDecimal(v)
    };

    private static bool? ConvertToBool(object? v) => v switch
    {
        bool b => b,
        null => null,
        _ => Convert.ToBoolean(v)
    };

    private static DateTimeOffset? ConvertToDateTimeOffset(object? v) => v switch
    {
        DateTimeOffset dto => dto,
        DateTime dt => new DateTimeOffset(dt),
        string s when DateTimeOffset.TryParse(s, out var parsed) => parsed,
        _ => null
    };
}
