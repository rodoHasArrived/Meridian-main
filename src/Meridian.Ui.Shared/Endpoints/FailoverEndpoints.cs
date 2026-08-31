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
            [FromServices] StreamingFailoverRegistry? registry) =>
        {
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
                var cfg = store.Load();
                var dataSources = cfg.DataSources ?? new DataSourcesConfig();

                var next = cfg with
                {
                    DataSources = dataSources with
                    {
                        EnableFailover = req.EnableFailover,
                        HealthCheckIntervalSeconds = req.HealthCheckIntervalSeconds,
                        AutoRecover = req.AutoRecover,
                        FailoverTimeoutSeconds = req.FailoverTimeoutSeconds
                    }
                };
                await store.SaveAsync(next, ct).ConfigureAwait(false);

                return Results.Ok();
            },
            "The failover configuration could not be saved.",
            loggerFactory.CreateLogger(nameof(FailoverEndpoints)),
            context: context).ConfigureAwait(false);
        })
            .WithName("UpdateFailoverConfig")
            .Produces(200)
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status500InternalServerError)
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

            return await EndpointHelpers.GuardAsync(async () =>
            {
                var cfg = store.Load();
                var dataSources = cfg.DataSources ?? new DataSourcesConfig();
                var rules = (dataSources.FailoverRules ?? Array.Empty<FailoverRuleConfig>()).ToList();

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

                var idx = rules.FindIndex(r => string.Equals(r.Id, id, StringComparison.OrdinalIgnoreCase));
                if (idx >= 0)
                    rules[idx] = rule;
                else
                    rules.Add(rule);

                var next = cfg with { DataSources = dataSources with { FailoverRules = rules.ToArray() } };
                await store.SaveAsync(next, ct).ConfigureAwait(false);

                return Results.Ok(new { id });
            },
            "The failover rule could not be saved.",
            loggerFactory.CreateLogger(nameof(FailoverEndpoints)),
            context: context).ConfigureAwait(false);
        })
            .WithName("UpsertFailoverRule")
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
            CancellationToken ct) =>
        {
            return await EndpointHelpers.GuardAsync(async () =>
            {
                var cfg = store.Load();
                var dataSources = cfg.DataSources ?? new DataSourcesConfig();
                var rules = (dataSources.FailoverRules ?? Array.Empty<FailoverRuleConfig>()).ToList();

                var removed = rules.RemoveAll(r => string.Equals(r.Id, id, StringComparison.OrdinalIgnoreCase)) > 0;
                if (!removed)
                    return ApiProblemDetails.NotFound(context, "The requested failover rule was not found.");

                var next = cfg with { DataSources = dataSources with { FailoverRules = rules.ToArray() } };
                await store.SaveAsync(next, ct).ConfigureAwait(false);

                return Results.Ok();
            },
            "The failover rule could not be deleted.",
            loggerFactory.CreateLogger(nameof(FailoverEndpoints)),
            context: context).ConfigureAwait(false);
        })
            .WithName("DeleteFailoverRule")
            .Produces(200)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status500InternalServerError)
            .RequirePermission(UserPermission.ManageProviders);

        // Force failover — wired to runtime StreamingFailoverService
        group.MapPost(UiApiRoutes.FailoverForce.Replace("{ruleId}", "{ruleId}"), async (
            HttpContext context,
            [FromServices] ConfigStore store,
            [FromServices] StreamingFailoverRegistry? registry,
            [FromServices] ILoggerFactory loggerFactory,
            string ruleId,
            ForceFailoverRequest req,
            CancellationToken ct) =>
        {
            var cfg = store.Load();
            var rules = cfg.DataSources?.FailoverRules ?? Array.Empty<FailoverRuleConfig>();
            var rule = rules.FirstOrDefault(r => string.Equals(r.Id, ruleId, StringComparison.OrdinalIgnoreCase));

            if (rule is null)
                return ApiProblemDetails.NotFound(context, "The requested failover rule was not found.");

            if (string.IsNullOrWhiteSpace(req.TargetProviderId))
                return ApiProblemDetails.Validation(
                    context,
                    "targetProviderId",
                    "TargetProviderId is required.");

            var providerIds = new[] { rule.PrimaryProviderId }.Concat(rule.BackupProviderIds);
            if (!providerIds.Any(id =>
                    string.Equals(id, req.TargetProviderId, StringComparison.OrdinalIgnoreCase)))
            {
                return ApiProblemDetails.Validation(
                    context,
                    "targetProviderId",
                    "TargetProviderId must identify the rule's primary provider or one of its backup providers.");
            }

            if (registry?.Service is not { } svc)
                return ApiProblemDetails.ServiceUnavailable(context, "streaming failover runtime");
            if (!svc.HasLiveTransitionHandler(ruleId))
                return ApiProblemDetails.ServiceUnavailable(context, "streaming failover runtime");

            try
            {
                var success = await svc
                    .ForceFailoverAsync(ruleId, req.TargetProviderId, ct)
                    .ConfigureAwait(false);
                if (!success)
                {
                    if (!svc.HasLiveTransitionHandler(ruleId))
                        return ApiProblemDetails.ServiceUnavailable(context, "streaming failover runtime");

                    return ApiProblemDetails.Conflict(
                        context,
                        "The active runtime rejected the requested failover transition.");
                }

                return Results.Json(new
                {
                    success = true,
                    implemented = true,
                    message = $"Failover executed: rule '{ruleId}' switched to provider '{req.TargetProviderId}'.",
                    ruleId,
                    targetProviderId = req.TargetProviderId
                }, jsonOptions);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (TimeoutException)
            {
                return ApiProblemDetails.Timeout(context);
            }
            catch (Exception ex)
            {
                loggerFactory.CreateLogger(nameof(FailoverEndpoints))
                    .LogError(ex, "Forced failover failed for rule {RuleId}.", ruleId);
                return ApiProblemDetails.Internal(
                    context,
                    "The failover transition could not be completed.");
            }
        })
            .WithName("ForceFailover")
            .Produces(200)
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status500InternalServerError)
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable)
            .ProducesProblem(StatusCodes.Status504GatewayTimeout)
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
