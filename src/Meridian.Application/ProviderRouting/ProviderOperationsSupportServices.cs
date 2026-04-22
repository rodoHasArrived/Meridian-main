using Meridian.Application.Config;
using Meridian.Contracts.Api;
using Meridian.Contracts.RuleEvaluation;

namespace Meridian.Application.ProviderRouting;

/// <summary>
/// Returns built-in and configured provider presets.
/// </summary>
public sealed class ProviderPresetService
{
    private readonly UI.ConfigStore _store;

    public ProviderPresetService(UI.ConfigStore store)
    {
        _store = store;
    }

    public Task<IReadOnlyList<ProviderPresetDto>> GetPresetsAsync(CancellationToken ct = default)
    {
        var cfg = _store.Load();
        var presets = ProviderRoutingConfigExtensions.GetEffectivePresets(cfg)
            .Select(ProviderRoutingMapper.ToDto)
            .ToArray();
        return Task.FromResult<IReadOnlyList<ProviderPresetDto>>(presets);
    }

    public async Task<ProviderPresetDto?> ApplyAsync(string presetId, CancellationToken ct = default)
    {
        var cfg = _store.Load();
        var section = ProviderRoutingConfigExtensions.GetSection(cfg);
        var presets = ProviderRoutingConfigExtensions.GetEffectivePresets(cfg).ToList();
        var target = presets.FirstOrDefault(p => string.Equals(p.PresetId, presetId, StringComparison.OrdinalIgnoreCase));
        if (target is null)
            return null;

        presets = presets
            .Select(p => p with { IsEnabled = string.Equals(p.PresetId, presetId, StringComparison.OrdinalIgnoreCase) })
            .ToList();

        var persisted = presets.Where(p => !p.IsBuiltIn || p.IsEnabled).ToArray();
        await _store.SaveAsync(cfg with
        {
            ProviderConnections = section with
            {
                Presets = persisted
            }
        }, ct).ConfigureAwait(false);

        return ProviderRoutingMapper.ToDto(presets.First(p => p.IsEnabled));
    }
}

/// <summary>
/// Calculates trust scores for configured provider connections.
/// </summary>
public sealed class ProviderTrustScoringService
{
    internal const string DecisionSchemaVersion = "1.0.0";
    internal const string KernelVersion = "provider-trust-csharp-v1";

    private readonly UI.ConfigStore _store;
    private readonly ProviderSdk.IProviderConnectionHealthSource _healthSource;

    public ProviderTrustScoringService(UI.ConfigStore store, ProviderSdk.IProviderConnectionHealthSource healthSource)
    {
        _store = store;
        _healthSource = healthSource;
    }

    public async Task<IReadOnlyList<ProviderTrustSnapshotDto>> GetTrustSnapshotsAsync(CancellationToken ct = default)
    {
        var cfg = _store.Load();
        var section = ProviderRoutingConfigExtensions.GetSection(cfg);
        var certifications = (section.Certifications ?? Array.Empty<ProviderCertificationConfig>())
            .ToDictionary(c => c.ConnectionId, StringComparer.OrdinalIgnoreCase);

        var snapshots = new List<ProviderTrustSnapshotDto>();
        foreach (var connection in section.Connections ?? Array.Empty<ProviderConnectionConfig>())
        {
            var health = await _healthSource.GetHealthAsync(connection.ConnectionId, connection.ProviderFamilyId, ct).ConfigureAwait(false);
            certifications.TryGetValue(connection.ConnectionId, out var certification);
            var reasons = new List<DecisionReason>();
            var score = 100.0;

            if (!connection.Enabled)
            {
                score -= 60;
                reasons.Add(new DecisionReason(
                    RuleId: "provider-trust.connection-enabled",
                    Weight: -60,
                    ReasonCode: "CONNECTION_DISABLED",
                    HumanExplanation: "Connection is disabled.",
                    Severity: DecisionSeverity.Critical,
                    EvidenceRefs: [$"connection:{connection.ConnectionId}"]));
            }

            if (!health.IsHealthy)
            {
                score -= 35;
                reasons.Add(new DecisionReason(
                    RuleId: "provider-trust.health-status",
                    Weight: -35,
                    ReasonCode: "HEALTH_NOT_HEALTHY",
                    HumanExplanation: $"Health check status is {health.Status}.",
                    Severity: DecisionSeverity.Error,
                    EvidenceRefs: [$"health-status:{health.Status}"]));
            }

            if (!connection.ProductionReady)
            {
                score -= 10;
                reasons.Add(new DecisionReason(
                    RuleId: "provider-trust.production-ready",
                    Weight: -10,
                    ReasonCode: "PRODUCTION_READY_FALSE",
                    HumanExplanation: "Connection has not been marked production ready.",
                    Severity: DecisionSeverity.Warning,
                    EvidenceRefs: [$"connection:{connection.ConnectionId}"]));
            }

            var isCertificationFresh = certification is not null &&
                                       (certification.ExpiresAt is null || certification.ExpiresAt >= DateTimeOffset.UtcNow);
            if (certification is null)
            {
                score -= 15;
                reasons.Add(new DecisionReason(
                    RuleId: "provider-trust.certification-presence",
                    Weight: -15,
                    ReasonCode: "CERTIFICATION_MISSING",
                    HumanExplanation: "No certification run has been recorded.",
                    Severity: DecisionSeverity.Warning,
                    EvidenceRefs: [$"connection:{connection.ConnectionId}"]));
            }
            else if (!isCertificationFresh)
            {
                score -= 20;
                reasons.Add(new DecisionReason(
                    RuleId: "provider-trust.certification-freshness",
                    Weight: -20,
                    ReasonCode: "CERTIFICATION_EXPIRED",
                    HumanExplanation: "Certification has expired.",
                    Severity: DecisionSeverity.Error,
                    EvidenceRefs: [$"certification-expiry:{certification.ExpiresAt:O}"]));
            }

            var clampedScore = Math.Clamp(score, 0, 100);
            var decision = new DecisionResult<double>(
                Score: clampedScore,
                Reasons: reasons,
                Trace: new DecisionTrace(
                    SchemaVersion: DecisionSchemaVersion,
                    KernelVersion: KernelVersion,
                    EvaluatedAt: DateTimeOffset.UtcNow,
                    CorrelationId: null,
                    Metadata: new Dictionary<string, string?>
                    {
                        ["kernel"] = "provider-trust",
                        ["connectionId"] = connection.ConnectionId,
                        ["providerFamilyId"] = connection.ProviderFamilyId
                    }));

            snapshots.Add(new ProviderTrustSnapshotDto(
                ConnectionId: connection.ConnectionId,
                ProviderFamilyId: connection.ProviderFamilyId,
                Score: decision.Score,
                IsHealthy: health.IsHealthy,
                HealthStatus: health.Status,
                IsProductionReady: connection.ProductionReady,
                IsCertificationFresh: isCertificationFresh,
                Signals: decision.Reasons.Select(r => r.HumanExplanation).ToArray(),
                Decision: decision));
        }

        return snapshots;
    }
}

/// <summary>
/// Runs and persists provider certifications.
/// </summary>
public sealed class ProviderCertificationService
{
    private readonly UI.ConfigStore _store;
    private readonly IProviderFamilyCatalogService _catalog;
    private readonly ProviderSdk.IProviderCertificationRunner _runner;

    public ProviderCertificationService(
        UI.ConfigStore store,
        IProviderFamilyCatalogService catalog,
        ProviderSdk.IProviderCertificationRunner runner)
    {
        _store = store;
        _catalog = catalog;
        _runner = runner;
    }

    public Task<IReadOnlyList<ProviderCertificationDto>> GetCertificationsAsync(CancellationToken ct = default)
    {
        var cfg = _store.Load();
        var certifications = (ProviderRoutingConfigExtensions.GetSection(cfg).Certifications ?? Array.Empty<ProviderCertificationConfig>())
            .Select(ProviderRoutingMapper.ToDto)
            .ToArray();
        return Task.FromResult<IReadOnlyList<ProviderCertificationDto>>(certifications);
    }

    public async Task<ProviderCertificationDto?> RunAsync(string connectionId, CancellationToken ct = default)
    {
        var cfg = _store.Load();
        var section = ProviderRoutingConfigExtensions.GetSection(cfg);
        var connection = (section.Connections ?? Array.Empty<ProviderConnectionConfig>())
            .FirstOrDefault(c => string.Equals(c.ConnectionId, connectionId, StringComparison.OrdinalIgnoreCase));
        if (connection is null)
            return null;

        var adapter = _catalog.GetFamily(connection.ProviderFamilyId);
        if (adapter is null)
            return null;

        var result = await _runner.RunAsync(connection.ConnectionId, adapter, ct).ConfigureAwait(false);

        var certifications = (section.Certifications ?? Array.Empty<ProviderCertificationConfig>()).ToList();
        var nextCertification = new ProviderCertificationConfig(
            ConnectionId: connection.ConnectionId,
            Status: result.Status,
            LastRunAt: result.RanAt,
            ExpiresAt: result.Success ? result.RanAt.AddDays(30) : result.RanAt.AddDays(7),
            ProductionReady: result.Success,
            Checks: result.Checks.ToArray(),
            Notes: result.Success ? ["Certification passed."] : ["Certification failed."]);

        var existingIndex = certifications.FindIndex(c => string.Equals(c.ConnectionId, connection.ConnectionId, StringComparison.OrdinalIgnoreCase));
        if (existingIndex >= 0)
            certifications[existingIndex] = nextCertification;
        else
            certifications.Add(nextCertification);

        var connections = (section.Connections ?? Array.Empty<ProviderConnectionConfig>())
            .Select(c => string.Equals(c.ConnectionId, connection.ConnectionId, StringComparison.OrdinalIgnoreCase)
                ? c with { ProductionReady = result.Success }
                : c)
            .ToArray();

        await _store.SaveAsync(cfg with
        {
            ProviderConnections = section with
            {
                Connections = connections,
                Certifications = certifications.ToArray()
            }
        }, ct).ConfigureAwait(false);

        return ProviderRoutingMapper.ToDto(nextCertification);
    }
}

/// <summary>
/// Converts route decisions into API-facing explainability models.
/// </summary>
public sealed class ProviderRouteExplainabilityService
{
    private readonly ProviderRoutingService _routingService;

    public ProviderRouteExplainabilityService(ProviderRoutingService routingService)
    {
        _routingService = routingService;
    }

    public async Task<RoutePreviewResponse> PreviewAsync(RoutePreviewRequest request, CancellationToken ct = default)
    {
        var result = await _routingService.RouteAsync(ProviderRoutingMapper.ToRouteContext(request), ct).ConfigureAwait(false);
        return ProviderRoutingMapper.ToDto(result);
    }

    public Task<IReadOnlyList<RoutePreviewResponse>> GetHistoryAsync(CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<RoutePreviewResponse>>(_routingService.GetRouteHistory()
            .Select(ProviderRoutingMapper.ToDto)
            .ToArray());
}
