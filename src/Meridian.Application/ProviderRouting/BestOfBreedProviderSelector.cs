using Meridian.Application.Monitoring;
using Meridian.Application.UI;
using Meridian.ProviderSdk;
using Meridian.Storage.Services;

namespace Meridian.Application.ProviderRouting;

public interface IBestOfBreedProviderSelector
{
    Task<ProviderRouteResult> SelectAsync(ProviderRouteContext context, CancellationToken ct = default);
}

public sealed class BestOfBreedProviderSelector : IBestOfBreedProviderSelector
{
    private const double HealthWeight = 0.35;
    private const double LatencyWeight = 0.20;
    private const double DataQualityWeight = 0.25;
    private const double CoverageWeight = 0.15;
    private const double PolicyGateWeight = 0.05;

    private readonly ProviderRoutingService _routingService;
    private readonly IProviderConnectionHealthSource _healthSource;
    private readonly IDataQualityService? _dataQualityService;
    private readonly ConfigStore _store;

    public BestOfBreedProviderSelector(
        ProviderRoutingService routingService,
        IProviderConnectionHealthSource healthSource,
        ConfigStore store,
        IDataQualityService? dataQualityService = null)
    {
        _routingService = routingService;
        _healthSource = healthSource;
        _store = store;
        _dataQualityService = dataQualityService;
    }

    public async Task<ProviderRouteResult> SelectAsync(ProviderRouteContext context, CancellationToken ct = default)
    {
        var routeResult = await _routingService.RouteAsync(context, ct).ConfigureAwait(false);
        if (routeResult.Candidates.Count == 0)
            return routeResult;

        var qualityScores = await LoadQualityScoresAsync(context, ct).ConfigureAwait(false);
        var metricsByProvider = (_store.TryLoadProviderMetrics()?.Providers ?? Array.Empty<ProviderMetrics>())
            .ToDictionary(static m => m.ProviderId, StringComparer.OrdinalIgnoreCase);

        var scored = new List<ProviderRouteDecision>(routeResult.Candidates.Count);
        foreach (var candidate in routeResult.Candidates)
        {
            var health = await _healthSource
                .GetHealthAsync(candidate.ConnectionId, candidate.ProviderFamilyId, ct)
                .ConfigureAwait(false);

            var healthScore = Clamp01(health.Score / 100d);
            var latencyScore = ComputeLatencyScore(candidate, metricsByProvider);
            var dataQualityScore = ComputeDataQualityScore(candidate, qualityScores);
            var coverageScore = ComputeCoverageScore(candidate.ScopeRank);
            var policyGateScore = IsPolicyGated(candidate) ? 0d : 1d;
            var composite =
                (healthScore * HealthWeight) +
                (latencyScore * LatencyWeight) +
                (dataQualityScore * DataQualityWeight) +
                (coverageScore * CoverageWeight) +
                (policyGateScore * PolicyGateWeight);

            var reasons = candidate.ReasonCodes.Concat(
            [
                $"Selector composite score: {composite:F4}",
                $"Weights h={HealthWeight:F2}, l={LatencyWeight:F2}, q={DataQualityWeight:F2}, c={CoverageWeight:F2}, p={PolicyGateWeight:F2}.",
                $"Component scores health={healthScore:F3}, latency={latencyScore:F3}, quality={dataQualityScore:F3}, coverage={coverageScore:F3}, policy={policyGateScore:F3}."
            ]).ToArray();

            scored.Add(candidate with
            {
                ReasonCodes = reasons,
                CompositeScore = composite,
                HealthScore = healthScore,
                LatencyScore = latencyScore,
                DataQualityScore = dataQualityScore,
                CoverageScore = coverageScore,
                PolicyGateScore = policyGateScore,
                IsHealthy = health.IsHealthy
            });
        }

        var rankedCandidates = scored
            .OrderByDescending(static c => c.CompositeScore)
            .ThenByDescending(static c => c.ScopeRank)
            .ThenBy(static c => c.Priority)
            .ThenBy(static c => c.ConnectionId, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var selected = rankedCandidates.FirstOrDefault(IsSelectable);
        var gate = selected is null
            ? rankedCandidates.Select(static c => c.PolicyGate).FirstOrDefault(static g => !string.IsNullOrWhiteSpace(g)) ?? routeResult.PolicyGate
            : null;

        return routeResult with
        {
            SelectedDecision = selected,
            Candidates = rankedCandidates,
            PolicyGate = gate
        };
    }

    private static bool IsSelectable(ProviderRouteDecision decision)
    {
        if (IsPolicyGated(decision))
            return false;

        if (decision.SafetyMode is ProviderSafetyMode.NoAutomaticFailover or ProviderSafetyMode.SameInstitutionOnly && !decision.IsHealthy)
            return false;

        return decision.SafetyMode != ProviderSafetyMode.ManualApprovalRequired;
    }

    private static bool IsPolicyGated(ProviderRouteDecision decision)
        => !string.IsNullOrWhiteSpace(decision.PolicyGate) ||
           decision.SafetyMode == ProviderSafetyMode.ManualApprovalRequired;

    private async Task<Dictionary<string, double>> LoadQualityScoresAsync(ProviderRouteContext context, CancellationToken ct)
    {
        if (_dataQualityService is null || string.IsNullOrWhiteSpace(context.Symbol))
            return new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);

        var rankings = await _dataQualityService
            .RankSourcesAsync(context.Symbol, DateTimeOffset.UtcNow, MarketEventType.Trade, ct)
            .ConfigureAwait(false);

        return rankings.ToDictionary(r => r.Source, r => Clamp01(r.QualityScore), StringComparer.OrdinalIgnoreCase);
    }

    private static double ComputeLatencyScore(
        ProviderRouteDecision candidate,
        IReadOnlyDictionary<string, ProviderMetrics> metricsByProvider)
    {
        if (!metricsByProvider.TryGetValue(candidate.ConnectionId, out var metrics) &&
            !metricsByProvider.TryGetValue(candidate.ProviderFamilyId, out metrics))
        {
            return 0.5d;
        }

        if (metrics.AverageLatencyMs <= 0)
            return 1d;

        return Clamp01(1d / (1d + (metrics.AverageLatencyMs / 250d)));
    }

    private static double ComputeDataQualityScore(
        ProviderRouteDecision candidate,
        IReadOnlyDictionary<string, double> qualityScores)
    {
        if (qualityScores.TryGetValue(candidate.ConnectionId, out var connectionScore))
            return connectionScore;

        if (qualityScores.TryGetValue(candidate.ProviderFamilyId, out var familyScore))
            return familyScore;

        return 0.5d;
    }

    private static double ComputeCoverageScore(int scopeRank)
    {
        if (scopeRank <= 0)
            return 0d;

        const double maxScopeRank = 500d;
        return Clamp01(Math.Max(1d, scopeRank) / maxScopeRank);
    }

    private static double Clamp01(double value) => Math.Clamp(value, 0d, 1d);
}
