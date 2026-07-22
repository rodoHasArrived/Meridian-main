using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using Meridian.Contracts.Operations;
using Meridian.Domain.Events;
using Meridian.Storage.Archival;
using Meridian.Storage.Interfaces;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Meridian.Storage.Services;

/// <summary>
/// Service for data quality scoring, best-of-breed selection, and quality monitoring.
/// </summary>
public sealed class DataQualityService : IDataQualityService
{
    private readonly StorageOptions _options;
    private readonly ISourceRegistry? _sourceRegistry;
    private readonly ILogger<DataQualityService> _logger;
    private readonly IQualityTrendStore _trendStore;
    private readonly ConcurrentDictionary<string, DataQualityScore> _scoreCache = new();
    private readonly ConcurrentDictionary<string, QualityTrend> _trendCache = new();

    public DataQualityService(
        StorageOptions options,
        ISourceRegistry? sourceRegistry = null,
        IQualityTrendStore? trendStore = null,
        ILogger<DataQualityService>? logger = null)
    {
        _options = options;
        _sourceRegistry = sourceRegistry;
        _trendStore = trendStore ?? new FileQualityTrendStore(options);
        _logger = logger ?? NullLogger<DataQualityService>.Instance;
    }

    public async Task<DataQualityScore> ScoreAsync(string path, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var sourcePath = path;
        var operationId = $"quality-evaluation-{Guid.NewGuid():N}";
        var startedAtUtc = DateTimeOffset.UtcNow;
        try
        {
            ct.ThrowIfCancellationRequested();
            sourcePath = Path.GetFullPath(path);
            return await ScoreCoreAsync(sourcePath, operationId, startedAtUtc, ct).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            var blocked = exception is OperationCanceledException && ct.IsCancellationRequested;
            var failed = CreateFailedQualityScore(
                sourcePath,
                operationId,
                startedAtUtc,
                exception,
                blocked);
            try
            {
                await PersistFailureOutcomeAsync(failed.Outcome!, CancellationToken.None).ConfigureAwait(false);
            }
            catch (Exception persistenceException)
            {
                _logger.LogError(
                    persistenceException,
                    "Failed to retain terminal quality outcome {OperationId}; returning the failure receipt to the caller.",
                    operationId);
            }
            return failed;
        }
    }

    private async Task<DataQualityScore> ScoreCoreAsync(
        string path,
        string operationId,
        DateTimeOffset startedAtUtc,
        CancellationToken ct)
    {
        var retainedInput = await RetainInputSnapshotAsync(path, ct).ConfigureAwait(false);
        var scorePath = retainedInput.SnapshotPath;
        var dimensions = new List<QualityDimension>();

        // Calculate each quality dimension
        var completeness = await CalculateCompletenessAsync(scorePath, ct);
        dimensions.Add(new QualityDimension("Completeness", completeness.Score, 0.20, completeness.Issues));

        var accuracy = await CalculateAccuracyAsync(scorePath, ct);
        dimensions.Add(new QualityDimension("Accuracy", accuracy.Score, 0.20, accuracy.Issues));

        var timeliness = await CalculateTimelinessAsync(scorePath, ct);
        dimensions.Add(new QualityDimension("Timeliness", timeliness.Score, 0.15, timeliness.Issues));

        var consistency = await CalculateConsistencyAsync(scorePath, ct);
        dimensions.Add(new QualityDimension("Consistency", consistency.Score, 0.20, consistency.Issues));

        var integrity = await CalculateIntegrityAsync(scorePath, ct);
        dimensions.Add(new QualityDimension("Integrity", integrity.Score, 0.15, integrity.Issues));

        var continuity = await CalculateContinuityAsync(scorePath, ct);
        dimensions.Add(new QualityDimension("Continuity", continuity.Score, 0.10, continuity.Issues));

        // Calculate weighted overall score
        var overallScore = dimensions.Sum(d => d.Score * d.Weight);

        var score = new DataQualityScore(
            Path: retainedInput.SourcePath,
            EvaluatedAt: DateTimeOffset.UtcNow,
            OverallScore: Math.Round(overallScore, 4),
            Dimensions: dimensions.ToArray()
        );

        var outcome = await PersistQualityHistoryAsync(
            retainedInput,
            score,
            operationId,
            startedAtUtc,
            ct).ConfigureAwait(false);
        score = score with { Outcome = outcome };
        _scoreCache[retainedInput.SourcePath] = score;
        return score;
    }

    private async Task<VerifiedOperationOutcome> PersistQualityHistoryAsync(
        RetainedQualityInput retainedInput,
        DataQualityScore score,
        string operationId,
        DateTimeOffset startedAtUtc,
        CancellationToken ct)
    {
        const string inputEvidenceId = "quality-input";
        const string resultEvidenceId = "quality-result";
        var issueMessages = score.Dimensions
            .SelectMany(static dimension => dimension.Issues.Select(issue => $"{dimension.Name}: {issue}"))
            .ToArray();
        var hasWarnings = issueMessages.Length > 0;
        var completedAtUtc = DateTimeOffset.UtcNow;
        var symbol = ExtractSymbol(retainedInput.SourcePath);
        var date = DateOnly.FromDateTime(score.EvaluatedAt.UtcDateTime);
        var provider = ExtractProvider(retainedInput.SourcePath);
        const string rulesetVersion = "data-quality.v1";
        var dimensionScores = score.Dimensions.ToDictionary(
            static dimension => dimension.Name,
            static dimension => dimension.Score,
            StringComparer.OrdinalIgnoreCase);
        var resultHash = QualityTrendResultHash.Compute(
            operationId,
            rulesetVersion,
            retainedInput.InputHashSha256,
            symbol,
            date,
            provider,
            score.EvaluatedAt,
            score.OverallScore,
            dimensionScores);
        var inputEvidence = new OperationEvidenceReference(
            inputEvidenceId,
            "input-file",
            $"Immutable quality evaluation snapshot for {Path.GetFileName(retainedInput.SourcePath)}.",
            new Uri(retainedInput.SnapshotPath).AbsoluteUri,
            retainedInput.InputHashSha256,
            completedAtUtc);
        var resultEvidence = new OperationEvidenceReference(
            resultEvidenceId,
            "quality-result",
            "Canonical hash of the evaluation identity, ruleset, retained input, aggregate score, and dimension scores.",
            ContentHashSha256: resultHash,
            CapturedAtUtc: completedAtUtc);
        var issues = hasWarnings
            ? new[]
            {
                new OperationIssue(
                    "quality-observations",
                    string.Join("; ", issueMessages),
                    OperationIssueSeverity.Warning,
                    EvidenceId: resultEvidenceId)
            }
            : Array.Empty<OperationIssue>();
        var recovery = hasWarnings
            ? new[]
            {
                new OperationRecoveryAction(
                    "review-quality-observations",
                    "Review quality observations",
                    "Inspect the retained input and dimension scores, repair the source data when appropriate, then rerun scoring.",
                    Retryable: true,
                    RequiresHumanAction: true)
                {
                    EvidenceIds = [inputEvidenceId, resultEvidenceId]
                }
            }
            : Array.Empty<OperationRecoveryAction>();
        var outcome = VerifiedOperationOutcomeValidator.ValidateAndThrow(new VerifiedOperationOutcome(
            operationId,
            "data-quality-evaluation",
            hasWarnings ? OperationTerminalState.CompletedWithWarnings : OperationTerminalState.Succeeded,
            startedAtUtc,
            completedAtUtc,
            AttemptNumber: 1,
            CorrelationId: operationId,
            InputHashSha256: retainedInput.InputHashSha256,
            Postconditions:
            [
                new OperationPostcondition(
                    "quality-dimensions-evaluated",
                    "All configured quality dimensions were evaluated against the retained input.",
                    OperationPostconditionState.Satisfied,
                    Required: true,
                    EvidenceIds: [inputEvidenceId, resultEvidenceId])
            ],
            Evidence: [inputEvidence, resultEvidence],
            Artifacts: [],
            Issues: issues,
            Recovery: recovery));

        await _trendStore.AppendAsync(
            new QualityTrendPoint(
                Symbol: symbol,
                Date: date,
                Provider: provider,
                ScoredAt: score.EvaluatedAt,
                OverallScore: score.OverallScore,
                DimensionScores: dimensionScores)
            {
                EvaluationId = operationId,
                InputHashSha256 = retainedInput.InputHashSha256,
                ResultHashSha256 = resultHash,
                RulesetVersion = rulesetVersion,
                Outcome = outcome
            },
            ct).ConfigureAwait(false);
        return outcome;
    }

    private DataQualityScore CreateFailedQualityScore(
        string sourcePath,
        string operationId,
        DateTimeOffset startedAtUtc,
        Exception exception,
        bool blocked)
    {
        var completedAtUtc = DateTimeOffset.UtcNow;
        var inputHash = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(
            $"meridian.data-quality-request.v1\n{sourcePath.Length}:{sourcePath}")));
        const string sourceEvidenceId = "quality-source-request";
        var evidence = new OperationEvidenceReference(
            sourceEvidenceId,
            "source-request",
            $"Canonical request identity for the failed quality evaluation of '{sourcePath}'.",
            ContentHashSha256: inputHash,
            CapturedAtUtc: startedAtUtc);
        var state = blocked ? OperationTerminalState.Blocked : OperationTerminalState.Failed;
        var guidance = blocked
            ? "Retry the quality evaluation when cancellation is cleared."
            : "Inspect the retained failure receipt, repair source access or quality-history storage, then retry with a new operation ID.";
        var outcome = VerifiedOperationOutcomeValidator.ValidateAndThrow(new VerifiedOperationOutcome(
            operationId,
            "data-quality-evaluation",
            state,
            startedAtUtc,
            completedAtUtc,
            AttemptNumber: 1,
            CorrelationId: operationId,
            InputHashSha256: inputHash,
            Postconditions:
            [
                new OperationPostcondition(
                    "quality-evaluation-retained",
                    "The quality evaluation and its verified trend evidence were retained.",
                    OperationPostconditionState.NotSatisfied,
                    Required: true,
                    EvidenceIds: [sourceEvidenceId])
            ],
            Evidence: [evidence],
            Artifacts: [],
            Issues:
            [
                new OperationIssue(
                    blocked ? "quality-evaluation-cancelled" : "quality-evaluation-failed",
                    exception.Message,
                    OperationIssueSeverity.Error,
                    EvidenceId: sourceEvidenceId)
            ],
            Recovery:
            [
                new OperationRecoveryAction(
                    "retry-quality-evaluation",
                    "Repair and retry quality evaluation",
                    guidance,
                    Retryable: true,
                    RequiresHumanAction: !blocked)
                {
                    EvidenceIds = [sourceEvidenceId]
                }
            ]));

        return new DataQualityScore(
            sourcePath,
            completedAtUtc,
            OverallScore: 0,
            Dimensions:
            [
                new QualityDimension(
                    "Evaluation",
                    0,
                    1,
                    [exception.Message])
            ])
        {
            Outcome = outcome
        };
    }

    private async Task PersistFailureOutcomeAsync(
        VerifiedOperationOutcome outcome,
        CancellationToken ct)
    {
        var directory = Path.Combine(_options.RootPath, "quality", "outcomes");
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, $"{outcome.OperationId}.json");
        var json = JsonSerializer.Serialize(
            outcome,
            OperationsContractsJsonContext.Default.VerifiedOperationOutcome);
        await AtomicFileWriter.WriteAsync(path, json, ct).ConfigureAwait(false);
    }

    private async Task<RetainedQualityInput> RetainInputSnapshotAsync(string path, CancellationToken ct)
    {
        var sourcePath = Path.GetFullPath(path);
        if (!File.Exists(sourcePath))
            throw new FileNotFoundException($"File not found: {sourcePath}", sourcePath);

        var evidenceDirectory = Path.Combine(_options.RootPath, "quality", "evidence");
        Directory.CreateDirectory(evidenceDirectory);
        var sourceName = Path.GetFileName(sourcePath);
        var suffixIndex = sourceName.IndexOf('.', StringComparison.Ordinal);
        var suffix = suffixIndex >= 0 ? sourceName[suffixIndex..] : ".snapshot";
        var snapshotPath = Path.Combine(evidenceDirectory, $"{Guid.NewGuid():N}{suffix}");
        var temporaryPath = snapshotPath + ".tmp";
        try
        {
            var sourceLastWriteUtc = File.GetLastWriteTimeUtc(sourcePath);
            await using (var source = new FileStream(
                             sourcePath,
                             FileMode.Open,
                             FileAccess.Read,
                             FileShare.Read,
                             bufferSize: 81920,
                             FileOptions.Asynchronous | FileOptions.SequentialScan))
            await using (var destination = new FileStream(
                             temporaryPath,
                             FileMode.CreateNew,
                             FileAccess.ReadWrite,
                             FileShare.None,
                             bufferSize: 81920,
                             FileOptions.Asynchronous | FileOptions.SequentialScan))
            {
                await source.CopyToAsync(destination, ct).ConfigureAwait(false);
                await destination.FlushAsync(ct).ConfigureAwait(false);
            }

            await using var snapshot = new FileStream(
                temporaryPath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                bufferSize: 81920,
                FileOptions.Asynchronous | FileOptions.SequentialScan);
            var inputHash = Convert.ToHexStringLower(
                await SHA256.HashDataAsync(snapshot, ct).ConfigureAwait(false));
            snapshot.Close();
            File.Move(temporaryPath, snapshotPath);
            File.SetLastWriteTimeUtc(snapshotPath, sourceLastWriteUtc);
            return new RetainedQualityInput(sourcePath, snapshotPath, inputHash);
        }
        finally
        {
            if (File.Exists(temporaryPath))
                File.Delete(temporaryPath);
        }
    }

    public async Task<DataQualityReport> GenerateReportAsync(QualityReportOptions options, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(options);
        var scores = new List<DataQualityScore>();
        var reportIssues = new List<DataQualityReportIssue>();
        var recommendations = new List<string>();
        var attempted = 0;

        foreach (var path in options.Paths)
        {
            ct.ThrowIfCancellationRequested();
            if (Directory.Exists(path))
            {
                string[] files;
                try
                {
                    files = Directory.EnumerateFiles(path, "*.jsonl*", SearchOption.AllDirectories).ToArray();
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    attempted++;
                    reportIssues.Add(new DataQualityReportIssue(path, ex.Message, ex.GetType().FullName));
                    _logger.LogWarning(ex, "Failed to enumerate quality-report directory {DirectoryPath}", path);
                    continue;
                }

                foreach (var file in files)
                {
                    ct.ThrowIfCancellationRequested();
                    attempted++;
                    try
                    {
                        var score = await ScoreAsync(file, ct);
                        if (IsFailedOutcome(score))
                        {
                            reportIssues.Add(ToReportIssue(file, score));
                        }
                        else
                        {
                            scores.Add(score);
                        }
                    }
                    catch (OperationCanceledException)
                    {
                        throw;
                    }
                    catch (Exception ex)
                    {
                        reportIssues.Add(new DataQualityReportIssue(file, ex.Message, ex.GetType().FullName));
                        _logger.LogWarning(ex, "Failed to score file {FilePath} during quality report generation", file);
                    }
                }
            }
            else
            {
                attempted++;
                try
                {
                    var score = await ScoreAsync(path, ct);
                    if (IsFailedOutcome(score))
                    {
                        reportIssues.Add(ToReportIssue(path, score));
                    }
                    else
                    {
                        scores.Add(score);
                    }
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    reportIssues.Add(new DataQualityReportIssue(path, ex.Message, ex.GetType().FullName));
                    _logger.LogWarning(ex, "Failed to score individual file {FilePath} for quality report", path);
                }
            }
        }

        var lowQualityScores = scores
            .Where(score => score.OverallScore < options.MinScoreThreshold)
            .OrderBy(score => score.OverallScore)
            .ToList();

        // Generate recommendations
        if (options.IncludeRecommendations)
        {
            recommendations = GenerateRecommendations(lowQualityScores);
        }

        // Calculate summary statistics
        var avgScore = scores.Count > 0 ? scores.Average(s => s.OverallScore) : 0.0;
        var byDimension = new Dictionary<string, double>();

        foreach (var dim in new[] { "Completeness", "Accuracy", "Timeliness", "Consistency", "Integrity", "Continuity" })
        {
            var dimScores = scores
                .SelectMany(s => s.Dimensions)
                .Where(d => d.Name == dim)
                .Select(d => d.Score)
                .ToList();

            if (dimScores.Count > 0)
                byDimension[dim] = dimScores.Average();
        }

        return new DataQualityReport(
            GeneratedAt: DateTimeOffset.UtcNow,
            FilesAnalyzed: scores.Count,
            AverageScore: avgScore,
            ScoresByDimension: byDimension,
            LowQualityFiles: lowQualityScores.Take(20).ToList(),
            Recommendations: recommendations
        )
        {
            FilesAttempted = attempted,
            FilesSucceeded = scores.Count,
            FilesFailed = reportIssues.Count,
            Issues = reportIssues
        };
    }

    public Task<DataQualityScore[]> GetHistoricalScoresAsync(string path, TimeSpan window, CancellationToken ct = default)
    {
        // In production, this would query stored historical scores
        var cacheKey = Path.GetFullPath(path);
        if (_scoreCache.TryGetValue(cacheKey, out var cached))
        {
            return Task.FromResult(new[] { cached });
        }
        return Task.FromResult(Array.Empty<DataQualityScore>());
    }

    public async Task<SourceRanking[]> RankSourcesAsync(string symbol, DateTimeOffset date, MarketEventType type, CancellationToken ct = default)
    {
        var rankings = new List<SourceRanking>();

        if (_sourceRegistry == null)
            return rankings.ToArray();

        var sources = _sourceRegistry.GetAllSources().Where(s => s.Enabled);

        foreach (var source in sources)
        {
            // Find data file for this source/symbol/date/type combination
            var possiblePaths = GetPossiblePaths(source.Id, symbol, date.Date, type);
            DataQualityScore? score = null;

            foreach (var path in possiblePaths)
            {
                if (File.Exists(path))
                {
                    try
                    {
                        score = await ScoreAsync(path, ct);
                        if (!IsFailedOutcome(score))
                            break;

                        _logger.LogWarning(
                            "Quality scoring for source {SourceId} returned {State}: {Issue}",
                            source.Id,
                            score.Outcome!.State,
                            score.Outcome.Issues.FirstOrDefault()?.Message ?? "No issue detail was retained.");
                        score = null;
                    }
                    catch (OperationCanceledException)
                    {
                        throw;
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Failed to score source file {FilePath} for source {SourceId} ranking", path, source.Id);
                    }
                }
            }

            if (score != null)
            {
                rankings.Add(new SourceRanking(
                    Source: source.Id,
                    QualityScore: score.OverallScore,
                    EventCount: await CountEventsAsync(possiblePaths.FirstOrDefault() ?? "", ct),
                    GapCount: CountGaps(score),
                    Latency: source.LatencyMs ?? 0,
                    IsRecommended: false
                ));
            }
        }

        // Sort by quality score and mark top as recommended
        var sorted = rankings.OrderByDescending(r => r.QualityScore).ToList();
        if (sorted.Count > 0)
        {
            sorted[0] = sorted[0] with { IsRecommended = true };
        }

        return sorted.ToArray();
    }

    private static bool IsFailedOutcome(DataQualityScore score) =>
        score.Outcome?.State is OperationTerminalState.Failed or OperationTerminalState.Blocked;

    private static DataQualityReportIssue ToReportIssue(string path, DataQualityScore score) =>
        new(
            path,
            score.Outcome?.Issues.FirstOrDefault()?.Message ?? "Quality evaluation did not complete.",
            score.Outcome?.State.ToString());

    public async Task<ConsolidatedDataset> CreateGoldenRecordAsync(string symbol, DateTimeOffset date, ConsolidationOptions options, CancellationToken ct = default)
    {
        var rankings = await RankSourcesAsync(symbol, date, MarketEventType.Trade, ct);
        var selectedSources = new List<string>();
        var eventCount = 0L;
        var gapsFilled = 0;

        if (rankings.Length == 0)
        {
            return new ConsolidatedDataset(
                Symbol: symbol,
                Date: date,
                SelectedSources: Array.Empty<string>(),
                TotalEvents: 0,
                GapsFilled: 0,
                QualityScore: 0,
                OutputPath: null
            );
        }

        // Select primary source
        var primary = options.Strategy switch
        {
            SourceSelectionStrategy.HighestQualityScore => rankings.OrderByDescending(r => r.QualityScore).First(),
            SourceSelectionStrategy.MostComplete => rankings.OrderByDescending(r => r.EventCount).First(),
            SourceSelectionStrategy.LowestLatency => rankings.OrderBy(r => r.Latency).First(),
            SourceSelectionStrategy.MostConsistent => rankings.OrderBy(r => r.GapCount).First(),
            _ => rankings.First(r => r.IsRecommended)
        };

        selectedSources.Add(primary.Source);
        eventCount = primary.EventCount;

        // Fill gaps from alternate sources if enabled
        if (options.FillGapsFromAlternates && primary.GapCount > 0)
        {
            foreach (var alt in rankings.Where(r => r.Source != primary.Source))
            {
                // In production, would read and merge actual events
                gapsFilled += Math.Min(alt.EventCount > 0 ? 1 : 0, primary.GapCount);
                if (gapsFilled > 0)
                    selectedSources.Add(alt.Source);
            }
        }

        // Create output path
        var outputDir = Path.Combine(_options.RootPath, "consolidated", symbol, "Trade");
        Directory.CreateDirectory(outputDir);
        var outputPath = Path.Combine(outputDir, $"{date:yyyy-MM-dd}.jsonl");

        return new ConsolidatedDataset(
            Symbol: symbol,
            Date: date,
            SelectedSources: selectedSources.ToArray(),
            TotalEvents: eventCount,
            GapsFilled: gapsFilled,
            QualityScore: primary.QualityScore,
            OutputPath: outputPath
        );
    }

    public async Task<QualityTrend> GetTrendAsync(string symbol, TimeSpan window, CancellationToken ct = default)
    {
        var cacheKey = $"{symbol}_{window.TotalDays}d";

        if (_trendCache.TryGetValue(cacheKey, out var cached))
            return cached;

        var now = DateTimeOffset.UtcNow;
        var windowStart = now - window;
        var baselineStart = windowStart - window;
        var points = await _trendStore.GetPointsAsync(symbol, baselineStart, now, ct);

        var currentPoints = points.Where(p => p.ScoredAt >= windowStart).OrderBy(p => p.ScoredAt).ToArray();
        var baselinePoints = points.Where(p => p.ScoredAt >= baselineStart && p.ScoredAt < windowStart).ToArray();

        var currentScore = currentPoints.Length > 0 ? currentPoints[^1].OverallScore : 0d;
        var baselineScore = baselinePoints.Length > 0
            ? baselinePoints.Average(p => p.OverallScore)
            : (currentPoints.Length > 0 ? currentPoints[0].OverallScore : 0d);

        var improving = new List<string>();
        var degrading = new List<string>();
        var baselineDims = AverageDimensions(baselinePoints);
        var currentDims = AverageDimensions(currentPoints);

        foreach (var (name, current) in currentDims)
        {
            baselineDims.TryGetValue(name, out var previous);
            var delta = current - previous;
            if (delta >= 0.01)
                improving.Add(name);
            else if (delta <= -0.01)
                degrading.Add(name);
        }

        var scoreHistory = currentPoints.Select(p => p.ScoredAt).ToArray();
        var scoreValues = currentPoints.Select(p => p.OverallScore).ToArray();
        var slope = ComputeSlopePerDay(scoreHistory, scoreValues);
        var dimensionSeries = BuildDimensionSeries(currentPoints);

        var trend = new QualityTrend(
            Symbol: symbol,
            CurrentScore: currentScore,
            PreviousScore: baselineScore,
            TrendDirection: slope,
            DegradingDimensions: degrading.ToArray(),
            ImprovingDimensions: improving.ToArray(),
            ScoreHistory: scoreHistory,
            ScoreValues: scoreValues,
            WindowGranularity: InferGranularity(scoreHistory),
            HasConfidence: currentPoints.Length >= 4,
            IsSparseData: currentPoints.Length < 3,
            DimensionSeries: dimensionSeries
        );

        _trendCache[cacheKey] = trend;
        return trend;
    }

    private static Dictionary<string, double> AverageDimensions(IEnumerable<QualityTrendPoint> points)
    {
        var buckets = new Dictionary<string, List<double>>(StringComparer.OrdinalIgnoreCase);
        foreach (var point in points)
        {
            foreach (var (name, value) in point.DimensionScores)
            {
                if (!buckets.TryGetValue(name, out var values))
                {
                    values = new List<double>();
                    buckets[name] = values;
                }

                values.Add(value);
            }
        }

        return buckets.ToDictionary(kvp => kvp.Key, kvp => kvp.Value.Average(), StringComparer.OrdinalIgnoreCase);
    }

    private static double ComputeSlopePerDay(DateTimeOffset[] history, double[] values)
    {
        if (history.Length < 2 || values.Length < 2 || history.Length != values.Length)
            return 0d;

        var x0 = history[0];
        var xs = history.Select(h => (h - x0).TotalDays).ToArray();
        var xMean = xs.Average();
        var yMean = values.Average();

        var numerator = 0d;
        var denominator = 0d;
        for (var i = 0; i < xs.Length; i++)
        {
            var dx = xs[i] - xMean;
            numerator += dx * (values[i] - yMean);
            denominator += dx * dx;
        }

        return denominator <= 0d ? 0d : numerator / denominator;
    }

    private static IReadOnlyDictionary<string, double[]> BuildDimensionSeries(IEnumerable<QualityTrendPoint> points)
    {
        var series = new Dictionary<string, List<double>>(StringComparer.OrdinalIgnoreCase);
        foreach (var point in points)
        {
            foreach (var (name, value) in point.DimensionScores)
            {
                if (!series.TryGetValue(name, out var values))
                {
                    values = new List<double>();
                    series[name] = values;
                }

                values.Add(value);
            }
        }

        return series.ToDictionary(kvp => kvp.Key, kvp => kvp.Value.ToArray(), StringComparer.OrdinalIgnoreCase);
    }

    private static string InferGranularity(IReadOnlyList<DateTimeOffset> scoreHistory)
    {
        if (scoreHistory.Count < 2)
            return "unknown";

        var averageHours = scoreHistory.Zip(scoreHistory.Skip(1), (a, b) => (b - a).TotalHours).Average();
        return averageHours switch
        {
            <= 1 => "hour",
            <= 36 => "day",
            <= 24 * 10 => "week",
            _ => "month"
        };
    }

    private string ExtractProvider(string path)
    {
        var root = Path.GetFullPath(_options.RootPath)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var fullPath = Path.GetFullPath(path);
        if (!fullPath.StartsWith(root, StringComparison.OrdinalIgnoreCase))
            return "unknown";

        var relative = fullPath[root.Length..].TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var parts = relative.Split(new[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar }, StringSplitOptions.RemoveEmptyEntries);
        return parts.Length > 0 ? parts[0] : "unknown";
    }

    private static string ExtractSymbol(string path)
    {
        var symbol = Path.GetFileName(Path.GetDirectoryName(Path.GetDirectoryName(path) ?? string.Empty) ?? string.Empty);
        return string.IsNullOrWhiteSpace(symbol) ? "UNKNOWN" : symbol;
    }

    public Task<QualityAlert[]> GetQualityAlertsAsync(CancellationToken ct = default)
    {
        var alerts = new List<QualityAlert>();

        foreach (var (path, score) in _scoreCache)
        {
            if (score.OverallScore < 0.85)
            {
                alerts.Add(new QualityAlert(
                    Symbol: Path.GetFileName(Path.GetDirectoryName(path)) ?? "Unknown",
                    Issue: "quality_below_threshold",
                    CurrentScore: score.OverallScore,
                    Threshold: 0.85,
                    Recommendation: "investigate_data_source"
                ));
            }
        }

        return Task.FromResult(alerts.ToArray());
    }

    private async Task<(double Score, string[] Issues)> CalculateCompletenessAsync(string path, CancellationToken ct)
    {
        var issues = new List<string>();
        var eventCount = await CountEventsAsync(path, ct);

        // Estimate expected events based on typical daily volume
        var expectedEvents = 50000L; // Would be calculated from historical average
        var score = Math.Min(1.0, (double)eventCount / expectedEvents);

        if (eventCount == 0)
        {
            issues.Add("No events found");
            score = 0;
        }
        else if (score < 0.9)
        {
            issues.Add($"Only {eventCount} events, expected ~{expectedEvents}");
        }

        return (score, issues.ToArray());
    }

    private Task<(double Score, string[] Issues)> CalculateAccuracyAsync(string path, CancellationToken ct)
    {
        // Would compare with other sources in production
        return Task.FromResult((0.95, Array.Empty<string>()));
    }

    private Task<(double Score, string[] Issues)> CalculateTimelinessAsync(string path, CancellationToken ct)
    {
        var issues = new List<string>();
        var fileInfo = new FileInfo(path);
        var age = DateTime.UtcNow - fileInfo.LastWriteTimeUtc;

        // Score based on how recent the data is
        var score = age.TotalHours switch
        {
            < 1 => 1.0,
            < 24 => 0.95,
            < 168 => 0.8,
            _ => 0.6
        };

        if (age.TotalDays > 7)
        {
            issues.Add($"Data is {age.TotalDays:F0} days old");
        }

        return Task.FromResult((score, issues.ToArray()));
    }

    private async Task<(double Score, string[] Issues)> CalculateConsistencyAsync(string path, CancellationToken ct)
    {
        var issues = new List<string>();
        var duplicates = 0;
        var schemaViolations = 0;
        var totalLines = 0;

        try
        {
            var seenSequences = new HashSet<long>();

            await foreach (var line in File.ReadLinesAsync(path, ct))
            {
                if (string.IsNullOrWhiteSpace(line))
                    continue;
                totalLines++;

                try
                {
                    var doc = JsonDocument.Parse(line);
                    if (doc.RootElement.TryGetProperty("Sequence", out var seqProp))
                    {
                        var seq = seqProp.GetInt64();
                        if (!seenSequences.Add(seq))
                            duplicates++;
                    }
                }
                catch (JsonException ex)
                {
                    schemaViolations++;
                    _logger.LogDebug(ex, "Schema violation at line {LineNumber} in {FilePath}", totalLines, path);
                }
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Consistency check failed for {FilePath}", path);
            return (0.5, new[] { "Could not read file for consistency check" });
        }

        if (totalLines == 0)
            return (1.0, Array.Empty<string>());

        var duplicateRate = (double)duplicates / totalLines;
        var violationRate = (double)schemaViolations / totalLines;

        var score = 1.0 - duplicateRate - violationRate;

        if (duplicates > 0)
            issues.Add($"{duplicates} duplicate events");
        if (schemaViolations > 0)
            issues.Add($"{schemaViolations} schema violations");

        return (Math.Max(0, score), issues.ToArray());
    }

    private async Task<(double Score, string[] Issues)> CalculateIntegrityAsync(string path, CancellationToken ct)
    {
        var issues = new List<string>();

        // Check for checksum file
        var checksumPath = path + ".sha256";
        if (File.Exists(checksumPath))
        {
            // Would verify checksum in production
            return (1.0, Array.Empty<string>());
        }

        // Check file is readable
        try
        {
            await using var stream = File.OpenRead(path);
            if (stream.Length == 0)
            {
                issues.Add("Empty file");
                return (0.0, issues.ToArray());
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Integrity check failed - file {FilePath} is unreadable", path);
            issues.Add("File unreadable");
            return (0.0, issues.ToArray());
        }

        return (0.9, issues.ToArray()); // Lower score without checksum verification
    }

    private async Task<(double Score, string[] Issues)> CalculateContinuityAsync(string path, CancellationToken ct)
    {
        var issues = new List<string>();
        var gaps = 0;
        long lastSeq = -1;
        var lineNumber = 0;

        try
        {
            await foreach (var line in File.ReadLinesAsync(path, ct))
            {
                lineNumber++;
                if (string.IsNullOrWhiteSpace(line))
                    continue;

                try
                {
                    var doc = JsonDocument.Parse(line);
                    if (doc.RootElement.TryGetProperty("Sequence", out var seqProp))
                    {
                        var seq = seqProp.GetInt64();
                        if (lastSeq >= 0 && seq != lastSeq + 1 && seq > lastSeq)
                        {
                            gaps++;
                        }
                        lastSeq = seq;
                    }
                }
                catch (JsonException ex)
                {
                    _logger.LogDebug(ex, "JSON parsing error at line {LineNumber} during continuity check of {FilePath}", lineNumber, path);
                }
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Continuity check failed for {FilePath}", path);
            return (0.5, new[] { "Could not read file for continuity check" });
        }

        var score = gaps == 0 ? 1.0 : Math.Max(0.5, 1.0 - (gaps * 0.1));

        if (gaps > 0)
            issues.Add($"{gaps} sequence gaps detected");

        return (score, issues.ToArray());
    }

    private async Task<long> CountEventsAsync(string path, CancellationToken ct)
    {
        if (!File.Exists(path))
            return 0;

        long count = 0;
        try
        {
            await foreach (var _ in File.ReadLinesAsync(path, ct))
            {
                count++;
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to count events in {FilePath}", path);
        }
        return count;
    }

    private int CountGaps(DataQualityScore score)
    {
        var continuity = score.Dimensions.FirstOrDefault(d => d.Name == "Continuity");
        if (continuity == null)
            return 0;

        var gapIssue = continuity.Issues.FirstOrDefault(i => i.Contains("gap"));
        if (gapIssue != null && int.TryParse(gapIssue.Split(' ')[0], out var gapCount))
            return gapCount;

        return 0;
    }

    private string[] GetPossiblePaths(string source, string symbol, DateTime date, MarketEventType type)
    {
        var paths = new List<string>();
        var dateStr = date.ToString("yyyy-MM-dd");
        var typeStr = type.ToString();

        // Try various naming conventions
        paths.Add(Path.Combine(_options.RootPath, source, symbol, typeStr, $"{dateStr}.jsonl"));
        paths.Add(Path.Combine(_options.RootPath, source, symbol, typeStr, $"{dateStr}.jsonl.gz"));
        paths.Add(Path.Combine(_options.RootPath, symbol, typeStr, $"{dateStr}.jsonl"));
        paths.Add(Path.Combine(_options.RootPath, dateStr, symbol, $"{typeStr}.jsonl"));

        return paths.ToArray();
    }

    private List<string> GenerateRecommendations(List<DataQualityScore> scores)
    {
        var recommendations = new List<string>();

        var avgCompleteness = scores
            .SelectMany(s => s.Dimensions)
            .Where(d => d.Name == "Completeness")
            .Select(d => d.Score)
            .DefaultIfEmpty(1.0)
            .Average();

        if (avgCompleteness < 0.8)
        {
            recommendations.Add("Consider running backfill to improve data completeness");
        }

        var lowIntegrity = scores.Where(s =>
            s.Dimensions.Any(d => d.Name == "Integrity" && d.Score < 0.9)).ToList();

        if (lowIntegrity.Count > 0)
        {
            recommendations.Add($"Run integrity checks on {lowIntegrity.Count} files with potential corruption");
        }

        return recommendations;
    }

    private sealed record RetainedQualityInput(
        string SourcePath,
        string SnapshotPath,
        string InputHashSha256);
}

/// <summary>
/// Interface for data quality service.
/// </summary>
public interface IDataQualityService
{
    Task<DataQualityScore> ScoreAsync(string path, CancellationToken ct = default);
    Task<DataQualityReport> GenerateReportAsync(QualityReportOptions options, CancellationToken ct = default);
    Task<DataQualityScore[]> GetHistoricalScoresAsync(string path, TimeSpan window, CancellationToken ct = default);
    Task<SourceRanking[]> RankSourcesAsync(string symbol, DateTimeOffset date, MarketEventType type, CancellationToken ct = default);
    Task<ConsolidatedDataset> CreateGoldenRecordAsync(string symbol, DateTimeOffset date, ConsolidationOptions options, CancellationToken ct = default);
    Task<QualityTrend> GetTrendAsync(string symbol, TimeSpan window, CancellationToken ct = default);
    Task<QualityAlert[]> GetQualityAlertsAsync(CancellationToken ct = default);
}

// Quality score types
public sealed record DataQualityScore(
    string Path,
    DateTimeOffset EvaluatedAt,
    double OverallScore,
    QualityDimension[] Dimensions
)
{
    public VerifiedOperationOutcome? Outcome { get; init; }
}

public sealed record QualityDimension(
    string Name,
    double Score,
    double Weight,
    string[] Issues
);

public sealed record DataQualityReport(
    DateTimeOffset GeneratedAt,
    int FilesAnalyzed,
    double AverageScore,
    Dictionary<string, double> ScoresByDimension,
    IReadOnlyList<DataQualityScore> LowQualityFiles,
    IReadOnlyList<string> Recommendations
)
{
    public int FilesAttempted { get; init; }
    public int FilesSucceeded { get; init; }
    public int FilesFailed { get; init; }
    public IReadOnlyList<DataQualityReportIssue> Issues { get; init; } = [];
}

public sealed record DataQualityReportIssue(
    string Path,
    string Message,
    string? ExceptionType);

public sealed record QualityReportOptions(
    string[] Paths,
    DateTimeOffset? From = null,
    DateTimeOffset? To = null,
    double MinScoreThreshold = 1.0,
    bool IncludeRecommendations = true,
    bool CompareAcrossSources = false
);

// Source ranking types
public sealed record SourceRanking(
    string Source,
    double QualityScore,
    long EventCount,
    int GapCount,
    double Latency,
    bool IsRecommended
);

public sealed record ConsolidatedDataset(
    string Symbol,
    DateTimeOffset Date,
    string[] SelectedSources,
    long TotalEvents,
    int GapsFilled,
    double QualityScore,
    string? OutputPath
);

public sealed record ConsolidationOptions(
    SourceSelectionStrategy Strategy = SourceSelectionStrategy.HighestQualityScore,
    bool FillGapsFromAlternates = true,
    bool ValidateCrossSource = true,
    decimal PriceTolerancePct = 0.01m,
    long VolumeTolerancePct = 5
);

public enum SourceSelectionStrategy : byte
{
    HighestQualityScore,
    MostComplete,
    LowestLatency,
    MostConsistent,
    Merge
}

// Trend and alert types
public sealed record QualityTrend(
    string Symbol,
    double CurrentScore,
    double PreviousScore,
    double TrendDirection,
    string[] DegradingDimensions,
    string[] ImprovingDimensions,
    DateTimeOffset[] ScoreHistory,
    double[] ScoreValues,
    string WindowGranularity = "day",
    bool HasConfidence = false,
    bool IsSparseData = true,
    IReadOnlyDictionary<string, double[]>? DimensionSeries = null
);

public sealed record QualityAlert(
    string Symbol,
    string Issue,
    double CurrentScore,
    double Threshold,
    string Recommendation
);
