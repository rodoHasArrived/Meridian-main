using System.Text.Json;
using Meridian.Contracts.Api;
using Meridian.Core.Config;
using Meridian.Infrastructure.Adapters.Failover;
using Meridian.Ui.Shared.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace Meridian.Ui.Shared.Endpoints;

/// <summary>One committed runtime transition path for both current and compatibility controls.</summary>
internal static class FailoverControlOperations
{
    internal static async Task<IResult> ExecuteAsync(
        HttpContext context, ConfigStore store, StreamingFailoverRegistry? registry,
        ILoggerFactory loggerFactory, JsonSerializerOptions jsonOptions,
        string? ruleId, string? targetProvider, bool reset, CancellationToken ct)
    {
        var cfg = store.Load();
        var rules = cfg.DataSources?.FailoverRules ?? [];
        var candidates = string.IsNullOrWhiteSpace(ruleId)
            ? rules
            : rules.Where(rule => string.Equals(rule.Id, ruleId, StringComparison.OrdinalIgnoreCase)).ToArray();
        if (candidates.Length == 0)
            return ApiProblemDetails.NotFound(context, "The requested failover rule was not found.");

        // Compatibility callers may supply the retained source's name or provider family.
        var target = targetProvider;
        if (!reset && !string.IsNullOrWhiteSpace(target))
        {
            var matchingSources = (cfg.DataSources?.Sources ?? []).Where(source =>
                string.Equals(source.Id, targetProvider, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(source.Name, targetProvider, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(source.Provider.ToString(), targetProvider, StringComparison.OrdinalIgnoreCase)).ToArray();
            if (matchingSources.Length == 1)
                target = matchingSources[0].Id;
            candidates = candidates.Where(rule => new[] { rule.PrimaryProviderId }.Concat(rule.BackupProviderIds)
                .Contains(target!, StringComparer.OrdinalIgnoreCase)).ToArray();
            if (candidates.Length == 0)
                return ApiProblemDetails.Validation(context, "targetProviderId", "TargetProviderId must identify the rule's primary provider or one of its backup providers.");
        }

        if (candidates.Length != 1)
            return ApiProblemDetails.Conflict(context, "Specify a ruleId; more than one failover rule matches this operation.");
        var selectedRule = candidates[0];
        target = reset ? selectedRule.PrimaryProviderId : target;
        if (string.IsNullOrWhiteSpace(target))
            return ApiProblemDetails.Validation(context, "targetProviderId", "A target provider is required.");
        if (registry?.Service is not { } service || !service.HasLiveTransitionHandler(selectedRule.Id))
            return ApiProblemDetails.ServiceUnavailable(context, "streaming failover runtime");

        try
        {
            // An idempotent reset is already satisfied, but it still requires actual runtime evidence.
            var alreadyActive = string.Equals(service.GetActiveProviderId(selectedRule.Id), target, StringComparison.OrdinalIgnoreCase);
            if (!alreadyActive && !await service.ForceFailoverAsync(selectedRule.Id, target, ct).ConfigureAwait(false))
                return service.HasLiveTransitionHandler(selectedRule.Id)
                    ? ApiProblemDetails.Conflict(context, "The active runtime rejected the requested failover transition.")
                    : ApiProblemDetails.ServiceUnavailable(context, "streaming failover runtime");

            return Results.Json(new
            {
                success = true, implemented = true, triggered = !reset, reset,
                ruleId = selectedRule.Id, targetProviderId = target,
                currentActiveProviderId = service.GetActiveProviderId(selectedRule.Id),
                message = alreadyActive ? "The requested provider is already active." : "The live provider hand-off completed.",
                timestamp = DateTimeOffset.UtcNow
            }, jsonOptions);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (TimeoutException) { return ApiProblemDetails.Timeout(context); }
        catch (Exception ex)
        {
            loggerFactory.CreateLogger(nameof(FailoverControlOperations)).LogError(ex,
                "Failover transition failed for rule {RuleId} to provider {TargetProviderId}.", selectedRule.Id, target);
            return ApiProblemDetails.Internal(context, "The failover transition could not be completed.");
        }
    }
}
