using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using Meridian.Application.Logging;
using Meridian.Application.Serialization;
using Meridian.Contracts.Catalog;
using Meridian.Storage.Archival;
using Meridian.Storage.Interfaces;
using Serilog;

namespace Meridian.Storage.Services;

/// <summary>
/// Service for managing the storage catalog and manifest system.
/// Provides comprehensive indexing, integrity verification, and metadata management.
/// </summary>
public sealed class StorageCatalogService : IStorageCatalogService
{
    private const string CatalogDirectoryName = "_catalog";
    private const string ManifestFileName = "manifest.json";
    private const string DirectoryIndexFileName = "_index.json";

    private readonly ILogger _log = LoggingSetup.ForContext<StorageCatalogService>();
    private readonly string _rootPath;
    private readonly string _catalogPath;
    private readonly StorageOptions _options;
    private readonly SemaphoreSlim _catalogLock = new(1, 1);

    private StorageCatalog _catalog;
    private readonly ConcurrentDictionary<string, DirectoryIndex> _directoryIndexCache = new();
    private readonly ConcurrentDictionary<string, IndexedFileEntry> _fileIndex = new();

    public StorageCatalogService(string rootPath, StorageOptions options)
    {
        _rootPath = Path.GetFullPath(rootPath);
        _catalogPath = Path.Combine(_rootPath, CatalogDirectoryName);
        _options = options;
        _catalog = new StorageCatalog();
    }

    public StorageCatalog GetCatalog() => _catalog;

    public async Task InitializeAsync(CancellationToken ct = default)
    {
        await _catalogLock.WaitAsync(ct);
        try
        {
            Directory.CreateDirectory(_catalogPath);
            Directory.CreateDirectory(Path.Combine(_catalogPath, "schemas"));

            var manifestPath = Path.Combine(_catalogPath, ManifestFileName);
            if (File.Exists(manifestPath))
            {
                try
                {
                    var json = await File.ReadAllTextAsync(manifestPath, ct);
                    var catalog = JsonSerializer.Deserialize<StorageCatalog>(json);
                    if (catalog != null)
                    {
                        _catalog = catalog;
                        _log.Information("Loaded catalog with {FileCount} files indexed", _catalog.Statistics.TotalFiles);

                        // Rebuild in-memory file index
                        await LoadFileIndexFromDirectoriesAsync(ct);
                    }
                }
                catch (Exception ex)
                {
                    _log.Warning(ex, "Failed to load catalog manifest, will rebuild");
                }
            }
            else
            {
                _catalog = new StorageCatalog
                {
                    CreatedAt = DateTime.UtcNow,
                    Configuration = new CatalogConfiguration
                    {
                        NamingConvention = _options.NamingConvention.ToString(),
                        DatePartition = _options.DatePartition.ToString(),
                        Compression = _options.CompressionCodec.ToString(),
                        RootPath = _rootPath
                    }
                };
                await SaveCatalogAsync(ct);
                _log.Information("Created new catalog at {Path}", manifestPath);
            }
        }
        finally
        {
            _catalogLock.Release();
        }
    }

    public async Task<CatalogRebuildResult> RebuildCatalogAsync(
        CatalogRebuildOptions? options = null,
        IProgress<CatalogRebuildProgress>? progress = null,
        CancellationToken ct = default)
    {
        options ??= new CatalogRebuildOptions();
        var stopwatch = Stopwatch.StartNew();
        var result = new CatalogRebuildResult();
        var errors = new ConcurrentBag<string>();
        var warnings = new ConcurrentBag<string>();

        await _catalogLock.WaitAsync(ct);
        try
        {
            _log.Information("Starting catalog rebuild for {RootPath}", _rootPath);

            // Clear existing catalog data
            _catalog = new StorageCatalog
            {
                CatalogId = Guid.NewGuid().ToString("N"),
                CreatedAt = DateTime.UtcNow,
                Configuration = new CatalogConfiguration
                {
                    NamingConvention = _options.NamingConvention.ToString(),
                    DatePartition = _options.DatePartition.ToString(),
                    Compression = _options.CompressionCodec.ToString(),
                    RootPath = _rootPath
                }
            };
            _fileIndex.Clear();
            _directoryIndexCache.Clear();

            // Find all data files
            var files = new List<string>();
            foreach (var pattern in options.IncludePatterns)
            {
                files.AddRange(Directory.EnumerateFiles(_rootPath, pattern,
                    options.Recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly));
            }

            // Filter out excluded paths
            files = files
                .Where(f => !options.ExcludePaths.Any(ex => f.Contains(Path.DirectorySeparatorChar + ex + Path.DirectorySeparatorChar) ||
                                                             f.Contains(Path.DirectorySeparatorChar + ex)))
                .ToList();

            progress?.Report(new CatalogRebuildProgress
            {
                Phase = "Scanning",
                TotalFiles = files.Count,
                FilesProcessed = 0
            });

            var filesProcessed = 0;
            var totalEvents = 0L;
            var totalBytes = 0L;
            var directoryIndexes = new ConcurrentDictionary<string, DirectoryIndex>();

            // Process files in parallel
            await Parallel.ForEachAsync(
                files,
                new ParallelOptions { MaxDegreeOfParallelism = options.MaxParallelism, CancellationToken = ct },
                async (filePath, token) =>
                {
                    try
                    {
                        var entry = await ScanFileAsync(filePath, options, token);
                        if (entry != null)
                        {
                            _fileIndex[entry.RelativePath] = entry;

                            // Update directory index
                            var dirPath = Path.GetDirectoryName(entry.RelativePath) ?? string.Empty;
                            var dirIndex = directoryIndexes.GetOrAdd(dirPath, _ => new DirectoryIndex
                            {
                                RelativePath = dirPath,
                                CreatedAt = DateTime.UtcNow
                            });

                            lock (dirIndex)
                            {
                                dirIndex.Files.Add(entry);
                            }

                            Interlocked.Add(ref totalEvents, entry.EventCount);
                            Interlocked.Add(ref totalBytes, entry.SizeBytes);
                        }
                    }
                    catch (Exception ex)
                    {
                        errors.Add($"Error processing {filePath}: {ex.Message}");
                        _log.Warning(ex, "Failed to process file {Path}", filePath);
                    }

                    var processed = Interlocked.Increment(ref filesProcessed);
                    if (processed % 100 == 0 || processed == files.Count)
                    {
                        progress?.Report(new CatalogRebuildProgress
                        {
                            Phase = "Indexing",
                            TotalFiles = files.Count,
                            FilesProcessed = processed,
                            CurrentFile = filePath
                        });
                    }
                });

            // Finalize directory indexes
            foreach (var (dirPath, dirIndex) in directoryIndexes)
            {
                FinalizeDirectoryIndex(dirIndex);
                _directoryIndexCache[dirPath] = dirIndex;

                // Save directory index
                var indexPath = Path.Combine(_rootPath, dirPath, DirectoryIndexFileName);
                await SaveDirectoryIndexAsync(dirIndex, indexPath, ct);
            }

            // Build catalog
            BuildCatalogFromFileIndex();

            // Save catalog
            await SaveCatalogAsync(ct);

            stopwatch.Stop();

            result = new CatalogRebuildResult
            {
                Success = !errors.Any(),
                FilesIndexed = _fileIndex.Count,
                DirectoriesIndexed = directoryIndexes.Count,
                TotalEvents = totalEvents,
                TotalBytes = totalBytes,
                Duration = stopwatch.Elapsed,
                Errors = errors.ToList(),
                Warnings = warnings.ToList()
            };

            _log.Information("Catalog rebuild completed: {Files} files, {Events} events, {Bytes} bytes in {Duration}",
                result.FilesIndexed, result.TotalEvents, result.TotalBytes, result.Duration);

            return result;
        }
        finally
        {
            _catalogLock.Release();
        }
    }

    public async Task UpdateFileEntryAsync(IndexedFileEntry entry, CancellationToken ct = default)
    {
        await _catalogLock.WaitAsync(ct);
        try
        {
            _fileIndex[entry.RelativePath] = entry;

            // Update directory index
            var dirPath = Path.GetDirectoryName(entry.RelativePath) ?? string.Empty;
            if (_directoryIndexCache.TryGetValue(dirPath, out var dirIndex))
            {
                var existingIdx = dirIndex.Files.FindIndex(f => f.RelativePath == entry.RelativePath);
                if (existingIdx >= 0)
                {
                    dirIndex.Files[existingIdx] = entry;
                }
                else
                {
                    dirIndex.Files.Add(entry);
                }

                dirIndex.LastUpdatedAt = DateTime.UtcNow;
                FinalizeDirectoryIndex(dirIndex);

                var indexPath = Path.Combine(_rootPath, dirPath, DirectoryIndexFileName);
                await SaveDirectoryIndexAsync(dirIndex, indexPath, ct);
            }

            // Update symbol entry
            if (!string.IsNullOrEmpty(entry.Symbol))
            {
                UpdateSymbolEntry(entry);
            }

            _catalog.LastUpdatedAt = DateTime.UtcNow;
            _catalog.Statistics.TotalFiles = _fileIndex.Count;
        }
        finally
        {
            _catalogLock.Release();
        }
    }

    public async Task RemoveFileEntryAsync(string relativePath, CancellationToken ct = default)
    {
        await _catalogLock.WaitAsync(ct);
        try
        {
            if (_fileIndex.TryRemove(relativePath, out var entry))
            {
                var dirPath = Path.GetDirectoryName(relativePath) ?? string.Empty;
                if (_directoryIndexCache.TryGetValue(dirPath, out var dirIndex))
                {
                    dirIndex.Files.RemoveAll(f => f.RelativePath == relativePath);
                    dirIndex.LastUpdatedAt = DateTime.UtcNow;
                    FinalizeDirectoryIndex(dirIndex);

                    var indexPath = Path.Combine(_rootPath, dirPath, DirectoryIndexFileName);
                    await SaveDirectoryIndexAsync(dirIndex, indexPath, ct);
                }

                _catalog.LastUpdatedAt = DateTime.UtcNow;
                _catalog.Statistics.TotalFiles = _fileIndex.Count;
            }
        }
        finally
        {
            _catalogLock.Release();
        }
    }

    public async Task<DirectoryIndex?> GetDirectoryIndexAsync(string relativePath, CancellationToken ct = default)
    {
        if (_directoryIndexCache.TryGetValue(relativePath, out var cached))
        {
            return cached;
        }

        var indexPath = Path.Combine(_rootPath, relativePath, DirectoryIndexFileName);
        if (File.Exists(indexPath))
        {
            try
            {
                var json = await File.ReadAllTextAsync(indexPath, ct);
                var index = JsonSerializer.Deserialize<DirectoryIndex>(json);
                if (index != null)
                {
                    _directoryIndexCache[relativePath] = index;
                    return index;
                }
            }
            catch (Exception ex)
            {
                _log.Warning(ex, "Failed to load directory index from {Path}", indexPath);
            }
        }

        return null;
    }

    public async Task UpdateDirectoryIndexAsync(DirectoryIndex index, CancellationToken ct = default)
    {
        _directoryIndexCache[index.RelativePath] = index;
        var indexPath = Path.Combine(_rootPath, index.RelativePath, DirectoryIndexFileName);
        await SaveDirectoryIndexAsync(index, indexPath, ct);
    }

    public async Task<DirectoryScanResult> ScanDirectoryAsync(
        string path,
        bool recursive = false,
        CancellationToken ct = default)
    {
        var stopwatch = Stopwatch.StartNew();
        var result = new DirectoryScanResult { Path = path };
        var warnings = new List<string>();

        try
        {
            var fullPath = Path.IsPathRooted(path) ? path : Path.Combine(_rootPath, path);
            var relativePath = Path.GetRelativePath(_rootPath, fullPath);

            if (!Directory.Exists(fullPath))
            {
                return new DirectoryScanResult
                {
                    Success = false,
                    Path = path,
                    Error = "Directory does not exist"
                };
            }

            var index = new DirectoryIndex
            {
                RelativePath = relativePath,
                CreatedAt = DateTime.UtcNow
            };

            // Scan files in directory
            var patterns = new[] { "*.jsonl", "*.jsonl.gz", "*.parquet" };
            foreach (var pattern in patterns)
            {
                foreach (var filePath in Directory.EnumerateFiles(fullPath, pattern))
                {
                    try
                    {
                        var entry = await ScanFileAsync(filePath, new CatalogRebuildOptions(), ct);
                        if (entry != null)
                        {
                            index.Files.Add(entry);
                            result.FilesScanned++;
                        }
                    }
                    catch (Exception ex)
                    {
                        warnings.Add($"Error scanning {filePath}: {ex.Message}");
                        result.FilesWithErrors++;
                    }
                }
            }

            // Find subdirectories
            if (recursive)
            {
                var subdirs = Directory.GetDirectories(fullPath)
                    .Where(d => !Path.GetFileName(d).StartsWith("_"))
                    .Select(d => Path.GetRelativePath(fullPath, d))
                    .ToArray();
                index.Subdirectories = subdirs;

                foreach (var subdir in subdirs)
                {
                    await ScanDirectoryAsync(Path.Combine(path, subdir), true, ct);
                }
            }

            FinalizeDirectoryIndex(index);
            _directoryIndexCache[relativePath] = index;

            var indexPath = Path.Combine(fullPath, DirectoryIndexFileName);
            await SaveDirectoryIndexAsync(index, indexPath, ct);

            stopwatch.Stop();

            result.Success = true;
            result.Index = index;
            result.DurationMs = stopwatch.ElapsedMilliseconds;
            result.Warnings = warnings;

            _log.Information("Scanned directory {Path}: {Files} files in {Duration}ms",
                path, result.FilesScanned, result.DurationMs);

            return result;
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            return new DirectoryScanResult
            {
                Success = false,
                Path = path,
                Error = ex.Message,
                DurationMs = stopwatch.ElapsedMilliseconds
            };
        }
    }

    public CatalogStatistics GetStatistics() => _catalog.Statistics;

    public async Task<CatalogVerificationResult> VerifyIntegrityAsync(
        CatalogVerificationOptions? options = null,
        IProgress<CatalogVerificationProgress>? progress = null,
        CancellationToken ct = default)
    {
        options ??= new CatalogVerificationOptions();
        var stopwatch = Stopwatch.StartNew();
        var issues = new ConcurrentBag<CatalogIntegrityIssue>();
        var filesVerified = 0;
        var checksumMismatches = 0;
        var missingFiles = 0;
        var countMismatches = 0;

        var files = _fileIndex.Values.ToList();
        progress?.Report(new CatalogVerificationProgress
        {
            FilesVerified = 0,
            TotalFiles = files.Count
        });

        await Parallel.ForEachAsync(
            files,
            new ParallelOptions { MaxDegreeOfParallelism = options.MaxParallelism, CancellationToken = ct },
            async (entry, token) =>
            {
                var fullPath = Path.Combine(_rootPath, entry.RelativePath);

                // Check file exists
                if (options.VerifyFileExists && !File.Exists(fullPath))
                {
                    Interlocked.Increment(ref missingFiles);
                    issues.Add(new CatalogIntegrityIssue
                    {
                        Severity = "Error",
                        Type = "MissingFile",
                        Path = entry.RelativePath,
                        Message = "File not found on disk"
                    });

                    if (options.StopOnFirstError)
                    {
                        throw new OperationCanceledException("Verification stopped on first error");
                    }
                    return;
                }

                // Verify checksum
                if (options.VerifyChecksums && !string.IsNullOrEmpty(entry.ChecksumSha256) && File.Exists(fullPath))
                {
                    try
                    {
                        var actualChecksum = await ComputeFileChecksumAsync(fullPath, token);
                        if (!string.Equals(actualChecksum, entry.ChecksumSha256, StringComparison.OrdinalIgnoreCase))
                        {
                            Interlocked.Increment(ref checksumMismatches);
                            issues.Add(new CatalogIntegrityIssue
                            {
                                Severity = "Error",
                                Type = "ChecksumMismatch",
                                Path = entry.RelativePath,
                                Message = $"Expected {entry.ChecksumSha256}, got {actualChecksum}"
                            });
                        }
                    }
                    catch (Exception ex)
                    {
                        issues.Add(new CatalogIntegrityIssue
                        {
                            Severity = "Warning",
                            Type = "ChecksumError",
                            Path = entry.RelativePath,
                            Message = $"Could not compute checksum: {ex.Message}"
                        });
                    }
                }

                var verified = Interlocked.Increment(ref filesVerified);
                if (verified % 50 == 0 || verified == files.Count)
                {
                    progress?.Report(new CatalogVerificationProgress
                    {
                        FilesVerified = verified,
                        TotalFiles = files.Count,
                        CurrentFile = entry.RelativePath,
                        ErrorsFound = issues.Count
                    });
                }
            });

        stopwatch.Stop();

        // Update catalog integrity info
        _catalog.Integrity = new CatalogIntegrity
        {
            Status = issues.Any(i => i.Severity == "Error") ? "Failed" : "Verified",
            LastVerifiedAt = DateTime.UtcNow,
            FilesVerified = filesVerified,
            ChecksumFailures = checksumMismatches,
            MissingFiles = missingFiles,
            Issues = issues.ToList()
        };

        return new CatalogVerificationResult
        {
            IsValid = !issues.Any(i => i.Severity == "Error"),
            FilesPassed = filesVerified - checksumMismatches - missingFiles - countMismatches,
            ChecksumMismatches = checksumMismatches,
            MissingFiles = missingFiles,
            CountMismatches = countMismatches,
            Duration = stopwatch.Elapsed,
            Issues = issues.ToList()
        };
    }

    public IEnumerable<IndexedFileEntry> GetFilesForSymbol(string symbol)
    {
        return _fileIndex.Values.Where(f =>
            string.Equals(f.Symbol, symbol, StringComparison.OrdinalIgnoreCase));
    }

    public IEnumerable<IndexedFileEntry> GetFilesForDateRange(DateTime start, DateTime end)
    {
        return _fileIndex.Values.Where(f =>
            f.Date.HasValue && f.Date.Value >= start && f.Date.Value <= end);
    }

    public IEnumerable<IndexedFileEntry> GetFilesForEventType(string eventType)
    {
        return _fileIndex.Values.Where(f =>
            string.Equals(f.EventType, eventType, StringComparison.OrdinalIgnoreCase));
    }

    public IEnumerable<IndexedFileEntry> SearchFiles(CatalogSearchCriteria criteria)
    {
        var query = _fileIndex.Values.AsEnumerable();

        if (criteria.Symbols?.Length > 0)
        {
            var symbolSet = new HashSet<string>(criteria.Symbols, StringComparer.OrdinalIgnoreCase);
            query = query.Where(f => f.Symbol != null && symbolSet.Contains(f.Symbol));
        }

        if (criteria.EventTypes?.Length > 0)
        {
            var typeSet = new HashSet<string>(criteria.EventTypes, StringComparer.OrdinalIgnoreCase);
            query = query.Where(f => f.EventType != null && typeSet.Contains(f.EventType));
        }

        if (criteria.Sources?.Length > 0)
        {
            var sourceSet = new HashSet<string>(criteria.Sources, StringComparer.OrdinalIgnoreCase);
            query = query.Where(f => f.Source != null && sourceSet.Contains(f.Source));
        }

        if (criteria.StartDate.HasValue)
        {
            query = query.Where(f => f.Date >= criteria.StartDate);
        }

        if (criteria.EndDate.HasValue)
        {
            query = query.Where(f => f.Date <= criteria.EndDate);
        }

        if (criteria.MinSizeBytes.HasValue)
        {
            query = query.Where(f => f.SizeBytes >= criteria.MinSizeBytes);
        }

        if (criteria.MaxSizeBytes.HasValue)
        {
            query = query.Where(f => f.SizeBytes <= criteria.MaxSizeBytes);
        }

        if (!string.IsNullOrEmpty(criteria.SchemaVersion))
        {
            query = query.Where(f => f.SchemaVersion == criteria.SchemaVersion);
        }

        return query.ToList();
    }

    public async Task SaveCatalogAsync(CancellationToken ct = default)
    {
        var manifestPath = Path.Combine(_catalogPath, ManifestFileName);
        var json = JsonSerializer.Serialize(_catalog, MarketDataJsonContext.PrettyPrintOptions);

        // Compute checksum
        using var sha256 = SHA256.Create();
        var hash = sha256.ComputeHash(System.Text.Encoding.UTF8.GetBytes(json));
        _catalog.Integrity.CatalogChecksum = Convert.ToHexString(hash).ToLowerInvariant();

        // Update the JSON with checksum
        json = JsonSerializer.Serialize(_catalog, MarketDataJsonContext.PrettyPrintOptions);

        await AtomicFileWriter.WriteAsync(manifestPath, json, ct);
        _log.Debug("Saved catalog manifest to {Path}", manifestPath);
    }

    public async Task ExportCatalogAsync(
        string outputPath,
        CatalogExportFormat format = CatalogExportFormat.Json,
        CancellationToken ct = default)
    {
        switch (format)
        {
            case CatalogExportFormat.Json:
                var json = JsonSerializer.Serialize(_catalog, MarketDataJsonContext.PrettyPrintOptions);
                await AtomicFileWriter.WriteAsync(outputPath, json, ct);
                break;

            case CatalogExportFormat.Csv:
                await ExportToCsvAsync(outputPath, ct);
                break;

            default:
                throw new NotSupportedException($"Export format {format} is not supported");
        }

        _log.Information("Exported catalog to {Path} in {Format} format", outputPath, format);
    }

    private async Task<IndexedFileEntry?> ScanFileAsync(string filePath, CatalogRebuildOptions options, CancellationToken ct)
    {
        var fileInfo = new FileInfo(filePath);
        if (!fileInfo.Exists)
            return null;

        var relativePath = Path.GetRelativePath(_rootPath, filePath);
        var fileName = Path.GetFileName(filePath);
        var isCompressed = fileName.EndsWith(".gz", StringComparison.OrdinalIgnoreCase);

        var entry = new IndexedFileEntry
        {
            FileName = fileName,
            RelativePath = relativePath,
            SizeBytes = fileInfo.Length,
            LastModified = fileInfo.LastWriteTimeUtc,
            IsCompressed = isCompressed,
            CompressionType = isCompressed ? "gzip" : null,
            IndexedAt = DateTime.UtcNow
        };

        // Parse metadata from path
        ParseFileMetadata(filePath, entry);

        // Determine format
        if (fileName.EndsWith(".parquet", StringComparison.OrdinalIgnoreCase))
        {
            entry.Format = "parquet";
        }
        else if (fileName.Contains(".jsonl", StringComparison.OrdinalIgnoreCase))
        {
            entry.Format = "jsonl";
        }

        // Compute checksum
        if (options.ComputeChecksums)
        {
            entry.ChecksumSha256 = await ComputeFileChecksumAsync(filePath, ct);
        }

        // Count events and extract sequence info
        if (options.CountEvents && entry.Format == "jsonl")
        {
            await ExtractJsonlMetadataAsync(filePath, entry, options, ct);
        }

        return entry;
    }

    private void ParseFileMetadata(string filePath, IndexedFileEntry entry)
    {
        var relativePath = Path.GetRelativePath(_rootPath, filePath);
        var parts = relativePath.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var fileName = Path.GetFileNameWithoutExtension(filePath);

        // Remove .gz extension if present
        if (fileName.EndsWith(".jsonl", StringComparison.OrdinalIgnoreCase))
        {
            fileName = Path.GetFileNameWithoutExtension(fileName);
        }

        // Try to extract symbol, type, date based on naming convention
        switch (_options.NamingConvention)
        {
            case FileNamingConvention.BySymbol:
                // {symbol}/{type}/{date}.jsonl
                if (parts.Length >= 3)
                {
                    entry.Symbol = parts[^3];
                    entry.EventType = parts[^2];
                    TryParseDate(fileName, out var date);
                    entry.Date = date;
                }
                break;

            case FileNamingConvention.ByDate:
                // {date}/{symbol}/{type}.jsonl
                if (parts.Length >= 3)
                {
                    TryParseDate(parts[^3], out var date);
                    entry.Date = date;
                    entry.Symbol = parts[^2];
                    entry.EventType = fileName;
                }
                break;

            case FileNamingConvention.ByType:
                // {type}/{symbol}/{date}.jsonl
                if (parts.Length >= 3)
                {
                    entry.EventType = parts[^3];
                    entry.Symbol = parts[^2];
                    TryParseDate(fileName, out var date);
                    entry.Date = date;
                }
                break;

            case FileNamingConvention.Flat:
                // {symbol}_{type}_{date}.jsonl
                var flatParts = fileName.Split('_');
                if (flatParts.Length >= 3)
                {
                    entry.Symbol = flatParts[0];
                    entry.EventType = flatParts[1];
                    TryParseDate(string.Join("_", flatParts.Skip(2)), out var date);
                    entry.Date = date;
                }
                break;

            case FileNamingConvention.BySource:
                // {source}/{symbol}/{type}/{date}.jsonl
                if (parts.Length >= 4)
                {
                    entry.Source = parts[^4];
                    entry.Symbol = parts[^3];
                    entry.EventType = parts[^2];
                    TryParseDate(fileName, out var date);
                    entry.Date = date;
                }
                break;

            case FileNamingConvention.Hierarchical:
                // {source}/{asset_class}/{symbol}/{type}/{date}.jsonl
                if (parts.Length >= 5)
                {
                    entry.Source = parts[^5];
                    // asset_class at parts[^4]
                    entry.Symbol = parts[^3];
                    entry.EventType = parts[^2];
                    TryParseDate(fileName, out var date);
                    entry.Date = date;
                }
                break;
        }
    }

    private static bool TryParseDate(string input, out DateTime? date)
    {
        date = null;
        if (string.IsNullOrEmpty(input))
            return false;

        // Try various date formats
        var formats = new[]
        {
            "yyyy-MM-dd",
            "yyyy-MM-dd_HH",
            "yyyy-MM",
            "yyyyMMdd"
        };

        foreach (var format in formats)
        {
            if (DateTime.TryParseExact(input, format, null, System.Globalization.DateTimeStyles.None, out var parsed))
            {
                date = parsed;
                return true;
            }
        }

        return false;
    }

    private async Task ExtractJsonlMetadataAsync(string filePath, IndexedFileEntry entry, CatalogRebuildOptions options, CancellationToken ct)
    {
        try
        {
            long eventCount = 0;
            DateTime? firstTimestamp = null;
            DateTime? lastTimestamp = null;
            long? firstSequence = null;
            long? lastSequence = null;
            long uncompressedSize = 0;

            await using var fileStream = File.OpenRead(filePath);
            Stream readStream = entry.IsCompressed
                ? new GZipStream(fileStream, CompressionMode.Decompress)
                : fileStream;

            using var reader = new StreamReader(readStream);
            string? line;
            while ((line = await reader.ReadLineAsync(ct)) != null)
            {
                if (string.IsNullOrWhiteSpace(line))
                    continue;

                eventCount++;
                uncompressedSize += System.Text.Encoding.UTF8.GetByteCount(line) + 1; // +1 for newline

                // Extract timestamp and sequence from first and last lines
                if (options.ExtractSequenceInfo)
                {
                    try
                    {
                        using var doc = JsonDocument.Parse(line);
                        var root = doc.RootElement;

                        if (root.TryGetProperty("Timestamp", out var tsElem) ||
                            root.TryGetProperty("timestamp", out tsElem))
                        {
                            if (tsElem.TryGetDateTime(out var ts))
                            {
                                firstTimestamp ??= ts;
                                lastTimestamp = ts;
                            }
                        }

                        if (root.TryGetProperty("Sequence", out var seqElem) ||
                            root.TryGetProperty("sequence", out seqElem))
                        {
                            if (seqElem.TryGetInt64(out var seq))
                            {
                                firstSequence ??= seq;
                                lastSequence = seq;
                            }
                        }
                    }
                    catch
                    {
                        // Ignore parse errors for individual lines
                    }
                }
            }

            entry.EventCount = eventCount;
            entry.FirstTimestamp = firstTimestamp;
            entry.LastTimestamp = lastTimestamp;
            entry.FirstSequence = firstSequence;
            entry.LastSequence = lastSequence;
            entry.UncompressedSizeBytes = uncompressedSize;

            if (entry.IsCompressed && entry.SizeBytes > 0 && uncompressedSize > 0)
            {
                entry.Metadata ??= new Dictionary<string, string>();
                entry.Metadata["compressionRatio"] = $"{(double)uncompressedSize / entry.SizeBytes:F2}";
            }
        }
        catch (Exception ex)
        {
            _log.Warning(ex, "Failed to extract JSONL metadata from {Path}", filePath);
        }
    }

    private static async Task<string> ComputeFileChecksumAsync(string filePath, CancellationToken ct)
    {
        using var sha256 = SHA256.Create();
        await using var stream = File.OpenRead(filePath);
        var hash = await sha256.ComputeHashAsync(stream, ct);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private void FinalizeDirectoryIndex(DirectoryIndex index)
    {
        index.LastUpdatedAt = DateTime.UtcNow;

        var stats = new DirectoryStatistics
        {
            FileCount = index.Files.Count,
            TotalEvents = index.Files.Sum(f => f.EventCount),
            TotalBytesCompressed = index.Files.Sum(f => f.SizeBytes),
            TotalBytesRaw = index.Files.Sum(f => f.UncompressedSizeBytes),
            SubdirectoryCount = index.Subdirectories.Length
        };

        // Compute counts by type and symbol
        foreach (var file in index.Files)
        {
            if (!string.IsNullOrEmpty(file.EventType))
            {
                if (!stats.EventCountsByType.ContainsKey(file.EventType))
                    stats.EventCountsByType[file.EventType] = 0;
                stats.EventCountsByType[file.EventType] += file.EventCount;
            }

            if (!string.IsNullOrEmpty(file.Symbol))
            {
                if (!stats.FileCountsBySymbol.ContainsKey(file.Symbol))
                    stats.FileCountsBySymbol[file.Symbol] = 0;
                stats.FileCountsBySymbol[file.Symbol]++;
            }
        }

        index.Statistics = stats;
        index.Symbols = index.Files.Where(f => !string.IsNullOrEmpty(f.Symbol))
            .Select(f => f.Symbol!).Distinct().OrderBy(s => s).ToArray();
        index.EventTypes = index.Files.Where(f => !string.IsNullOrEmpty(f.EventType))
            .Select(f => f.EventType!).Distinct().OrderBy(t => t).ToArray();
        index.Sources = index.Files.Where(f => !string.IsNullOrEmpty(f.Source))
            .Select(f => f.Source!).Distinct().OrderBy(s => s).ToArray();

        // Date range
        var filesWithDates = index.Files.Where(f => f.FirstTimestamp.HasValue || f.Date.HasValue).ToList();
        if (filesWithDates.Any())
        {
            var earliest = filesWithDates.Min(f => f.FirstTimestamp ?? f.Date!.Value);
            var latest = filesWithDates.Max(f => f.LastTimestamp ?? f.Date!.Value);
            index.DateRange = new DirectoryDateRange
            {
                Earliest = earliest,
                Latest = latest,
                DatesWithData = filesWithDates.Where(f => f.Date.HasValue)
                    .Select(f => f.Date!.Value.ToString("yyyy-MM-dd"))
                    .Distinct()
                    .OrderBy(d => d)
                    .ToArray()
            };
        }
    }

    private async Task SaveDirectoryIndexAsync(DirectoryIndex index, string path, CancellationToken ct)
    {
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var json = JsonSerializer.Serialize(index, MarketDataJsonContext.PrettyPrintOptions);
        await AtomicFileWriter.WriteAsync(path, json, ct);
    }

    private void BuildCatalogFromFileIndex()
    {
        _catalog.LastUpdatedAt = DateTime.UtcNow;

        var stats = new CatalogStatistics
        {
            TotalFiles = _fileIndex.Count,
            TotalEvents = _fileIndex.Values.Sum(f => f.EventCount),
            TotalBytesCompressed = _fileIndex.Values.Sum(f => f.SizeBytes),
            TotalBytesRaw = _fileIndex.Values.Sum(f => f.UncompressedSizeBytes)
        };

        if (stats.TotalBytesCompressed > 0)
        {
            stats.CompressionRatio = (float)((double)stats.TotalBytesRaw / stats.TotalBytesCompressed);
        }

        // Build symbol entries
        var symbolGroups = _fileIndex.Values
            .Where(f => !string.IsNullOrEmpty(f.Symbol))
            .GroupBy(f => f.Symbol!, StringComparer.OrdinalIgnoreCase);

        foreach (var group in symbolGroups)
        {
            var files = group.ToList();
            var symbolEntry = new SymbolCatalogEntry
            {
                Symbol = group.Key,
                FileCount = files.Count,
                EventCount = files.Sum(f => f.EventCount),
                TotalBytes = files.Sum(f => f.SizeBytes),
                EventTypes = files.Where(f => !string.IsNullOrEmpty(f.EventType))
                    .Select(f => f.EventType!).Distinct().ToArray(),
                Sources = files.Where(f => !string.IsNullOrEmpty(f.Source))
                    .Select(f => f.Source!).Distinct().ToArray(),
                LastUpdated = files.Max(f => f.LastModified)
            };

            var filesWithDates = files.Where(f => f.FirstTimestamp.HasValue || f.Date.HasValue).ToList();
            if (filesWithDates.Any())
            {
                var earliest = filesWithDates.Min(f => f.FirstTimestamp ?? f.Date!.Value);
                var latest = filesWithDates.Max(f => f.LastTimestamp ?? f.Date!.Value);
                symbolEntry.DateRange = new CatalogDateRange
                {
                    Earliest = earliest,
                    Latest = latest,
                    CalendarDays = (int)(latest - earliest).TotalDays + 1
                };
            }

            // Sequence range
            var filesWithSequence = files.Where(f => f.FirstSequence.HasValue && f.LastSequence.HasValue).ToList();
            if (filesWithSequence.Any())
            {
                symbolEntry.SequenceRange = new SequenceRange
                {
                    FirstSequence = filesWithSequence.Min(f => f.FirstSequence!.Value),
                    LastSequence = filesWithSequence.Max(f => f.LastSequence!.Value),
                    ActualCount = files.Sum(f => f.EventCount),
                    GapsDetected = files.Sum(f => f.SequenceGaps)
                };
                symbolEntry.SequenceRange.ExpectedCount =
                    symbolEntry.SequenceRange.LastSequence - symbolEntry.SequenceRange.FirstSequence + 1;
            }

            _catalog.Symbols[group.Key] = symbolEntry;
        }

        stats.UniqueSymbols = _catalog.Symbols.Count;

        // Event type counts
        foreach (var group in _fileIndex.Values
            .Where(f => !string.IsNullOrEmpty(f.EventType))
            .GroupBy(f => f.EventType!))
        {
            stats.EventTypeCounts[group.Key] = group.Sum(f => f.EventCount);
        }

        // Sources
        _catalog.Sources = _fileIndex.Values
            .Where(f => !string.IsNullOrEmpty(f.Source))
            .Select(f => f.Source!)
            .Distinct()
            .ToArray();
        stats.UniqueSources = _catalog.Sources.Length;

        // Directory indexes
        _catalog.DirectoryIndexes = _directoryIndexCache.Keys.ToArray();

        // Global date range
        var allFilesWithDates = _fileIndex.Values
            .Where(f => f.FirstTimestamp.HasValue || f.Date.HasValue)
            .ToList();
        if (allFilesWithDates.Any())
        {
            var earliest = allFilesWithDates.Min(f => f.FirstTimestamp ?? f.Date!.Value);
            var latest = allFilesWithDates.Max(f => f.LastTimestamp ?? f.Date!.Value);
            _catalog.DateRange = new CatalogDateRange
            {
                Earliest = earliest,
                Latest = latest,
                CalendarDays = (int)(latest - earliest).TotalDays + 1
            };
        }

        _catalog.Statistics = stats;
    }

    private void UpdateSymbolEntry(IndexedFileEntry entry)
    {
        if (string.IsNullOrEmpty(entry.Symbol))
            return;

        if (!_catalog.Symbols.TryGetValue(entry.Symbol, out var symbolEntry))
        {
            symbolEntry = new SymbolCatalogEntry { Symbol = entry.Symbol };
            _catalog.Symbols[entry.Symbol] = symbolEntry;
        }

        symbolEntry.FileCount = _fileIndex.Values.Count(f =>
            string.Equals(f.Symbol, entry.Symbol, StringComparison.OrdinalIgnoreCase));
        symbolEntry.EventCount = _fileIndex.Values
            .Where(f => string.Equals(f.Symbol, entry.Symbol, StringComparison.OrdinalIgnoreCase))
            .Sum(f => f.EventCount);
        symbolEntry.LastUpdated = DateTime.UtcNow;
    }

    private async Task LoadFileIndexFromDirectoriesAsync(CancellationToken ct)
    {
        // Load all directory indexes and populate file index
        foreach (var dirPath in _catalog.DirectoryIndexes)
        {
            var index = await GetDirectoryIndexAsync(dirPath, ct);
            if (index != null)
            {
                foreach (var file in index.Files)
                {
                    _fileIndex[file.RelativePath] = file;
                }
            }
        }
    }

    private async Task ExportToCsvAsync(string outputPath, CancellationToken ct)
    {
        await using var writer = new StreamWriter(outputPath);
        await writer.WriteLineAsync("RelativePath,Symbol,EventType,Source,Date,EventCount,SizeBytes,ChecksumSha256");

        foreach (var file in _fileIndex.Values.OrderBy(f => f.RelativePath))
        {
            var date = file.Date?.ToString("yyyy-MM-dd") ?? "";
            await writer.WriteLineAsync(
                $"\"{file.RelativePath}\",\"{file.Symbol}\",\"{file.EventType}\",\"{file.Source}\",{date},{file.EventCount},{file.SizeBytes},{file.ChecksumSha256}");
        }
    }
}
