using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Meridian.Contracts.Integrations;

namespace Meridian.Application.Integrations;

public sealed class ProviderIntegrationSyncOrchestrationService
{
    private readonly ProviderIntegrationSyncPlanningService planner;
    private readonly ProviderIntegrationRestDryRunService restDryRun;
    private readonly IProviderIntegrationManifestStore store;

    public ProviderIntegrationSyncOrchestrationService(
        ProviderIntegrationSyncPlanningService planner,
        ProviderIntegrationRestDryRunService restDryRun,
        IProviderIntegrationManifestStore store)
    {
        this.planner = planner ?? throw new ArgumentNullException(nameof(planner));
        this.restDryRun = restDryRun ?? throw new ArgumentNullException(nameof(restDryRun));
        this.store = store ?? throw new ArgumentNullException(nameof(store));
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
        var scopedStore = ResolveStore(tenantId);
        var connection = await scopedStore.GetConnectionAsync(request.ConnectionId, ct).ConfigureAwait(false)
            ?? throw new KeyNotFoundException($"Provider integration connection '{request.ConnectionId}' was not found.");
        var manifest = await scopedStore.GetManifestAsync(plan.ManifestId, ct).ConfigureAwait(false)
            ?? throw new KeyNotFoundException($"Provider integration manifest '{plan.ManifestId}' was not found.");

        var items = new List<ProviderIntegrationRunDueSyncItemResultDto>(plan.Items.Count);
        var completedDryRunsByEndpoint = new Dictionary<string, ProviderIntegrationDryRunResultDto>(StringComparer.OrdinalIgnoreCase);
        foreach (var planItem in plan.Items)
        {
            ct.ThrowIfCancellationRequested();
            items.Add(await RunPlanItemAsync(
                tenantId,
                request,
                plan,
                manifest,
                connection,
                scopedStore,
                completedDryRunsByEndpoint,
                planItem,
                ct).ConfigureAwait(false));
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
        ProviderIntegrationManifestDto manifest,
        ProviderConnectionDto connection,
        IProviderIntegrationManifestStore scopedStore,
        IDictionary<string, ProviderIntegrationDryRunResultDto> completedDryRunsByEndpoint,
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

        var endpoint = manifest.Endpoints.FirstOrDefault(candidate =>
            StringComparer.OrdinalIgnoreCase.Equals(candidate.EndpointKey, planItem.EndpointKey) &&
            candidate.Capability == planItem.Capability);
        if (endpoint is null)
        {
            return Skipped(
                planItem,
                "endpoint-missing",
                [
                    new ValidationIssueDto(
                        "sync-run.endpoint-missing",
                        ProviderIntegrationIssueSeverityDto.Critical,
                        $"Endpoint '{planItem.EndpointKey}' is not configured for {planItem.Capability}.",
                        planItem.EndpointKey,
                        "Map a read-only REST endpoint before running scheduled sync.")
                ]);
        }

        var pathParameters = ResolveParameterBag(request.PathParametersByCapability, planItem.Capability);
        if (endpoint.DependsOn is not null &&
            !pathParameters.ContainsKey(endpoint.DependsOn.ParameterName))
        {
            try
            {
                return await RunDependentPlanItemAsync(
                    tenantId,
                    request,
                    plan,
                    manifest,
                    connection,
                    scopedStore,
                    completedDryRunsByEndpoint,
                    planItem,
                    endpoint,
                    ct).ConfigureAwait(false);
            }
            catch (InvalidOperationException ex)
            {
                return RuntimeBlocked(planItem, ex.Message);
            }
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
                        pathParameters,
                        ResolveParameterBag(request.QueryParametersByCapability, planItem.Capability),
                        request.RequestedBy,
                        request.RequestedAt,
                        request.MaxPages <= 0 ? 1 : request.MaxPages),
                    ct)
                .ConfigureAwait(false);
            completedDryRunsByEndpoint[planItem.EndpointKey] = dryRun;

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
            return RuntimeBlocked(planItem, ex.Message);
        }
    }

    private async Task<ProviderIntegrationRunDueSyncItemResultDto> RunDependentPlanItemAsync(
        string? tenantId,
        ProviderIntegrationRunDueSyncRequestDto request,
        ProviderIntegrationSyncPlanDto plan,
        ProviderIntegrationManifestDto manifest,
        ProviderConnectionDto connection,
        IProviderIntegrationManifestStore scopedStore,
        IDictionary<string, ProviderIntegrationDryRunResultDto> completedDryRunsByEndpoint,
        ProviderIntegrationSyncPlanItemDto planItem,
        EndpointDefinitionDto endpoint,
        CancellationToken ct)
    {
        var dependency = endpoint.DependsOn!;
        var dependencyEndpoint = manifest.Endpoints.FirstOrDefault(candidate =>
            StringComparer.OrdinalIgnoreCase.Equals(candidate.EndpointKey, dependency.EndpointKey));
        if (dependencyEndpoint is null)
        {
            return Skipped(
                planItem,
                "dependency-endpoint-missing",
                [
                    new ValidationIssueDto(
                        "sync-run.dependency-endpoint-missing",
                        ProviderIntegrationIssueSeverityDto.Critical,
                        $"Dependency endpoint '{dependency.EndpointKey}' is not configured.",
                        endpoint.EndpointKey,
                        "Map the dependency endpoint or supply the required path parameter manually.")
                ]);
        }

        if (!connection.EnabledCapabilities.Contains(dependencyEndpoint.Capability))
        {
            return Skipped(
                planItem,
                "dependency-capability-not-enabled",
                [
                    new ValidationIssueDto(
                        "sync-run.dependency-capability-not-enabled",
                        ProviderIntegrationIssueSeverityDto.Critical,
                        $"{planItem.Capability} depends on {dependencyEndpoint.Capability}, but the connection does not enable that capability.",
                        dependencyEndpoint.Capability.ToString(),
                        $"Enable {dependencyEndpoint.Capability} or supply path parameter '{dependency.ParameterName}'.")
                ]);
        }

        if (!completedDryRunsByEndpoint.TryGetValue(dependency.EndpointKey, out var dependencyDryRun))
        {
            var dependencySyncRunId = BuildSyncRunId(
                request.ConnectionId,
                dependencyEndpoint.Capability,
                request.RequestedAt,
                dependency.EndpointKey);
            dependencyDryRun = await restDryRun
                .RunRestDryRunAsync(
                    tenantId,
                    new ProviderIntegrationRestDryRunRequestDto(
                        dependencySyncRunId,
                        plan.ManifestId,
                        request.ConnectionId,
                        dependencyEndpoint.Capability,
                        dependencyEndpoint.EndpointKey,
                        ResolveParameterBag(request.PathParametersByCapability, dependencyEndpoint.Capability),
                        ResolveParameterBag(request.QueryParametersByCapability, dependencyEndpoint.Capability),
                        request.RequestedBy,
                        request.RequestedAt,
                        request.MaxPages <= 0 ? 1 : request.MaxPages),
                    ct)
                .ConfigureAwait(false);
            completedDryRunsByEndpoint[dependency.EndpointKey] = dependencyDryRun;
        }

        var dependencyValues = await ResolveDependencyValuesAsync(
            scopedStore,
            dependencyEndpoint,
            dependencyDryRun,
            dependency.OutputPath,
            ct).ConfigureAwait(false);
        if (dependencyValues.Count == 0)
        {
            return Skipped(
                planItem,
                "dependency-output-missing",
                [
                    new ValidationIssueDto(
                        "sync-run.dependency-output-missing",
                        ProviderIntegrationIssueSeverityDto.Critical,
                        $"Dependency endpoint '{dependency.EndpointKey}' did not produce values at '{dependency.OutputPath}'.",
                        dependency.OutputPath,
                        $"Confirm the dependency output path or supply path parameter '{dependency.ParameterName}'.")
                ]);
        }

        var childResults = new List<ProviderIntegrationDryRunResultDto>(dependencyValues.Count);
        var basePathParameters = ResolveParameterBag(request.PathParametersByCapability, planItem.Capability);
        foreach (var dependencyValue in dependencyValues)
        {
            ct.ThrowIfCancellationRequested();
            var childPathParameters = new Dictionary<string, string>(basePathParameters, StringComparer.OrdinalIgnoreCase)
            {
                [dependency.ParameterName] = dependencyValue
            };
            var childSyncRunId = BuildSyncRunId(
                request.ConnectionId,
                planItem.Capability,
                request.RequestedAt,
                dependencyValue);
            var childResult = await restDryRun
                .RunRestDryRunAsync(
                    tenantId,
                    new ProviderIntegrationRestDryRunRequestDto(
                        childSyncRunId,
                        plan.ManifestId,
                        request.ConnectionId,
                        planItem.Capability,
                        endpoint.EndpointKey,
                        childPathParameters,
                        ResolveParameterBag(request.QueryParametersByCapability, planItem.Capability),
                        request.RequestedBy,
                        request.RequestedAt,
                        request.MaxPages <= 0 ? 1 : request.MaxPages),
                    ct)
                .ConfigureAwait(false);
            childResults.Add(childResult);
        }

        var aggregate = AggregateChildResults(planItem.Capability, childResults);
        completedDryRunsByEndpoint[endpoint.EndpointKey] = aggregate;
        return new ProviderIntegrationRunDueSyncItemResultDto(
            planItem.Capability,
            endpoint.EndpointKey,
            Started: true,
            Skipped: false,
            "started-with-dependency",
            aggregate.SyncRunId,
            aggregate,
            aggregate.Issues);
    }

    private static async Task<IReadOnlyList<string>> ResolveDependencyValuesAsync(
        IProviderIntegrationManifestStore scopedStore,
        EndpointDefinitionDto dependencyEndpoint,
        ProviderIntegrationDryRunResultDto dependencyDryRun,
        string outputPath,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(dependencyDryRun.RawPayloadId))
        {
            return [];
        }

        var payload = await scopedStore
            .GetRawPayloadAsync(dependencyDryRun.SyncRunId, dependencyDryRun.RawPayloadId, ct)
            .ConfigureAwait(false);
        if (payload is null)
        {
            return [];
        }

        return ExtractRecords(payload.RawPayload, dependencyEndpoint.Response.RecordsPath)
            .Select(record => ReadJsonString(record, outputPath))
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static ProviderIntegrationDryRunResultDto AggregateChildResults(
        ProviderCapabilityKindDto capability,
        IReadOnlyList<ProviderIntegrationDryRunResultDto> childResults)
    {
        var issues = childResults.SelectMany(result => result.Issues).ToArray();
        var status = childResults.Any(result => result.Status == ProviderIntegrationProcessingStatusDto.Blocked)
            ? ProviderIntegrationProcessingStatusDto.Blocked
            : childResults.Any(result => result.Status == ProviderIntegrationProcessingStatusDto.Quarantined)
                ? ProviderIntegrationProcessingStatusDto.Quarantined
                : ProviderIntegrationProcessingStatusDto.Validated;
        var first = childResults[0];
        return new ProviderIntegrationDryRunResultDto(
            first.SyncRunId,
            first.RawPayloadId,
            capability,
            childResults.Sum(result => result.RecordsReceived),
            childResults.Sum(result => result.RecordsAccepted),
            childResults.Sum(result => result.RecordsQuarantined),
            status,
            issues);
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

    private static ProviderIntegrationRunDueSyncItemResultDto RuntimeBlocked(
        ProviderIntegrationSyncPlanItemDto planItem,
        string message)
        => Skipped(
            planItem,
            "runtime-blocked",
            [
                new ValidationIssueDto(
                    "sync-run.runtime-blocked",
                    ProviderIntegrationIssueSeverityDto.Critical,
                    message,
                    planItem.EndpointKey,
                    "Review endpoint parameters, integration type, mapping, and activation state before rerunning due sync.")
            ]);

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

    private IProviderIntegrationManifestStore ResolveStore(string? tenantId)
        => string.IsNullOrWhiteSpace(tenantId)
            ? store
            : store is IProviderIntegrationTenantManifestStoreFactory factory
                ? factory.ForTenant(tenantId)
                : store;

    private static string BuildSyncRunId(
        string connectionId,
        ProviderCapabilityKindDto capability,
        DateTimeOffset requestedAt)
        => BuildSyncRunId(connectionId, capability, requestedAt, capability.ToString());

    private static string BuildSyncRunId(
        string connectionId,
        ProviderCapabilityKindDto capability,
        DateTimeOffset requestedAt,
        string discriminator)
    {
        var input = string.Join(
            "|",
            "run-due",
            connectionId,
            capability.ToString(),
            discriminator,
            requestedAt.UtcDateTime.ToString("O", CultureInfo.InvariantCulture));
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(input));
        return $"run-due-{Convert.ToHexString(hash)[..24].ToLowerInvariant()}";
    }

    private static IReadOnlyList<JsonElement> ExtractRecords(JsonElement responseBody, string recordsPath)
    {
        if (string.IsNullOrWhiteSpace(recordsPath) ||
            StringComparer.Ordinal.Equals(recordsPath.Trim(), "$"))
        {
            if (responseBody.ValueKind == JsonValueKind.Array)
            {
                return responseBody.EnumerateArray().Select(record => record.Clone()).ToArray();
            }

            return responseBody.ValueKind == JsonValueKind.Object ? [responseBody.Clone()] : [];
        }

        var current = responseBody;
        foreach (var part in NormalizeSourcePath(recordsPath).Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (current.ValueKind != JsonValueKind.Object ||
                !current.TryGetProperty(part, out current))
            {
                return [];
            }
        }

        if (current.ValueKind == JsonValueKind.Array)
        {
            return current.EnumerateArray().Select(record => record.Clone()).ToArray();
        }

        return current.ValueKind == JsonValueKind.Object ? [current.Clone()] : [];
    }

    private static string? ReadJsonString(JsonElement root, string sourcePath)
    {
        var current = root;
        foreach (var part in NormalizeSourcePath(sourcePath).Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (current.ValueKind != JsonValueKind.Object ||
                !current.TryGetProperty(part, out current))
            {
                return null;
            }
        }

        return current.ValueKind switch
        {
            JsonValueKind.String => current.GetString(),
            JsonValueKind.Number => current.GetRawText(),
            JsonValueKind.True => bool.TrueString,
            JsonValueKind.False => bool.FalseString,
            _ => null
        };
    }

    private static string NormalizeSourcePath(string sourcePath)
    {
        var normalized = sourcePath.Trim();
        if (normalized.StartsWith("$.", StringComparison.Ordinal))
        {
            normalized = normalized[2..];
        }

        if (normalized.StartsWith("$['", StringComparison.Ordinal) &&
            normalized.EndsWith("']", StringComparison.Ordinal))
        {
            normalized = normalized[3..^2];
        }

        return normalized;
    }
}
