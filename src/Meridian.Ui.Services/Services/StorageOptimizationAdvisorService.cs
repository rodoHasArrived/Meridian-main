using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using ZstdSharp;

namespace Meridian.Ui.Services;

/// <summary>
/// Service for analyzing storage and providing optimization recommendations.
/// Implements Feature Refinement #63 - Archive Storage Optimization Advisor.
///
/// This service provides storage analysis and delegates operations to the core service
/// via HTTP API endpoints when available:
/// - /api/storage/catalog - Storage catalog for file discovery
/// - /api/storage/tiers/statistics - Tier statistics
/// - /api/storage/tiers/plan - Migration planning
/// - /api/storage/tiers/migrate - Tier migration execution
/// - /api/storage/maintenance/defrag - Defragmentation execution
/// </summary>
public sealed class StorageOptimizationAdvisorService
{
    private static readonly Lazy<StorageOptimizationAdvisorService> _instance = new(() => new StorageOptimizationAdvisorService());
    // Small file threshold (1 MB)
    private const long SmallFileThreshold = 1024 * 1024;

    // Compression thresholds
    private const double GoodCompressionRatio = 0.7; // 30% reduction
    private const double ExcellentCompressionRatio = 0.5; // 50% reduction

    private readonly ApiClientService _apiClient;

    public static StorageOptimizationAdvisorService Instance => _instance.Value;

    private StorageOptimizationAdvisorService()
    {
        _apiClient = ApiClientService.Instance;
    }

    /// <summary>
    /// Analyzes storage and generates optimization recommendations.
    /// </summary>
    public async Task<StorageOptimizationReport> AnalyzeStorageAsync(
        string dataRoot,
        StorageAnalysisOptions options,
        IProgress<StorageAnalysisProgress>? progress = null,
        CancellationToken ct = default)
    {
        var report = new StorageOptimizationReport
        {
            AnalyzedAt = DateTime.UtcNow,
            DataRoot = dataRoot
        };

        try
        {
            if (!Directory.Exists(dataRoot))
            {
                report.Errors.Add($"Data root directory not found: {dataRoot}");
                return report;
            }

            // Get all files
            progress?.Report(new StorageAnalysisProgress { Stage = "Scanning files", Percentage = 5 });
            var files = Directory.GetFiles(dataRoot, "*.*", SearchOption.AllDirectories);
            report.TotalFiles = files.Length;

            // Analyze files
            var fileInfos = new List<AnalyzedFile>();
            var processedCount = 0;

            foreach (var file in files)
            {
                ct.ThrowIfCancellationRequested();

                var info = await AnalyzeFileAsync(file, options.CalculateHashes, ct);
                fileInfos.Add(info);
                report.TotalBytes += info.Size;

                processedCount++;
                if (processedCount % 100 == 0)
                {
                    var pct = 10 + (int)(processedCount * 70.0 / files.Length);
                    progress?.Report(new StorageAnalysisProgress
                    {
                        Stage = $"Analyzing files ({processedCount}/{files.Length})",
                        Percentage = pct
                    });
                }
            }

            report.AnalyzedFiles = fileInfos;

            // Generate recommendations
            progress?.Report(new StorageAnalysisProgress { Stage = "Generating recommendations", Percentage = 85 });

            if (options.FindDuplicates)
            {
                await FindDuplicatesAsync(report, fileInfos, ct);
            }

            if (options.AnalyzeCompression)
            {
                AnalyzeCompressionOpportunities(report, fileInfos);
            }

            if (options.FindSmallFiles)
            {
                FindSmallFilesToMerge(report, fileInfos);
            }

            if (options.AnalyzeTiering)
            {
                AnalyzeTieringOpportunities(report, fileInfos, options.ColdTierAgeDays);
            }

            // Calculate potential savings
            progress?.Report(new StorageAnalysisProgress { Stage = "Calculating savings", Percentage = 95 });
            CalculatePotentialSavings(report);

            report.AnalysisDuration = DateTime.UtcNow - report.AnalyzedAt;
            progress?.Report(new StorageAnalysisProgress { Stage = "Complete", Percentage = 100 });
        }
        catch (OperationCanceledException)
        {
            report.WasCancelled = true;
        }
        catch (Exception ex)
        {
            report.Errors.Add($"Analysis failed: {ex.Message}");
        }

        return report;
    }

    private async Task<AnalyzedFile> AnalyzeFileAsync(string path, bool calculateHash, CancellationToken ct)
    {
        var fileInfo = new FileInfo(path);
        var analyzed = new AnalyzedFile
        {
            Path = path,
            FileName = fileInfo.Name,
            Size = fileInfo.Length,
            LastModified = fileInfo.LastWriteTimeUtc,
            Extension = fileInfo.Extension.ToLowerInvariant()
        };

        // Parse symbol from path (assumes format: .../SYMBOL/type/date.ext)
        var parts = path.Split(Path.DirectorySeparatorChar);
        for (int i = parts.Length - 1; i >= 0; i--)
        {
            if (parts[i].All(c => char.IsUpper(c) || char.IsDigit(c)) && parts[i].Length <= 6)
            {
                analyzed.Symbol = parts[i];
                break;
            }
        }

        // Determine data type
        analyzed.DataType = DetermineDataType(path);

        // Determine compression status
        analyzed.IsCompressed = IsCompressedFile(analyzed.Extension);

        // Determine tier
        analyzed.StorageTier = DetermineTier(path, fileInfo.LastWriteTimeUtc);

        // Calculate hash if requested
        if (calculateHash)
        {
            analyzed.Hash = await ComputeFileHashAsync(path, ct);
        }

        return analyzed;
    }

    private static async Task<string> ComputeFileHashAsync(string path, CancellationToken ct)
    {
        try
        {
            using var sha256 = SHA256.Create();
            await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, true);

            // Only hash first 1MB for performance
            var buffer = new byte[Math.Min(1024 * 1024, stream.Length)];
            var bytesRead = await stream.ReadAsync(buffer, ct);
            var hash = sha256.ComputeHash(buffer, 0, bytesRead);
            return Convert.ToHexString(hash).ToLowerInvariant();
        }
        catch
        {
            return string.Empty;
        }
    }

    private static string DetermineDataType(string path)
    {
        var lower = path.ToLowerInvariant();
        if (lower.Contains("trade") || lower.Contains("tick"))
            return "Tick";
        if (lower.Contains("bar") || lower.Contains("ohlc"))
            return "Bar";
        if (lower.Contains("quote") || lower.Contains("bbo"))
            return "Quote";
        if (lower.Contains("depth") || lower.Contains("l2") || lower.Contains("lob"))
            return "Depth";
        return "Unknown";
    }

    private static bool IsCompressedFile(string extension)
    {
        return extension switch
        {
            ".gz" or ".gzip" => true,
            ".zst" or ".zstd" => true,
            ".lz4" => true,
            ".zip" => true,
            ".7z" => true,
            ".parquet" => true, // Parquet has internal compression
            _ => false
        };
    }

    private static string DetermineTier(string path, DateTime lastModified)
    {
        if (path.Contains("_archive") || path.Contains("cold"))
            return "Cold";
        if (path.Contains("warm") || lastModified < DateTime.UtcNow.AddDays(-30))
            return "Warm";
        return "Hot";
    }

    private async Task FindDuplicatesAsync(StorageOptimizationReport report, List<AnalyzedFile> files, CancellationToken ct)
    {
        // Group by hash
        var byHash = files
            .Where(f => !string.IsNullOrEmpty(f.Hash))
            .GroupBy(f => f.Hash)
            .Where(g => g.Count() > 1)
            .ToList();

        foreach (var group in byHash)
        {
            ct.ThrowIfCancellationRequested();

            var duplicates = group.ToList();
            var original = duplicates.OrderBy(f => f.LastModified).First();
            var dupes = duplicates.Skip(1).ToList();

            var recommendation = new OptimizationRecommendation
            {
                Type = OptimizationType.RemoveDuplicates,
                Priority = RecommendationPriority.High,
                Title = $"Remove {dupes.Count} duplicate file(s)",
                Description = $"Found {dupes.Count} duplicate(s) of '{original.FileName}'",
                PotentialSavingsBytes = dupes.Sum(f => f.Size),
                AffectedFiles = dupes.Select(f => f.Path).ToList(),
                OriginalFile = original.Path
            };

            report.Recommendations.Add(recommendation);
            report.DuplicateFilesCount += dupes.Count;
            report.DuplicateBytesTotal += recommendation.PotentialSavingsBytes;
        }

        // Also check for files with same size and name (potential duplicates without hashing)
        if (!files.Any(f => !string.IsNullOrEmpty(f.Hash)))
        {
            var bySizeAndName = files
                .GroupBy(f => (f.Size, f.FileName))
                .Where(g => g.Count() > 1)
                .ToList();

            foreach (var group in bySizeAndName)
            {
                var recommendation = new OptimizationRecommendation
                {
                    Type = OptimizationType.PotentialDuplicates,
                    Priority = RecommendationPriority.Medium,
                    Title = $"Potential duplicates: {group.First().FileName}",
                    Description = $"Found {group.Count()} files with same name and size",
                    PotentialSavingsBytes = (group.Count() - 1) * group.First().Size,
                    AffectedFiles = group.Select(f => f.Path).ToList()
                };

                report.Recommendations.Add(recommendation);
            }
        }

        await Task.CompletedTask;
    }

    private void AnalyzeCompressionOpportunities(StorageOptimizationReport report, List<AnalyzedFile> files)
    {
        // Find uncompressed files that could benefit from compression
        var uncompressed = files.Where(f => !f.IsCompressed && f.Size > 10 * 1024).ToList();

        if (uncompressed.Any())
        {
            var byTier = uncompressed.GroupBy(f => f.StorageTier);

            foreach (var group in byTier)
            {
                var tier = group.Key;
                var filesInTier = group.ToList();
                var totalSize = filesInTier.Sum(f => f.Size);

                // Estimate compression savings (typically 70-90% for text/JSON data)
                var estimatedRatio = tier switch
                {
                    "Cold" => 0.15, // ZSTD-19 can achieve 85% reduction
                    "Warm" => 0.25, // ZSTD-6 achieves ~75% reduction
                    _ => 0.4 // LZ4 achieves ~60% reduction
                };

                var recommendedCodec = tier switch
                {
                    "Cold" => "zstd (level 19)",
                    "Warm" => "zstd (level 6)",
                    _ => "lz4"
                };

                var recommendation = new OptimizationRecommendation
                {
                    Type = OptimizationType.Compress,
                    Priority = tier == "Cold" ? RecommendationPriority.High : RecommendationPriority.Medium,
                    Title = $"Compress {filesInTier.Count} {tier.ToLower()}-tier files with {recommendedCodec}",
                    Description = $"Uncompressed files in {tier} tier could be compressed for significant savings",
                    PotentialSavingsBytes = (long)(totalSize * (1 - estimatedRatio)),
                    AffectedFiles = filesInTier.Select(f => f.Path).ToList(),
                    EstimatedTime = TimeSpan.FromSeconds(totalSize / (50.0 * 1024 * 1024)) // ~50 MB/s compression
                };

                report.Recommendations.Add(recommendation);
                report.UncompressedFilesCount += filesInTier.Count;
                report.CompressionSavingsEstimate += recommendation.PotentialSavingsBytes;
            }
        }

        // Find files that could be recompressed with better codec
        var suboptimalCompression = files
            .Where(f => f.IsCompressed && f.StorageTier == "Cold" && f.Extension == ".gz")
            .ToList();

        if (suboptimalCompression.Any())
        {
            var totalSize = suboptimalCompression.Sum(f => f.Size);

            var recommendation = new OptimizationRecommendation
            {
                Type = OptimizationType.Recompress,
                Priority = RecommendationPriority.Low,
                Title = $"Recompress {suboptimalCompression.Count} cold-tier files from gzip to zstd",
                Description = "Cold-tier files using gzip could achieve better compression with zstd",
                PotentialSavingsBytes = (long)(totalSize * 0.3), // ~30% additional savings
                AffectedFiles = suboptimalCompression.Select(f => f.Path).ToList()
            };

            report.Recommendations.Add(recommendation);
        }
    }

    private void FindSmallFilesToMerge(StorageOptimizationReport report, List<AnalyzedFile> files)
    {
        // Find small files that could be merged
        var smallFiles = files.Where(f => f.Size < SmallFileThreshold).ToList();

        if (smallFiles.Count > 10)
        {
            // Group by symbol and data type
            var groups = smallFiles
                .GroupBy(f => (f.Symbol, f.DataType))
                .Where(g => g.Count() > 5)
                .ToList();

            foreach (var group in groups)
            {
                var filesInGroup = group.ToList();
                var totalSize = filesInGroup.Sum(f => f.Size);

                var recommendation = new OptimizationRecommendation
                {
                    Type = OptimizationType.MergeSmallFiles,
                    Priority = RecommendationPriority.Low,
                    Title = $"Merge {filesInGroup.Count} small files for {group.Key.Symbol} {group.Key.DataType}",
                    Description = $"Many small files (<1MB) could be merged to improve access performance",
                    PotentialSavingsBytes = 0, // Doesn't save space, improves performance
                    AffectedFiles = filesInGroup.Select(f => f.Path).ToList(),
                    PerformanceImpact = "Improved read performance"
                };

                report.Recommendations.Add(recommendation);
            }

            report.SmallFilesCount = smallFiles.Count;
        }
    }

    private void AnalyzeTieringOpportunities(StorageOptimizationReport report, List<AnalyzedFile> files, int coldTierAgeDays)
    {
        var cutoffDate = DateTime.UtcNow.AddDays(-coldTierAgeDays);

        // Find hot/warm tier files that should be moved to cold
        var toMoveToCold = files
            .Where(f => f.StorageTier != "Cold" && f.LastModified < cutoffDate)
            .ToList();

        if (toMoveToCold.Any())
        {
            var totalSize = toMoveToCold.Sum(f => f.Size);

            var recommendation = new OptimizationRecommendation
            {
                Type = OptimizationType.MoveToColdTier,
                Priority = RecommendationPriority.Medium,
                Title = $"Move {toMoveToCold.Count} files to cold tier",
                Description = $"Files older than {coldTierAgeDays} days could be moved to cold storage",
                PotentialSavingsBytes = 0, // Tiering doesn't reduce size
                AffectedFiles = toMoveToCold.Select(f => f.Path).ToList(),
                PerformanceImpact = "Reduced SSD usage"
            };

            report.Recommendations.Add(recommendation);
            report.TieringCandidatesCount = toMoveToCold.Count;
            report.TieringCandidatesBytes = totalSize;
        }
    }

    private void CalculatePotentialSavings(StorageOptimizationReport report)
    {
        report.TotalPotentialSavings = report.Recommendations.Sum(r => r.PotentialSavingsBytes);
        report.ProjectedUsageAfterOptimization = report.TotalBytes - report.TotalPotentialSavings;

        // Estimate time to complete all optimizations
        var totalTime = TimeSpan.Zero;
        foreach (var rec in report.Recommendations.Where(r => r.EstimatedTime.HasValue))
        {
            totalTime += rec.EstimatedTime!.Value;
        }
        report.EstimatedOptimizationTime = totalTime;
    }

    /// <summary>
    /// Executes a specific optimization recommendation.
    /// </summary>
    public async Task<OptimizationExecutionResult> ExecuteOptimizationAsync(
        OptimizationRecommendation recommendation,
        IProgress<OptimizationProgress>? progress = null,
        CancellationToken ct = default)
    {
        var result = new OptimizationExecutionResult
        {
            RecommendationType = recommendation.Type,
            StartedAt = DateTime.UtcNow
        };

        try
        {
            switch (recommendation.Type)
            {
                case OptimizationType.RemoveDuplicates:
                    await ExecuteRemoveDuplicatesAsync(recommendation, result, progress, ct);
                    break;
                case OptimizationType.Compress:
                    await ExecuteCompressionAsync(recommendation, result, progress, ct);
                    break;
                case OptimizationType.MergeSmallFiles:
                    await ExecuteMergeFilesAsync(recommendation, result, progress, ct);
                    break;
                case OptimizationType.MoveToColdTier:
                    await ExecuteTierMigrationAsync(recommendation, result, progress, ct);
                    break;
                case OptimizationType.PotentialDuplicates:
                    await ExecutePotentialDuplicatesAsync(recommendation, result, progress, ct);
                    break;
                case OptimizationType.Recompress:
                    await ExecuteRecompressAsync(recommendation, result, progress, ct);
                    break;
                case OptimizationType.MoveToWarmTier:
                    await ExecuteMoveToWarmTierAsync(recommendation, result, progress, ct);
                    break;
                case OptimizationType.DeleteStale:
                    await ExecuteDeleteStaleAsync(recommendation, result, progress, ct);
                    break;
            }

            result.CompletedAt = DateTime.UtcNow;
            result.Success = !result.Errors.Any();
        }
        catch (OperationCanceledException)
        {
            result.WasCancelled = true;
        }
        catch (Exception ex)
        {
            result.Errors.Add($"Optimization failed: {ex.Message}");
        }

        return result;
    }

    private async Task ExecuteRemoveDuplicatesAsync(
        OptimizationRecommendation recommendation,
        OptimizationExecutionResult result,
        IProgress<OptimizationProgress>? progress,
        CancellationToken ct)
    {
        var processed = 0;
        foreach (var file in recommendation.AffectedFiles)
        {
            ct.ThrowIfCancellationRequested();

            if (file == recommendation.OriginalFile)
                continue;

            try
            {
                if (File.Exists(file))
                {
                    var size = new FileInfo(file).Length;
                    File.Delete(file);
                    result.BytesSaved += size;
                    result.FilesProcessed++;
                }
            }
            catch (Exception ex)
            {
                result.Errors.Add($"Failed to delete {file}: {ex.Message}");
            }

            processed++;
            progress?.Report(new OptimizationProgress
            {
                CurrentFile = file,
                Percentage = (int)(processed * 100.0 / recommendation.AffectedFiles.Count)
            });
        }

        await Task.CompletedTask;
    }

    private async Task ExecuteCompressionAsync(
        OptimizationRecommendation recommendation,
        OptimizationExecutionResult result,
        IProgress<OptimizationProgress>? progress,
        CancellationToken ct)
    {
        var processed = 0;
        var totalFiles = recommendation.AffectedFiles.Count;

        foreach (var file in recommendation.AffectedFiles)
        {
            ct.ThrowIfCancellationRequested();

            progress?.Report(new OptimizationProgress
            {
                Stage = "Compressing files",
                CurrentFile = Path.GetFileName(file),
                Percentage = (int)(processed * 100.0 / totalFiles)
            });

            try
            {
                if (!File.Exists(file))
                {
                    continue;
                }

                // Skip already compressed files
                if (IsCompressedFile(Path.GetExtension(file).ToLowerInvariant()))
                {
                    continue;
                }

                var originalSize = new FileInfo(file).Length;
                var compressedPath = file + ".gz";

                // Compress using GZip
                await using (var sourceStream = File.OpenRead(file))
                await using (var destStream = File.Create(compressedPath))
                await using (var gzipStream = new System.IO.Compression.GZipStream(
                    destStream,
                    System.IO.Compression.CompressionLevel.Optimal))
                {
                    await sourceStream.CopyToAsync(gzipStream, ct);
                }

                // Verify compressed file was created successfully
                if (File.Exists(compressedPath))
                {
                    var compressedSize = new FileInfo(compressedPath).Length;

                    // Only keep compressed version if it's smaller
                    if (compressedSize < originalSize)
                    {
                        File.Delete(file);
                        result.BytesSaved += originalSize - compressedSize;
                        result.FilesProcessed++;
                    }
                    else
                    {
                        // Compression didn't help, remove compressed file
                        File.Delete(compressedPath);
                    }
                }
            }
            catch (Exception ex)
            {
                result.Errors.Add($"Failed to compress {file}: {ex.Message}");
            }

            processed++;
        }

        progress?.Report(new OptimizationProgress
        {
            Stage = "Compression complete",
            CurrentFile = string.Empty,
            Percentage = 100
        });
    }

    private async Task ExecuteMergeFilesAsync(
        OptimizationRecommendation recommendation,
        OptimizationExecutionResult result,
        IProgress<OptimizationProgress>? progress,
        CancellationToken ct)
    {
        if (recommendation.AffectedFiles.Count < 2)
        {
            return;
        }

        var processed = 0;
        var totalFiles = recommendation.AffectedFiles.Count;

        // Group files by directory and base name pattern
        var fileGroups = recommendation.AffectedFiles
            .GroupBy(f => Path.GetDirectoryName(f) ?? "")
            .ToList();

        foreach (var group in fileGroups)
        {
            ct.ThrowIfCancellationRequested();

            var directory = group.Key;
            var filesToMerge = group.OrderBy(f => f).ToList();

            if (filesToMerge.Count < 2)
            {
                continue;
            }

            progress?.Report(new OptimizationProgress
            {
                Stage = $"Merging {filesToMerge.Count} files",
                CurrentFile = Path.GetFileName(directory),
                Percentage = (int)(processed * 100.0 / totalFiles)
            });

            try
            {
                // Determine merged file name based on first and last file dates
                var firstFile = filesToMerge.First();
                var lastFile = filesToMerge.Last();
                var extension = Path.GetExtension(firstFile);
                var baseName = GetBaseFileName(Path.GetFileName(firstFile));

                var mergedFileName = $"{baseName}_merged{extension}";
                var mergedPath = Path.Combine(directory, mergedFileName);

                // Check if it's a gzipped file
                var isGzipped = extension.Equals(".gz", StringComparison.OrdinalIgnoreCase);

                // Merge JSONL files
                await using (var outputStream = File.Create(mergedPath))
                {
                    Stream writeStream = outputStream;
                    if (isGzipped)
                    {
                        writeStream = new System.IO.Compression.GZipStream(
                            outputStream,
                            System.IO.Compression.CompressionLevel.Optimal);
                    }

                    await using (writeStream)
                    await using (var writer = new StreamWriter(writeStream))
                    {
                        foreach (var file in filesToMerge)
                        {
                            ct.ThrowIfCancellationRequested();

                            Stream inputStream = File.OpenRead(file);
                            if (file.EndsWith(".gz", StringComparison.OrdinalIgnoreCase))
                            {
                                inputStream = new System.IO.Compression.GZipStream(
                                    inputStream,
                                    System.IO.Compression.CompressionMode.Decompress);
                            }

                            await using (inputStream)
                            using (var reader = new StreamReader(inputStream))
                            {
                                string? line;
                                while ((line = await reader.ReadLineAsync(ct)) != null)
                                {
                                    if (!string.IsNullOrWhiteSpace(line))
                                    {
                                        await writer.WriteLineAsync(line);
                                    }
                                }
                            }

                            processed++;
                        }
                    }
                }

                // Delete original files after successful merge
                var originalTotalSize = filesToMerge.Sum(f => new FileInfo(f).Length);
                foreach (var file in filesToMerge)
                {
                    if (File.Exists(file) && file != mergedPath)
                    {
                        File.Delete(file);
                    }
                }

                var mergedSize = new FileInfo(mergedPath).Length;
                result.FilesProcessed += filesToMerge.Count;

                // Merging may not save space but reduces file count.
                // BytesSaved represents the difference if any.
                if (originalTotalSize > mergedSize)
                {
                    result.BytesSaved += originalTotalSize - mergedSize;
                }
            }
            catch (Exception ex)
            {
                result.Errors.Add($"Failed to merge files in {directory}: {ex.Message}");
            }
        }

        progress?.Report(new OptimizationProgress
        {
            Stage = "Merge complete",
            CurrentFile = string.Empty,
            Percentage = 100
        });
    }

    /// <summary>
    /// Extracts the base file name without date suffixes.
    /// </summary>
    private static string GetBaseFileName(string fileName)
    {
        // Remove extension
        var name = Path.GetFileNameWithoutExtension(fileName);

        // Remove .gz extension if double-extension
        if (name.EndsWith(".jsonl", StringComparison.OrdinalIgnoreCase))
        {
            name = Path.GetFileNameWithoutExtension(name);
        }

        // Remove date patterns (YYYY-MM-DD or YYYYMMDD)
        var patterns = new[]
        {
            @"_\d{4}-\d{2}-\d{2}$",
            @"_\d{8}$",
            @"-\d{4}-\d{2}-\d{2}$",
            @"-\d{8}$"
        };

        foreach (var pattern in patterns)
        {
            name = System.Text.RegularExpressions.Regex.Replace(name, pattern, "");
        }

        return name;
    }

    private async Task ExecuteTierMigrationAsync(
        OptimizationRecommendation recommendation,
        OptimizationExecutionResult result,
        IProgress<OptimizationProgress>? progress,
        CancellationToken ct)
    {
        // Try to use core API for tier migration
        var apiResult = await ExecuteTierMigrationViaApiAsync(recommendation, progress, ct);
        if (apiResult != null)
        {
            result.FilesProcessed = apiResult.FilesMigrated;
            result.BytesSaved = apiResult.BytesMigrated;
            if (apiResult.Errors.Any())
            {
                result.Errors.AddRange(apiResult.Errors);
            }
            return;
        }

        // Fallback: Note that tier migration requires storage configuration
        result.Errors.Add("Tier migration via API unavailable; manual configuration required");
    }

    private async Task ExecutePotentialDuplicatesAsync(
        OptimizationRecommendation recommendation,
        OptimizationExecutionResult result,
        IProgress<OptimizationProgress>? progress,
        CancellationToken ct)
    {
        // Potential duplicates have the same name and size but haven't been verified via hash
        // First, compute hashes for all files to verify they're actually duplicates
        var processed = 0;
        var totalFiles = recommendation.AffectedFiles.Count;
        var fileHashes = new Dictionary<string, string>();

        progress?.Report(new OptimizationProgress
        {
            Stage = "Computing file hashes",
            CurrentFile = string.Empty,
            Percentage = 5
        });

        // Compute hashes for all affected files
        foreach (var file in recommendation.AffectedFiles)
        {
            ct.ThrowIfCancellationRequested();

            if (!File.Exists(file))
            {
                continue;
            }

            var hash = await ComputeFileHashAsync(file, ct);
            if (!string.IsNullOrEmpty(hash))
            {
                fileHashes[file] = hash;
            }

            processed++;
            progress?.Report(new OptimizationProgress
            {
                Stage = "Computing file hashes",
                CurrentFile = Path.GetFileName(file),
                Percentage = (int)(processed * 50.0 / totalFiles)
            });
        }

        // Group by hash to find confirmed duplicates
        var confirmedDuplicates = fileHashes
            .GroupBy(kvp => kvp.Value)
            .Where(g => g.Count() > 1)
            .ToList();

        if (!confirmedDuplicates.Any())
        {
            progress?.Report(new OptimizationProgress
            {
                Stage = "No confirmed duplicates found",
                CurrentFile = string.Empty,
                Percentage = 100
            });
            return;
        }

        // Remove duplicates (keep the oldest file as original)
        processed = 0;
        var totalDuplicates = confirmedDuplicates.Sum(g => g.Count() - 1);

        foreach (var group in confirmedDuplicates)
        {
            ct.ThrowIfCancellationRequested();

            var files = group.OrderBy(kvp => new FileInfo(kvp.Key).LastWriteTimeUtc).ToList();
            var original = files.First().Key;

            // Remove all but the original
            foreach (var duplicate in files.Skip(1))
            {
                try
                {
                    if (File.Exists(duplicate.Key))
                    {
                        var size = new FileInfo(duplicate.Key).Length;
                        File.Delete(duplicate.Key);
                        result.BytesSaved += size;
                        result.FilesProcessed++;
                    }
                }
                catch (Exception ex)
                {
                    result.Errors.Add($"Failed to delete {duplicate.Key}: {ex.Message}");
                }

                processed++;
                progress?.Report(new OptimizationProgress
                {
                    Stage = "Removing confirmed duplicates",
                    CurrentFile = Path.GetFileName(duplicate.Key),
                    Percentage = 50 + (int)(processed * 50.0 / totalDuplicates)
                });
            }
        }

        progress?.Report(new OptimizationProgress
        {
            Stage = "Duplicate removal complete",
            CurrentFile = string.Empty,
            Percentage = 100
        });
    }

    private async Task ExecuteRecompressAsync(
        OptimizationRecommendation recommendation,
        OptimizationExecutionResult result,
        IProgress<OptimizationProgress>? progress,
        CancellationToken ct)
    {
        var processed = 0;
        var totalFiles = recommendation.AffectedFiles.Count;

        foreach (var file in recommendation.AffectedFiles)
        {
            ct.ThrowIfCancellationRequested();

            progress?.Report(new OptimizationProgress
            {
                Stage = "Recompressing files",
                CurrentFile = Path.GetFileName(file),
                Percentage = (int)(processed * 100.0 / totalFiles)
            });

            try
            {
                if (!File.Exists(file))
                {
                    continue;
                }

                // Only process gzip files for recompression to zstd
                var extension = Path.GetExtension(file).ToLowerInvariant();
                if (extension != ".gz" && extension != ".gzip")
                {
                    continue;
                }

                var originalSize = new FileInfo(file).Length;

                // Determine new file path (.zst instead of .gz)
                var newPath = Path.ChangeExtension(file, ".zst");
                if (file.EndsWith(".jsonl.gz", StringComparison.OrdinalIgnoreCase))
                {
                    newPath = file[..^3] + ".zst"; // Remove .gz and add .zst
                }

                // Decompress gzip and recompress using zstd via ZstdSharp.
                // If zstd compression fails unexpectedly, fall back to gzip to preserve behavior.
                await using (var sourceStream = File.OpenRead(file))
                await using (var gzipDecompressStream = new System.IO.Compression.GZipStream(
                    sourceStream, System.IO.Compression.CompressionMode.Decompress))
                await using (var destStream = File.Create(newPath))
                await using (var buffer = new MemoryStream())
                {
                    await gzipDecompressStream.CopyToAsync(buffer, ct);
                    var rawBytes = buffer.ToArray();

                    try
                    {
                        using var compressor = new Compressor(9);
                        var compressed = compressor.Wrap(rawBytes);
                        await destStream.WriteAsync(new ReadOnlyMemory<byte>(compressed.ToArray()), ct);
                    }
                    catch
                    {
                        destStream.SetLength(0);
                        await using var fallbackStream = new System.IO.Compression.GZipStream(
                            destStream,
                            System.IO.Compression.CompressionLevel.SmallestSize,
                            leaveOpen: true);
                        await fallbackStream.WriteAsync(rawBytes, ct);
                    }
                }

                // Verify recompressed file was created successfully
                if (File.Exists(newPath))
                {
                    var newSize = new FileInfo(newPath).Length;

                    // Only keep new version if it's smaller
                    if (newSize < originalSize)
                    {
                        File.Delete(file);
                        result.BytesSaved += originalSize - newSize;
                        result.FilesProcessed++;
                    }
                    else
                    {
                        // Recompression didn't help, remove new file
                        File.Delete(newPath);
                    }
                }
            }
            catch (Exception ex)
            {
                result.Errors.Add($"Failed to recompress {file}: {ex.Message}");
            }

            processed++;
        }

        progress?.Report(new OptimizationProgress
        {
            Stage = "Recompression complete",
            CurrentFile = string.Empty,
            Percentage = 100
        });
    }

    private async Task ExecuteMoveToWarmTierAsync(
        OptimizationRecommendation recommendation,
        OptimizationExecutionResult result,
        IProgress<OptimizationProgress>? progress,
        CancellationToken ct)
    {
        // Try to use core API for tier migration to warm tier
        var apiResult = await ExecuteWarmTierMigrationViaApiAsync(recommendation, progress, ct);
        if (apiResult != null)
        {
            result.FilesProcessed = apiResult.FilesMigrated;
            result.BytesSaved = apiResult.BytesMigrated;
            if (apiResult.Errors.Any())
            {
                result.Errors.AddRange(apiResult.Errors);
            }
            return;
        }

        // Fallback: Note that tier migration requires storage configuration
        result.Errors.Add("Warm tier migration via API unavailable; manual configuration required");
    }

    /// <summary>
    /// Executes warm tier migration via the core API.
    /// </summary>
    private async Task<TierMigrationApiResult?> ExecuteWarmTierMigrationViaApiAsync(
        OptimizationRecommendation recommendation,
        IProgress<OptimizationProgress>? progress,
        CancellationToken ct)
    {
        try
        {
            var request = new
            {
                TargetTier = "warm",
                Files = recommendation.AffectedFiles,
                Compress = true,
                CompressionCodec = "zstd"
            };

            progress?.Report(new OptimizationProgress
            {
                Stage = "Initiating warm tier migration via API",
                CurrentFile = string.Empty,
                Percentage = 10
            });

            var response = await _apiClient.PostWithResponseAsync<TierMigrationApiResult>(
                "/api/storage/tiers/migrate", request, ct);

            if (response.Success && response.Data != null)
            {
                progress?.Report(new OptimizationProgress
                {
                    Stage = "Warm tier migration complete",
                    CurrentFile = string.Empty,
                    Percentage = 100
                });
                return response.Data;
            }

            return null;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[StorageOptimization] Warm tier migration API failed: {ex.Message}");
            return null;
        }
    }

    private async Task ExecuteDeleteStaleAsync(
        OptimizationRecommendation recommendation,
        OptimizationExecutionResult result,
        IProgress<OptimizationProgress>? progress,
        CancellationToken ct)
    {
        var processed = 0;
        var totalFiles = recommendation.AffectedFiles.Count;

        foreach (var file in recommendation.AffectedFiles)
        {
            ct.ThrowIfCancellationRequested();

            progress?.Report(new OptimizationProgress
            {
                Stage = "Deleting stale files",
                CurrentFile = Path.GetFileName(file),
                Percentage = (int)(processed * 100.0 / totalFiles)
            });

            try
            {
                if (File.Exists(file))
                {
                    var size = new FileInfo(file).Length;
                    File.Delete(file);
                    result.BytesSaved += size;
                    result.FilesProcessed++;
                }
            }
            catch (Exception ex)
            {
                result.Errors.Add($"Failed to delete stale file {file}: {ex.Message}");
            }

            processed++;
        }

        progress?.Report(new OptimizationProgress
        {
            Stage = "Stale file deletion complete",
            CurrentFile = string.Empty,
            Percentage = 100
        });

        await Task.CompletedTask;
    }

    /// <summary>
    /// Executes tier migration via the core API.
    /// </summary>
    private async Task<TierMigrationApiResult?> ExecuteTierMigrationViaApiAsync(
        OptimizationRecommendation recommendation,
        IProgress<OptimizationProgress>? progress,
        CancellationToken ct)
    {
        try
        {
            var request = new
            {
                TargetTier = "cold",
                Files = recommendation.AffectedFiles,
                Compress = true,
                CompressionCodec = "zstd"
            };

            progress?.Report(new OptimizationProgress
            {
                Stage = "Initiating tier migration via API",
                CurrentFile = string.Empty,
                Percentage = 10
            });

            var response = await _apiClient.PostWithResponseAsync<TierMigrationApiResult>(
                "/api/storage/tiers/migrate", request, ct);

            if (response.Success && response.Data != null)
            {
                progress?.Report(new OptimizationProgress
                {
                    Stage = "Migration complete",
                    CurrentFile = string.Empty,
                    Percentage = 100
                });
                return response.Data;
            }

            return null;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[StorageOptimization] Tier migration API failed: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// Gets tier statistics from the core API.
    /// </summary>
    public async Task<TierStatisticsApiResult?> GetTierStatisticsAsync(CancellationToken ct = default)
    {
        try
        {
            var response = await _apiClient.GetWithResponseAsync<TierStatisticsApiResult>(
                "/api/storage/tiers/statistics", ct);

            return response.Success ? response.Data : null;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[StorageOptimization] Tier statistics API failed: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// Gets a migration plan from the core API for files to move between tiers.
    /// </summary>
    public async Task<MigrationPlanApiResult?> GetMigrationPlanAsync(
        string targetTier = "cold",
        int ageDaysThreshold = 90,
        CancellationToken ct = default)
    {
        try
        {
            var request = new
            {
                TargetTier = targetTier,
                AgeDaysThreshold = ageDaysThreshold,
                PreviewOnly = true
            };

            var response = await _apiClient.PostWithResponseAsync<MigrationPlanApiResult>(
                "/api/storage/tiers/plan", request, ct);

            return response.Success ? response.Data : null;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[StorageOptimization] Migration plan API failed: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// Runs defragmentation via the core API.
    /// </summary>
    public async Task<DefragmentationApiResult?> RunDefragmentationAsync(CancellationToken ct = default)
    {
        try
        {
            var response = await _apiClient.PostWithResponseAsync<DefragmentationApiResult>(
                "/api/storage/maintenance/defrag", null, ct);

            return response.Success ? response.Data : null;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[StorageOptimization] Defragmentation API failed: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// Gets the storage catalog from the core API for comprehensive file analysis.
    /// </summary>
    public async Task<StorageCatalogApiResult?> GetStorageCatalogAsync(CancellationToken ct = default)
    {
        try
        {
            var response = await _apiClient.GetWithResponseAsync<StorageCatalogApiResult>(
                "/api/storage/catalog", ct);

            return response.Success ? response.Data : null;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[StorageOptimization] Storage catalog API failed: {ex.Message}");
            return null;
        }
    }
}

#region Models

/// <summary>
/// Options for storage analysis.
/// </summary>
public sealed class StorageAnalysisOptions
{
    public bool CalculateHashes { get; set; } = true;
    public bool FindDuplicates { get; set; } = true;
    public bool AnalyzeCompression { get; set; } = true;
    public bool FindSmallFiles { get; set; } = true;
    public bool AnalyzeTiering { get; set; } = true;
    public int ColdTierAgeDays { get; set; } = 90;
}

/// <summary>
/// Progress report for storage analysis.
/// </summary>
public sealed class StorageAnalysisProgress
{
    public string Stage { get; set; } = string.Empty;
    public int Percentage { get; set; }
}

/// <summary>
/// Storage optimization report.
/// </summary>
public sealed class StorageOptimizationReport
{
    public DateTime AnalyzedAt { get; set; }
    public TimeSpan AnalysisDuration { get; set; }
    public string DataRoot { get; set; } = string.Empty;
    public bool WasCancelled { get; set; }

    // Summary statistics
    public int TotalFiles { get; set; }
    public long TotalBytes { get; set; }
    public List<AnalyzedFile> AnalyzedFiles { get; set; } = new();

    // Recommendations
    public List<OptimizationRecommendation> Recommendations { get; set; } = new();

    // Duplicate analysis
    public int DuplicateFilesCount { get; set; }
    public long DuplicateBytesTotal { get; set; }

    // Compression analysis
    public int UncompressedFilesCount { get; set; }
    public long CompressionSavingsEstimate { get; set; }

    // Small files analysis
    public int SmallFilesCount { get; set; }

    // Tiering analysis
    public int TieringCandidatesCount { get; set; }
    public long TieringCandidatesBytes { get; set; }

    // Overall savings
    public long TotalPotentialSavings { get; set; }
    public long ProjectedUsageAfterOptimization { get; set; }
    public TimeSpan EstimatedOptimizationTime { get; set; }

    // Errors
    public List<string> Errors { get; set; } = new();

    /// <summary>
    /// Gets a formatted report summary.
    /// </summary>
    public string GetSummary()
    {
        var lines = new List<string>
        {
            $"Storage Optimization Report - {AnalyzedAt:yyyy-MM-dd HH:mm}",
            new string('-', 50),
            $"Current Usage: {FormatBytes(TotalBytes)}",
            "Recommended Actions:"
        };

        var actionNumber = 1;
        foreach (var rec in Recommendations.OrderByDescending(r => r.PotentialSavingsBytes).Take(5))
        {
            lines.Add($"  {actionNumber}. {rec.Title} → Save {FormatBytes(rec.PotentialSavingsBytes)}");
            actionNumber++;
        }

        lines.Add(string.Empty);
        lines.Add($"Projected Usage After Optimization: {FormatBytes(ProjectedUsageAfterOptimization)}");
        lines.Add($"Estimated Time to Complete: {EstimatedOptimizationTime.TotalMinutes:F0} minutes");

        return string.Join(Environment.NewLine, lines);
    }

    private static string FormatBytes(long bytes) => FormatHelpers.FormatBytes(bytes);
}

/// <summary>
/// Information about an analyzed file.
/// </summary>
public sealed class AnalyzedFile
{
    public string Path { get; set; } = string.Empty;
    public string FileName { get; set; } = string.Empty;
    public long Size { get; set; }
    public DateTime LastModified { get; set; }
    public string Extension { get; set; } = string.Empty;
    public string Symbol { get; set; } = string.Empty;
    public string DataType { get; set; } = string.Empty;
    public bool IsCompressed { get; set; }
    public string StorageTier { get; set; } = string.Empty;
    public string Hash { get; set; } = string.Empty;
}

/// <summary>
/// An optimization recommendation.
/// </summary>
public sealed class OptimizationRecommendation
{
    public OptimizationType Type { get; set; }
    public RecommendationPriority Priority { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public long PotentialSavingsBytes { get; set; }
    public List<string> AffectedFiles { get; set; } = new();
    public string? OriginalFile { get; set; }
    public TimeSpan? EstimatedTime { get; set; }
    public string? PerformanceImpact { get; set; }
}

/// <summary>
/// Type of optimization.
/// </summary>
public enum OptimizationType : byte
{
    RemoveDuplicates,
    PotentialDuplicates,
    Compress,
    Recompress,
    MergeSmallFiles,
    MoveToColdTier,
    MoveToWarmTier,
    DeleteStale
}

/// <summary>
/// Priority level for recommendations.
/// </summary>
public enum RecommendationPriority : byte
{
    Low,
    Medium,
    High,
    Critical
}

/// <summary>
/// Progress for optimization execution.
/// </summary>
public sealed class OptimizationProgress
{
    public string Stage { get; set; } = string.Empty;
    public string CurrentFile { get; set; } = string.Empty;
    public int Percentage { get; set; }
}

/// <summary>
/// Result of optimization execution.
/// </summary>
public sealed class OptimizationExecutionResult
{
    public OptimizationType RecommendationType { get; set; }
    public DateTime StartedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    public bool Success { get; set; }
    public bool WasCancelled { get; set; }
    public int FilesProcessed { get; set; }
    public long BytesSaved { get; set; }
    public List<string> Errors { get; set; } = new();
}

#endregion

#region API Response Models

/// <summary>
/// Response from /api/storage/tiers/migrate endpoint.
/// </summary>
public sealed class TierMigrationApiResult
{
    public bool Success { get; set; }
    public int FilesMigrated { get; set; }
    public long BytesMigrated { get; set; }
    public TimeSpan Duration { get; set; }
    public List<string> Errors { get; set; } = new();
}

/// <summary>
/// Response from /api/storage/tiers/statistics endpoint.
/// </summary>
public sealed class TierStatisticsApiResult
{
    public DateTime GeneratedAt { get; set; }
    public TierStats? Hot { get; set; }
    public TierStats? Warm { get; set; }
    public TierStats? Cold { get; set; }
    public long TotalBytes { get; set; }
    public int TotalFiles { get; set; }
}

/// <summary>
/// Statistics for a storage tier.
/// </summary>
public sealed class TierStats
{
    public string Name { get; set; } = string.Empty;
    public int FileCount { get; set; }
    public long TotalBytes { get; set; }
    public double PercentageOfTotal { get; set; }
    public DateTime? OldestFile { get; set; }
    public DateTime? NewestFile { get; set; }
}

/// <summary>
/// Response from /api/storage/tiers/plan endpoint.
/// </summary>
public sealed class MigrationPlanApiResult
{
    public string TargetTier { get; set; } = string.Empty;
    public int FilesToMigrate { get; set; }
    public long BytesToMigrate { get; set; }
    public TimeSpan EstimatedDuration { get; set; }
    public List<MigrationPlanItem> Items { get; set; } = new();
}

/// <summary>
/// Item in a migration plan.
/// </summary>
public sealed class MigrationPlanItem
{
    public string Path { get; set; } = string.Empty;
    public string CurrentTier { get; set; } = string.Empty;
    public long SizeBytes { get; set; }
    public DateTime LastAccessed { get; set; }
}

/// <summary>
/// Response from /api/storage/maintenance/defrag endpoint.
/// </summary>
public sealed class DefragmentationApiResult
{
    public bool Success { get; set; }
    public int FilesProcessed { get; set; }
    public long BytesReclaimed { get; set; }
    public TimeSpan Duration { get; set; }
    public List<string> Errors { get; set; } = new();
}

/// <summary>
/// Response from /api/storage/catalog endpoint.
/// </summary>
public sealed class StorageCatalogApiResult
{
    public DateTime GeneratedAt { get; set; }
    public int TotalFiles { get; set; }
    public long TotalBytes { get; set; }
    public List<CatalogEntry> Entries { get; set; } = new();
    public Dictionary<string, long> BytesBySymbol { get; set; } = new();
    public Dictionary<string, long> BytesByDataType { get; set; } = new();
    public Dictionary<string, long> BytesByTier { get; set; } = new();
}

/// <summary>
/// Entry in the storage catalog.
/// </summary>
public sealed class CatalogEntry
{
    public string Path { get; set; } = string.Empty;
    public string? Symbol { get; set; }
    public string? DataType { get; set; }
    public string Tier { get; set; } = string.Empty;
    public long SizeBytes { get; set; }
    public DateTime LastModified { get; set; }
    public bool IsCompressed { get; set; }
    public string? Checksum { get; set; }
}

#endregion
