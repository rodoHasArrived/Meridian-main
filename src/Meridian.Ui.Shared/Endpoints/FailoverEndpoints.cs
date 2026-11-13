using System.Text.Json;
using Meridian.Core.Config;
using Meridian.Application.Monitoring;
using Meridian.DataIntegration.Monitoring;
using Meridian.Contracts.Api;
using Meridian.Identity.Auth;
using Meridian.Infrastructure.Adapters.Failover;
using Meridian.Ui.Shared.Services;
using Microsoft.Extensions.Logging;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Meridian.Ui.Shared.Endpoints;

/// <summary>
/// Extension methods for registering failover-related API endpoints.
/// Shared between web dashboard and desktop application hosts.
/// </summary>
/// <remarks>
/// Endpoints that expose runtime state require a live <see cref="StreamingFailoverRegistry"/>
/// service and fail closed when its state or supporting health evidence is unavailable.
/// </remarks>
public static class FailoverEndpoints
{
    /// <summary>
    /// Maps all failover API endpoints.
    /// </summary>
    public static void MapFailoverEndpoints(this WebApplication app, JsonSerializerOptions jsonOptions)
    {
        var group = app.MapGroup("").WithTags("Failover");
        group.RequireWorkstationTenantScope();

        // Get failover configuration enriched with required live state.
        group.MapGet(UiApiRoutes.FailoverConfig, (
            HttpContext context,
            [FromServices] ConfigStore store,
            [FromServices] StreamingFailoverRegistry? registry,
            bool? configurationOnly) =>
        {
            // Editing retained settings must remain possible when streaming is stopped. This opt-in
            // read never fabricates an active provider or implies the saved rules are running.
            if (configurationOnly == true)
            {
                var settings = store.Load().DataSources ?? new DataSourcesConfig();
                var snapshots = registry?.Service?.GetRuleSnapshots()
                    .ToDictionary(snapshot => snapshot.RuleId, StringComparer.OrdinalIgnoreCase);
                var configuredRules = (settings.FailoverRules ?? []).Select(rule =>
                {
                    var live = registry?.Service?.HasLiveTransitionHandler(rule.Id) == true
                        ? snapshots?.GetValueOrDefault(rule.Id)
                        : null;
                    return new FailoverRuleResponse(rule.Id, rule.PrimaryProviderId, rule.BackupProviderIds,
                        rule.FailoverThreshold, rule.RecoveryThreshold, rule.DataQualityThreshold, rule.MaxLatencyMs,
                        live?.IsInFailoverState ?? false, live?.CurrentActiveProviderId);
                }).ToArray();
                return Results.Json(new FailoverConfigResponse(settings.EnableFailover,
                    settings.HealthCheckIntervalSeconds, settings.AutoRecover, settings.FailoverTimeoutSeconds,
                    configuredRules, configuredRules.Any(rule => rule.CurrentActiveProviderId is not null)), jsonOptions);
            }
            if (registry?.Service is not { } svc)
                return ApiProblemDetails.ServiceUnavailable(context, "streaming failover runtime");

            var cfg = store.Load();
            var dataSources = cfg.DataSources ?? new DataSourcesConfig();
            var rules = dataSources.FailoverRules ?? Array.Empty<FailoverRuleConfig>();
            if (!TryBuildLiveRuleResponses(rules, svc, out var liveRules))
                return ApiProblemDetails.ServiceUnavailable(context, "streaming failover runtime state");

            var response = new FailoverConfigResponse(
                EnableFailover: dataSources.EnableFailover,
                HealthCheckIntervalSeconds: dataSources.HealthCheckIntervalSeconds,
                AutoRecover: dataSources.AutoRecover,
                FailoverTimeoutSeconds: dataSources.FailoverTimeoutSeconds,
                Rules: liveRules
            );

            return Results.Json(response, jsonOptions);
        })
            .WithName("GetFailoverConfig")
            .Produces(200)
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable)
            .RequireAnyPermission(UserPermission.ViewDiagnostics, UserPermission.ManageProviders);

        // Update failover configuration
        group.MapPost(UiApiRoutes.FailoverConfig, async (
            HttpContext context,
            [FromServices] ConfigStore store,
            [FromServices] ILoggerFactory loggerFactory,
            FailoverConfigRequest req,
            CancellationToken ct) =>
        {
            if (req.HealthCheckIntervalSeconds < 1)
                return ApiProblemDetails.Validation(
                    context,
                    "healthCheckIntervalSeconds",
                    "HealthCheckIntervalSeconds must be at least 1.");
            if (req.FailoverTimeoutSeconds < 1)
                return ApiProblemDetails.Validation(
                    context,
                    "failoverTimeoutSeconds",
                    "FailoverTimeoutSeconds must be at least 1.");

            return await EndpointHelpers.GuardAsync(async () =>
            {
                await store.UpdateAsync(cfg =>
                {
                    var dataSources = cfg.DataSources ?? new DataSourcesConfig();

                    return cfg with
                    {
                        DataSources = dataSources with
                        {
                            EnableFailover = req.EnableFailover,
                            HealthCheckIntervalSeconds = req.HealthCheckIntervalSeconds,
                            AutoRecover = req.AutoRecover,
                            FailoverTimeoutSeconds = req.FailoverTimeoutSeconds
                        }
                    };
                }, ct).ConfigureAwait(false);

                return Results.Ok(new { changeImpact = store.GetChangeStatus() });
            },
            "The failover configuration could not be saved.",
            loggerFactory.CreateLogger(nameof(FailoverEndpoints)),
            context: context).ConfigureAwait(false);
        })
            .WithName("UpdateFailoverConfig")
            .Produces(200)
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status500InternalServerError)
            .RequireRateLimiting(UiEndpoints.MutationRateLimitPolicy)
            .RequirePermission(UserPermission.ManageProviders);

        // Get all failover rules enriched with required live state.
        group.MapGet(UiApiRoutes.FailoverRules, (
            HttpContext context,
            [FromServices] ConfigStore store,
            [FromServices] StreamingFailoverRegistry? registry) =>
        {
            if (registry?.Service is not { } svc)
                return ApiProblemDetails.ServiceUnavailable(context, "streaming failover runtime");

            var cfg = store.Load();
            var rules = cfg.DataSources?.FailoverRules ?? Array.Empty<FailoverRuleConfig>();
            if (!TryBuildLiveRuleResponses(rules, svc, out var response))
                return ApiProblemDetails.ServiceUnavailable(context, "streaming failover runtime state");

            return Results.Json(response, jsonOptions);
        })
            .WithName("GetFailoverRules")
            .Produces(200)
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable)
            .RequireAnyPermission(UserPermission.ViewDiagnostics, UserPermission.ManageProviders);

        // Create or update failover rule
        group.MapPost(UiApiRoutes.FailoverRules, async (
            HttpContext context,
            [FromServices] ConfigStore store,
            [FromServices] ILoggerFactory loggerFactory,
            FailoverRuleRequest req,
            CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(req.PrimaryProviderId))
                return ApiProblemDetails.Validation(
                    context,
                    "primaryProviderId",
                    "PrimaryProviderId is required.");

            if (req.BackupProviderIds is null || req.BackupProviderIds.Length == 0)
                return ApiProblemDetails.Validation(
                    context,
                    "backupProviderIds",
                    "At least one backup provider is required.");
            if (req.BackupProviderIds.Any(string.IsNullOrWhiteSpace))
                return ApiProblemDetails.Validation(
                    context,
                    "backupProviderIds",
                    "Backup provider identifiers cannot be empty.");
            if (req.BackupProviderIds.Any(id =>
                    string.Equals(id, req.PrimaryProviderId, StringComparison.OrdinalIgnoreCase)))
            {
                return ApiProblemDetails.Validation(
                    context,
                    "backupProviderIds",
                    "The primary provider cannot also be a backup provider.");
            }
            if (req.FailoverThreshold < 1 || req.RecoveryThreshold < 1)
            {
                return ApiProblemDetails.Validation(
                    context,
                    "thresholds",
                    "FailoverThreshold and RecoveryThreshold must be at least 1.");
            }
            if (!double.IsFinite(req.DataQualityThreshold) || req.DataQualityThreshold < 0 || req.DataQualityThreshold > 100 ||
                !double.IsFinite(req.MaxLatencyMs) || req.MaxLatencyMs < 0)
                return ApiProblemDetails.Validation(context, "thresholds", "Quality must be between 0 and 100 and latency must be non-negative; all thresholds must be finite.");
            if (req.BackupProviderIds.Distinct(StringComparer.OrdinalIgnoreCase).Count() != req.BackupProviderIds.Length)
                return ApiProblemDetails.Validation(context, "backupProviderIds", "Backup provider identifiers must be unique.");
            if (req.Reason?.Length > 2000)
                return ApiProblemDetails.Validation(context, "reason", "The change reason cannot exceed 2000 characters.");

            return await EndpointHelpers.GuardAsync(async () =>
            {
                var id = string.IsNullOrWhiteSpace(req.Id) ? Guid.NewGuid().ToString("N") : req.Id;
                var rule = new FailoverRuleConfig(
                    Id: id,
                    PrimaryProviderId: req.PrimaryProviderId,
                    BackupProviderIds: req.BackupProviderIds,
                    FailoverThreshold: req.FailoverThreshold,
                    RecoveryThreshold: req.RecoveryThreshold,
                    DataQualityThreshold: req.DataQualityThreshold,
                    MaxLatencyMs: req.MaxLatencyMs
                );

                var rejection = await store.UpdateAsync<IResult?>(cfg =>
                {
                    var dataSources = cfg.DataSources ?? new DataSourcesConfig();
                    var rules = (dataSources.FailoverRules ?? Array.Empty<FailoverRuleConfig>()).ToList();

                    var idx = rules.FindIndex(r => string.Equals(r.Id, id, StringComparison.OrdinalIgnoreCase));
                    var audit = dataSources.FailoverRuleAudit ?? [];
                    if (idx >= 0)
                    {
                        var previous = rules[idx];
                        var loosened = rule.DataQualityThreshold < previous.DataQualityThreshold ||
                            (previous.MaxLatencyMs > 0 && (rule.MaxLatencyMs == 0 || rule.MaxLatencyMs > previous.MaxLatencyMs)) ||
                            rule.FailoverThreshold > previous.FailoverThreshold || rule.RecoveryThreshold < previous.RecoveryThreshold;
                        if (loosened)
                        {
                            if (string.IsNullOrWhiteSpace(req.Reason))
                                return (cfg, ApiProblemDetails.Validation(context, "reason", "A recorded reason is required when loosening a rule threshold."));
                            var actor = HttpContextWorkstationTenantContextAccessor.Resolve(context).Actor;
                            if (string.IsNullOrWhiteSpace(actor))
                                return (cfg, ApiProblemDetails.Forbidden(context, "An authenticated actor is required for threshold relaxation."));
                            audit = [.. audit, new FailoverRuleThresholdAudit(id, actor, req.Reason.Trim(), DateTimeOffset.UtcNow,
                                previous.DataQualityThreshold, rule.DataQualityThreshold, previous.MaxLatencyMs, rule.MaxLatencyMs,
                                previous.FailoverThreshold, rule.FailoverThreshold, previous.RecoveryThreshold, rule.RecoveryThreshold)];
                        }
                    }
                    if (idx >= 0)
                        rules[idx] = rule;
                    else
                        rules.Add(rule);

                    return (cfg with { DataSources = dataSources with { FailoverRules = rules.ToArray(), FailoverRuleAudit = audit } }, null);
                }, ct).ConfigureAwait(false);

                if (rejection is not null)
                    return rejection;
                loggerFactory.CreateLogger(nameof(FailoverEndpoints)).LogInformation(
                    "Failover rule {RuleId} saved by {Actor}; reason {Reason}; quality {DataQualityThreshold}; latency {MaxLatencyMs}",
                    id, HttpContextWorkstationTenantContextAccessor.Resolve(context).Actor, req.Reason, req.DataQualityThreshold, req.MaxLatencyMs);
                return Results.Ok(new { id, changeImpact = store.GetChangeStatus() });
            },
            "The failover rule could not be saved.",
            loggerFactory.CreateLogger(nameof(FailoverEndpoints)),
            context: context).ConfigureAwait(false);
        })
            .WithName("UpsertFailoverRule")
            .RequireRateLimiting(UiEndpoints.MutationRateLimitPolicy)
            .Produces(200)
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status500InternalServerError)
            .RequirePermission(UserPermission.ManageProviders);

        // Delete failover rule
        group.MapDelete(UiApiRoutes.FailoverRules + "/{id}", async (
            HttpContext context,
            [FromServices] ConfigStore store,
            [FromServices] ILoggerFactory loggerFactory,
            string id,
            [FromBody] FailoverRuleDeleteRequest? req,
            CancellationToken ct) =>
        {
            return await EndpointHelpers.GuardAsync(async () =>
            {
                var outcome = await store.UpdateAsync<IResult?>(cfg =>
                {
                    var dataSources = cfg.DataSources ?? new DataSourcesConfig();
                    var rules = (dataSources.FailoverRules ?? Array.Empty<FailoverRuleConfig>()).ToList();

                    var previous = rules.FirstOrDefault(rule => string.Equals(rule.Id, id, StringComparison.OrdinalIgnoreCase));
                    if (previous is null)
                        return (cfg, ApiProblemDetails.NotFound(context, "The requested failover rule was not found."));
                    if (string.IsNullOrWhiteSpace(req?.Reason) || req.Reason.Length > 2000)
                        return (cfg, ApiProblemDetails.Validation(context, "reason", "Deleting a rule removes its validation protections; provide a recorded reason of at most 2000 characters."));
                    var actor = HttpContextWorkstationTenantContextAccessor.Resolve(context).Actor;
                    if (string.IsNullOrWhiteSpace(actor))
                        return (cfg, ApiProblemDetails.Forbidden(context, "An authenticated actor is required to remove a failover rule."));
                    var audit = new FailoverRuleThresholdAudit(previous.Id, actor, req.Reason.Trim(), DateTimeOffset.UtcNow,
                        previous.DataQualityThreshold, 0, previous.MaxLatencyMs, 0,
                        previous.FailoverThreshold, previous.FailoverThreshold, previous.RecoveryThreshold, previous.RecoveryThreshold,
                        Operation: "RuleDeleted", PrimaryProviderId: previous.PrimaryProviderId, BackupProviderIds: previous.BackupProviderIds);
                    rules.Remove(previous);
                    return (cfg with { DataSources = dataSources with
                    {
                        FailoverRules = rules.ToArray(),
                        FailoverRuleAudit = [.. (dataSources.FailoverRuleAudit ?? []), audit]
                    } }, null);
                }, ct).ConfigureAwait(false);

                if (outcome is null)
                    loggerFactory.CreateLogger(nameof(FailoverEndpoints)).LogInformation(
                        "Failover rule {RuleId} deleted by {Actor}; reason {Reason}", id,
                        HttpContextWorkstationTenantContextAccessor.Resolve(context).Actor, req?.Reason);
                return outcome ?? Results.Ok(new { changeImpact = store.GetChangeStatus() });
            },
            "The failover rule could not be deleted.",
            loggerFactory.CreateLogger(nameof(FailoverEndpoints)),
            context: context).ConfigureAwait(false);
        })
            .WithName("DeleteFailoverRule")
            .RequireRateLimiting(UiEndpoints.MutationRateLimitPolicy)
            .Produces(200)
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status500InternalServerError)
            .RequirePermission(UserPermission.ManageProviders);

        group.MapPost(UiApiRoutes.FailoverForce, (
            HttpContext context, [FromServices] ConfigStore store,
            [FromServices] StreamingFailoverRegistry? registry, [FromServices] ILoggerFactory loggerFactory,
            string ruleId, ForceFailoverRequest req, CancellationToken ct) =>
            FailoverControlOperations.ExecuteAsync(context, store, registry, loggerFactory, jsonOptions,
                ruleId, req.TargetProviderId, reset: false, ct))
            .WithName("ForceFailover")
            .Produces(200).ProducesValidationProblem().ProducesProblem(404).ProducesProblem(409)
            .ProducesProblem(500).ProducesProblem(503).ProducesProblem(504)
            .RequireRateLimiting(UiEndpoints.MutationRateLimitPolicy)
            .RequirePermission(UserPermission.ManageProviders);

        // Get provider health — returns live data from StreamingFailoverService when available
        group.MapGet(UiApiRoutes.FailoverHealth, async (HttpContext context, [FromServices] ConfigStore store, [FromServices] StreamingFailoverRegistry? registry, [FromServices] ProviderDegradationScorer? scorer, [FromServices] ILoggerFactory? loggerFactory, CancellationToken ct) =>
        {
            if (registry?.Service is not { } svc)
                return ApiProblemDetails.ServiceUnavailable(context, "streaming failover runtime");
            if (scorer is null)
                return ApiProblemDetails.ServiceUnavailable(context, "provider degradation scorer");

            string? calibrationDir = null;
            ProviderKernelProvenanceResponse provenance;
            KernelPromotionRecommendationResponse recommendation;
            try
            {
                var cfg = store.Load();
                // Calibration artifacts live under the configured data root (where the calibration
                // command writes them), not under the install/base directory.
                var dataRoot = store.GetDataRoot(cfg);
                calibrationDir = Path.Combine(dataRoot, "calibration", "provider-degradation");
                var snapshotStore = new ProviderKernelCalibrationSnapshotStore(dataRoot);
                var snapshot = await snapshotStore.GetLatestAsync(ct).ConfigureAwait(false)
                    ?? throw new FileNotFoundException("No provider degradation calibration snapshot was found.");
                var governancePath = Path.Combine(calibrationDir, "latest-governance-decision.json");
                if (!File.Exists(governancePath))
                    throw new FileNotFoundException("No provider degradation governance decision was found.");

                var promotion = JsonSerializer.Deserialize<KernelPromotionDecision>(
                    await File.ReadAllTextAsync(governancePath, ct).ConfigureAwait(false))
                    ?? throw new InvalidDataException("The provider degradation governance decision is invalid.");
                if (snapshot.KernelLineage is null || promotion.BlockingReasons is null)
                    throw new InvalidDataException("Provider degradation calibration evidence is incomplete.");

                provenance = new ProviderKernelProvenanceResponse(
                    snapshot.KernelLineage.BaselineKernelVersion,
                    snapshot.KernelLineage.CandidateKernelVersion,
                    snapshot.KernelLineage.DatasetId,
                    snapshot.KernelLineage.CalibratedAt,
                    snapshot.KernelLineage.CalibratedBy);
                recommendation = new KernelPromotionRecommendationResponse(
                    promotion.Approved,
                    promotion.CalibrationPass,
                    promotion.FreshnessPass,
                    promotion.BlockingReasons.ToArray());
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                loggerFactory?.CreateLogger(nameof(FailoverEndpoints))
                    .LogWarning(
                        ex,
                        "Failed to load provider calibration artifacts from {CalibrationDir}",
                        calibrationDir ?? "unresolved");
                return ApiProblemDetails.ServiceUnavailable(context, "provider degradation calibration evidence");
            }

            try
            {
                var healthSnapshots = svc.GetProviderHealthSnapshots();
                var health = healthSnapshots.Select(h =>
                {
                    var degradationScore = scorer.GetScore(h.ProviderId);
                    var reasons = degradationScore.Reasons
                        .Select(r => new ProviderScoreReasonResponse(r.Code, r.Contribution))
                        .ToArray();

                    return new ProviderHealthResponse(
                        ProviderId: h.ProviderId,
                        ConsecutiveFailures: (int)h.ConsecutiveFailures,
                        ConsecutiveSuccesses: (int)h.ConsecutiveSuccesses,
                        LastIssueTime: h.LastFailureTime,
                        LastSuccessTime: h.LastSuccessTime,
                        DegradationScore: degradationScore.CompositeScore,
                        Reasons: reasons,
                        RecentIssues: h.RecentIssues.Select(issue => new HealthIssueResponse(
                            Type: "provider_health_issue",
                            Message: issue,
                            Timestamp: DateTimeOffset.UtcNow)).ToArray(),
                        KernelProvenance: provenance,
                        PromotionRecommendation: recommendation);
                }).ToArray();

                return Results.Json(health, jsonOptions);
            }
            catch (Exception ex)
            {
                loggerFactory?.CreateLogger(nameof(FailoverEndpoints))
                    .LogWarning(ex, "Failed to calculate live provider degradation health.");
                return ApiProblemDetails.ServiceUnavailable(context, "provider degradation scoring");
            }
        })
            .WithName("GetFailoverHealth")
            .Produces(200)
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable)
            .RequireAnyPermission(UserPermission.ViewDiagnostics, UserPermission.ManageProviders);
    }

    private static bool TryBuildLiveRuleResponses(
        IReadOnlyCollection<FailoverRuleConfig> rules,
        StreamingFailoverService service,
        out FailoverRuleResponse[] responses)
    {
        var snapshots = service.GetRuleSnapshots()
            .ToDictionary(snapshot => snapshot.RuleId, StringComparer.OrdinalIgnoreCase);
        var result = new List<FailoverRuleResponse>(rules.Count);

        foreach (var rule in rules)
        {
            if (!snapshots.TryGetValue(rule.Id, out var liveState))
            {
                responses = Array.Empty<FailoverRuleResponse>();
                return false;
            }

            result.Add(new FailoverRuleResponse(
                Id: rule.Id,
                PrimaryProviderId: rule.PrimaryProviderId,
                BackupProviderIds: rule.BackupProviderIds,
                FailoverThreshold: rule.FailoverThreshold,
                RecoveryThreshold: rule.RecoveryThreshold,
                DataQualityThreshold: rule.DataQualityThreshold,
                MaxLatencyMs: rule.MaxLatencyMs,
                IsInFailoverState: liveState.IsInFailoverState,
                CurrentActiveProviderId: liveState.CurrentActiveProviderId));
        }

        responses = result.ToArray();
        return true;
    }
}
