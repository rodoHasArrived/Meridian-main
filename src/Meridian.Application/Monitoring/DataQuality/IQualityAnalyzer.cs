using Meridian.Infrastructure.Contracts;

namespace Meridian.Application.Monitoring.DataQuality;

/// <summary>
/// Quality analysis severity levels.
/// </summary>
public enum QualityIssueSeverity : byte
{
    /// <summary>Informational - no action needed.</summary>
    Info,

    /// <summary>Minor issue - may warrant investigation.</summary>
    Minor,

    /// <summary>Moderate issue - should be addressed.</summary>
    Moderate,

    /// <summary>Significant issue - requires attention.</summary>
    Significant,

    /// <summary>Critical issue - immediate action required.</summary>
    Critical
}

/// <summary>
/// Categories of quality issues for filtering and routing.
/// </summary>
public enum QualityIssueCategory : byte
{
    /// <summary>Data gaps or missing values.</summary>
    Gaps,

    /// <summary>Sequence anomalies (out-of-order, duplicates).</summary>
    Sequence,

    /// <summary>Value anomalies (outliers, invalid prices).</summary>
    ValueAnomaly,

    /// <summary>Latency issues.</summary>
    Latency,

    /// <summary>Completeness issues.</summary>
    Completeness,

    /// <summary>Consistency issues across sources.</summary>
    Consistency,

    /// <summary>Format or schema issues.</summary>
    Format,

    /// <summary>Timeliness issues.</summary>
    Timeliness
}

/// <summary>
/// Represents a quality issue detected by an analyzer.
/// </summary>
/// <param name="Id">Unique issue identifier.</param>
/// <param name="AnalyzerName">Name of the analyzer that detected the issue.</param>
/// <param name="Severity">Issue severity.</param>
/// <param name="Category">Issue category.</param>
/// <param name="Symbol">Affected symbol (if applicable).</param>
/// <param name="Title">Short title.</param>
/// <param name="Description">Detailed description.</param>
/// <param name="DetectedAt">When the issue was detected.</param>
/// <param name="AffectedTimeRange">Time range of affected data.</param>
/// <param name="Metrics">Quantitative metrics about the issue.</param>
/// <param name="SuggestedAction">Recommended remediation action.</param>
public sealed record QualityIssue(
    string Id,
    string AnalyzerName,
    QualityIssueSeverity Severity,
    QualityIssueCategory Category,
    string? Symbol,
    string Title,
    string Description,
    DateTimeOffset DetectedAt,
    (DateTimeOffset Start, DateTimeOffset End)? AffectedTimeRange = null,
    IReadOnlyDictionary<string, double>? Metrics = null,
    string? SuggestedAction = null)
{
    /// <summary>
    /// Creates a new quality issue with auto-generated ID.
    /// </summary>
    public static QualityIssue Create(
        string analyzerName,
        QualityIssueSeverity severity,
        QualityIssueCategory category,
        string title,
        string description,
        string? symbol = null,
        (DateTimeOffset, DateTimeOffset)? timeRange = null,
        IReadOnlyDictionary<string, double>? metrics = null,
        string? suggestedAction = null)
    {
        return new QualityIssue(
            Id: Guid.NewGuid().ToString("N")[..12],
            AnalyzerName: analyzerName,
            Severity: severity,
            Category: category,
            Symbol: symbol,
            Title: title,
            Description: description,
            DetectedAt: DateTimeOffset.UtcNow,
            AffectedTimeRange: timeRange,
            Metrics: metrics,
            SuggestedAction: suggestedAction);
    }
}

/// <summary>
/// Result of a quality analysis run.
/// </summary>
/// <param name="AnalyzerName">Name of the analyzer.</param>
/// <param name="StartedAt">When analysis started.</param>
/// <param name="CompletedAt">When analysis completed.</param>
/// <param name="Issues">Detected issues.</param>
/// <param name="Metrics">Overall metrics from the analysis.</param>
/// <param name="Summary">Human-readable summary.</param>
/// <param name="Error">Error if analysis failed.</param>
public sealed record QualityAnalysisResult(
    string AnalyzerName,
    DateTimeOffset StartedAt,
    DateTimeOffset CompletedAt,
    IReadOnlyList<QualityIssue> Issues,
    IReadOnlyDictionary<string, double>? Metrics = null,
    string? Summary = null,
    string? Error = null)
{
    /// <summary>
    /// Duration of the analysis.
    /// </summary>
    public TimeSpan Duration => CompletedAt - StartedAt;

    /// <summary>
    /// Whether the analysis completed successfully.
    /// </summary>
    public bool IsSuccess => Error == null;

    /// <summary>
    /// Highest severity among detected issues.
    /// </summary>
    public QualityIssueSeverity HighestSeverity =>
        Issues.Count > 0 ? Issues.Max(i => i.Severity) : QualityIssueSeverity.Info;

    /// <summary>
    /// Creates a successful result with no issues.
    /// </summary>
    public static QualityAnalysisResult Success(string analyzerName, DateTimeOffset startedAt, string? summary = null)
        => new(analyzerName, startedAt, DateTimeOffset.UtcNow, Array.Empty<QualityIssue>(), Summary: summary);

    /// <summary>
    /// Creates a failed result.
    /// </summary>
    public static QualityAnalysisResult Failure(string analyzerName, DateTimeOffset startedAt, string error)
        => new(analyzerName, startedAt, DateTimeOffset.UtcNow, Array.Empty<QualityIssue>(), Error: error);
}

/// <summary>
/// Configuration for a quality analyzer.
/// </summary>
/// <param name="Enabled">Whether the analyzer is enabled.</param>
/// <param name="MinSeverityToReport">Minimum severity to include in results.</param>
/// <param name="Symbols">Symbols to analyze (null = all).</param>
/// <param name="TimeRange">Time range to analyze.</param>
/// <param name="CustomSettings">Analyzer-specific settings.</param>
public sealed record QualityAnalyzerConfig(
    bool Enabled = true,
    QualityIssueSeverity MinSeverityToReport = QualityIssueSeverity.Minor,
    IReadOnlyList<string>? Symbols = null,
    (DateTimeOffset Start, DateTimeOffset End)? TimeRange = null,
    IReadOnlyDictionary<string, object>? CustomSettings = null);

/// <summary>
/// Base interface for quality analyzers providing plugin-style architecture.
/// Analyzers can be discovered, registered, and run by the quality analysis engine.
/// </summary>
/// <typeparam name="TData">Type of data the analyzer processes.</typeparam>
/// <remarks>
/// Addresses orphaned analyzer implementations by providing:
/// - Standardized interface for all quality analyzers
/// - Discovery and registration mechanism
/// - Configurable analysis parameters
/// - Consistent result format
///
/// Existing analyzers to migrate:
/// - LatencyHistogramAnalyzer
/// - CompletenessScoreCalculator
/// - GapAnalyzer
/// - AnomalyDetector
/// - SequenceErrorTracker
/// </remarks>
[ImplementsAdr("ADR-001", "Quality analyzer plugin interface")]
public interface IQualityAnalyzer<TData>
{
    /// <summary>
    /// Unique name of the analyzer.
    /// </summary>
    string Name { get; }

    /// <summary>
    /// Human-readable display name.
    /// </summary>
    string DisplayName { get; }

    /// <summary>
    /// Description of what the analyzer checks.
    /// </summary>
    string Description { get; }

    /// <summary>
    /// Categories of issues this analyzer can detect.
    /// </summary>
    IReadOnlyList<QualityIssueCategory> Categories { get; }

    /// <summary>
    /// Priority for execution order (lower = earlier).
    /// </summary>
    int Priority { get; }

    /// <summary>
    /// Analyzes data and returns quality issues.
    /// </summary>
    /// <param name="data">Data to analyze.</param>
    /// <param name="config">Analysis configuration.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Analysis result with detected issues.</returns>
    Task<QualityAnalysisResult> AnalyzeAsync(
        TData data,
        QualityAnalyzerConfig? config = null,
        CancellationToken ct = default);

    /// <summary>
    /// Validates configuration for this analyzer.
    /// </summary>
    /// <param name="config">Configuration to validate.</param>
    /// <returns>Validation errors (empty if valid).</returns>
    IReadOnlyList<string> ValidateConfig(QualityAnalyzerConfig config);
}

/// <summary>
/// Non-generic interface for analyzer registration and discovery.
/// </summary>
[ImplementsAdr("ADR-001", "Quality analyzer discovery interface")]
public interface IQualityAnalyzerMetadata
{
    /// <summary>
    /// Unique name of the analyzer.
    /// </summary>
    string Name { get; }

    /// <summary>
    /// Human-readable display name.
    /// </summary>
    string DisplayName { get; }

    /// <summary>
    /// Description of what the analyzer checks.
    /// </summary>
    string Description { get; }

    /// <summary>
    /// Categories of issues this analyzer can detect.
    /// </summary>
    IReadOnlyList<QualityIssueCategory> Categories { get; }

    /// <summary>
    /// Priority for execution order.
    /// </summary>
    int Priority { get; }

    /// <summary>
    /// Type of data the analyzer processes.
    /// </summary>
    Type DataType { get; }
}

/// <summary>
/// Registry for quality analyzers providing discovery and management.
/// </summary>
[ImplementsAdr("ADR-001", "Quality analyzer registry")]
public interface IQualityAnalyzerRegistry
{
    /// <summary>
    /// Registers an analyzer.
    /// </summary>
    /// <typeparam name="TData">Data type the analyzer processes.</typeparam>
    /// <param name="analyzer">Analyzer to register.</param>
    void Register<TData>(IQualityAnalyzer<TData> analyzer);

    /// <summary>
    /// Gets an analyzer by name.
    /// </summary>
    /// <typeparam name="TData">Expected data type.</typeparam>
    /// <param name="name">Analyzer name.</param>
    /// <returns>Analyzer or null if not found.</returns>
    IQualityAnalyzer<TData>? Get<TData>(string name);

    /// <summary>
    /// Gets all analyzers for a data type.
    /// </summary>
    /// <typeparam name="TData">Data type.</typeparam>
    /// <returns>List of analyzers ordered by priority.</returns>
    IReadOnlyList<IQualityAnalyzer<TData>> GetAll<TData>();

    /// <summary>
    /// Gets metadata for all registered analyzers.
    /// </summary>
    /// <returns>List of analyzer metadata.</returns>
    IReadOnlyList<IQualityAnalyzerMetadata> GetAllMetadata();

    /// <summary>
    /// Gets analyzers that can detect issues in a category.
    /// </summary>
    /// <param name="category">Issue category.</param>
    /// <returns>List of analyzer metadata.</returns>
    IReadOnlyList<IQualityAnalyzerMetadata> GetByCategory(QualityIssueCategory category);
}

/// <summary>
/// Engine for running quality analysis across multiple analyzers.
/// </summary>
[ImplementsAdr("ADR-001", "Quality analysis engine")]
public interface IQualityAnalysisEngine
{
    /// <summary>
    /// Runs all registered analyzers on the provided data.
    /// </summary>
    /// <typeparam name="TData">Data type.</typeparam>
    /// <param name="data">Data to analyze.</param>
    /// <param name="config">Optional configuration.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Aggregated analysis results.</returns>
    Task<AggregatedQualityReport> AnalyzeAllAsync<TData>(
        TData data,
        QualityAnalyzerConfig? config = null,
        CancellationToken ct = default);

    /// <summary>
    /// Runs specific analyzers by name.
    /// </summary>
    /// <typeparam name="TData">Data type.</typeparam>
    /// <param name="data">Data to analyze.</param>
    /// <param name="analyzerNames">Names of analyzers to run.</param>
    /// <param name="config">Optional configuration.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Analysis results from specified analyzers.</returns>
    Task<AggregatedQualityReport> AnalyzeWithAsync<TData>(
        TData data,
        IEnumerable<string> analyzerNames,
        QualityAnalyzerConfig? config = null,
        CancellationToken ct = default);
}

/// <summary>
/// Aggregated quality report from multiple analyzers.
/// </summary>
/// <param name="GeneratedAt">When the report was generated.</param>
/// <param name="TotalDuration">Total analysis duration.</param>
/// <param name="AnalyzerResults">Results from each analyzer.</param>
/// <param name="AllIssues">All issues from all analyzers.</param>
/// <param name="OverallScore">Overall quality score (0-100).</param>
public sealed record AggregatedQualityReport(
    DateTimeOffset GeneratedAt,
    TimeSpan TotalDuration,
    IReadOnlyList<QualityAnalysisResult> AnalyzerResults,
    IReadOnlyList<QualityIssue> AllIssues,
    double OverallScore)
{
    /// <summary>
    /// Highest severity across all issues.
    /// </summary>
    public QualityIssueSeverity HighestSeverity =>
        AllIssues.Count > 0 ? AllIssues.Max(i => i.Severity) : QualityIssueSeverity.Info;

    /// <summary>
    /// Issues grouped by category.
    /// </summary>
    public IReadOnlyDictionary<QualityIssueCategory, IReadOnlyList<QualityIssue>> IssuesByCategory =>
        AllIssues.GroupBy(i => i.Category)
            .ToDictionary(g => g.Key, g => (IReadOnlyList<QualityIssue>)g.ToList());

    /// <summary>
    /// Issues grouped by severity.
    /// </summary>
    public IReadOnlyDictionary<QualityIssueSeverity, IReadOnlyList<QualityIssue>> IssuesBySeverity =>
        AllIssues.GroupBy(i => i.Severity)
            .ToDictionary(g => g.Key, g => (IReadOnlyList<QualityIssue>)g.ToList());

    /// <summary>
    /// Issues grouped by symbol.
    /// </summary>
    public IReadOnlyDictionary<string, IReadOnlyList<QualityIssue>> IssuesBySymbol =>
        AllIssues.Where(i => i.Symbol != null)
            .GroupBy(i => i.Symbol!)
            .ToDictionary(g => g.Key, g => (IReadOnlyList<QualityIssue>)g.ToList());
}
