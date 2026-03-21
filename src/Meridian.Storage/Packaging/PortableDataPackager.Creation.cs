using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Meridian.Application.Serialization;

namespace Meridian.Storage.Packaging;

/// <summary>
/// Package creation, file scanning, manifest building, and archive writing methods.
/// </summary>
public sealed partial class PortableDataPackager
{
    private Task<List<SourceFileInfo>> ScanSourceFilesAsync(PackageOptions options, CancellationToken ct)
    {
        var files = new List<SourceFileInfo>();

        if (!Directory.Exists(_dataRoot))
        {
            return Task.FromResult(files);
        }

        var patterns = new[] { "*.jsonl", "*.jsonl.gz", "*.jsonl.zst", "*.parquet", "*.csv" };

        foreach (var pattern in patterns)
        {
            foreach (var filePath in Directory.EnumerateFiles(_dataRoot, pattern, SearchOption.AllDirectories))
            {
                ct.ThrowIfCancellationRequested();

                var fileInfo = ParseSourceFile(filePath);
                if (fileInfo == null)
                    continue;

                // Apply filters
                if (!MatchesFilters(fileInfo, options))
                    continue;

                files.Add(fileInfo);
            }
        }

        return Task.FromResult(files.OrderBy(f => f.Symbol).ThenBy(f => f.Date).ToList());
    }

    private SourceFileInfo? ParseSourceFile(string path)
    {
        var fileName = Path.GetFileName(path);
        var relativePath = Path.GetRelativePath(_dataRoot, path);

        var info = new SourceFileInfo
        {
            FullPath = path,
            RelativePath = relativePath,
            FileName = fileName,
            SizeBytes = new FileInfo(path).Length
        };

        // Determine compression
        if (fileName.EndsWith(".gz", StringComparison.OrdinalIgnoreCase))
        {
            info.IsCompressed = true;
            info.CompressionType = "gzip";
        }
        else if (fileName.EndsWith(".zst", StringComparison.OrdinalIgnoreCase))
        {
            info.IsCompressed = true;
            info.CompressionType = "zstd";
        }

        // Parse path components to extract metadata
        var pathParts = relativePath.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

        // Try to extract symbol, event type, date from path/filename
        ExtractMetadataFromPath(info, pathParts, fileName);

        return info;
    }

    private void ExtractMetadataFromPath(SourceFileInfo info, string[] pathParts, string fileName)
    {
        // First, check path parts for symbol and event type (prioritize directory structure)
        // Common patterns: SYMBOL/EventType/date.jsonl or Provider/SYMBOL/EventType/date.jsonl
        for (var i = 0; i < pathParts.Length - 1; i++) // Exclude filename itself
        {
            var part = pathParts[i];

            // Skip common provider/root directories
            if (i == 0 && (part.Equals("live", StringComparison.OrdinalIgnoreCase) ||
                           part.Equals("historical", StringComparison.OrdinalIgnoreCase) ||
                           part.Equals("data", StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            // Check for known event types in path
            if (IsKnownEventType(part))
            {
                info.EventType ??= part;
                continue;
            }

            // If we haven't found symbol yet and this isn't a date, it's likely the symbol
            if (info.Symbol == null && !DateTime.TryParse(part, out _))
            {
                info.Symbol = part.ToUpperInvariant();
            }
        }

        // Remove extensions to get base name
        var baseName = fileName;
        foreach (var ext in new[] { ".gz", ".zst", ".jsonl", ".parquet", ".csv" })
        {
            if (baseName.EndsWith(ext, StringComparison.OrdinalIgnoreCase))
            {
                baseName = baseName[..^ext.Length];
            }
        }

        // Try different naming conventions from filename if we still don't have metadata
        // Pattern: SYMBOL.EventType.Date or SYMBOL_EventType_Date
        var parts = baseName.Split('.', '_');

        if (parts.Length >= 1 && info.Symbol == null)
        {
            info.Symbol = parts[0].ToUpperInvariant();
        }

        if (parts.Length >= 2 && info.EventType == null)
        {
            info.EventType = parts[1];
        }

        // Try to parse date from filename parts
        foreach (var part in parts)
        {
            if (DateTime.TryParse(part, out var date))
            {
                info.Date = date;
                break;
            }

            // Try yyyy-MM-dd format
            if (part.Length == 10 && DateTime.TryParseExact(part, "yyyy-MM-dd", null,
                    System.Globalization.DateTimeStyles.None, out date))
            {
                info.Date = date;
                break;
            }
        }

        // Also check path parts for date if not found yet
        if (info.Date == null)
        {
            foreach (var pathPart in pathParts)
            {
                if (DateTime.TryParse(pathPart, out var date))
                {
                    info.Date = date;
                    break;
                }
            }
        }

        // Determine format
        if (fileName.EndsWith(".jsonl", StringComparison.OrdinalIgnoreCase) ||
            fileName.EndsWith(".jsonl.gz", StringComparison.OrdinalIgnoreCase) ||
            fileName.EndsWith(".jsonl.zst", StringComparison.OrdinalIgnoreCase))
        {
            info.Format = "jsonl";
        }
        else if (fileName.EndsWith(".parquet", StringComparison.OrdinalIgnoreCase))
        {
            info.Format = "parquet";
        }
        else if (fileName.EndsWith(".csv", StringComparison.OrdinalIgnoreCase))
        {
            info.Format = "csv";
        }
    }

    private static bool IsKnownEventType(string value)
    {
        var knownTypes = new[] { "Trade", "BboQuote", "Quote", "L2Snapshot", "OrderBook", "Bar", "Depth" };
        return knownTypes.Any(t => t.Equals(value, StringComparison.OrdinalIgnoreCase));
    }

    private bool MatchesFilters(SourceFileInfo file, PackageOptions options)
    {
        // Symbol filter
        if (options.Symbols != null && options.Symbols.Length > 0)
        {
            if (file.Symbol == null || !options.Symbols.Contains(file.Symbol, StringComparer.OrdinalIgnoreCase))
            {
                return false;
            }
        }

        // Event type filter
        if (options.EventTypes != null && options.EventTypes.Length > 0)
        {
            if (file.EventType == null || !options.EventTypes.Contains(file.EventType, StringComparer.OrdinalIgnoreCase))
            {
                return false;
            }
        }

        // Date filter
        if (options.StartDate.HasValue && file.Date.HasValue)
        {
            if (file.Date.Value < options.StartDate.Value.Date)
            {
                return false;
            }
        }

        if (options.EndDate.HasValue && file.Date.HasValue)
        {
            if (file.Date.Value > options.EndDate.Value.Date)
            {
                return false;
            }
        }

        return true;
    }

    private async Task<PackageManifest> BuildManifestAsync(
        List<SourceFileInfo> files,
        PackageOptions options,
        CancellationToken ct)
    {
        var manifest = new PackageManifest
        {
            Name = options.Name,
            Description = options.Description,
            CreatedAt = DateTime.UtcNow,
            CreatedBy = Environment.MachineName,
            Format = options.Format.ToString(),
            Layout = options.InternalLayout.ToString(),
            Tags = options.Tags,
            CustomMetadata = options.CustomMetadata,
            Encrypted = !string.IsNullOrEmpty(options.Password)
        };

        var fileEntries = new List<PackageFileEntry>();
        var symbols = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var eventTypes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var sources = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        DateTime? minDate = null;
        DateTime? maxDate = null;
        long totalEvents = 0;
        long totalUncompressedSize = 0;

        foreach (var file in files)
        {
            ct.ThrowIfCancellationRequested();

            var checksum = options.VerifyChecksums
                ? await ComputeFileChecksumAsync(file.FullPath, ct)
                : string.Empty;

            var eventCount = await EstimateEventCountAsync(file.FullPath, ct);

            var entry = new PackageFileEntry
            {
                Path = GetPackageInternalPath(file, options.InternalLayout),
                Symbol = file.Symbol,
                EventType = file.EventType,
                Date = file.Date,
                Source = file.Source,
                Format = file.Format ?? "jsonl",
                Compressed = file.IsCompressed,
                CompressionType = file.CompressionType,
                SizeBytes = file.SizeBytes,
                UncompressedSizeBytes = file.IsCompressed ? file.SizeBytes * 5 : file.SizeBytes, // Estimate
                EventCount = eventCount,
                ChecksumSha256 = checksum
            };

            fileEntries.Add(entry);

            if (!string.IsNullOrEmpty(file.Symbol))
                symbols.Add(file.Symbol);
            if (!string.IsNullOrEmpty(file.EventType))
                eventTypes.Add(file.EventType);
            if (!string.IsNullOrEmpty(file.Source))
                sources.Add(file.Source);

            if (file.Date.HasValue)
            {
                minDate = minDate == null ? file.Date : (file.Date < minDate ? file.Date : minDate);
                maxDate = maxDate == null ? file.Date : (file.Date > maxDate ? file.Date : maxDate);
            }

            totalEvents += eventCount;
            totalUncompressedSize += entry.UncompressedSizeBytes;
        }

        manifest.Files = fileEntries.ToArray();
        manifest.TotalFiles = fileEntries.Count;
        manifest.TotalEvents = totalEvents;
        manifest.UncompressedSizeBytes = totalUncompressedSize;
        manifest.Symbols = symbols.OrderBy(s => s).ToArray();
        manifest.EventTypes = eventTypes.OrderBy(t => t).ToArray();
        manifest.Sources = sources.OrderBy(s => s).ToArray();

        if (minDate.HasValue && maxDate.HasValue)
        {
            manifest.DateRange = new PackageDateRange
            {
                Start = minDate.Value,
                End = maxDate.Value,
                CalendarDays = (maxDate.Value - minDate.Value).Days + 1,
                TradingDays = CountTradingDays(minDate.Value, maxDate.Value)
            };
        }

        // Build schemas if requested
        if (options.IncludeSchemas)
        {
            manifest.Schemas = BuildSchemas(eventTypes);
        }

        return manifest;
    }

    private string GetPackageInternalPath(SourceFileInfo file, PackageLayout layout)
    {
        var fileName = file.FileName;
        var symbol = file.Symbol ?? "UNKNOWN";
        var eventType = file.EventType ?? "Unknown";
        var date = file.Date?.ToString("yyyy-MM-dd") ?? "undated";

        return layout switch
        {
            PackageLayout.ByDate => $"{DataDirectory}/{date}/{symbol}/{eventType}/{fileName}",
            PackageLayout.BySymbol => $"{DataDirectory}/{symbol}/{eventType}/{date}/{fileName}",
            PackageLayout.ByType => $"{DataDirectory}/{eventType}/{symbol}/{date}/{fileName}",
            PackageLayout.Flat => $"{DataDirectory}/{fileName}",
            _ => $"{DataDirectory}/{date}/{symbol}/{eventType}/{fileName}"
        };
    }

    private async Task<long> EstimateEventCountAsync(string path, CancellationToken ct)
    {
        try
        {
            // For JSONL files, count lines
            if (path.Contains(".jsonl", StringComparison.OrdinalIgnoreCase))
            {
                long count = 0;
                Stream stream = File.OpenRead(path);

                if (path.EndsWith(".gz", StringComparison.OrdinalIgnoreCase))
                {
                    stream = new GZipStream(stream, CompressionMode.Decompress);
                }

                await using (stream)
                using (var reader = new StreamReader(stream))
                {
                    while (await reader.ReadLineAsync(ct) != null)
                    {
                        count++;
                        if (count > 100000) // Sample first 100k lines for large files
                        {
                            // Estimate based on file size
                            var fileSize = new FileInfo(path).Length;
                            var bytesRead = stream.Position;
                            if (bytesRead > 0)
                            {
                                count = (long)(count * (double)fileSize / bytesRead);
                            }
                            break;
                        }
                    }
                }

                return count;
            }

            // For other formats, estimate based on file size
            var size = new FileInfo(path).Length;
            return size / 100; // Rough estimate: 100 bytes per record
        }
        catch
        {
            return 0;
        }
    }

    private async Task CreatePackageFileAsync(
        string packagePath,
        List<SourceFileInfo> sourceFiles,
        PackageManifest manifest,
        PackageOptions options,
        CancellationToken ct)
    {
        var compressionLevel = options.CompressionLevel switch
        {
            PackageCompressionLevel.None => CompressionLevel.NoCompression,
            PackageCompressionLevel.Fast => CompressionLevel.Fastest,
            PackageCompressionLevel.Balanced => CompressionLevel.Optimal,
            PackageCompressionLevel.Maximum => CompressionLevel.SmallestSize,
            _ => CompressionLevel.Optimal
        };

        await using var packageStream = File.Create(packagePath);

        if (options.Format == PackageFormat.Zip)
        {
            using var archive = new ZipArchive(packageStream, ZipArchiveMode.Create, leaveOpen: true);

            // Add manifest
            await AddFileToZipAsync(archive, ManifestFileName,
                JsonSerializer.Serialize(manifest, MarketDataJsonContext.PrettyPrintOptions),
                compressionLevel, ct);

            // Add data files
            var processedCount = 0;
            foreach (var file in sourceFiles)
            {
                ct.ThrowIfCancellationRequested();

                var internalPath = GetPackageInternalPath(file, options.InternalLayout);
                await AddFileToZipFromPathAsync(archive, internalPath, file.FullPath, compressionLevel, ct);

                processedCount++;
                ReportProgress(manifest.PackageId, PackageStage.Writing, processedCount, sourceFiles.Count,
                    0, manifest.UncompressedSizeBytes);
            }

            // Add supplementary files
            await AddSupplementaryFilesAsync(archive, manifest, options, compressionLevel, ct);
        }
        else if (options.Format == PackageFormat.TarGz)
        {
            // For tar.gz, we'll create a simple implementation
            // In production, you'd use SharpZipLib or similar
            await CreateTarGzPackageAsync(packageStream, sourceFiles, manifest, options, compressionLevel, ct);
        }
    }

    private async Task AddFileToZipAsync(
        ZipArchive archive,
        string entryName,
        string content,
        CompressionLevel level,
        CancellationToken ct)
    {
        var entry = archive.CreateEntry(entryName, level);
        await using var stream = entry.Open();
        await using var writer = new StreamWriter(stream, Encoding.UTF8);
        await writer.WriteAsync(content);
    }

    private async Task AddFileToZipFromPathAsync(
        ZipArchive archive,
        string entryName,
        string sourcePath,
        CompressionLevel level,
        CancellationToken ct)
    {
        var entry = archive.CreateEntry(entryName, level);
        await using var entryStream = entry.Open();
        await using var sourceStream = File.OpenRead(sourcePath);
        await sourceStream.CopyToAsync(entryStream, ct);
    }

    private async Task AddSupplementaryFilesAsync(
        ZipArchive archive,
        PackageManifest manifest,
        PackageOptions options,
        CompressionLevel level,
        CancellationToken ct)
    {
        var supplementaryFiles = new List<SupplementaryFileInfo>();

        // Add README
        var readme = GenerateReadme(manifest, options);
        await AddFileToZipAsync(archive, ReadmeFileName, readme, level, ct);
        supplementaryFiles.Add(new SupplementaryFileInfo
        {
            Path = ReadmeFileName,
            Type = "readme",
            Description = "Package documentation"
        });

        // Add data dictionary if requested
        if (options.IncludeDataDictionary)
        {
            var dictionary = GenerateDataDictionary(manifest);
            var dictPath = $"{MetadataDirectory}/{DataDictionaryFileName}";
            await AddFileToZipAsync(archive, dictPath, dictionary, level, ct);
            supplementaryFiles.Add(new SupplementaryFileInfo
            {
                Path = dictPath,
                Type = "data_dictionary",
                Description = "Field definitions and data types"
            });
        }

        // Add loader scripts if requested
        if (options.IncludeLoaderScripts)
        {
            var pythonScript = GeneratePythonLoader(manifest);
            var pythonPath = $"{ScriptsDirectory}/load_data.py";
            await AddFileToZipAsync(archive, pythonPath, pythonScript, level, ct);
            supplementaryFiles.Add(new SupplementaryFileInfo
            {
                Path = pythonPath,
                Type = "loader_script",
                Description = "Python loader script"
            });

            var rScript = GenerateRLoader(manifest);
            var rPath = $"{ScriptsDirectory}/load_data.R";
            await AddFileToZipAsync(archive, rPath, rScript, level, ct);
            supplementaryFiles.Add(new SupplementaryFileInfo
            {
                Path = rPath,
                Type = "loader_script",
                Description = "R loader script"
            });
        }

        // Add import scripts if requested
        if (options.GenerateImportScripts && options.ImportScriptTargets != null)
        {
            foreach (var target in options.ImportScriptTargets)
            {
                var script = GenerateImportScript(manifest, target);
                var scriptPath = $"{ScriptsDirectory}/import_{target.ToString().ToLowerInvariant()}.sql";
                await AddFileToZipAsync(archive, scriptPath, script, level, ct);
                supplementaryFiles.Add(new SupplementaryFileInfo
                {
                    Path = scriptPath,
                    Type = "import_script",
                    Description = $"{target} import script"
                });
            }
        }

        manifest.SupplementaryFiles = supplementaryFiles.ToArray();
    }

    private async Task CreateTarGzPackageAsync(
        Stream outputStream,
        List<SourceFileInfo> sourceFiles,
        PackageManifest manifest,
        PackageOptions options,
        CompressionLevel level,
        CancellationToken ct)
    {
        // Simplified tar.gz implementation using GZipStream
        // For a complete implementation, use a proper tar library
        await using var gzipStream = new GZipStream(outputStream, level, leaveOpen: true);
        await using var writer = new StreamWriter(gzipStream);

        // Write manifest as first entry marker
        await writer.WriteLineAsync($"__MANIFEST__:{ManifestFileName}");
        await writer.WriteLineAsync(JsonSerializer.Serialize(manifest, MarketDataJsonContext.PrettyPrintOptions));
        await writer.WriteLineAsync("__END_MANIFEST__");

        // Write file entries
        foreach (var file in sourceFiles)
        {
            ct.ThrowIfCancellationRequested();

            var internalPath = GetPackageInternalPath(file, options.InternalLayout);
            await writer.WriteLineAsync($"__FILE__:{internalPath}:{file.SizeBytes}");

            await using var fileStream = File.OpenRead(file.FullPath);
            using var reader = new StreamReader(fileStream);
            var content = await reader.ReadToEndAsync(ct);
            await writer.WriteAsync(content);
            await writer.WriteLineAsync();
            await writer.WriteLineAsync("__END_FILE__");
        }
    }

    private async Task UpdateManifestInPackageAsync(
        string packagePath,
        PackageManifest manifest,
        PackageFormat format,
        CancellationToken ct)
    {
        if (format != PackageFormat.Zip)
            return;

        // Reopen and update manifest
        using var stream = new FileStream(packagePath, FileMode.Open, FileAccess.ReadWrite);
        using var archive = new ZipArchive(stream, ZipArchiveMode.Update);

        var manifestEntry = archive.GetEntry(ManifestFileName);
        if (manifestEntry != null)
        {
            manifestEntry.Delete();
        }

        var newEntry = archive.CreateEntry(ManifestFileName, CompressionLevel.Optimal);
        await using var entryStream = newEntry.Open();
        await JsonSerializer.SerializeAsync(entryStream, manifest,
            MarketDataJsonContext.PrettyPrintOptions, ct);
    }

}
