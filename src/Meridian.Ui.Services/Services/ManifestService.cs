using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading.Tasks;

namespace Meridian.Ui.Services;

/// <summary>
/// Service for generating and managing data manifests (#56 - P0 Critical).
/// Creates comprehensive manifest files for collection sessions and archive packages.
/// </summary>
public sealed class ManifestService
{
    private static readonly Lazy<ManifestService> _instance = new(() => new ManifestService());
    private readonly ConfigService _configService;
    private readonly string _catalogPath;

    public static ManifestService Instance => _instance.Value;

    private ManifestService()
    {
        _configService = new ConfigService();
        _catalogPath = Path.Combine(AppContext.BaseDirectory, "_catalog");
    }

    /// <summary>
    /// Generates a manifest for a collection session.
    /// </summary>
    public async Task<(DataManifest, string)> GenerateManifestForSessionAsync(CollectionSession session, CancellationToken ct = default)
    {
        var config = await _configService.LoadConfigAsync();
        var dataRoot = config?.DataRoot ?? "data";
        var basePath = Path.IsPathRooted(dataRoot)
            ? dataRoot
            : Path.Combine(AppContext.BaseDirectory, dataRoot);

        var manifest = new DataManifest
        {
            SessionId = session.Id,
            SessionName = session.Name,
            Symbols = session.Symbols,
            GeneratedAt = DateTime.UtcNow
        };

        // Collect file entries
        var fileEntries = new List<ManifestFileEntry>();

        if (Directory.Exists(basePath))
        {
            await Task.Run(() =>
            {
                var files = Directory.GetFiles(basePath, "*.*", SearchOption.AllDirectories)
                    .Where(f => f.EndsWith(".jsonl") || f.EndsWith(".jsonl.gz") || f.EndsWith(".parquet"));

                foreach (var file in files)
                {
                    var fileInfo = new FileInfo(file);

                    // Filter by session date range if available
                    if (session.StartedAt.HasValue && fileInfo.LastWriteTimeUtc < session.StartedAt.Value)
                    {
                        continue;
                    }

                    var entry = CreateManifestEntry(file, basePath);
                    fileEntries.Add(entry);
                }
            });
        }

        manifest.Files = fileEntries.ToArray();
        manifest.TotalFiles = fileEntries.Count;
        manifest.TotalEvents = fileEntries.Sum(f => f.EventCount);
        manifest.TotalBytesRaw = fileEntries.Sum(f => f.SizeBytes);
        manifest.TotalBytesCompressed = fileEntries.Where(f => f.CompressedSizeBytes.HasValue).Sum(f => f.CompressedSizeBytes!.Value);

        // Set date range
        var fileDates = fileEntries.Where(f => f.Date.HasValue).Select(f => f.Date!.Value).ToList();
        if (fileDates.Any())
        {
            manifest.DateRange = new DateRangeInfo
            {
                Start = fileDates.Min(),
                End = fileDates.Max(),
                TradingDays = CalculateTradingDays(fileDates.Min(), fileDates.Max())
            };
        }

        // Calculate quality metrics
        manifest.QualityMetrics = CalculateQualityMetrics(fileEntries, session);

        // Save manifest
        var manifestPath = await SaveManifestAsync(manifest, session.Name);

        ManifestGenerated?.Invoke(this, new ManifestEventArgs { Manifest = manifest, Path = manifestPath });

        return (manifest, manifestPath);
    }

    /// <summary>
    /// Generates a manifest for a date range.
    /// </summary>
    public async Task<(DataManifest, string)> GenerateManifestForDateRangeAsync(DateTime startDate, DateTime endDate, string[]? symbols = null, CancellationToken ct = default)
    {
        var config = await _configService.LoadConfigAsync();
        var dataRoot = config?.DataRoot ?? "data";
        var basePath = Path.IsPathRooted(dataRoot)
            ? dataRoot
            : Path.Combine(AppContext.BaseDirectory, dataRoot);

        var manifest = new DataManifest
        {
            Symbols = symbols ?? Array.Empty<string>(),
            GeneratedAt = DateTime.UtcNow,
            DateRange = new DateRangeInfo
            {
                Start = startDate,
                End = endDate,
                TradingDays = CalculateTradingDays(startDate, endDate)
            }
        };

        var fileEntries = new List<ManifestFileEntry>();

        if (Directory.Exists(basePath))
        {
            await Task.Run(() =>
            {
                var files = Directory.GetFiles(basePath, "*.*", SearchOption.AllDirectories)
                    .Where(f => f.EndsWith(".jsonl") || f.EndsWith(".jsonl.gz") || f.EndsWith(".parquet"));

                foreach (var file in files)
                {
                    var fileInfo = new FileInfo(file);

                    // Filter by date range
                    if (fileInfo.LastWriteTimeUtc < startDate || fileInfo.LastWriteTimeUtc > endDate.AddDays(1))
                    {
                        continue;
                    }

                    var entry = CreateManifestEntry(file, basePath);

                    // Filter by symbols if specified
                    if (symbols != null && symbols.Length > 0 && !string.IsNullOrEmpty(entry.Symbol))
                    {
                        if (!symbols.Contains(entry.Symbol, StringComparer.OrdinalIgnoreCase))
                        {
                            continue;
                        }
                    }

                    fileEntries.Add(entry);
                }
            });
        }

        manifest.Files = fileEntries.ToArray();
        manifest.TotalFiles = fileEntries.Count;
        manifest.TotalEvents = fileEntries.Sum(f => f.EventCount);
        manifest.TotalBytesRaw = fileEntries.Sum(f => f.SizeBytes);
        manifest.TotalBytesCompressed = fileEntries.Where(f => f.CompressedSizeBytes.HasValue).Sum(f => f.CompressedSizeBytes!.Value);
        manifest.Symbols = fileEntries.Where(f => !string.IsNullOrEmpty(f.Symbol)).Select(f => f.Symbol!).Distinct().ToArray();

        // Calculate quality metrics
        manifest.QualityMetrics = CalculateQualityMetrics(fileEntries, null);

        // Save manifest
        var name = $"manifest_{startDate:yyyy-MM-dd}_to_{endDate:yyyy-MM-dd}";
        var manifestPath = await SaveManifestAsync(manifest, name);

        ManifestGenerated?.Invoke(this, new ManifestEventArgs { Manifest = manifest, Path = manifestPath });

        return (manifest, manifestPath);
    }

    /// <summary>
    /// Loads an existing manifest from file.
    /// </summary>
    public async Task<DataManifest?> LoadManifestAsync(string path, CancellationToken ct = default)
    {
        try
        {
            if (!File.Exists(path))
            {
                return null;
            }

            var json = await File.ReadAllTextAsync(path);
            return JsonSerializer.Deserialize<DataManifest>(json, DesktopJsonOptions.Compact);
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>
    /// Verifies a manifest against actual files.
    /// </summary>
    public async Task<ManifestVerificationResult> VerifyManifestAsync(DataManifest manifest, IProgress<double>? progress = null, CancellationToken ct = default)
    {
        var result = new ManifestVerificationResult
        {
            ManifestId = manifest.SessionId ?? Guid.NewGuid().ToString(),
            StartedAt = DateTime.UtcNow
        };

        var totalFiles = manifest.Files.Length;
        var processedFiles = 0;

        foreach (var entry in manifest.Files)
        {
            try
            {
                if (!File.Exists(entry.Path))
                {
                    result.MissingFiles.Add(entry.Path);
                    result.FailedFiles++;
                }
                else
                {
                    // Verify checksum
                    var actualChecksum = await ComputeFileChecksumAsync(entry.Path);
                    if (actualChecksum != entry.ChecksumSha256)
                    {
                        result.ChecksumMismatches.Add(new ChecksumMismatch
                        {
                            FilePath = entry.Path,
                            ExpectedChecksum = entry.ChecksumSha256,
                            ActualChecksum = actualChecksum
                        });
                        result.FailedFiles++;
                    }
                    else
                    {
                        result.VerifiedFiles++;
                        entry.VerificationStatus = "Verified";
                        entry.LastVerifiedAt = DateTime.UtcNow;
                    }
                }

                processedFiles++;
                progress?.Report((double)processedFiles / totalFiles * 100);
            }
            catch (Exception ex)
            {
                result.Errors.Add($"{entry.Path}: {ex.Message}");
                result.FailedFiles++;
            }
        }

        result.CompletedAt = DateTime.UtcNow;
        result.IsValid = result.FailedFiles == 0;

        if (result.IsValid)
        {
            manifest.VerificationStatus = "Verified";
            manifest.LastVerifiedAt = DateTime.UtcNow;
        }
        else
        {
            manifest.VerificationStatus = "Failed";
        }

        ManifestVerified?.Invoke(this, new ManifestVerificationEventArgs { Result = result });

        return result;
    }

    /// <summary>
    /// Gets all manifests in the catalog.
    /// </summary>
    public async Task<DataManifest[]> GetAllManifestsAsync(CancellationToken ct = default)
    {
        var manifests = new List<DataManifest>();

        EnsureCatalogExists();

        var manifestFiles = Directory.GetFiles(_catalogPath, "*.json");
        foreach (var file in manifestFiles)
        {
            var manifest = await LoadManifestAsync(file);
            if (manifest != null)
            {
                manifests.Add(manifest);
            }
        }

        return manifests.ToArray();
    }

    private ManifestFileEntry CreateManifestEntry(string filePath, string basePath)
    {
        var fileInfo = new FileInfo(filePath);
        var relativePath = Path.GetRelativePath(basePath, filePath);

        var entry = new ManifestFileEntry
        {
            Path = filePath,
            RelativePath = relativePath,
            SizeBytes = fileInfo.Length,
            IsCompressed = filePath.EndsWith(".gz") || filePath.EndsWith(".parquet"),
            VerificationStatus = "Pending"
        };

        // Extract metadata from path
        var pathParts = relativePath.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        entry.Symbol = ExtractSymbol(pathParts);
        entry.EventType = ExtractEventType(pathParts, filePath);
        entry.Date = ExtractDate(pathParts, filePath);

        if (entry.IsCompressed)
        {
            entry.CompressionType = filePath.EndsWith(".gz") ? "gzip" : "parquet";
            entry.CompressedSizeBytes = fileInfo.Length;
        }

        // Estimate event count based on file size and type
        entry.EventCount = EstimateEventCount(fileInfo.Length, entry.EventType ?? "Trade", entry.IsCompressed);

        // Compute checksum
        try
        {
            entry.ChecksumSha256 = ComputeFileChecksum(filePath);
        }
        catch
        {
            entry.ChecksumSha256 = "error";
        }

        return entry;
    }

    private static string? ExtractSymbol(string[] pathParts)
    {
        foreach (var part in pathParts)
        {
            if (IsLikelySymbol(part))
            {
                return part.ToUpper();
            }
        }
        return null;
    }

    private static bool IsLikelySymbol(string value)
    {
        if (string.IsNullOrEmpty(value) || value.Length < 1 || value.Length > 6)
            return false;

        return value.All(c => char.IsLetterOrDigit(c)) &&
               value.Any(char.IsLetter) &&
               !new[] { "Trade", "Depth", "Quote", "Bar", "Historical", "Data" }.Contains(value, StringComparer.OrdinalIgnoreCase);
    }

    private static string? ExtractEventType(string[] pathParts, string filePath)
    {
        var eventTypes = new[] { "Trade", "Quote", "Depth", "Bar", "BboQuote", "L2" };
        foreach (var eventType in eventTypes)
        {
            if (pathParts.Any(p => p.Equals(eventType, StringComparison.OrdinalIgnoreCase)) ||
                filePath.Contains(eventType, StringComparison.OrdinalIgnoreCase))
            {
                return eventType;
            }
        }
        return "Unknown";
    }

    private static DateTime? ExtractDate(string[] pathParts, string filePath)
    {
        // Try to extract date from path parts or filename
        foreach (var part in pathParts)
        {
            if (DateTime.TryParse(part, out var date))
            {
                return date;
            }
        }

        // Try common date formats in filename
        var fileName = Path.GetFileNameWithoutExtension(filePath).Replace(".jsonl", "");
        var datePatterns = new[] { "yyyy-MM-dd", "yyyyMMdd", "yyyy_MM_dd" };
        foreach (var pattern in datePatterns)
        {
            if (DateTime.TryParseExact(fileName.Substring(Math.Max(0, fileName.Length - pattern.Length)), pattern,
                null, System.Globalization.DateTimeStyles.None, out var date))
            {
                return date;
            }
        }

        return null;
    }

    private static long EstimateEventCount(long fileSize, string eventType, bool isCompressed)
    {
        // Rough estimation based on average event sizes
        var avgEventSize = eventType.ToLower() switch
        {
            "trade" => isCompressed ? 30 : 150,
            "quote" or "bboqoute" => isCompressed ? 40 : 200,
            "depth" or "l2" => isCompressed ? 200 : 1000,
            "bar" => isCompressed ? 50 : 250,
            _ => isCompressed ? 50 : 200
        };

        return fileSize / avgEventSize;
    }

    private static string ComputeFileChecksum(string filePath)
    {
        using var sha256 = SHA256.Create();
        using var stream = File.OpenRead(filePath);
        var hash = sha256.ComputeHash(stream);
        return Convert.ToHexString(hash).ToLower();
    }

    private static async Task<string> ComputeFileChecksumAsync(string filePath, CancellationToken ct = default)
    {
        using var sha256 = SHA256.Create();
        await using var stream = File.OpenRead(filePath);
        var hash = await sha256.ComputeHashAsync(stream);
        return Convert.ToHexString(hash).ToLower();
    }

    private static int CalculateTradingDays(DateTime start, DateTime end)
    {
        var tradingDays = 0;
        var current = start;
        while (current <= end)
        {
            if (current.DayOfWeek != DayOfWeek.Saturday && current.DayOfWeek != DayOfWeek.Sunday)
            {
                tradingDays++;
            }
            current = current.AddDays(1);
        }
        return tradingDays;
    }

    private static DataQualityMetrics CalculateQualityMetrics(List<ManifestFileEntry> files, CollectionSession? session)
    {
        var metrics = new DataQualityMetrics
        {
            ActualEvents = files.Sum(f => f.EventCount)
        };

        // Calculate completeness based on file coverage
        var symbols = files.Where(f => !string.IsNullOrEmpty(f.Symbol)).Select(f => f.Symbol).Distinct().Count();
        var dates = files.Where(f => f.Date.HasValue).Select(f => f.Date!.Value.Date).Distinct().Count();

        if (dates > 0)
        {
            var expectedFilesPerDay = symbols * 2; // Assume trade + quote per symbol
            var expectedFiles = expectedFilesPerDay * dates;
            metrics.CompletenessScore = (float)Math.Min(100, (double)files.Count / expectedFiles * 100);
        }
        else
        {
            metrics.CompletenessScore = files.Count > 0 ? 100 : 0;
        }

        // Use session statistics for integrity if available
        if (session?.Statistics != null)
        {
            var errorPenalty = (session.Statistics.GapsDetected * 2) + session.Statistics.SequenceErrors;
            metrics.IntegrityScore = (float)Math.Max(0, 100 - errorPenalty);
            metrics.GapsDetected = session.Statistics.GapsDetected;
            metrics.SequenceErrors = session.Statistics.SequenceErrors;
        }
        else
        {
            metrics.IntegrityScore = 100; // Assume perfect if no session data
        }

        // Calculate overall score
        metrics.OverallScore = (metrics.CompletenessScore * 0.6f) + (metrics.IntegrityScore * 0.4f);

        return metrics;
    }

    private void EnsureCatalogExists()
    {
        if (!Directory.Exists(_catalogPath))
        {
            Directory.CreateDirectory(_catalogPath);
        }
    }

    private async Task<string> SaveManifestAsync(DataManifest manifest, string name, CancellationToken ct = default)
    {
        EnsureCatalogExists();

        var safeName = string.Join("_", name.Split(Path.GetInvalidFileNameChars()));
        var fileName = $"{safeName}_{DateTime.UtcNow:yyyyMMdd_HHmmss}.json";
        var filePath = Path.Combine(_catalogPath, fileName);

        var json = JsonSerializer.Serialize(manifest, DesktopJsonOptions.PrettyPrint);

        await File.WriteAllTextAsync(filePath, json);

        return filePath;
    }

    // Events
    public event EventHandler<ManifestEventArgs>? ManifestGenerated;
    public event EventHandler<ManifestVerificationEventArgs>? ManifestVerified;
}

/// <summary>
/// Event args for manifest events.
/// </summary>
public sealed class ManifestEventArgs : EventArgs
{
    public DataManifest? Manifest { get; set; }
    public string? Path { get; set; }
}

/// <summary>
/// Event args for manifest verification.
/// </summary>
public sealed class ManifestVerificationEventArgs : EventArgs
{
    public ManifestVerificationResult? Result { get; set; }
}

/// <summary>
/// Result of manifest verification.
/// </summary>
public sealed class ManifestVerificationResult
{
    public string ManifestId { get; set; } = string.Empty;
    public bool IsValid { get; set; }
    public DateTime StartedAt { get; set; }
    public DateTime CompletedAt { get; set; }
    public int VerifiedFiles { get; set; }
    public int FailedFiles { get; set; }
    public List<string> MissingFiles { get; set; } = new();
    public List<ChecksumMismatch> ChecksumMismatches { get; set; } = new();
    public List<string> Errors { get; set; } = new();
}

/// <summary>
/// Checksum mismatch details.
/// </summary>
public sealed class ChecksumMismatch
{
    public string FilePath { get; set; } = string.Empty;
    public string ExpectedChecksum { get; set; } = string.Empty;
    public string ActualChecksum { get; set; } = string.Empty;
}
