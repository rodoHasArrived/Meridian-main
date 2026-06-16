using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Meridian.Contracts.Integrations;

namespace Meridian.Application.Integrations;

public sealed class ProviderIntegrationSyncOrchestrationService
{
    private readonly ProviderIntegrationSyncPlanningService planner;
    private readonly ProviderIntegrationRestDryRunService restDryRun;

    public ProviderIntegrationSyncOrchestrationService(
        ProviderIntegrationSyncPlanningService planner,
        ProviderIntegrationRestDryRunService restDryRun)
    {
        this.planner = planner ?? throw new ArgumentNullException(nameof(planner));
        this.restDryRun = restDryRun ?? throw new ArgumentNullException(nameof(restDryRun));
    }

    public async Task<ProviderIntegrationRunDueSyncResultDto> RunDueAsync(
        ProviderIntegrationRunDueSyncRequestDto request,
        CancellationToken ct = default)
        => await RunDueAsync(null, request, ct).ConfigureAwait(false);

    public async Task<ProviderIntegrationRunDueSyncResultDto> RunDueAsync(
        string? tenantId,
        ProviderIntegrationRunDueSyncRequestDto request,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.ConnectionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.RequestedBy);
        ct.ThrowIfCancellationRequested();

        var plan = await planner
            .PlanAsync(
                tenantId,
                new ProviderIntegrationSyncPlanRequestDto(request.ConnectionId, request.RequestedAt),
                ct)
            .ConfigureAwait(false);

        var items = new List<ProviderIntegrationRunDueSyncItemResultDto>(plan.Items.Count);
        foreach (var planItem in plan.Items)
        {
            ct.ThrowIfCancellationRequested();
            items.Add(await RunPlanItemAsync(tenantId, request, plan, planItem, ct).ConfigureAwait(false));
        }

        return new ProviderIntegrationRunDueSyncResultDto(
            request.ConnectionId,
            request.RequestedAt,
            StartedCount: items.Count(item => item.Started),
            SkippedCount: items.Count(item => item.Skipped),
            items);
    }

    private async Task<ProviderIntegrationRunDueSyncItemResultDto> RunPlanItemAsync(
        string? tenantId,
        ProviderIntegrationRunDueSyncRequestDto request,
        ProviderIntegrationSyncPlanDto plan,
        ProviderIntegrationSyncPlanItemDto planItem,
        CancellationToken ct)
    {
        if (planItem.IsBlocked)
        {
            return Skipped(planItem, planItem.Reason, planItem.Issues);
        }

        if (!planItem.IsDue)
        {
            return Skipped(planItem, planItem.Reason, planItem.Issues);
        }

        if (string.IsNullOrWhiteSpace(planItem.EndpointKey))
        {
            return Skipped(
                planItem,
                "endpoint-missing",
                [
                    new ValidationIssueDto(
                        "sync-run.endpoint-missing",
                        ProviderIntegrationIssueSeverityDto.Critical,
                        $"No executable endpoint is configured for {planItem.Capability}.",
                        planItem.Capability.ToString(),
                        "Map a read-only REST endpoint before running scheduled sync.")
                ]);
        }

        var syncRunId = BuildSyncRunId(request.ConnectionId, planItem.Capability, request.RequestedAt);
        try
        {
            var dryRun = await restDryRun
                .RunRestDryRunAsync(
                    tenantId,
                    new ProviderIntegrationRestDryRunRequestDto(
                        syncRunId,
                        plan.ManifestId,
                        request.ConnectionId,
                        planItem.Capability,
                        planItem.EndpointKey,
                        ResolveParameterBag(request.PathParametersByCapability, planItem.Capability),
                        ResolveParameterBag(request.QueryParametersByCapability, planItem.Capability),
                        request.RequestedBy,
                        request.RequestedAt,
                        request.MaxPages <= 0 ? 1 : request.MaxPages),
                    ct)
                .ConfigureAwait(false);

            return new ProviderIntegrationRunDueSyncItemResultDto(
                planItem.Capability,
                planItem.EndpointKey,
                Started: true,
                Skipped: false,
                dryRun.Status == ProviderIntegrationProcessingStatusDto.Blocked ? "blocked" : "started",
                syncRunId,
                dryRun,
                dryRun.Issues);
        }
        catch (InvalidOperationException ex)
        {
            return Skipped(
                planItem,
                "runtime-blocked",
                [
                    new ValidationIssueDto(
                        "sync-run.runtime-blocked",
                        ProviderIntegrationIssueSeverityDto.Critical,
                        ex.Message,
                        planItem.EndpointKey,
                        "Review endpoint parameters, integration type, mapping, and activation state before rerunning due sync.")
                ]);
        }
    }

    private static ProviderIntegrationRunDueSyncItemResultDto Skipped(
        ProviderIntegrationSyncPlanItemDto planItem,
        string reason,
        IReadOnlyList<ValidationIssueDto> issues)
        => new(
            planItem.Capability,
            planItem.EndpointKey,
            Started: false,
            Skipped: true,
            reason,
            SyncRunId: null,
            DryRunResult: null,
            issues);

    private static IReadOnlyDictionary<string, string> ResolveParameterBag(
        IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> bags,
        ProviderCapabilityKindDto capability)
    {
        if (bags.TryGetValue(capability.ToString(), out var exact))
        {
            return exact;
        }

        var lowerCamel = char.ToLowerInvariant(capability.ToString()[0]) + capability.ToString()[1..];
        if (bags.TryGetValue(lowerCamel, out var camel))
        {
            return camel;
        }

        var match = bags.FirstOrDefault(candidate =>
            StringComparer.OrdinalIgnoreCase.Equals(candidate.Key, capability.ToString()));
        return match.Value ?? new Dictionary<string, string>();
    }

    private static string BuildSyncRunId(
        string connectionId,
        ProviderCapabilityKindDto capability,
        DateTimeOffset requestedAt)
    {
        var input = string.Join(
            "|",
            "run-due",
            connectionId,
            capability.ToString(),
            requestedAt.UtcDateTime.ToString("O", CultureInfo.InvariantCulture));
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(input));
        return $"run-due-{Convert.ToHexString(hash)[..24].ToLowerInvariant()}";
    }
}
