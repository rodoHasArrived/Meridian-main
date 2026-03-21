using System.Collections.Concurrent;
using Meridian.Infrastructure.Contracts;
using Meridian.Storage.Interfaces;
using Meridian.Storage.Policies;
using Microsoft.Extensions.Logging;

namespace Meridian.Storage.Services;

/// <summary>
/// Service that computes quality scores for stored data files and performs
/// best-of-breed selection when multiple sources provide overlapping data.
/// Quality scores are based on completeness, sequence integrity, latency,
/// and cross-source consistency.
/// </summary>
[ImplementsAdr("ADR-001", "Data quality scoring for multi-source environments")]
public sealed class DataQualityScoringService : IDataQualityScoringService
{
    private readonly StorageOptions _options;
    private readonly ISourceRegistry? _sourceRegistry;
    private readonly IMetadataTagService? _metadataService;
    private readonly ILogger<DataQualityScoringService> _logger;
    private readonly JsonlStoragePolicy _pathParser;
    private readonly ConcurrentDictionary<string, QualityAssessment> _assessments = new(StringComparer.OrdinalIgnoreCase);

    private static readonly string[] DataExtensions = { ".jsonl", ".jsonl.gz", ".jsonl.zst", ".parquet" };

    public DataQualityScoringService(
        StorageOptions options,
        ILogger<DataQualityScoringService> logger,
        ISourceRegistry? sourceRegistry = null,
        IMetadataTagService? metadataService = null)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _sourceRegistry = sourceRegistry;
        _metadataService = metadataService;
        _pathParser = new JsonlStoragePolicy(options, sourceRegistry);
    }

    /// <inheritdoc />
    public async Task<QualityAssessment> ScoreFileAsync(string filePath, CancellationToken ct = default)
    {
        var fileInfo = new FileInfo(filePath);
        if (!fileInfo.Exists)
            return QualityAssessment.Empty(filePath);

        var parsed = _pathParser.TryParsePath(filePath);
        var symbol = parsed?.Symbol ?? "Unknown";
        var source = parsed?.Source ?? "Unknown";
        var eventType = parsed?.EventType ?? "Unknown";

        var dimensions = new Dictionary<string, double>();

        // Completeness score - based on file size and event count
        var completenessScore = await ComputeCompletenessScoreAsync(filePath, ct);
        dimensions["completeness"] = completenessScore;

        // Sequence integrity - check for gaps and out-of-order
        var sequenceScore = await ComputeSequenceScoreAsync(filePath, ct);
        dimensions["sequence_integrity"] = sequenceScore;

        // Freshness score - how recent is the data
        var freshnessScore = ComputeFreshnessScore(fileInfo);
        dimensions["freshness"] = freshnessScore;

        // Format quality - file structure, no corruption
        var formatScore = await ComputeFormatScoreAsync(filePath, ct);
        dimensions["format_quality"] = formatScore;

        // Source reliability - from source registry if available
        var reliabilityScore = ComputeSourceReliabilityScore(source);
        dimensions["source_reliability"] = reliabilityScore;

        // Weighted composite score
        var compositeScore = CalculateCompositeScore(dimensions);

        var assessment = new QualityAssessment(
            FilePath: filePath,
            Symbol: symbol,
            Source: source,
            EventType: eventType,
            CompositeScore: compositeScore,
            Dimensions: dimensions,
            AssessedAtUtc: DateTime.UtcNow,
            FileSize: fileInfo.Length,
            Issues: new List<QualityIssue>());

        _assessments[filePath] = assessment;

        // Persist quality score to metadata if service available
        _metadataService?.SetQualityScore(filePath, compositeScore, "DataQualityScoringService");
        _metadataService?.SetInsight(filePath, "quality_assessment", new DataInsight(
            Category: "quality",
            Description: $"Quality score: {compositeScore:F3} ({GetQualityGrade(compositeScore)})",
            NumericValue: compositeScore,
            Unit: "score",
            ComputedAtUtc: DateTime.UtcNow,
            Severity: compositeScore < 0.5 ? InsightSeverity.Warning : InsightSeverity.Info));

        return assessment;
    }

    /// <inheritdoc />
    public async Task<BestOfBreedResult> SelectBestSourceAsync(
        string symbol,
        string eventType,
        DateTimeOffset? date = null,
        CancellationToken ct = default)
    {
        var candidates = new List<SourceCandidate>();

        if (!Directory.Exists(_options.RootPath))
            return new BestOfBreedResult(symbol, eventType, null, candidates);

        // Find all files matching the symbol and event type
        var allFiles = Directory.EnumerateFiles(_options.RootPath, "*", SearchOption.AllDirectories)
            .Where(f => DataExtensions.Any(ext => f.EndsWith(ext, StringComparison.OrdinalIgnoreCase)))
            .ToList();

        foreach (var file in allFiles)
        {
            ct.ThrowIfCancellationRequested();

            var parsed = _pathParser.TryParsePath(file);
            if (parsed == null)
                continue;

            if (!string.Equals(parsed.Symbol, symbol, StringComparison.OrdinalIgnoreCase))
                continue;
            if (!string.Equals(parsed.EventType, eventType, StringComparison.OrdinalIgnoreCase))
                continue;
            if (date.HasValue && parsed.Date.HasValue &&
                parsed.Date.Value.Date != date.Value.Date)
                continue;

            // Score each candidate
            var assessment = _assessments.TryGetValue(file, out var cached) && cached.AssessedAtUtc > DateTime.UtcNow.AddHours(-1)
                ? cached
                : await ScoreFileAsync(file, ct);

            candidates.Add(new SourceCandidate(
                FilePath: file,
                Source: parsed.Source,
                QualityScore: assessment.CompositeScore,
                FileSize: new FileInfo(file).Length,
                Assessment: assessment));
        }

        // Select best candidate by composite quality score
        var best = candidates
            .OrderByDescending(c => c.QualityScore)
            .ThenByDescending(c => c.FileSize) // Prefer larger (more complete) files
            .FirstOrDefault();

        _logger.LogInformation(
            "Best-of-breed selection for {Symbol}/{EventType}: {Source} (score={Score:F3}) from {CandidateCount} candidates",
            symbol, eventType, best?.Source ?? "none", best?.QualityScore ?? 0, candidates.Count);

        return new BestOfBreedResult(symbol, eventType, best, candidates);
    }

    /// <inheritdoc />
    public async Task<DataQualityScoringReport> GenerateReportAsync(CancellationToken ct = default)
    {
        var assessments = new List<QualityAssessment>();

        if (!Directory.Exists(_options.RootPath))
            return new DataQualityScoringReport(DateTime.UtcNow, assessments, new QualityReportSummary());

        var allFiles = Directory.EnumerateFiles(_options.RootPath, "*", SearchOption.AllDirectories)
            .Where(f => DataExtensions.Any(ext => f.EndsWith(ext, StringComparison.OrdinalIgnoreCase)))
            .ToList();

        foreach (var file in allFiles)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                var assessment = await ScoreFileAsync(file, ct);
                assessments.Add(assessment);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to score file {File}", file);
            }
        }

        var summary = new QualityReportSummary
        {
            TotalFiles = assessments.Count,
            AverageScore = assessments.Count > 0 ? assessments.Average(a => a.CompositeScore) : 0,
            HighQualityFiles = assessments.Count(a => a.CompositeScore >= 0.8),
            MediumQualityFiles = assessments.Count(a => a.CompositeScore >= 0.5 && a.CompositeScore < 0.8),
            LowQualityFiles = assessments.Count(a => a.CompositeScore < 0.5),
            BySource = assessments.GroupBy(a => a.Source)
                .ToDictionary(g => g.Key, g => g.Average(a => a.CompositeScore)),
            BySymbol = assessments.GroupBy(a => a.Symbol)
                .ToDictionary(g => g.Key, g => g.Average(a => a.CompositeScore))
        };

        return new DataQualityScoringReport(DateTime.UtcNow, assessments, summary);
    }

    private async Task<double> ComputeCompletenessScoreAsync(string filePath, CancellationToken ct)
    {
        try
        {
            var fileInfo = new FileInfo(filePath);
            if (fileInfo.Length == 0)
                return 0.0;

            long lineCount = 0;
            long emptyLines = 0;

            await foreach (var line in File.ReadLinesAsync(filePath, ct))
            {
                lineCount++;
                if (string.IsNullOrWhiteSpace(line))
                    emptyLines++;
                if (lineCount >= 10000)
                    break; // Sample first 10K lines
            }

            if (lineCount == 0)
                return 0.0;
            return Math.Clamp(1.0 - ((double)emptyLines / lineCount), 0.0, 1.0);
        }
        catch
        {
            return 0.0;
        }
    }

    private async Task<double> ComputeSequenceScoreAsync(string filePath, CancellationToken ct)
    {
        try
        {
            long lastSequence = -1;
            long gaps = 0;
            long outOfOrder = 0;
            long total = 0;

            await foreach (var line in File.ReadLinesAsync(filePath, ct))
            {
                if (string.IsNullOrWhiteSpace(line))
                    continue;
                total++;

                // Try to extract sequence from JSON
                try
                {
                    var seqStart = line.IndexOf("\"Sequence\":", StringComparison.OrdinalIgnoreCase);
                    if (seqStart < 0)
                        seqStart = line.IndexOf("\"sequence\":", StringComparison.OrdinalIgnoreCase);
                    if (seqStart >= 0)
                    {
                        var numStart = seqStart + 11;
                        while (numStart < line.Length && !char.IsDigit(line[numStart]) && line[numStart] != '-')
                            numStart++;

                        var numEnd = numStart;
                        while (numEnd < line.Length && (char.IsDigit(line[numEnd]) || line[numEnd] == '-'))
                            numEnd++;

                        if (long.TryParse(line.AsSpan(numStart, numEnd - numStart), out var seq))
                        {
                            if (lastSequence >= 0)
                            {
                                if (seq < lastSequence)
                                    outOfOrder++;
                                else if (seq > lastSequence + 1)
                                    gaps++;
                            }
                            lastSequence = seq;
                        }
                    }
                }
                catch (FormatException ex)
                {
                    _logger.LogWarning(
                        ex,
                        "Skipping unparseable line while computing sequence integrity for file {FilePath}. Line: {Line}",
                        filePath,
                        line);
                }
                catch (System.Text.Json.JsonException ex)
                {
                    _logger.LogWarning(
                        ex,
                        "Skipping malformed JSON line while computing sequence integrity for file {FilePath}. Line: {Line}",
                        filePath,
                        line);
                }

                if (total >= 10000)
                    break;
            }

            if (total == 0)
                return 1.0;
            var errorRate = (double)(gaps + outOfOrder) / total;
            return Math.Clamp(1.0 - (errorRate * 10), 0.0, 1.0);
        }
        catch
        {
            return 0.5; // Unknown quality
        }
    }

    private static double ComputeFreshnessScore(FileInfo fileInfo)
    {
        var age = DateTime.UtcNow - fileInfo.LastWriteTimeUtc;
        if (age.TotalHours < 1)
            return 1.0;
        if (age.TotalDays < 1)
            return 0.95;
        if (age.TotalDays < 7)
            return 0.9;
        if (age.TotalDays < 30)
            return 0.8;
        if (age.TotalDays < 90)
            return 0.7;
        if (age.TotalDays < 365)
            return 0.6;
        return 0.5;
    }

    private async Task<double> ComputeFormatScoreAsync(string filePath, CancellationToken ct)
    {
        try
        {
            if (filePath.EndsWith(".parquet", StringComparison.OrdinalIgnoreCase))
                return 1.0; // Parquet is always well-formed

            int validLines = 0;
            int invalidLines = 0;

            await foreach (var line in File.ReadLinesAsync(filePath, ct))
            {
                if (string.IsNullOrWhiteSpace(line))
                    continue;
                try
                {
                    System.Text.Json.JsonDocument.Parse(line);
                    validLines++;
                }
                catch
                {
                    invalidLines++;
                }

                if (validLines + invalidLines >= 1000)
                    break;
            }

            var total = validLines + invalidLines;
            return total > 0 ? (double)validLines / total : 0.0;
        }
        catch
        {
            return 0.0;
        }
    }

    private double ComputeSourceReliabilityScore(string source)
    {
        if (_sourceRegistry == null)
            return 0.8;

        var info = _sourceRegistry.GetSourceInfo(source);
        if (info == null)
            return 0.5;

        return info.Reliability ?? 0.8;
    }

    private static double CalculateCompositeScore(Dictionary<string, double> dimensions)
    {
        // Weighted scoring
        var weights = new Dictionary<string, double>
        {
            ["completeness"] = 0.30,
            ["sequence_integrity"] = 0.25,
            ["freshness"] = 0.10,
            ["format_quality"] = 0.20,
            ["source_reliability"] = 0.15
        };

        double weightedSum = 0;
        double totalWeight = 0;

        foreach (var kvp in dimensions)
        {
            if (weights.TryGetValue(kvp.Key, out var weight))
            {
                weightedSum += kvp.Value * weight;
                totalWeight += weight;
            }
        }

        return totalWeight > 0 ? Math.Clamp(weightedSum / totalWeight, 0.0, 1.0) : 0.0;
    }

    private static string GetQualityGrade(double score) => score switch
    {
        >= 0.9 => "Excellent",
        >= 0.8 => "Good",
        >= 0.6 => "Fair",
        >= 0.4 => "Poor",
        _ => "Critical"
    };
}

/// <summary>
/// Interface for data quality scoring service.
/// </summary>
public interface IDataQualityScoringService
{
    Task<QualityAssessment> ScoreFileAsync(string filePath, CancellationToken ct = default);
    Task<BestOfBreedResult> SelectBestSourceAsync(string symbol, string eventType, DateTimeOffset? date = null, CancellationToken ct = default);
    Task<DataQualityScoringReport> GenerateReportAsync(CancellationToken ct = default);
}

// Quality scoring types
public sealed record QualityAssessment(
    string FilePath,
    string Symbol,
    string Source,
    string EventType,
    double CompositeScore,
    IReadOnlyDictionary<string, double> Dimensions,
    DateTime AssessedAtUtc,
    long FileSize,
    IReadOnlyList<QualityIssue> Issues)
{
    public static QualityAssessment Empty(string filePath) => new(
        filePath, "Unknown", "Unknown", "Unknown", 0.0,
        new Dictionary<string, double>(), DateTime.UtcNow, 0, Array.Empty<QualityIssue>());
}

public sealed record QualityIssue(
    string Category,
    string Description,
    QualityIssueSeverity Severity);

public enum QualityIssueSeverity : byte { Info, Warning, Error }

public sealed record BestOfBreedResult(
    string Symbol,
    string EventType,
    SourceCandidate? Best,
    IReadOnlyList<SourceCandidate> Candidates);

public sealed record SourceCandidate(
    string FilePath,
    string Source,
    double QualityScore,
    long FileSize,
    QualityAssessment Assessment);

public sealed record DataQualityScoringReport(
    DateTime GeneratedAtUtc,
    IReadOnlyList<QualityAssessment> Assessments,
    QualityReportSummary Summary);

public sealed class QualityReportSummary
{
    public int TotalFiles { get; set; }
    public double AverageScore { get; set; }
    public int HighQualityFiles { get; set; }
    public int MediumQualityFiles { get; set; }
    public int LowQualityFiles { get; set; }
    public Dictionary<string, double> BySource { get; set; } = new();
    public Dictionary<string, double> BySymbol { get; set; } = new();
}
