using System.IO.Compression;
using System.Text;
using System.Text.Json;
using System.Globalization;

namespace Meridian.Storage.Export;

/// <summary>
/// Simple format export methods (CSV, JSONL, Lean, SQL).
/// Complex formats are in separate partials: Parquet, Xlsx, Arrow.
/// </summary>
public sealed partial class AnalysisExportService
{
    private async Task<List<ExportedFile>> ExportToCsvAsync(
        List<SourceFile> sourceFiles,
        ExportRequest request,
        ExportProfile profile,
        CancellationToken ct)
    {
        var exportedFiles = new List<ExportedFile>();
        var hasFeatures = request.Features is not null && HasAnyFeature(request.Features);

        foreach (var group in GroupBySymbolIfRequired(sourceFiles, profile.SplitBySymbol))
        {
            var symbol = group.Key ?? "combined";
            var outputPath = Path.Combine(
                request.OutputDirectory,
                $"{symbol}_{DateTime.UtcNow:yyyyMMdd}.csv");

            if (hasFeatures)
            {
                // Buffer all records for feature computation (requires full series)
                var allRecords = new List<Dictionary<string, object?>>();
                foreach (var sourceFile in group)
                {
                    await foreach (var record in ReadJsonlRecordsAsync(sourceFile.Path, ct))
                    {
                        allRecords.Add(record);
                    }
                }

                allRecords = EnrichWithFeatures(allRecords, request.Features!);
                var transformedRecords = allRecords
                    .Select(record => TransformRecordForProfile(record, profile))
                    .ToList();
                var columns = GetCsvColumns(transformedRecords, profile);

                await using (var writer = new StreamWriter(outputPath, false, Encoding.UTF8))
                {
                    if (transformedRecords.Count > 0)
                    {
                        await writer.WriteLineAsync(string.Join(",", columns));
                        foreach (var record in transformedRecords)
                        {
                            var values = columns.Select(column => EscapeCsvValue(GetCsvValue(record, column)));
                            await writer.WriteLineAsync(string.Join(",", values));
                        }
                    }
                }

                _log.Information("Enriched {Count} records with features for {Symbol}", transformedRecords.Count, symbol);

                var enrichedInfo = new FileInfo(outputPath);
                exportedFiles.Add(new ExportedFile
                {
                    Path = outputPath,
                    RelativePath = Path.GetFileName(outputPath),
                    Symbol = symbol,
                    Format = "csv",
                    SizeBytes = enrichedInfo.Length,
                    RecordCount = transformedRecords.Count,
                    ChecksumSha256 = await ComputeChecksumAsync(outputPath, ct)
                });
            }
            else
            {
                // Streaming export without features (no memory buffering needed)
                var recordCount = 0L;
                string[]? columns = null;

                await using (var writer = new StreamWriter(outputPath, false, Encoding.UTF8))
                {
                    foreach (var sourceFile in group)
                    {
                        await foreach (var record in ReadJsonlRecordsAsync(sourceFile.Path, ct))
                        {
                            var transformedRecord = TransformRecordForProfile(record, profile);

                            if (columns is null)
                            {
                                columns = GetCsvColumns(new[] { transformedRecord }, profile);
                                await writer.WriteLineAsync(string.Join(",", columns));
                            }

                            var values = columns.Select(column => EscapeCsvValue(GetCsvValue(transformedRecord, column)));
                            await writer.WriteLineAsync(string.Join(",", values));
                            recordCount++;
                        }
                    }
                }

                var fileInfo = new FileInfo(outputPath);
                exportedFiles.Add(new ExportedFile
                {
                    Path = outputPath,
                    RelativePath = Path.GetFileName(outputPath),
                    Symbol = symbol,
                    Format = "csv",
                    SizeBytes = fileInfo.Length,
                    RecordCount = recordCount,
                    ChecksumSha256 = await ComputeChecksumAsync(outputPath, ct)
                });
            }
        }

        return exportedFiles;
    }

    private static Dictionary<string, object?> TransformRecordForProfile(
        Dictionary<string, object?> record,
        ExportProfile profile)
    {
        var exclude = profile.ExcludeFields is { Length: > 0 }
            ? new HashSet<string>(profile.ExcludeFields, StringComparer.OrdinalIgnoreCase)
            : null;
        var transformed = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);

        if (profile.IncludeFields is { Length: > 0 })
        {
            foreach (var field in profile.IncludeFields)
            {
                if (exclude?.Contains(field) == true)
                    continue;

                if (record.TryGetValue(field, out var value))
                    transformed[field] = TransformFieldValue(field, value, profile);
            }

            return transformed;
        }

        foreach (var (key, value) in record)
        {
            if (exclude?.Contains(key) == true)
                continue;

            transformed[key] = TransformFieldValue(key, value, profile);
        }

        return transformed;
    }

    private static string[] GetCsvColumns(
        IEnumerable<Dictionary<string, object?>> records,
        ExportProfile profile)
    {
        if (profile.IncludeFields is { Length: > 0 })
        {
            return profile.IncludeFields
                .Where(field => profile.ExcludeFields?.Contains(field, StringComparer.OrdinalIgnoreCase) != true)
                .ToArray();
        }

        return records.FirstOrDefault()?.Keys.ToArray() ?? Array.Empty<string>();
    }

    private static object? TransformFieldValue(string key, object? value, ExportProfile profile)
    {
        if (!key.Equals("Timestamp", StringComparison.OrdinalIgnoreCase) || value is null)
            return value;

        if (!TryParseTimestamp(value, out var timestamp))
            return value;

        return profile.TimestampSettings.Format switch
        {
            TimestampFormat.Iso8601 => timestamp.UtcDateTime.ToString("o", CultureInfo.InvariantCulture),
            TimestampFormat.UnixSeconds => timestamp.ToUnixTimeSeconds(),
            TimestampFormat.UnixMilliseconds => timestamp.ToUnixTimeMilliseconds(),
            TimestampFormat.UnixNanoseconds => ToUnixNanoseconds(timestamp),
            TimestampFormat.ExcelSerial => timestamp.UtcDateTime.ToOADate(),
            _ => value
        };
    }

    private static bool TryParseTimestamp(object value, out DateTimeOffset timestamp)
    {
        switch (value)
        {
            case DateTimeOffset dto:
                timestamp = dto;
                return true;
            case DateTime dt:
                timestamp = new DateTimeOffset(dt, dt.Kind == DateTimeKind.Unspecified ? TimeSpan.Zero : default).ToUniversalTime();
                return true;
            case string s when DateTimeOffset.TryParse(s, CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var parsed):
                timestamp = parsed;
                return true;
            default:
                timestamp = default;
                return false;
        }
    }

    private static long ToUnixNanoseconds(DateTimeOffset timestamp)
    {
        var delta = timestamp.UtcDateTime - DateTime.UnixEpoch;
        return checked(delta.Ticks * 100L);
    }

    private static string GetCsvValue(Dictionary<string, object?> record, string column)
    {
        if (!record.TryGetValue(column, out var value) || value is null)
            return string.Empty;

        return value switch
        {
            IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
            _ => value.ToString() ?? string.Empty
        };
    }

    private static bool HasAnyFeature(FeatureSettings f) =>
        f.IncludeReturns || f.IncludeRollingStats ||
        f.IncludeTechnicalIndicators || f.IncludeMicrostructure;

    private async Task<List<ExportedFile>> ExportToJsonlAsync(
        List<SourceFile> sourceFiles,
        ExportRequest request,
        ExportProfile profile,
        CancellationToken ct)
    {
        var exportedFiles = new List<ExportedFile>();

        foreach (var sourceFile in sourceFiles)
        {
            var outputFileName = Path.GetFileName(sourceFile.Path);
            if (sourceFile.IsCompressed)
            {
                outputFileName = outputFileName[..^3]; // Remove .gz
            }

            var outputPath = Path.Combine(request.OutputDirectory, outputFileName);

            // Copy and optionally decompress
            if (sourceFile.IsCompressed && profile.Compression.Type == CompressionType.None)
            {
                await using var input = new GZipStream(
                    File.OpenRead(sourceFile.Path), CompressionMode.Decompress);
                await using var output = File.Create(outputPath);
                await input.CopyToAsync(output, ct);
            }
            else
            {
                File.Copy(sourceFile.Path, outputPath, request.OverwriteExisting);
            }

            var recordCount = await CountRecordsAsync(outputPath, ct);
            var fileInfo = new FileInfo(outputPath);

            exportedFiles.Add(new ExportedFile
            {
                Path = outputPath,
                RelativePath = Path.GetFileName(outputPath),
                Symbol = sourceFile.Symbol,
                EventType = sourceFile.EventType,
                Format = "jsonl",
                SizeBytes = fileInfo.Length,
                RecordCount = recordCount,
                ChecksumSha256 = await ComputeChecksumAsync(outputPath, ct)
            });
        }

        return exportedFiles;
    }

    private async Task<List<ExportedFile>> ExportToLeanAsync(
        List<SourceFile> sourceFiles,
        ExportRequest request,
        ExportProfile profile,
        CancellationToken ct)
    {
        // Lean format: data/{security_type}/{market}/{resolution}/{symbol}/{date}_{type}.zip
        var exportedFiles = new List<ExportedFile>();

        var grouped = sourceFiles.GroupBy(f => f.Symbol);

        foreach (var symbolGroup in grouped)
        {
            var symbol = symbolGroup.Key?.ToLowerInvariant() ?? "unknown";
            var symbolDir = Path.Combine(request.OutputDirectory, "equity", "usa", "tick", symbol);
            Directory.CreateDirectory(symbolDir);

            foreach (var sourceFile in symbolGroup)
            {
                var date = sourceFile.Date ?? DateTime.UtcNow;
                var eventType = sourceFile.EventType?.ToLowerInvariant() ?? "trade";
                var zipFileName = $"{date:yyyyMMdd}_{eventType}.zip";
                var zipPath = Path.Combine(symbolDir, zipFileName);

                await using var zipStream = File.Create(zipPath);
                using var archive = new System.IO.Compression.ZipArchive(zipStream, System.IO.Compression.ZipArchiveMode.Create);

                var entry = archive.CreateEntry($"{date:yyyyMMdd}_{eventType}.csv");
                await using var entryStream = entry.Open();
                await using var writer = new StreamWriter(entryStream);

                var recordCount = 0L;
                await foreach (var record in ReadJsonlRecordsAsync(sourceFile.Path, ct))
                {
                    // Convert to Lean format: Timestamp,Price,Size
                    if (record.TryGetValue("Timestamp", out var ts) &&
                        record.TryGetValue("Price", out var price) &&
                        record.TryGetValue("Size", out var size))
                    {
                        await writer.WriteLineAsync($"{ts},{price},{size}");
                        recordCount++;
                    }
                }

                exportedFiles.Add(new ExportedFile
                {
                    Path = zipPath,
                    RelativePath = Path.GetRelativePath(request.OutputDirectory, zipPath),
                    Symbol = symbol,
                    EventType = eventType,
                    Format = "lean",
                    SizeBytes = new FileInfo(zipPath).Length,
                    RecordCount = recordCount
                });
            }
        }

        return exportedFiles;
    }

    private async Task<List<ExportedFile>> ExportToSqlAsync(
        List<SourceFile> sourceFiles,
        ExportRequest request,
        ExportProfile profile,
        CancellationToken ct)
    {
        var exportedFiles = new List<ExportedFile>();

        // Generate DDL
        var ddlPath = Path.Combine(request.OutputDirectory, "create_tables.sql");
        await File.WriteAllTextAsync(ddlPath, GenerateDdl(request.EventTypes), ct);

        // Generate INSERT statements
        foreach (var sourceFile in sourceFiles)
        {
            var tableName = $"market_{sourceFile.EventType?.ToLowerInvariant() ?? "data"}";
            var sqlPath = Path.Combine(
                request.OutputDirectory,
                $"{sourceFile.Symbol}_{sourceFile.EventType}.sql");

            await using var writer = new StreamWriter(sqlPath);
            var recordCount = 0L;

            await foreach (var record in ReadJsonlRecordsAsync(sourceFile.Path, ct))
            {
                var columns = string.Join(", ", record.Keys);
                var values = string.Join(", ", record.Values.Select(v => SqlEscape(v)));
                await writer.WriteLineAsync($"INSERT INTO {tableName} ({columns}) VALUES ({values});");
                recordCount++;
            }

            exportedFiles.Add(new ExportedFile
            {
                Path = sqlPath,
                RelativePath = Path.GetFileName(sqlPath),
                Symbol = sourceFile.Symbol,
                EventType = sourceFile.EventType,
                Format = "sql",
                SizeBytes = new FileInfo(sqlPath).Length,
                RecordCount = recordCount
            });
        }

        return exportedFiles;
    }

    private string GenerateDdl(string[] eventTypes)
    {
        var sb = new StringBuilder();
        sb.AppendLine("-- Market Data Tables");
        sb.AppendLine("-- Generated by Meridian AnalysisExportService");
        sb.AppendLine();

        foreach (var eventType in eventTypes)
        {
            switch (eventType.ToLowerInvariant())
            {
                case "trade":
                    sb.AppendLine(@"
CREATE TABLE IF NOT EXISTS market_trade (
    id SERIAL PRIMARY KEY,
    timestamp TIMESTAMPTZ NOT NULL,
    symbol VARCHAR(20) NOT NULL,
    price DECIMAL(18,8) NOT NULL,
    size BIGINT NOT NULL,
    side VARCHAR(10),
    exchange VARCHAR(20),
    trade_id VARCHAR(50),
    conditions TEXT[],
    created_at TIMESTAMPTZ DEFAULT NOW()
);
CREATE INDEX IF NOT EXISTS idx_trade_symbol_time ON market_trade(symbol, timestamp);
");
                    break;
                case "bbo":
                case "bboquote":
                case "quote":
                    sb.AppendLine(@"
CREATE TABLE IF NOT EXISTS market_quote (
    id SERIAL PRIMARY KEY,
    timestamp TIMESTAMPTZ NOT NULL,
    symbol VARCHAR(20) NOT NULL,
    bid_price DECIMAL(18,8),
    bid_size BIGINT,
    ask_price DECIMAL(18,8),
    ask_size BIGINT,
    exchange VARCHAR(20),
    created_at TIMESTAMPTZ DEFAULT NOW()
);
CREATE INDEX IF NOT EXISTS idx_quote_symbol_time ON market_quote(symbol, timestamp);
");
                    break;
            }
        }

        return sb.ToString();
    }
}
