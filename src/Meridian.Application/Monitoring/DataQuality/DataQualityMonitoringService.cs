using System.Collections.Concurrent;
using Meridian.Application.Logging;
using Meridian.Contracts.Domain.Enums;
using Serilog;

namespace Meridian.Application.Monitoring.DataQuality;

/// <summary>
/// Central orchestrator for all data quality monitoring components.
/// Provides a unified interface for real-time monitoring and reporting.
/// </summary>
public sealed class DataQualityMonitoringService : IAsyncDisposable
{
    private readonly ILogger _log = LoggingSetup.ForContext<DataQualityMonitoringService>();

    public CompletenessScoreCalculator Completeness { get; }
    public GapAnalyzer GapAnalyzer { get; }
    public SequenceErrorTracker SequenceTracker { get; }
    public AnomalyDetector AnomalyDetector { get; }
    public LatencyHistogram LatencyHistogram { get; }
    public CrossProviderComparisonService CrossProvider { get; }
    public DataQualityReportGenerator ReportGenerator { get; }

    private readonly IEventMetrics _eventMetrics;
    private readonly Timer _metricsUpdateTimer;
    private readonly ConcurrentDictionary<string, SymbolHealthStatus> _healthStatus = new();
    private volatile bool _isDisposed;

    /// <summary>
    /// Event raised when the overall health state changes.
    /// </summary>
    public event Action<RealTimeQualityMetrics>? OnMetricsUpdated;

    public DataQualityMonitoringService(DataQualityMonitoringConfig? config = null, IEventMetrics? eventMetrics = null)
    {
        config ??= DataQualityMonitoringConfig.Default;
        _eventMetrics = eventMetrics ?? new DefaultEventMetrics();

        Completeness = new CompletenessScoreCalculator(config.CompletenessConfig);
        GapAnalyzer = new GapAnalyzer(config.GapAnalyzerConfig);
        SequenceTracker = new SequenceErrorTracker(config.SequenceErrorConfig);
        AnomalyDetector = new AnomalyDetector(config.AnomalyConfig);
        LatencyHistogram = new LatencyHistogram(config.LatencyConfig);
        CrossProvider = new CrossProviderComparisonService(config.CrossProviderConfig);

        ReportGenerator = new DataQualityReportGenerator(
            Completeness, GapAnalyzer, SequenceTracker, AnomalyDetector,
            LatencyHistogram, CrossProvider, config.ReportOutputDirectory);

        _metricsUpdateTimer = new Timer(UpdateMetrics, null,
            TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(5));

        // Wire up events
        GapAnalyzer.OnGapDetected += gap =>
        {
            _log.Debug("Gap detected for {Symbol}: {Duration}", gap.Symbol, gap.Duration);
            UpdateSymbolHealth(gap.Symbol, HealthState.Degraded, $"Gap: {gap.Duration}");
        };

        AnomalyDetector.OnAnomalyDetected += anomaly =>
        {
            var state = anomaly.Severity >= AnomalySeverity.Error ? HealthState.Unhealthy : HealthState.Degraded;
            UpdateSymbolHealth(anomaly.Symbol, state, $"Anomaly: {anomaly.Type}");
        };

        _log.Information("DataQualityMonitoringService initialized");
    }

    /// <summary>
    /// Registers a symbol's liquidity profile across all monitoring sub-services.
    /// This adjusts gap detection thresholds, completeness expectations,
    /// stale-data alerting, and SLA freshness for the symbol so that
    /// illiquid instruments do not trigger false-positive quality alerts.
    /// </summary>
    public void RegisterSymbolLiquidity(string symbol, LiquidityProfile profile)
    {
        GapAnalyzer.RegisterSymbolLiquidity(symbol, profile);
        Completeness.RegisterSymbolLiquidity(symbol, profile);
        AnomalyDetector.RegisterSymbolLiquidity(symbol, profile);

        _log.Information("Registered liquidity profile {Profile} for {Symbol} across all quality monitors",
            profile, symbol);
    }

    /// <summary>
    /// Processes a trade event through all quality monitors.
    /// </summary>
    public void ProcessTrade(
        string symbol,
        DateTimeOffset timestamp,
        decimal price,
        decimal volume,
        long? sequence = null,
        string? provider = null,
        double? latencyMs = null)
    {
        if (_isDisposed)
            return;

        // Completeness tracking
        Completeness.RecordEvent(symbol, timestamp, "Trade");

        // Gap analysis
        GapAnalyzer.RecordEvent(symbol, "Trade", timestamp, sequence);

        // Sequence tracking
        if (sequence.HasValue)
        {
            SequenceTracker.CheckSequence(symbol, "Trade", sequence.Value, timestamp, null, provider);
        }

        // Anomaly detection
        AnomalyDetector.ProcessTrade(symbol, timestamp, price, volume, provider);

        // Latency tracking
        if (latencyMs.HasValue)
        {
            LatencyHistogram.RecordLatency(symbol, latencyMs.Value, provider);
        }

        // Cross-provider tracking
        if (provider != null)
        {
            CrossProvider.RecordTrade(symbol, provider, timestamp, price, volume, sequence);
        }

        // Update health
        UpdateSymbolHealth(symbol, HealthState.Healthy, null);
    }

    /// <summary>
    /// Processes a quote event through quality monitors.
    /// </summary>
    public void ProcessQuote(
        string symbol,
        DateTimeOffset timestamp,
        decimal bidPrice,
        decimal askPrice,
        decimal bidSize,
        decimal askSize,
        string? provider = null,
        double? latencyMs = null)
    {
        if (_isDisposed)
            return;

        Completeness.RecordEvent(symbol, timestamp, "Quote");
        GapAnalyzer.RecordEvent(symbol, "Quote", timestamp, null);
        AnomalyDetector.ProcessQuote(symbol, timestamp, bidPrice, askPrice, provider);

        if (latencyMs.HasValue)
        {
            LatencyHistogram.RecordLatency(symbol, latencyMs.Value, provider);
        }

        if (provider != null)
        {
            CrossProvider.RecordQuote(symbol, provider, timestamp, bidPrice, askPrice, bidSize, askSize);
        }

        UpdateSymbolHealth(symbol, HealthState.Healthy, null);
    }

    /// <summary>
    /// Gets real-time quality metrics.
    /// </summary>
    public RealTimeQualityMetrics GetRealTimeMetrics()
    {
        var now = DateTimeOffset.UtcNow;
        var fiveMinutesAgo = now.AddMinutes(-5);

        var recentGaps = GapAnalyzer.GetRecentGaps(1000)
            .Count(g => g.GapEnd >= fiveMinutesAgo);

        var recentErrors = SequenceTracker.GetRecentErrors(1000)
            .Count(e => e.Timestamp >= fiveMinutesAgo);

        var recentAnomalies = AnomalyDetector.GetRecentAnomalies(1000)
            .Count(a => a.Timestamp >= fiveMinutesAgo);

        var symbolHealth = _healthStatus.Values
            .OrderBy(s => s.State)
            .ThenByDescending(s => s.TimeSinceLastEvent)
            .Take(50)
            .ToList();

        var healthScore = CalculateOverallHealth(symbolHealth);
        var symbolsWithIssues = symbolHealth.Count(s => s.State != HealthState.Healthy);

        return new RealTimeQualityMetrics(
            Timestamp: now,
            ActiveSymbols: _healthStatus.Count,
            OverallHealthScore: Math.Round(healthScore, 4),
            EventsPerSecond: (long)_eventMetrics.EventsPerSecond,
            GapsLast5Minutes: recentGaps,
            SequenceErrorsLast5Minutes: recentErrors,
            AnomaliesLast5Minutes: recentAnomalies,
            AverageLatencyMs: Math.Round(LatencyHistogram.GetStatistics().GlobalMeanMs, 2),
            SymbolsWithIssues: symbolsWithIssues,
            SymbolHealth: symbolHealth
        );
    }

    /// <summary>
    /// Gets the health status for a specific symbol.
    /// </summary>
    public SymbolHealthStatus? GetSymbolHealth(string symbol)
    {
        var key = symbol.ToUpperInvariant();
        return _healthStatus.TryGetValue(key, out var status) ? status : null;
    }

    /// <summary>
    /// Gets all symbols with degraded or unhealthy status.
    /// </summary>
    public IReadOnlyList<SymbolHealthStatus> GetUnhealthySymbols()
    {
        return _healthStatus.Values
            .Where(s => s.State != HealthState.Healthy)
            .OrderBy(s => s.State)
            .ToList();
    }

    /// <summary>
    /// Generates a daily report.
    /// </summary>
    public Task<DailyQualityReport> GenerateDailyReportAsync(
        DateOnly date,
        ReportGenerationOptions? options = null,
        CancellationToken ct = default)
    {
        return ReportGenerator.GenerateDailyReportAsync(date, options, ct);
    }

    /// <summary>
    /// Generates a weekly report.
    /// </summary>
    public Task<WeeklyQualityReport> GenerateWeeklyReportAsync(
        DateOnly weekStart,
        ReportGenerationOptions? options = null,
        CancellationToken ct = default)
    {
        return ReportGenerator.GenerateWeeklyReportAsync(weekStart, options, ct);
    }

    /// <summary>
    /// Exports a report.
    /// </summary>
    public Task<string> ExportReportAsync(
        DailyQualityReport report,
        ReportExportFormat format,
        CancellationToken ct = default)
    {
        return ReportGenerator.ExportReportAsync(report, format, ct);
    }

    /// <summary>
    /// Gets a comprehensive quality dashboard snapshot.
    /// </summary>
    public DataQualityDashboard GetDashboard()
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var metrics = GetRealTimeMetrics();
        var completenessStats = Completeness.GetSummary();
        var gapStats = GapAnalyzer.GetStatistics(today);
        var sequenceStats = SequenceTracker.GetStatistics();
        var anomalyStats = AnomalyDetector.GetStatistics();
        var latencyStats = LatencyHistogram.GetStatistics();

        return new DataQualityDashboard(
            Timestamp: DateTimeOffset.UtcNow,
            RealTimeMetrics: metrics,
            CompletenessStats: completenessStats,
            GapStats: gapStats,
            SequenceStats: sequenceStats,
            AnomalyStats: anomalyStats,
            LatencyStats: latencyStats,
            RecentGaps: GapAnalyzer.GetRecentGaps(10),
            RecentErrors: SequenceTracker.GetRecentErrors(10),
            RecentAnomalies: AnomalyDetector.GetRecentAnomalies(10),
            StaleSymbols: AnomalyDetector.GetStaleSymbols()
        );
    }

    private void UpdateSymbolHealth(string symbol, HealthState state, string? issue)
    {
        var key = symbol.ToUpperInvariant();
        var now = DateTimeOffset.UtcNow;

        _healthStatus.AddOrUpdate(key,
            _ => new SymbolHealthStatus(
                Symbol: symbol,
                State: state,
                Score: state == HealthState.Healthy ? 1.0 : 0.5,
                LastEvent: now,
                TimeSinceLastEvent: TimeSpan.Zero,
                ActiveIssues: issue != null ? new[] { issue } : Array.Empty<string>()
            ),
            (_, existing) =>
            {
                var issues = issue != null
                    ? existing.ActiveIssues.Append(issue).Distinct().TakeLast(5).ToArray()
                    : (state == HealthState.Healthy ? Array.Empty<string>() : existing.ActiveIssues);

                var newState = state == HealthState.Healthy && existing.State != HealthState.Unknown
                    ? HealthState.Healthy : state;

                return new SymbolHealthStatus(
                    Symbol: symbol,
                    State: newState,
                    Score: newState == HealthState.Healthy ? 1.0 : 0.5,
                    LastEvent: now,
                    TimeSinceLastEvent: TimeSpan.Zero,
                    ActiveIssues: issues
                );
            });
    }

    private static double CalculateOverallHealth(IReadOnlyList<SymbolHealthStatus> symbols)
    {
        if (symbols.Count == 0)
            return 1.0;

        var healthyCount = symbols.Count(s => s.State == HealthState.Healthy);
        var degradedCount = symbols.Count(s => s.State == HealthState.Degraded);
        var unhealthyCount = symbols.Count(s => s.State == HealthState.Unhealthy);

        return (healthyCount * 1.0 + degradedCount * 0.5 + unhealthyCount * 0.0) / symbols.Count;
    }

    private void UpdateMetrics(object? state)
    {
        if (_isDisposed)
            return;

        try
        {
            // Update stale status for symbols, using per-symbol liquidity-aware thresholds
            var now = DateTimeOffset.UtcNow;
            var defaultStaleThreshold = TimeSpan.FromSeconds(60);

            foreach (var key in _healthStatus.Keys)
            {
                if (_healthStatus.TryGetValue(key, out var status))
                {
                    var timeSinceLastEvent = now - status.LastEvent;
                    var symbolProfile = GapAnalyzer.GetSymbolLiquidity(key);
                    var symbolThreshold = symbolProfile == LiquidityProfile.High
                        ? defaultStaleThreshold
                        : TimeSpan.FromSeconds(LiquidityProfileProvider.GetThresholds(symbolProfile).StaleDataThresholdSeconds);

                    if (timeSinceLastEvent > symbolThreshold && status.State != HealthState.Stale)
                    {
                        _healthStatus[key] = status with
                        {
                            State = HealthState.Stale,
                            TimeSinceLastEvent = timeSinceLastEvent,
                            ActiveIssues = status.ActiveIssues.Append("No recent data").Distinct().ToArray()
                        };
                    }
                    else if (status.State != HealthState.Stale)
                    {
                        _healthStatus[key] = status with { TimeSinceLastEvent = timeSinceLastEvent };
                    }
                }
            }

            // Raise metrics update event
            var metrics = GetRealTimeMetrics();
            OnMetricsUpdated?.Invoke(metrics);
        }
        catch (Exception ex)
        {
            _log.Error(ex, "Error updating quality metrics");
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_isDisposed)
            return;
        _isDisposed = true;

        await _metricsUpdateTimer.DisposeAsync();

        Completeness.Dispose();
        GapAnalyzer.Dispose();
        SequenceTracker.Dispose();
        AnomalyDetector.Dispose();
        LatencyHistogram.Dispose();
        CrossProvider.Dispose();

        _healthStatus.Clear();
        _log.Information("DataQualityMonitoringService disposed");
    }
}

/// <summary>
/// Configuration for the data quality monitoring service.
/// </summary>
public sealed record DataQualityMonitoringConfig
{
    public CompletenessConfig? CompletenessConfig { get; init; }
    public GapAnalyzerConfig? GapAnalyzerConfig { get; init; }
    public SequenceErrorConfig? SequenceErrorConfig { get; init; }
    public AnomalyDetectionConfig? AnomalyConfig { get; init; }
    public LatencyHistogramConfig? LatencyConfig { get; init; }
    public CrossProviderConfig? CrossProviderConfig { get; init; }
    public string? ReportOutputDirectory { get; init; }

    public static DataQualityMonitoringConfig Default => new();
}

/// <summary>
/// Dashboard snapshot containing all quality metrics.
/// </summary>
public sealed record DataQualityDashboard(
    DateTimeOffset Timestamp,
    RealTimeQualityMetrics RealTimeMetrics,
    CompletenessSummary CompletenessStats,
    GapStatistics GapStats,
    SequenceErrorStatistics SequenceStats,
    AnomalyStatistics AnomalyStats,
    LatencyStatistics LatencyStats,
    IReadOnlyList<DataGap> RecentGaps,
    IReadOnlyList<SequenceError> RecentErrors,
    IReadOnlyList<DataAnomaly> RecentAnomalies,
    IReadOnlyList<string> StaleSymbols
);
