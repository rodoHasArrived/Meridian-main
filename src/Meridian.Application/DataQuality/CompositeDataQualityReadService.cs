using System.Collections.Concurrent;
using System.Globalization;
using Meridian.Contracts.Api.Quality;
using Meridian.DataIntegration.Monitoring.DataQuality;
using Meridian.Infrastructure.Adapters.Core;
using Meridian.Storage.Interfaces;
using Meridian.Storage.Services;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using StreamingDataGap = Meridian.DataIntegration.Monitoring.DataQuality.DataGap;
using Meridian.Contracts.Integrity;

namespace Meridian.Application.DataQuality;

/// <summary>
/// Shared read boundary for the browser and WPF data-quality workstations.
/// </summary>
public interface ICompositeDataQualityReadService
{
    Task<QualityCompositeDashboardResponse> GetDashboardAsync(CancellationToken ct = default);

    bool TryResolveGap(string symbol, string gapId, out CompositeDataQualityGap target);
}

/// <summary>
/// Server-owned remediation target resolved from an opaque composite gap identifier.
/// </summary>
public sealed record CompositeDataQualityGap(
    string GapId,
    string DashboardVersion,
    StreamingDataGap Gap,
    string? Provider);

/// <summary>
/// Unifies streaming freshness, stored-file completeness, and adapter gap integrity into one
/// per-symbol 0-100 projection. Expensive storage and adapter reads are cached independently so
/// live streaming state can still refresh on every dashboard request.
/// </summary>
public sealed class CompositeDataQualityReadService : ICompositeDataQualityReadService
{
    private const double StoredWeight = 0.40;
    private const double StreamingWeight = 0.35;
    private const double AdapterWeight = 0.25;
    private static readonly TimeSpan ExpensiveSignalCacheDuration = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan AdapterLookback = TimeSpan.FromDays(30);
    private static readonly StringComparer SymbolComparer = StringComparer.OrdinalIgnoreCase;

    private readonly DataQualityMonitoringService _streaming;
    private readonly IDataQualityScoringService? _stored;
    private readonly DataQualityMonitor? _adapter;
    private readonly ISymbolRegistryService? _symbols;
    private readonly ILogger<CompositeDataQualityReadService> _logger;
    private readonly SemaphoreSlim _storedRefreshGate = new(1, 1);
    private DataQualityScoringReport? _storedCache;
    private DateTimeOffset _storedCacheExpiresAt;
    private IReadOnlyDictionary<string, CompositeDataQualityGap> _gapLookup =
        new Dictionary<string, CompositeDataQualityGap>(StringComparer.OrdinalIgnoreCase);

    public CompositeDataQualityReadService(
        DataQualityMonitoringService streaming,
        IDataQualityScoringService? stored = null,
        DataQualityMonitor? adapter = null,
        ISymbolRegistryService? symbols = null,
        ILogger<CompositeDataQualityReadService>? logger = null)
    {
        _streaming = streaming ?? throw new ArgumentNullException(nameof(streaming));
        _stored = stored;
        _adapter = adapter;
        _symbols = symbols;
        _logger = logger ?? NullLogger<CompositeDataQualityReadService>.Instance;
    }

    public async Task<QualityCompositeDashboardResponse> GetDashboardAsync(CancellationToken ct = default)
    {
        var observedAt = DateTimeOffset.UtcNow;
        var version = observedAt.ToUnixTimeMilliseconds().ToString(CultureInfo.InvariantCulture);
        var streamingDashboard = _streaming.GetDashboard();
        var storedReport = await GetStoredReportAsync(observedAt, ct).ConfigureAwait(false);
        var rawGaps = _streaming.GapAnalyzer.GetRecentGaps(500);

        var symbolNames = new HashSet<string>(SymbolComparer);
        foreach (var health in streamingDashboard.RealTimeMetrics.SymbolHealth)
            AddSymbol(symbolNames, health.Symbol);
        foreach (var gap in rawGaps)
            AddSymbol(symbolNames, gap.Symbol);
        foreach (var anomaly in _streaming.AnomalyDetector.GetRecentAnomalies(1000))
            AddSymbol(symbolNames, anomaly.Symbol);
        if (storedReport is not null)
        {
            foreach (var symbol in storedReport.Summary.BySymbol.Keys)
                AddSymbol(symbolNames, symbol);
        }
        if (_adapter is not null)
        {
            foreach (var symbol in _adapter.GetAllScores().Keys)
                AddSymbol(symbolNames, symbol);
        }
        if (_symbols is not null)
        {
            foreach (var entry in _symbols.GetAllSymbols().Where(static entry => entry.IsActive))
                AddSymbol(symbolNames, entry.Canonical);
        }

        var adapterCandidates = symbolNames
            .Where(symbol => _streaming.GetSymbolHealth(symbol) is not null ||
                             storedReport?.Summary.BySymbol.ContainsKey(symbol) == true ||
                             rawGaps.Any(gap => SymbolComparer.Equals(gap.Symbol, symbol)))
            .ToArray();
        var adapterScores = await GetAdapterScoresAsync(adapterCandidates, observedAt, ct).ConfigureAwait(false);

        var gapTargets = new Dictionary<string, CompositeDataQualityGap>(StringComparer.OrdinalIgnoreCase);
        var gapRows = rawGaps
            .Select(gap => CreateGap(gap, version, gap.Provider, gapTargets))
            .OrderByDescending(static gap => gap.From)
            .ToArray();

        var symbolRows = new List<QualityCompositeSymbolResponse>(symbolNames.Count);
        foreach (var symbol in symbolNames.OrderBy(static symbol => symbol, StringComparer.OrdinalIgnoreCase))
        {
            ct.ThrowIfCancellationRequested();
            symbolRows.Add(BuildSymbolRow(
                symbol,
                observedAt,
                storedReport,
                adapterScores.TryGetValue(symbol, out var adapterScore) ? adapterScore : null,
                gapRows.Where(gap => SymbolComparer.Equals(gap.Symbol, symbol)).ToArray()));
        }

        _gapLookup = gapTargets;

        var aggregateComponents = BuildAggregateComponents(symbolRows);
        var measuredScores = symbolRows
            .Where(static row => row.CompositeScore.HasValue)
            .Select(static row => row.CompositeScore!.Value)
            .ToArray();
        var aggregateScore = measuredScores.Length == 0
            ? (double?)null
            : Math.Round(measuredScores.Average(), 1);
        var coverageWeight = symbolRows.Count == 0
            ? 0
            : Math.Round(symbolRows.Average(static row => row.CoverageWeight), 2);
        var isPartial = symbolRows.Count == 0 || symbolRows.Any(static row => row.IsPartial);

        return new QualityCompositeDashboardResponse(
            Version: version,
            ObservedAt: observedAt,
            CompositeScore: aggregateScore,
            Status: GetStatus(aggregateScore, isPartial),
            IsPartial: isPartial,
            CoverageWeight: coverageWeight,
            Components: aggregateComponents,
            Symbols: symbolRows,
            OpenGaps: gapRows,
            AnomalyCount: _streaming.AnomalyDetector.GetRecentAnomalies(1000).Count);
    }

    public bool TryResolveGap(string symbol, string gapId, out CompositeDataQualityGap target)
    {
        if (string.IsNullOrWhiteSpace(symbol) || string.IsNullOrWhiteSpace(gapId))
        {
            target = null!;
            return false;
        }

        return _gapLookup.TryGetValue(GapLookupKey(symbol, gapId), out target!);
    }

    private QualityCompositeSymbolResponse BuildSymbolRow(
        string symbol,
        DateTimeOffset observedAt,
        DataQualityScoringReport? storedReport,
        QualityScore? adapterScore,
        IReadOnlyList<QualityCompositeGapResponse> gaps)
    {
        var health = _streaming.GetSymbolHealth(symbol);
        var latestCompleteness = _streaming.Completeness.GetScoresForSymbol(symbol).FirstOrDefault();
        var symbolAssessments = storedReport?.Assessments
            .Where(assessment => SymbolComparer.Equals(assessment.Symbol, symbol))
            .ToArray() ?? [];
        var storedScore = 0d;
        var hasStoredScore = storedReport?.Summary.BySymbol.TryGetValue(symbol, out storedScore) == true;
        var anomalies = _streaming.AnomalyDetector.GetAnomalies(symbol, 1000);

        var components = new[]
        {
            BuildStreamingComponent(health, observedAt),
            new QualityComponentResponse(
                Kind: "StoredCompleteness",
                Label: "Stored completeness",
                Weight: StoredWeight,
                Score: hasStoredScore ? ToPercent(storedScore) : null,
                Availability: hasStoredScore ? "Measured" : "Unavailable",
                ObservedAt: hasStoredScore && storedReport is not null
                    ? new DateTimeOffset(DateTime.SpecifyKind(storedReport.GeneratedAtUtc, DateTimeKind.Utc))
                    : null,
                IssueCount: symbolAssessments.Sum(static assessment => assessment.Issues.Count),
                Detail: hasStoredScore
                    ? $"{symbolAssessments.Length} stored artifact(s) assessed."
                    : "No stored quality assessment is available."),
            new QualityComponentResponse(
                Kind: "AdapterGapIntegrity",
                Label: "Adapter gap integrity",
                Weight: AdapterWeight,
                Score: adapterScore is null ? null : ToPercent(adapterScore.OverallScore),
                Availability: adapterScore is null ? "Unavailable" : "Measured",
                ObservedAt: adapterScore?.CalculatedAt,
                IssueCount: adapterScore?.Issues.Count ?? 0,
                Detail: adapterScore is null
                    ? "No adapter gap assessment is available."
                    : $"{adapterScore.Issues.Count} adapter issue(s) across the bounded lookback.")
        };

        var measured = components.Where(static component => component.Score.HasValue).ToArray();
        var coverageWeight = Math.Round(measured.Sum(static component => component.Weight), 2);
        var compositeScore = measured.Length == 0
            ? (double?)null
            : Math.Round(
                measured.Sum(component => component.Score!.Value * component.Weight) /
                measured.Sum(static component => component.Weight),
                1);
        var isPartial = coverageWeight < 0.999;
        var issues = (health?.ActiveIssues ?? [])
            .Concat(symbolAssessments.SelectMany(static assessment => assessment.Issues.Select(static issue => issue.Description)))
            .Concat(adapterScore?.Issues.Select(static issue => issue.Message) ?? [])
            .Concat(gaps.Select(static gap => $"Open {gap.EventType} gap from {gap.From:u} to {gap.To:u}."))
            .Where(static issue => !string.IsNullOrWhiteSpace(issue))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(10)
            .ToArray();

        return new QualityCompositeSymbolResponse(
            Symbol: symbol,
            CompositeScore: compositeScore,
            Status: GetStatus(compositeScore, isPartial),
            IsPartial: isPartial,
            CoverageWeight: coverageWeight,
            ExpectedEvents: latestCompleteness?.ExpectedEvents,
            ObservedEvents: latestCompleteness?.ActualEvents,
            AnomalyCount: anomalies.Count,
            Components: components,
            OpenGaps: gaps,
            ProviderFreshness: BuildProviderFreshness(symbol, observedAt),
            Issues: issues);
    }

    private static QualityComponentResponse BuildStreamingComponent(SymbolHealthStatus? health, DateTimeOffset observedAt)
    {
        if (health is null)
        {
            return new QualityComponentResponse(
                "StreamingFreshness",
                "Streaming freshness",
                StreamingWeight,
                null,
                "Unavailable",
                null,
                0,
                "No streaming observation is available.");
        }

        var age = observedAt - health.LastEvent;
        var freshnessScore = age switch
        {
            { TotalMinutes: <= 2 } => 100d,
            { TotalMinutes: <= 5 } => 90d,
            { TotalMinutes: <= 15 } => 70d,
            { TotalMinutes: <= 60 } => 40d,
            _ => 0d
        };
        var score = Math.Round(Math.Min(ToPercent(health.Score), freshnessScore), 1);
        var stale = health.State == HealthState.Stale || age > TimeSpan.FromMinutes(15);

        return new QualityComponentResponse(
            "StreamingFreshness",
            "Streaming freshness",
            StreamingWeight,
            score,
            stale ? "Stale" : "Measured",
            health.LastEvent,
            health.ActiveIssues.Length,
            $"Last streaming event {Math.Max(0, age.TotalSeconds):N0} seconds ago; state {health.State}.");
    }

    private IReadOnlyList<QualityProviderFreshnessResponse> BuildProviderFreshness(string symbol, DateTimeOffset observedAt)
    {
        try
        {
            var comparison = _streaming.CrossProvider.Compare(
                symbol,
                DateOnly.FromDateTime(observedAt.UtcDateTime),
                "Trade");
            return comparison.Providers
                .OrderBy(static provider => provider.Provider, StringComparer.OrdinalIgnoreCase)
                .Select(provider =>
                {
                    var hasEvent = provider.EventCount > 0 && provider.LastEvent > DateTimeOffset.MinValue;
                    var age = hasEvent ? observedAt - provider.LastEvent : (TimeSpan?)null;
                    return new QualityProviderFreshnessResponse(
                        Provider: provider.Provider,
                        LastEventAt: hasEvent ? provider.LastEvent : null,
                        AgeMilliseconds: age.HasValue ? Math.Max(0, age.Value.TotalMilliseconds) : null,
                        Status: !age.HasValue ? "Unavailable" : age <= TimeSpan.FromMinutes(15) ? "Fresh" : "Stale",
                        CompletenessScore: hasEvent ? ToPercent(provider.CompletenessScore) : null,
                        GapCount: provider.GapCount);
                })
                .ToArray();
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Provider freshness comparison is unavailable for {Symbol}", symbol);
            return [];
        }
    }

    private async Task<DataQualityScoringReport?> GetStoredReportAsync(DateTimeOffset now, CancellationToken ct)
    {
        if (_stored is null)
            return null;
        if (_storedCache is not null && now < _storedCacheExpiresAt)
            return _storedCache;

        await _storedRefreshGate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (_storedCache is not null && now < _storedCacheExpiresAt)
                return _storedCache;

            _storedCache = await _stored.GenerateReportAsync(ct).ConfigureAwait(false);
            _storedCacheExpiresAt = now + ExpensiveSignalCacheDuration;
            return _storedCache;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Stored data-quality assessment is unavailable");
            _storedCacheExpiresAt = now + TimeSpan.FromMinutes(1);
            return _storedCache;
        }
        finally
        {
            _storedRefreshGate.Release();
        }
    }

    private async Task<IReadOnlyDictionary<string, QualityScore>> GetAdapterScoresAsync(
        IReadOnlyList<string> symbols,
        DateTimeOffset now,
        CancellationToken ct)
    {
        if (_adapter is null || symbols.Count == 0)
            return new Dictionary<string, QualityScore>(SymbolComparer);

        var scores = new ConcurrentDictionary<string, QualityScore>(SymbolComparer);
        await Parallel.ForEachAsync(
            symbols,
            new ParallelOptions { CancellationToken = ct, MaxDegreeOfParallelism = 4 },
            async (symbol, token) =>
            {
                try
                {
                    var score = _adapter.GetCachedScore(symbol);
                    if (score is null || now - score.CalculatedAt > ExpensiveSignalCacheDuration)
                    {
                        var end = DateOnly.FromDateTime(now.UtcDateTime);
                        var start = DateOnly.FromDateTime(now.Subtract(AdapterLookback).UtcDateTime);
                        score = await _adapter.CalculateScoreAsync(symbol, start, end, token).ConfigureAwait(false);
                    }
                    scores[symbol] = score;
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    _logger.LogWarning(ex, "Adapter data-quality assessment is unavailable for {Symbol}", symbol);
                }
            }).ConfigureAwait(false);

        return scores;
    }

    private static QualityCompositeGapResponse CreateGap(
        StreamingDataGap gap,
        string dashboardVersion,
        string? provider,
        IDictionary<string, CompositeDataQualityGap> targets)
    {
        var gapId = CreateGapId(gap, provider);
        targets[GapLookupKey(gap.Symbol, gapId)] = new CompositeDataQualityGap(
            gapId,
            dashboardVersion,
            gap,
            provider);
        var hasValidRange = gap.GapEnd >= gap.GapStart && gap.EstimatedMissedEvents > 0;
        var hasProviderProvenance = !string.IsNullOrWhiteSpace(provider);
        var canBackfill = hasValidRange && hasProviderProvenance;
        var disabledReason = !hasValidRange
            ? "The gap does not contain a valid remediable range."
            : !hasProviderProvenance
                ? "The originating provider is unavailable, so Meridian cannot safely target this remediation."
                : null;
        return new QualityCompositeGapResponse(
            GapId: gapId,
            Symbol: gap.Symbol,
            Provider: provider,
            EventType: gap.EventType,
            From: gap.GapStart,
            To: gap.GapEnd,
            EstimatedMissingEvents: gap.EstimatedMissedEvents,
            Severity: gap.Severity.ToString(),
            Status: "Open",
            CanBackfill: canBackfill,
            DisabledReason: disabledReason);
    }

    private static string CreateGapId(StreamingDataGap gap, string? provider)
    {
        var canonical = string.Join(
            "|",
            gap.Symbol.Trim().ToUpperInvariant(),
            gap.EventType.Trim().ToUpperInvariant(),
            provider?.Trim().ToUpperInvariant() ?? string.Empty,
            gap.GapStart.UtcTicks.ToString(CultureInfo.InvariantCulture),
            gap.GapEnd.UtcTicks.ToString(CultureInfo.InvariantCulture));
        return Sha256Digest.ComputeUtf8(canonical)[..24];
    }

    private static IReadOnlyList<QualityComponentResponse> BuildAggregateComponents(
        IReadOnlyList<QualityCompositeSymbolResponse> symbols)
    {
        return new[] { "StreamingFreshness", "StoredCompleteness", "AdapterGapIntegrity" }
            .Select(kind =>
            {
                var entries = symbols.SelectMany(static symbol => symbol.Components)
                    .Where(component => component.Kind == kind)
                    .ToArray();
                var measured = entries.Where(static component => component.Score.HasValue).ToArray();
                var prototype = entries.FirstOrDefault();
                var observedAt = measured
                    .Where(static component => component.ObservedAt.HasValue)
                    .Select(static component => component.ObservedAt!.Value)
                    .DefaultIfEmpty()
                    .Max();
                return new QualityComponentResponse(
                    Kind: kind,
                    Label: prototype?.Label ?? kind,
                    Weight: prototype?.Weight ?? 0,
                    Score: measured.Length == 0 ? null : Math.Round(measured.Average(component => component.Score!.Value), 1),
                    Availability: entries.Length == 0 || measured.Length == 0
                        ? "Unavailable"
                        : measured.Length == entries.Length ? "Measured" : "Partial",
                    ObservedAt: observedAt == default ? null : observedAt,
                    IssueCount: entries.Sum(static component => component.IssueCount),
                    Detail: $"Measured for {measured.Length} of {entries.Length} symbol(s).");
            })
            .ToArray();
    }

    private static string GetStatus(double? score, bool isPartial)
    {
        if (!score.HasValue)
            return "Unavailable";
        if (score < 60)
            return "Red";
        if (score < 80 || isPartial)
            return "Amber";
        return "Green";
    }

    private static double ToPercent(double score) => Math.Round(Math.Clamp(score, 0, 1) * 100, 1);

    private static string GapLookupKey(string symbol, string gapId) =>
        $"{symbol.Trim().ToUpperInvariant()}|{gapId.Trim().ToLowerInvariant()}";

    private static void AddSymbol(ISet<string> symbols, string? symbol)
    {
        if (!string.IsNullOrWhiteSpace(symbol))
            symbols.Add(symbol.Trim().ToUpperInvariant());
    }
}
