using System.Collections.Concurrent;
using Meridian.Identity.Auth;
using System.IO.Compression;
using System.Text.Json;
using Meridian.Contracts.Api;
using Meridian.Storage;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Meridian.Ui.Shared.Endpoints;

/// <summary>
/// Extension methods for registering data sampling API endpoints.
/// Reads actual JSONL data files from storage for sampling operations.
/// </summary>
public static class SamplingEndpoints
{
    /// <summary>Largest sample a single request may ask for; larger requests are refused.</summary>
    internal const int MaxSampleSize = 100_000;

    /// <summary>
    /// Saved samples are held in memory only. The oldest are evicted beyond this count so a
    /// caller cannot grow the process without bound by creating samples repeatedly.
    /// </summary>
    internal const int MaxSavedSamples = 50;

    public static void MapSamplingEndpoints(this WebApplication app, JsonSerializerOptions jsonOptions)
    {
        var savedSamples = new ConcurrentDictionary<string, SampleResult>(StringComparer.OrdinalIgnoreCase);
        var group = app.MapGroup("").WithTags("Sampling");

        // Create sample - reads actual data files and applies sampling strategy
        group.MapPost(UiApiRoutes.SamplingCreate, async (SamplingCreateRequest req, [FromServices] StorageOptions? storageOptions, CancellationToken ct) =>
        {
            var rootPath = storageOptions?.RootPath ?? "data";
            var sampleId = Guid.NewGuid().ToString("N")[..12];
            var strategy = req.Strategy ?? "random";
            var sampleSize = req.SampleSize ?? 1000;
            var symbol = req.Symbol;

            // A zero size divided by zero in systematic sampling (a 500) and an unbounded size let
            // one request hold the whole population in memory; both are refused up front.
            if (sampleSize < 1 || sampleSize > MaxSampleSize)
            {
                return Results.BadRequest(new
                {
                    error = $"sampleSize must be between 1 and {MaxSampleSize}."
                });
            }

            // Find relevant JSONL files
            var files = FindDataFiles(rootPath, symbol);
            if (files.Count == 0)
            {
                var result = new SampleResult
                {
                    SampleId = sampleId,
                    Symbol = symbol,
                    Strategy = strategy,
                    RequestedSize = sampleSize,
                    ActualSize = 0,
                    PopulationSize = 0,
                    Status = "no_data",
                    Events = Array.Empty<string>(),
                    CreatedAt = DateTimeOffset.UtcNow
                };
                SaveSample(savedSamples, result);

                return Results.Json(new
                {
                    sampleId,
                    symbol,
                    strategy,
                    sampleSize = 0,
                    populationSize = 0,
                    status = "no_data",
                    // The storage root is server layout and is not echoed to the caller.
                    message = $"No data files found{(symbol != null ? $" for symbol '{symbol}'" : "")}.",
                    createdAt = DateTimeOffset.UtcNow
                }, jsonOptions);
            }

            // Read events from files and sample them
            var allLines = new List<string>();
            foreach (var file in files.Take(10)) // Limit to 10 files to avoid excessive memory
            {
                ct.ThrowIfCancellationRequested();
                try
                {
                    var lines = await ReadJsonlLinesAsync(file, ct);
                    allLines.AddRange(lines);
                }
                catch (OperationCanceledException) { throw; }
                catch { /* skip unreadable files */ }
            }

            var populationSize = allLines.Count;
            var sampledLines = ApplySamplingStrategy(allLines, strategy, sampleSize);

            var sampleResult = new SampleResult
            {
                SampleId = sampleId,
                Symbol = symbol,
                Strategy = strategy,
                RequestedSize = sampleSize,
                ActualSize = sampledLines.Count,
                PopulationSize = populationSize,
                Status = "created",
                Events = sampledLines.ToArray(),
                CreatedAt = DateTimeOffset.UtcNow
            };
            SaveSample(savedSamples, sampleResult);

            return Results.Json(new
            {
                sampleId,
                symbol,
                strategy,
                sampleSize = sampledLines.Count,
                populationSize,
                filesScanned = files.Count,
                status = "created",
                createdAt = DateTimeOffset.UtcNow
            }, jsonOptions);
        })
        .WithName("CreateSample").RequirePermission(UserPermission.ViewHistoricalData)
        .Produces(200)
        .RequireRateLimiting(UiEndpoints.MutationRateLimitPolicy);

        // Estimate sample size - uses actual population size from storage
        group.MapGet(UiApiRoutes.SamplingEstimate, (string? symbol, double? confidence, double? marginOfError, [FromServices] StorageOptions? storageOptions, CancellationToken ct) =>
        {
            var rootPath = storageOptions?.RootPath ?? "data";
            var conf = confidence ?? 0.95;
            var margin = marginOfError ?? 0.05;

            // A margin of zero divides by zero in Cochran's formula; both inputs are fractions.
            if (conf <= 0 || conf >= 1 || margin <= 0 || margin >= 1)
            {
                return Results.BadRequest(new
                {
                    error = "confidence and marginOfError must each be greater than 0 and less than 1."
                });
            }

            // Count actual lines in data files for population estimate. The scan is synchronous
            // per file, so cancellation is honoured between files.
            long populationSize = 0;
            var files = FindDataFiles(rootPath, symbol);
            foreach (var file in files)
            {
                ct.ThrowIfCancellationRequested();
                try
                {
                    populationSize += CountLines(file);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
                {
                    // Skip unreadable files; they contribute nothing to the population.
                }
            }

            // With no stored data there is no population to estimate from. The previous fallback
            // invented a population of 100,000, which reported a recommendation for data that
            // does not exist.
            int recommended = 0;
            if (populationSize > 0)
            {
                // Cochran's formula for sample size estimation
                var z = conf >= 0.99 ? 2.576 : conf >= 0.95 ? 1.96 : 1.645;
                var p = 0.5;
                var n0 = (z * z * p * (1 - p)) / (margin * margin);
                recommended = (int)Math.Ceiling(n0 / (1 + (n0 - 1) / populationSize));
            }

            return Results.Json(new
            {
                symbol,
                confidence = conf,
                marginOfError = margin,
                estimatedPopulation = populationSize,
                dataFilesFound = files.Count,
                recommendedSampleSize = recommended,
                status = populationSize > 0 ? "estimated" : "no_data",
                timestamp = DateTimeOffset.UtcNow
            }, jsonOptions);
        })
        .WithName("EstimateSampleSize").RequirePermission(UserPermission.ViewHistoricalData)
        .Produces(200);

        // List saved samples
        group.MapGet(UiApiRoutes.SamplingSaved, () =>
        {
            var summaries = savedSamples.Values.Select(s => new
            {
                s.SampleId,
                s.Symbol,
                s.Strategy,
                sampleSize = s.ActualSize,
                s.PopulationSize,
                s.Status,
                s.CreatedAt
            });

            return Results.Json(new
            {
                samples = summaries,
                total = savedSamples.Count,
                timestamp = DateTimeOffset.UtcNow
            }, jsonOptions);
        })
        .WithName("GetSavedSamples").RequirePermission(UserPermission.ViewHistoricalData)
        .Produces(200);

        // Get sample by ID - returns actual sampled events
        group.MapGet(UiApiRoutes.SamplingById, (string sampleId) =>
        {
            if (!savedSamples.TryGetValue(sampleId, out var sample))
                return Results.NotFound(new { error = $"Sample '{sampleId}' not found" });

            return Results.Json(new
            {
                sample.SampleId,
                sample.Symbol,
                sample.Strategy,
                sampleSize = sample.ActualSize,
                sample.PopulationSize,
                sample.Status,
                events = sample.Events.Take(100), // Limit response size
                totalEvents = sample.Events.Length,
                sample.CreatedAt
            }, jsonOptions);
        })
        .WithName("GetSampleById").RequirePermission(UserPermission.ViewHistoricalData)
        .Produces(200)
        .Produces(404);
    }

    private static void SaveSample(ConcurrentDictionary<string, SampleResult> savedSamples, SampleResult sample)
    {
        savedSamples[sample.SampleId] = sample;
        while (savedSamples.Count > MaxSavedSamples)
        {
            var oldest = savedSamples.Values.MinBy(static s => s.CreatedAt);
            if (oldest is null || !savedSamples.TryRemove(oldest.SampleId, out _))
                break;
        }
    }

    private static List<string> FindDataFiles(string rootPath, string? symbol)
    {
        var files = new List<string>();
        if (!Directory.Exists(rootPath))
            return files;

        try
        {
            foreach (var file in Directory.EnumerateFiles(rootPath, "*.jsonl*", SearchOption.AllDirectories))
            {
                if (symbol != null && !file.Contains(symbol, StringComparison.OrdinalIgnoreCase))
                    continue;
                files.Add(file);
            }
        }
        catch (UnauthorizedAccessException) { /* skip */ }

        return files;
    }

    private static async Task<List<string>> ReadJsonlLinesAsync(string filePath, CancellationToken ct)
    {
        var lines = new List<string>();
        await using var fs = File.OpenRead(filePath);
        Stream stream = fs;

        if (filePath.EndsWith(".gz", StringComparison.OrdinalIgnoreCase))
            stream = new GZipStream(fs, CompressionMode.Decompress);

        using var reader = new StreamReader(stream);
        while (true)
        {
            ct.ThrowIfCancellationRequested();
            var line = await reader.ReadLineAsync(ct);
            if (line is null)
            {
                break;
            }

            if (!string.IsNullOrWhiteSpace(line))
            {
                lines.Add(line);
            }

            // Safety limit to avoid reading enormous files
            if (lines.Count >= 100_000)
            {
                break;
            }
        }

        return lines;
    }

    private static long CountLines(string filePath)
    {
        // Fast line counting for uncompressed files
        if (filePath.EndsWith(".gz", StringComparison.OrdinalIgnoreCase))
        {
            // For compressed files, estimate from file size
            var fileInfo = new FileInfo(filePath);
            return fileInfo.Length / 200; // Rough estimate: ~200 bytes per line
        }

        long count = 0;
        using var reader = new StreamReader(filePath);
        while (reader.ReadLine() != null)
            count++;
        return count;
    }

    private static List<string> ApplySamplingStrategy(List<string> population, string strategy, int sampleSize)
    {
        if (population.Count == 0)
            return new List<string>();
        sampleSize = Math.Min(sampleSize, population.Count);

        return strategy.ToLowerInvariant() switch
        {
            "systematic" => SystematicSample(population, sampleSize),
            "first" => population.Take(sampleSize).ToList(),
            "last" => population.Skip(Math.Max(0, population.Count - sampleSize)).ToList(),
            _ => RandomSample(population, sampleSize) // "random" or default
        };
    }

    private static List<string> RandomSample(List<string> population, int sampleSize)
    {
        var rng = new Random();
        var indices = new HashSet<int>();
        while (indices.Count < sampleSize)
        {
            indices.Add(rng.Next(population.Count));
        }
        return indices.OrderBy(i => i).Select(i => population[i]).ToList();
    }

    private static List<string> SystematicSample(List<string> population, int sampleSize)
    {
        var interval = Math.Max(1, population.Count / sampleSize);
        var result = new List<string>(sampleSize);
        for (int i = 0; i < population.Count && result.Count < sampleSize; i += interval)
        {
            result.Add(population[i]);
        }
        return result;
    }

    private sealed record SamplingCreateRequest(string? Symbol, string? Strategy, int? SampleSize, DateTime? FromDate, DateTime? ToDate);

    private sealed class SampleResult
    {
        public string SampleId { get; init; } = string.Empty;
        public string? Symbol { get; init; }
        public string Strategy { get; init; } = string.Empty;
        public int RequestedSize { get; init; }
        public int ActualSize { get; init; }
        public long PopulationSize { get; init; }
        public string Status { get; init; } = string.Empty;
        public string[] Events { get; init; } = Array.Empty<string>();
        public DateTimeOffset CreatedAt { get; init; }
    }
}
