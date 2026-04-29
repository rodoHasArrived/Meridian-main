using System.Text;
using System.Text.Json;

namespace Meridian.Application.Monitoring;

/// <summary>
/// Historical labeled incident dataset used for offline provider-degradation kernel calibration.
/// </summary>
public sealed record ProviderIncidentCalibrationDataset(
    string DatasetId,
    DateTimeOffset GeneratedAt,
    string Source,
    IReadOnlyList<ProviderIncidentWindow> Windows)
{
    public static async Task<ProviderIncidentCalibrationDataset> LoadAsync(string path, CancellationToken ct = default)
    {
        await using var stream = File.OpenRead(path);
        var dataset = await JsonSerializer.DeserializeAsync<ProviderIncidentCalibrationDataset>(stream, cancellationToken: ct)
            .ConfigureAwait(false);
        return dataset ?? throw new InvalidOperationException($"Failed to parse calibration dataset from '{path}'.");
    }

    public async Task SaveAsync(string path, CancellationToken ct = default)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path) ?? ".");
        await using var stream = File.Create(path);
        await JsonSerializer.SerializeAsync(stream, this, cancellationToken: ct).ConfigureAwait(false);
    }
}

/// <summary>
/// A historical replay window with precomputed signal components and labeled incident severity.
/// </summary>
public sealed record ProviderIncidentWindow(
    string Provider,
    DateTimeOffset WindowStart,
    DateTimeOffset WindowEnd,
    IncidentSeverity ObservedSeverity,
    double ConnectionScore,
    double LatencyScore,
    double ErrorRateScore,
    double ReconnectScore,
    int EventsObserved,
    int AlertVolumeObserved);

public enum IncidentSeverity : byte
{
    None = 0,
    Minor = 1,
    Moderate = 2,
    Major = 3,
    Critical = 4
}

/// <summary>
/// Snapshot of an offline calibration run, persisted for governance and promotion checks.
/// </summary>
public sealed record ProviderKernelCalibrationSnapshot(
    string SnapshotId,
    DateTimeOffset CreatedAt,
    string DatasetId,
    string BaselineKernelVersion,
    string CandidateKernelVersion,
    IReadOnlyList<SeverityThresholdMetrics> BaselineMetrics,
    IReadOnlyList<SeverityThresholdMetrics> CandidateMetrics,
    CalibrationComparisonSummary Comparison,
    string RunBy,
    bool CalibrationPass);

public sealed record SeverityThresholdMetrics(
    IncidentSeverity Severity,
    double Threshold,
    double Precision,
    double Recall,
    int TruePositives,
    int FalsePositives,
    int FalseNegatives,
    int AlertCount);

public sealed record CalibrationComparisonSummary(
    double ExpectedAlertVolumeChangePercent,
    int ExpectedAlertCountBaseline,
    int ExpectedAlertCountCandidate,
    IncidentSeverity DecisionSeverity,
    double CandidatePrecision,
    double CandidateRecall,
    double BaselinePrecision,
    double BaselineRecall);

/// <summary>
/// Candidate kernel profile used by calibration and governance promotion.
/// </summary>
public sealed record ProviderDegradationKernelProfile(
    string KernelVersion,
    ProviderDegradationConfig Config,
    IReadOnlyDictionary<IncidentSeverity, double> SeverityThresholds)
{
    public static ProviderDegradationKernelProfile Default(string kernelVersion = "default") =>
        new(
            kernelVersion,
            ProviderDegradationConfig.Default,
            new Dictionary<IncidentSeverity, double>
            {
                [IncidentSeverity.Minor] = 0.35,
                [IncidentSeverity.Moderate] = 0.50,
                [IncidentSeverity.Major] = 0.65,
                [IncidentSeverity.Critical] = ProviderDegradationConfig.Default.DegradationThreshold
            });
}

public sealed class ProviderDegradationCalibrationRunner
{
    public ProviderKernelCalibrationSnapshot Run(
        ProviderIncidentCalibrationDataset dataset,
        ProviderDegradationKernelProfile baseline,
        ProviderDegradationKernelProfile candidate,
        string runBy,
        DateTimeOffset? now = null)
    {
        ArgumentNullException.ThrowIfNull(dataset);
        ArgumentNullException.ThrowIfNull(baseline);
        ArgumentNullException.ThrowIfNull(candidate);

        var baselineMetrics = ComputeMetrics(dataset.Windows, baseline);
        var candidateMetrics = ComputeMetrics(dataset.Windows, candidate);

        var decisionSeverity = IncidentSeverity.Critical;
        var baselineDecision = baselineMetrics.Single(m => m.Severity == decisionSeverity);
        var candidateDecision = candidateMetrics.Single(m => m.Severity == decisionSeverity);

        var comparison = new CalibrationComparisonSummary(
            ExpectedAlertVolumeChangePercent: baselineDecision.AlertCount == 0
                ? (candidateDecision.AlertCount > 0 ? 100 : 0)
                : ((double)(candidateDecision.AlertCount - baselineDecision.AlertCount) / baselineDecision.AlertCount) * 100,
            ExpectedAlertCountBaseline: baselineDecision.AlertCount,
            ExpectedAlertCountCandidate: candidateDecision.AlertCount,
            DecisionSeverity: decisionSeverity,
            CandidatePrecision: candidateDecision.Precision,
            CandidateRecall: candidateDecision.Recall,
            BaselinePrecision: baselineDecision.Precision,
            BaselineRecall: baselineDecision.Recall);

        var calibrationPass = candidateDecision.Precision >= baselineDecision.Precision
            && candidateDecision.Recall >= baselineDecision.Recall;

        return new ProviderKernelCalibrationSnapshot(
            SnapshotId: Guid.NewGuid().ToString("N"),
            CreatedAt: now ?? DateTimeOffset.UtcNow,
            DatasetId: dataset.DatasetId,
            BaselineKernelVersion: baseline.KernelVersion,
            CandidateKernelVersion: candidate.KernelVersion,
            BaselineMetrics: baselineMetrics,
            CandidateMetrics: candidateMetrics,
            Comparison: comparison,
            RunBy: runBy,
            CalibrationPass: calibrationPass);
    }

    private static IReadOnlyList<SeverityThresholdMetrics> ComputeMetrics(
        IReadOnlyList<ProviderIncidentWindow> windows,
        ProviderDegradationKernelProfile profile)
    {
        var metrics = new List<SeverityThresholdMetrics>();

        foreach (var (severity, threshold) in profile.SeverityThresholds.OrderBy(kvp => kvp.Key))
        {
            var tp = 0;
            var fp = 0;
            var fn = 0;
            var alerts = 0;

            foreach (var window in windows)
            {
                var score = ComputeComposite(window, profile.Config);
                var predicted = score >= threshold;
                var actual = window.ObservedSeverity >= severity;

                if (predicted)
                {
                    alerts += window.AlertVolumeObserved > 0 ? window.AlertVolumeObserved : 1;
                }

                if (predicted && actual)
                    tp++;
                else if (predicted && !actual)
                    fp++;
                else if (!predicted && actual)
                    fn++;
            }

            var precision = tp + fp == 0 ? 1.0 : (double)tp / (tp + fp);
            var recall = tp + fn == 0 ? 1.0 : (double)tp / (tp + fn);

            metrics.Add(new SeverityThresholdMetrics(
                Severity: severity,
                Threshold: threshold,
                Precision: precision,
                Recall: recall,
                TruePositives: tp,
                FalsePositives: fp,
                FalseNegatives: fn,
                AlertCount: alerts));
        }

        return metrics;
    }

    private static double ComputeComposite(ProviderIncidentWindow window, ProviderDegradationConfig config)
    {
        var composite =
            window.ConnectionScore * config.ConnectionWeight +
            window.LatencyScore * config.LatencyWeight +
            window.ErrorRateScore * config.ErrorRateWeight +
            window.ReconnectScore * config.ReconnectWeight;

        return Math.Clamp(composite, 0.0, 1.0);
    }
}

public sealed class ProviderKernelCalibrationSnapshotStore
{
    private readonly string _rootPath;

    public ProviderKernelCalibrationSnapshotStore(string rootPath)
    {
        _rootPath = rootPath;
    }

    public async Task<string> SaveAsync(ProviderKernelCalibrationSnapshot snapshot, CancellationToken ct = default)
    {
        var dir = Path.Combine(_rootPath, "calibration", "provider-degradation");
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, $"{snapshot.CreatedAt:yyyyMMddHHmmss}_{snapshot.CandidateKernelVersion}_{snapshot.SnapshotId}.json");

        await using var stream = File.Create(path);
        await JsonSerializer.SerializeAsync(stream, snapshot, cancellationToken: ct).ConfigureAwait(false);
        return path;
    }

    public async Task<ProviderKernelCalibrationSnapshot?> GetLatestAsync(CancellationToken ct = default)
    {
        var dir = Path.Combine(_rootPath, "calibration", "provider-degradation");
        if (!Directory.Exists(dir))
        {
            return null;
        }

        var latestFile = new DirectoryInfo(dir)
            .GetFiles("*.json")
            .OrderByDescending(f => f.CreationTimeUtc)
            .FirstOrDefault();

        if (latestFile is null)
        {
            return null;
        }

        await using var stream = latestFile.OpenRead();
        return await JsonSerializer.DeserializeAsync<ProviderKernelCalibrationSnapshot>(stream, cancellationToken: ct)
            .ConfigureAwait(false);
    }
}

public sealed record ProviderKernelCalibrationPolicy(
    TimeSpan MaxSnapshotAge,
    double MinPrecision,
    double MinRecall,
    IncidentSeverity RequiredSeverity)
{
    public static ProviderKernelCalibrationPolicy Default =>
        new(TimeSpan.FromDays(14), MinPrecision: 0.65, MinRecall: 0.65, RequiredSeverity: IncidentSeverity.Critical);

    public CalibrationGateDecision Evaluate(ProviderKernelCalibrationSnapshot? snapshot, string candidateKernelVersion, DateTimeOffset? now = null)
    {
        if (snapshot is null)
        {
            return new CalibrationGateDecision(false, false, ["No calibration snapshot found for candidate kernel."]);
        }

        var errors = new List<string>();
        var evaluationTime = now ?? DateTimeOffset.UtcNow;
        var freshnessPass = evaluationTime - snapshot.CreatedAt <= MaxSnapshotAge;
        if (!freshnessPass)
        {
            errors.Add($"Calibration snapshot is stale (created {snapshot.CreatedAt:O}, max age {MaxSnapshotAge}).");
        }

        if (!string.Equals(snapshot.CandidateKernelVersion, candidateKernelVersion, StringComparison.Ordinal))
        {
            errors.Add($"Calibration snapshot candidate kernel '{snapshot.CandidateKernelVersion}' does not match requested '{candidateKernelVersion}'.");
        }

        var metric = snapshot.CandidateMetrics.FirstOrDefault(m => m.Severity == RequiredSeverity);
        if (metric is null)
        {
            errors.Add($"Calibration snapshot is missing required severity '{RequiredSeverity}'.");
        }
        else
        {
            if (metric.Precision < MinPrecision)
            {
                errors.Add($"Candidate precision {metric.Precision:F3} is below required {MinPrecision:F3}.");
            }

            if (metric.Recall < MinRecall)
            {
                errors.Add($"Candidate recall {metric.Recall:F3} is below required {MinRecall:F3}.");
            }
        }

        return new CalibrationGateDecision(errors.Count == 0, freshnessPass, errors);
    }
}

public sealed record CalibrationGateDecision(
    bool Passed,
    bool FreshnessPassed,
    IReadOnlyList<string> BlockingReasons);

public sealed class KernelWeightGovernanceWorkflowService
{
    private readonly ProviderKernelCalibrationPolicy _policy;

    public KernelWeightGovernanceWorkflowService(ProviderKernelCalibrationPolicy? policy = null)
    {
        _policy = policy ?? ProviderKernelCalibrationPolicy.Default;
    }

    public KernelPromotionDecision EvaluatePromotion(
        string candidateKernelVersion,
        ProviderKernelCalibrationSnapshot? snapshot,
        string requestedBy,
        DateTimeOffset? now = null)
    {
        var gate = _policy.Evaluate(snapshot, candidateKernelVersion, now);
        return new KernelPromotionDecision(
            CandidateKernelVersion: candidateKernelVersion,
            RequestedBy: requestedBy,
            EvaluatedAt: now ?? DateTimeOffset.UtcNow,
            CalibrationPass: gate.Passed,
            FreshnessPass: gate.FreshnessPassed,
            Approved: gate.Passed,
            BlockingReasons: gate.BlockingReasons);
    }
}

public sealed record KernelPromotionDecision(
    string CandidateKernelVersion,
    string RequestedBy,
    DateTimeOffset EvaluatedAt,
    bool CalibrationPass,
    bool FreshnessPass,
    bool Approved,
    IReadOnlyList<string> BlockingReasons);

public static class ProviderCalibrationReportWriter
{
    public static string BuildMarkdown(ProviderKernelCalibrationSnapshot snapshot)
    {
        var builder = new StringBuilder();
        builder.AppendLine("# Provider Degradation Kernel Calibration Report");
        builder.AppendLine();
        builder.AppendLine($"- Snapshot: `{snapshot.SnapshotId}`");
        builder.AppendLine($"- Created: `{snapshot.CreatedAt:O}`");
        builder.AppendLine($"- Dataset: `{snapshot.DatasetId}`");
        builder.AppendLine($"- Baseline kernel: `{snapshot.BaselineKernelVersion}`");
        builder.AppendLine($"- Candidate kernel: `{snapshot.CandidateKernelVersion}`");
        builder.AppendLine($"- Calibration pass: `{snapshot.CalibrationPass}`");
        builder.AppendLine();
        builder.AppendLine("## Candidate vs baseline metrics");
        builder.AppendLine();
        builder.AppendLine("| Severity | Baseline precision | Candidate precision | Baseline recall | Candidate recall | Threshold (candidate) |");
        builder.AppendLine("|---|---:|---:|---:|---:|---:|");

        foreach (var candidate in snapshot.CandidateMetrics.OrderBy(m => m.Severity))
        {
            var baseline = snapshot.BaselineMetrics.Single(m => m.Severity == candidate.Severity);
            builder.AppendLine($"| {candidate.Severity} | {baseline.Precision:F3} | {candidate.Precision:F3} | {baseline.Recall:F3} | {candidate.Recall:F3} | {candidate.Threshold:F3} |");
        }

        builder.AppendLine();
        builder.AppendLine("## Alert volume impact");
        builder.AppendLine();
        builder.AppendLine($"- Decision severity: `{snapshot.Comparison.DecisionSeverity}`");
        builder.AppendLine($"- Baseline expected alerts: `{snapshot.Comparison.ExpectedAlertCountBaseline}`");
        builder.AppendLine($"- Candidate expected alerts: `{snapshot.Comparison.ExpectedAlertCountCandidate}`");
        builder.AppendLine($"- Expected change: `{snapshot.Comparison.ExpectedAlertVolumeChangePercent:F2}%`");

        return builder.ToString();
    }
}
