using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Meridian.Ui.Services;

/// <summary>
/// Service for browsing and inspecting archived data.
/// Implements Feature #30: Archive Browser and Inspector
/// </summary>
public sealed class ArchiveBrowserService
{
    private readonly ManifestService _manifestService;

    public ArchiveBrowserService(ManifestService manifestService)
    {
        _manifestService = manifestService;
    }

    /// <summary>
    /// Gets the hierarchical tree structure of the archive.
    /// </summary>
    public Task<ArchiveTree> GetArchiveTreeAsync(
        string rootPath,
        CancellationToken ct = default)
    {
        var tree = new ArchiveTree
        {
            RootPath = rootPath,
            GeneratedAt = DateTime.UtcNow
        };

        if (!Directory.Exists(rootPath))
        {
            return Task.FromResult(tree);
        }

        // Build year -> month -> day -> symbol -> type hierarchy
        var years = new List<YearNode>();

        foreach (var file in Directory.GetFiles(rootPath, "*.jsonl*", SearchOption.AllDirectories))
        {
            ct.ThrowIfCancellationRequested();

            var info = ParseFilePath(file, rootPath);
            if (info == null) continue;

            var yearNode = years.FirstOrDefault(y => y.Year == info.Year);
            if (yearNode == null)
            {
                yearNode = new YearNode { Year = info.Year };
                years.Add(yearNode);
            }

            var monthNode = yearNode.Months.FirstOrDefault(m => m.Month == info.Month);
            if (monthNode == null)
            {
                monthNode = new MonthNode { Month = info.Month };
                yearNode.Months.Add(monthNode);
            }

            var dayNode = monthNode.Days.FirstOrDefault(d => d.Day == info.Day);
            if (dayNode == null)
            {
                dayNode = new DayNode { Day = info.Day, Date = new DateOnly(info.Year, info.Month, info.Day) };
                monthNode.Days.Add(dayNode);
            }

            var symbolNode = dayNode.Symbols.FirstOrDefault(s => s.Symbol == info.Symbol);
            if (symbolNode == null)
            {
                symbolNode = new SymbolNode { Symbol = info.Symbol };
                dayNode.Symbols.Add(symbolNode);
            }

            symbolNode.Files.Add(new BrowserArchiveFileInfo
            {
                FullPath = file,
                RelativePath = Path.GetRelativePath(rootPath, file),
                FileName = Path.GetFileName(file),
                EventType = info.EventType,
                Size = new FileInfo(file).Length,
                IsCompressed = file.EndsWith(".gz", StringComparison.OrdinalIgnoreCase),
                LastModified = File.GetLastWriteTimeUtc(file)
            });
        }

        // Sort and calculate statistics
        tree.Years = years.OrderByDescending(y => y.Year).ToList();
        foreach (var year in tree.Years)
        {
            year.Months = year.Months.OrderByDescending(m => m.Month).ToList();
            foreach (var month in year.Months)
            {
                month.Days = month.Days.OrderByDescending(d => d.Day).ToList();
                foreach (var day in month.Days)
                {
                    day.Symbols = day.Symbols.OrderBy(s => s.Symbol).ToList();
                    day.TotalFiles = day.Symbols.Sum(s => s.Files.Count);
                    day.TotalSize = day.Symbols.Sum(s => s.Files.Sum(f => f.Size));
                }
                month.TotalFiles = month.Days.Sum(d => d.TotalFiles);
                month.TotalSize = month.Days.Sum(d => d.TotalSize);
            }
            year.TotalFiles = year.Months.Sum(m => m.TotalFiles);
            year.TotalSize = year.Months.Sum(m => m.TotalSize);
        }

        tree.TotalFiles = tree.Years.Sum(y => y.TotalFiles);
        tree.TotalSize = tree.Years.Sum(y => y.TotalSize);

        return Task.FromResult(tree);
    }

    /// <summary>
    /// Gets detailed metadata for a specific file.
    /// </summary>
    public async Task<FileMetadata> GetFileMetadataAsync(
        string filePath,
        CancellationToken ct = default)
    {
        if (!File.Exists(filePath))
            throw new FileNotFoundException("File not found", filePath);

        var fileInfo = new FileInfo(filePath);
        var isCompressed = filePath.EndsWith(".gz", StringComparison.OrdinalIgnoreCase);

        var metadata = new FileMetadata
        {
            FullPath = filePath,
            FileName = fileInfo.Name,
            Size = fileInfo.Length,
            IsCompressed = isCompressed,
            LastModified = fileInfo.LastWriteTimeUtc,
            CreatedAt = fileInfo.CreationTimeUtc
        };

        // Compute checksum
        metadata.Checksum = await ComputeChecksumAsync(filePath, ct);

        // Count events and get timestamps
        await AnalyzeFileContentsAsync(filePath, metadata, ct);

        return metadata;
    }

    /// <summary>
    /// Gets a preview of file contents (first/last N events).
    /// </summary>
    public async Task<FilePreview> GetFilePreviewAsync(
        string filePath,
        int headCount = 10,
        int tailCount = 10,
        CancellationToken ct = default)
    {
        if (!File.Exists(filePath))
            throw new FileNotFoundException("File not found", filePath);

        var preview = new FilePreview
        {
            FullPath = filePath,
            HeadCount = headCount,
            TailCount = tailCount
        };

        var allLines = await ReadAllLinesAsync(filePath, ct);
        preview.TotalLines = allLines.Count;

        preview.HeadLines = allLines.Take(headCount).ToList();
        preview.TailLines = allLines.Skip(Math.Max(0, allLines.Count - tailCount)).ToList();

        // Parse as JSON for pretty display
        preview.HeadEvents = preview.HeadLines
            .Select(TryParseJson)
            .Where(j => j != null)
            .ToList()!;

        preview.TailEvents = preview.TailLines
            .Select(TryParseJson)
            .Where(j => j != null)
            .ToList()!;

        return preview;
    }

    /// <summary>
    /// Searches within an archive file.
    /// </summary>
    public async Task<List<SearchResult>> SearchInFileAsync(
        string filePath,
        FileSearchQuery query,
        CancellationToken ct = default)
    {
        var results = new List<SearchResult>();
        var lines = await ReadAllLinesAsync(filePath, ct);
        var lineNumber = 0;

        foreach (var line in lines)
        {
            ct.ThrowIfCancellationRequested();
            lineNumber++;

            if (MatchesQuery(line, query))
            {
                results.Add(new SearchResult
                {
                    LineNumber = lineNumber,
                    Content = line,
                    Event = TryParseJson(line)
                });

                if (query.MaxResults.HasValue && results.Count >= query.MaxResults.Value)
                    break;
            }
        }

        return results;
    }

    /// <summary>
    /// Compares two files for differences.
    /// </summary>
    public async Task<FileComparisonResult> CompareFilesAsync(
        string file1,
        string file2,
        CancellationToken ct = default)
    {
        var info1 = new FileInfo(file1);
        var info2 = new FileInfo(file2);

        var result = new FileComparisonResult
        {
            File1 = file1,
            File2 = file2,
            File1Size = info1.Length,
            File2Size = info2.Length,
            SizeDifference = info2.Length - info1.Length
        };

        // Quick check: same size and checksum = identical
        if (info1.Length == info2.Length)
        {
            var hash1 = await ComputeChecksumAsync(file1, ct);
            var hash2 = await ComputeChecksumAsync(file2, ct);
            result.File1Checksum = hash1;
            result.File2Checksum = hash2;
            result.AreIdentical = string.Equals(hash1, hash2, StringComparison.OrdinalIgnoreCase);

            if (result.AreIdentical)
            {
                result.IsPossibleDuplicate = true;
                return result;
            }
        }

        // Detailed comparison: count events and check timestamps
        var lines1 = await ReadAllLinesAsync(file1, ct);
        var lines2 = await ReadAllLinesAsync(file2, ct);

        result.File1EventCount = lines1.Count;
        result.File2EventCount = lines2.Count;
        result.EventCountDifference = lines2.Count - lines1.Count;

        // Find common and unique events
        var set1 = new HashSet<string>(lines1);
        var set2 = new HashSet<string>(lines2);

        result.CommonEvents = set1.Intersect(set2).Count();
        result.UniqueToFile1 = set1.Except(set2).Count();
        result.UniqueToFile2 = set2.Except(set1).Count();

        result.OverlapPercentage = set1.Count > 0 || set2.Count > 0
            ? (double)result.CommonEvents / Math.Max(set1.Count, set2.Count) * 100
            : 100;

        result.IsPossibleDuplicate = result.OverlapPercentage > 95;

        return result;
    }

    /// <summary>
    /// Exports selected files to a destination.
    /// </summary>
    public async Task<ArchiveExportResult> ExportFilesAsync(
        IEnumerable<string> files,
        string destinationPath,
        ExportOptions options,
        IProgress<ArchiveExportProgress>? progress = null,
        CancellationToken ct = default)
    {
        var fileList = files.ToList();
        var exported = 0;
        var totalSize = 0L;

        Directory.CreateDirectory(destinationPath);

        foreach (var file in fileList)
        {
            ct.ThrowIfCancellationRequested();

            var destFile = Path.Combine(destinationPath, Path.GetFileName(file));

            if (options.Decompress && file.EndsWith(".gz"))
            {
                destFile = destFile.Replace(".gz", "");
                await DecompressFileAsync(file, destFile, ct);
            }
            else
            {
                File.Copy(file, destFile, options.Overwrite);
            }

            totalSize += new FileInfo(destFile).Length;
            exported++;

            progress?.Report(new ArchiveExportProgress
            {
                FilesExported = exported,
                TotalFiles = fileList.Count,
                CurrentFile = Path.GetFileName(file),
                PercentComplete = (int)(100.0 * exported / fileList.Count)
            });
        }

        return new ArchiveExportResult
        {
            Success = true,
            FilesExported = exported,
            TotalSize = totalSize,
            DestinationPath = destinationPath
        };
    }

    /// <summary>
    /// Verifies a file's integrity.
    /// </summary>
    public async Task<FileVerificationResult> VerifyFileAsync(
        string filePath,
        string? expectedChecksum = null,
        CancellationToken ct = default)
    {
        var result = new FileVerificationResult
        {
            FilePath = filePath,
            VerifiedAt = DateTime.UtcNow,
            ExpectedChecksum = expectedChecksum
        };

        if (!File.Exists(filePath))
        {
            result.IsValid = false;
            result.Issues.Add("File not found");
            return result;
        }

        try
        {
            // Compute checksum
            result.ActualChecksum = await ComputeChecksumAsync(filePath, ct);

            if (!string.IsNullOrEmpty(expectedChecksum))
            {
                result.ChecksumMatch = string.Equals(
                    result.ActualChecksum, expectedChecksum, StringComparison.OrdinalIgnoreCase);

                if (!result.ChecksumMatch)
                {
                    result.Issues.Add("Checksum mismatch");
                }
            }

            // Verify file is readable
            var lines = await ReadAllLinesAsync(filePath, ct);
            result.EventCount = lines.Count;

            // Verify JSON is parseable
            var parseErrors = 0;
            foreach (var line in lines.Take(100)) // Sample first 100 lines
            {
                if (TryParseJson(line) == null)
                    parseErrors++;
            }

            if (parseErrors > 0)
            {
                result.Issues.Add($"{parseErrors} JSON parse errors in sample");
            }

            result.IsValid = result.Issues.Count == 0 && (expectedChecksum == null || result.ChecksumMatch);
        }
        catch (Exception ex)
        {
            result.IsValid = false;
            result.Issues.Add($"Verification error: {ex.Message}");
        }

        return result;
    }

    private FilePathInfo? ParseFilePath(string filePath, string rootPath)
    {
        try
        {
            var relativePath = Path.GetRelativePath(rootPath, filePath);
            var parts = relativePath.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

            // Try to extract symbol and date
            string? symbol = null;
            string? eventType = null;
            DateOnly? date = null;

            foreach (var part in parts)
            {
                if (part.Length <= 10 && DateOnly.TryParse(part.Split('.')[0], out var parsedDate))
                {
                    date = parsedDate;
                }
                else if (IsEventType(part))
                {
                    eventType = part;
                }
                else if (IsSymbol(part))
                {
                    symbol = part;
                }
            }

            // Try to get date from filename
            var fileName = Path.GetFileNameWithoutExtension(filePath);
            if (fileName.EndsWith(".jsonl")) fileName = Path.GetFileNameWithoutExtension(fileName);
            if (DateOnly.TryParse(fileName, out var fileDate))
            {
                date = fileDate;
            }

            if (date == null)
                return null;

            return new FilePathInfo
            {
                Symbol = symbol ?? "Unknown",
                EventType = eventType ?? DetermineEventType(filePath),
                Year = date.Value.Year,
                Month = date.Value.Month,
                Day = date.Value.Day
            };
        }
        catch
        {
            return null;
        }
    }

    private static bool IsEventType(string s) =>
        s is "Trade" or "Trades" or "Quote" or "Quotes" or "BboQuote"
            or "Depth" or "LOBSnapshot" or "Bar" or "HistoricalBar";

    private static bool IsSymbol(string s) =>
        s.Length >= 1 && s.Length <= 10 && s.All(c => char.IsLetterOrDigit(c) || c == '.' || c == '-');

    private static string DetermineEventType(string filePath)
    {
        var lower = filePath.ToLowerInvariant();
        if (lower.Contains("trade")) return "Trade";
        if (lower.Contains("quote") || lower.Contains("bbo")) return "Quote";
        if (lower.Contains("depth") || lower.Contains("lob")) return "Depth";
        if (lower.Contains("bar")) return "Bar";
        return "Unknown";
    }

    private static async Task<string> ComputeChecksumAsync(string filePath, CancellationToken ct)
    {
        using var stream = File.OpenRead(filePath);
        using var sha256 = SHA256.Create();
        var hash = await sha256.ComputeHashAsync(stream, ct);
        return BitConverter.ToString(hash).Replace("-", "").ToLowerInvariant();
    }

    private async Task AnalyzeFileContentsAsync(string filePath, FileMetadata metadata, CancellationToken ct)
    {
        var lines = await ReadAllLinesAsync(filePath, ct);
        metadata.EventCount = lines.Count;

        if (lines.Count > 0)
        {
            metadata.FirstEventJson = lines[0];
            metadata.LastEventJson = lines[^1];

            if (TryExtractTimestamp(lines[0], out var firstTs))
                metadata.FirstTimestamp = firstTs;
            if (TryExtractTimestamp(lines[^1], out var lastTs))
                metadata.LastTimestamp = lastTs;
        }
    }

    private static async Task<List<string>> ReadAllLinesAsync(string filePath, CancellationToken ct)
    {
        using var stream = File.OpenRead(filePath);
        Stream readStream = filePath.EndsWith(".gz", StringComparison.OrdinalIgnoreCase)
            ? new GZipStream(stream, CompressionMode.Decompress)
            : stream;

        using var reader = new StreamReader(readStream);
        var lines = new List<string>();
        string? line;
        while ((line = await reader.ReadLineAsync(ct)) != null)
        {
            lines.Add(line);
        }
        return lines;
    }

    private static async Task DecompressFileAsync(string source, string dest, CancellationToken ct)
    {
        using var sourceStream = File.OpenRead(source);
        using var gzip = new GZipStream(sourceStream, CompressionMode.Decompress);
        using var destStream = File.Create(dest);
        await gzip.CopyToAsync(destStream, ct);
    }

    private static bool TryExtractTimestamp(string line, out DateTime timestamp)
    {
        timestamp = default;
        try
        {
            var idx = line.IndexOf("\"Timestamp\":", StringComparison.OrdinalIgnoreCase);
            if (idx < 0) idx = line.IndexOf("\"timestamp\":");
            if (idx < 0) return false;

            var start = line.IndexOf('"', idx + 12);
            var end = line.IndexOf('"', start + 1);
            if (start < 0 || end < 0) return false;

            return DateTime.TryParse(line.Substring(start + 1, end - start - 1), out timestamp);
        }
        catch
        {
            return false;
        }
    }

    private static JsonDocument? TryParseJson(string line)
    {
        try
        {
            return JsonDocument.Parse(line);
        }
        catch
        {
            return null;
        }
    }

    private static bool MatchesQuery(string line, FileSearchQuery query)
    {
        if (!string.IsNullOrEmpty(query.TextPattern))
        {
            if (!line.Contains(query.TextPattern, query.CaseSensitive
                ? StringComparison.Ordinal
                : StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
        }

        if (query.StartTime.HasValue || query.EndTime.HasValue)
        {
            if (!TryExtractTimestamp(line, out var timestamp))
                return false;

            if (query.StartTime.HasValue && timestamp < query.StartTime.Value)
                return false;
            if (query.EndTime.HasValue && timestamp > query.EndTime.Value)
                return false;
        }

        return true;
    }

    private sealed record FilePathInfo
    {
        public string Symbol { get; init; } = "";
        public string EventType { get; init; } = "";
        public int Year { get; init; }
        public int Month { get; init; }
        public int Day { get; init; }
    }
}

#region Models

public sealed record ArchiveTree
{
    public string RootPath { get; init; } = "";
    public DateTime GeneratedAt { get; init; }
    public int TotalFiles { get; set; }
    public long TotalSize { get; set; }
    public List<YearNode> Years { get; set; } = new();
}

public sealed record YearNode
{
    public int Year { get; init; }
    public int TotalFiles { get; set; }
    public long TotalSize { get; set; }
    public List<MonthNode> Months { get; set; } = new();
}

public sealed record MonthNode
{
    public int Month { get; init; }
    public int TotalFiles { get; set; }
    public long TotalSize { get; set; }
    public List<DayNode> Days { get; set; } = new();
}

public sealed record DayNode
{
    public int Day { get; init; }
    public DateOnly Date { get; init; }
    public int TotalFiles { get; set; }
    public long TotalSize { get; set; }
    public List<SymbolNode> Symbols { get; set; } = new();
}

public sealed record SymbolNode
{
    public string Symbol { get; init; } = "";
    public List<BrowserArchiveFileInfo> Files { get; init; } = new();
}

public sealed record BrowserArchiveFileInfo
{
    public string FullPath { get; init; } = "";
    public string RelativePath { get; init; } = "";
    public string FileName { get; init; } = "";
    public string EventType { get; init; } = "";
    public long Size { get; init; }
    public bool IsCompressed { get; init; }
    public DateTime LastModified { get; init; }
}

public sealed record FileMetadata
{
    public string FullPath { get; init; } = "";
    public string FileName { get; init; } = "";
    public long Size { get; init; }
    public bool IsCompressed { get; init; }
    public DateTime LastModified { get; init; }
    public DateTime CreatedAt { get; init; }
    public string? Checksum { get; set; }
    public int EventCount { get; set; }
    public DateTime? FirstTimestamp { get; set; }
    public DateTime? LastTimestamp { get; set; }
    public string? FirstEventJson { get; set; }
    public string? LastEventJson { get; set; }
}

public sealed record FilePreview
{
    public string FullPath { get; init; } = "";
    public int HeadCount { get; init; }
    public int TailCount { get; init; }
    public int TotalLines { get; set; }
    public List<string> HeadLines { get; set; } = new();
    public List<string> TailLines { get; set; } = new();
    public List<JsonDocument> HeadEvents { get; set; } = new();
    public List<JsonDocument> TailEvents { get; set; } = new();
}

public sealed record FileSearchQuery
{
    public string? TextPattern { get; init; }
    public bool CaseSensitive { get; init; }
    public DateTime? StartTime { get; init; }
    public DateTime? EndTime { get; init; }
    public int? MaxResults { get; init; }
}

public sealed record SearchResult
{
    public int LineNumber { get; init; }
    public string Content { get; init; } = "";
    public JsonDocument? Event { get; init; }
}

public sealed record FileComparisonResult
{
    public string File1 { get; init; } = "";
    public string File2 { get; init; } = "";
    public long File1Size { get; init; }
    public long File2Size { get; init; }
    public long SizeDifference { get; init; }
    public string? File1Checksum { get; set; }
    public string? File2Checksum { get; set; }
    public bool AreIdentical { get; set; }
    public int File1EventCount { get; set; }
    public int File2EventCount { get; set; }
    public int EventCountDifference { get; set; }
    public int CommonEvents { get; set; }
    public int UniqueToFile1 { get; set; }
    public int UniqueToFile2 { get; set; }
    public double OverlapPercentage { get; set; }
    public bool IsPossibleDuplicate { get; set; }
}

public sealed record ExportOptions
{
    public bool Decompress { get; init; }
    public bool Overwrite { get; init; }
    public bool PreserveStructure { get; init; }
}

public sealed record ArchiveExportProgress
{
    public int FilesExported { get; init; }
    public int TotalFiles { get; init; }
    public string CurrentFile { get; init; } = "";
    public int PercentComplete { get; init; }
}

public sealed record ArchiveExportResult
{
    public bool Success { get; init; }
    public int FilesExported { get; init; }
    public long TotalSize { get; init; }
    public string DestinationPath { get; init; } = "";
    public string? ErrorMessage { get; init; }
}

public sealed record FileVerificationResult
{
    public string FilePath { get; init; } = "";
    public DateTime VerifiedAt { get; init; }
    public bool IsValid { get; set; }
    public string? ExpectedChecksum { get; init; }
    public string? ActualChecksum { get; set; }
    public bool ChecksumMatch { get; set; }
    public int EventCount { get; set; }
    public List<string> Issues { get; init; } = new();
}

#endregion
