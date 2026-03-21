using System.Text.Json;
using Meridian.Application.Config;
using Meridian.Contracts.Api;
using Meridian.Infrastructure.Adapters.Failover;
using Meridian.Ui.Shared.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Meridian.Ui.Shared.Endpoints;

/// <summary>
/// Extension methods for registering failover-related API endpoints.
/// Shared between web dashboard and desktop application hosts.
/// </summary>
/// <remarks>
/// When a <see cref="StreamingFailoverRegistry"/> is registered in DI, endpoints
/// return live runtime data (real failover state, actual health metrics).
/// Otherwise, they fall back to configuration-only responses.
/// </remarks>
public static class FailoverEndpoints
{
    /// <summary>
    /// Maps all failover API endpoints.
    /// </summary>
    public static void MapFailoverEndpoints(this WebApplication app, JsonSerializerOptions jsonOptions)
    {
        var group = app.MapGroup("").WithTags("Failover");

        // Get failover configuration (enriched with live state when available)
        group.MapGet(UiApiRoutes.FailoverConfig, (ConfigStore store, [FromServices] StreamingFailoverRegistry? registry) =>
        {
            var cfg = store.Load();
            var dataSources = cfg.DataSources ?? new DataSourcesConfig();
            var ruleSnapshots = registry?.Service?.GetRuleSnapshots();

            var response = new FailoverConfigResponse(
                EnableFailover: dataSources.EnableFailover,
                HealthCheckIntervalSeconds: dataSources.HealthCheckIntervalSeconds,
                AutoRecover: dataSources.AutoRecover,
                FailoverTimeoutSeconds: dataSources.FailoverTimeoutSeconds,
                Rules: (dataSources.FailoverRules ?? Array.Empty<FailoverRuleConfig>())
                    .Select(r =>
                    {
                        var liveState = ruleSnapshots?.FirstOrDefault(s =>
                            string.Equals(s.RuleId, r.Id, StringComparison.OrdinalIgnoreCase));

                        return new FailoverRuleResponse(
                            Id: r.Id,
                            PrimaryProviderId: r.PrimaryProviderId,
                            BackupProviderIds: r.BackupProviderIds,
                            FailoverThreshold: r.FailoverThreshold,
                            RecoveryThreshold: r.RecoveryThreshold,
                            DataQualityThreshold: r.DataQualityThreshold,
                            MaxLatencyMs: r.MaxLatencyMs,
                            IsInFailoverState: liveState?.IsInFailoverState ?? false,
                            CurrentActiveProviderId: liveState?.CurrentActiveProviderId ?? r.PrimaryProviderId
                        );
                    }).ToArray()
            );

            return Results.Json(response, jsonOptions);
        })
            .WithName("GetFailoverConfig")
            .Produces(200);

        // Update failover configuration
        group.MapPost(UiApiRoutes.FailoverConfig, async (ConfigStore store, FailoverConfigRequest req) =>
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
            await store.SaveAsync(next);

            return Results.Ok();
        })
            .WithName("UpdateFailoverConfig")
            .Produces(200);

        // Get all failover rules (enriched with live state)
        group.MapGet(UiApiRoutes.FailoverRules, (ConfigStore store, [FromServices] StreamingFailoverRegistry? registry) =>
        {
            var cfg = store.Load();
            var rules = cfg.DataSources?.FailoverRules ?? Array.Empty<FailoverRuleConfig>();
            var ruleSnapshots = registry?.Service?.GetRuleSnapshots();

            var response = rules.Select(r =>
            {
                var liveState = ruleSnapshots?.FirstOrDefault(s =>
                    string.Equals(s.RuleId, r.Id, StringComparison.OrdinalIgnoreCase));

                return new FailoverRuleResponse(
                    Id: r.Id,
                    PrimaryProviderId: r.PrimaryProviderId,
                    BackupProviderIds: r.BackupProviderIds,
                    FailoverThreshold: r.FailoverThreshold,
                    RecoveryThreshold: r.RecoveryThreshold,
                    DataQualityThreshold: r.DataQualityThreshold,
                    MaxLatencyMs: r.MaxLatencyMs,
                    IsInFailoverState: liveState?.IsInFailoverState ?? false,
                    CurrentActiveProviderId: liveState?.CurrentActiveProviderId ?? r.PrimaryProviderId
                );
            }).ToArray();

            return Results.Json(response, jsonOptions);
        })
            .WithName("GetFailoverRules")
            .Produces(200);

        // Create or update failover rule
        group.MapPost(UiApiRoutes.FailoverRules, async (ConfigStore store, FailoverRuleRequest req) =>
        {
            if (string.IsNullOrWhiteSpace(req.PrimaryProviderId))
                return Results.BadRequest("PrimaryProviderId is required.");

            if (req.BackupProviderIds is null || req.BackupProviderIds.Length == 0)
                return Results.BadRequest("At least one backup provider is required.");

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
            await store.SaveAsync(next);

            return Results.Ok(new { id });
        })
            .WithName("UpsertFailoverRule")
            .Produces(200)
            .Produces(400);

        // Delete failover rule
        group.MapDelete(UiApiRoutes.FailoverRules + "/{id}", async (ConfigStore store, string id) =>
        {
            var cfg = store.Load();
            var dataSources = cfg.DataSources ?? new DataSourcesConfig();
            var rules = (dataSources.FailoverRules ?? Array.Empty<FailoverRuleConfig>()).ToList();

            var removed = rules.RemoveAll(r => string.Equals(r.Id, id, StringComparison.OrdinalIgnoreCase)) > 0;
            if (!removed)
                return Results.NotFound();

            var next = cfg with { DataSources = dataSources with { FailoverRules = rules.ToArray() } };
            await store.SaveAsync(next);

            return Results.Ok();
        })
            .WithName("DeleteFailoverRule")
            .Produces(200)
            .Produces(404);

        // Force failover — wired to runtime StreamingFailoverService
        group.MapPost(UiApiRoutes.FailoverForce.Replace("{ruleId}", "{ruleId}"), (ConfigStore store, [FromServices] StreamingFailoverRegistry? registry, string ruleId, ForceFailoverRequest req) =>
        {
            var cfg = store.Load();
            var rules = cfg.DataSources?.FailoverRules ?? Array.Empty<FailoverRuleConfig>();
            var rule = rules.FirstOrDefault(r => string.Equals(r.Id, ruleId, StringComparison.OrdinalIgnoreCase));

            if (rule is null)
                return Results.NotFound(new { error = $"Failover rule '{ruleId}' not found." });

            if (string.IsNullOrWhiteSpace(req.TargetProviderId))
                return Results.BadRequest(new { error = "TargetProviderId is required." });

            if (registry?.Service is { } svc)
            {
                var success = svc.ForceFailover(ruleId, req.TargetProviderId);
                return Results.Json(new
                {
                    success,
                    implemented = true,
                    message = success
                        ? $"Failover executed: rule '{ruleId}' switched to provider '{req.TargetProviderId}'."
                        : $"Failover failed: rule '{ruleId}' could not switch to provider '{req.TargetProviderId}'. Check provider availability.",
                    ruleId,
                    targetProviderId = req.TargetProviderId
                }, jsonOptions);
            }

            return Results.Json(new
            {
                success = false,
                implemented = false,
                message = $"Failover rule '{ruleId}' found, but streaming failover is not active in the current session. " +
                          $"Enable failover in configuration and restart with failover rules to use runtime failover.",
                ruleId,
                targetProviderId = req.TargetProviderId
            }, jsonOptions);
        })
            .WithName("ForceFailover")
            .Produces(200)
            .Produces(400)
            .Produces(404);

        // Get provider health — returns live data from StreamingFailoverService when available
        group.MapGet(UiApiRoutes.FailoverHealth, (ConfigStore store, [FromServices] StreamingFailoverRegistry? registry) =>
        {
            if (registry?.Service is { } svc)
            {
                var healthSnapshots = svc.GetProviderHealthSnapshots();
                var health = healthSnapshots.Select(h => new
                {
                    providerId = h.ProviderId,
                    consecutiveFailures = h.ConsecutiveFailures,
                    consecutiveSuccesses = h.ConsecutiveSuccesses,
                    lastIssueTime = h.LastFailureTime,
                    lastSuccessTime = h.LastSuccessTime,
                    averageLatencyMs = h.AverageLatencyMs,
                    recentIssues = h.RecentIssues,
                    isSimulated = false
                }).ToArray();

                return Results.Json(health, jsonOptions);
            }

            var cfg = store.Load();
            var sources = cfg.DataSources?.Sources ?? Array.Empty<DataSourceConfig>();
            var metricsStatus = store.TryLoadProviderMetrics();

            var fallbackHealth = sources.Select(s =>
            {
                var realMetrics = metricsStatus?.Providers.FirstOrDefault(p =>
                    string.Equals(p.ProviderId, s.Id, StringComparison.OrdinalIgnoreCase));

                var hasRealData = realMetrics is not null;

                return new
                {
                    providerId = s.Id,
                    consecutiveFailures = hasRealData ? realMetrics!.ConnectionFailures : 0L,
                    consecutiveSuccesses = hasRealData ? (realMetrics!.ConnectionAttempts - realMetrics.ConnectionFailures) : 0L,
                    lastIssueTime = (DateTimeOffset?)null,
                    lastSuccessTime = hasRealData ? realMetrics!.Timestamp : (DateTimeOffset?)null,
                    averageLatencyMs = 0.0,
                    recentIssues = Array.Empty<string>(),
                    isSimulated = !hasRealData
                };
            }).ToArray();

            return Results.Json(fallbackHealth, jsonOptions);
        })
            .WithName("GetFailoverHealth")
            .Produces(200);
    }
}
