using Meridian.Contracts.Integrations;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Meridian.Application.Integrations;

public sealed class ProviderIntegrationSyncPlanningService
{
    private static readonly ProviderIntegrationProcessingStatusDto[] SuccessfulSyncStatuses =
    [
        ProviderIntegrationProcessingStatusDto.Validated,
        ProviderIntegrationProcessingStatusDto.Loaded,
        ProviderIntegrationProcessingStatusDto.Published
    ];

    private readonly ILogger<ProviderIntegrationSyncPlanningService> logger;
    private readonly IProviderIntegrationManifestStore store;

    public ProviderIntegrationSyncPlanningService(
        IProviderIntegrationManifestStore store,
        ILogger<ProviderIntegrationSyncPlanningService>? logger = null)
    {
        this.store = store ?? throw new ArgumentNullException(nameof(store));
        this.logger = logger ?? NullLogger<ProviderIntegrationSyncPlanningService>.Instance;
    }

    public async Task<ProviderIntegrationSyncPlanDto> PlanAsync(
        ProviderIntegrationSyncPlanRequestDto request,
        CancellationToken ct = default)
        => await PlanAsync(null, request, ct).ConfigureAwait(false);

    public async Task<ProviderIntegrationSyncPlanDto> PlanAsync(
        string? tenantId,
        ProviderIntegrationSyncPlanRequestDto request,
        CancellationToken ct = default)
        => await ProviderIntegrationServiceBoundary.RunAsync(
            logger,
            "sync-plan",
            new ProviderIntegrationBoundaryContext(
                TenantId: tenantId,
                ConnectionId: request?.ConnectionId),
            async () =>
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.ConnectionId);
        ct.ThrowIfCancellationRequested();

        var scopedStore = ResolveStore(tenantId);
        var connection = await scopedStore.GetConnectionAsync(request.ConnectionId, ct).ConfigureAwait(false)
            ?? throw new KeyNotFoundException($"Provider integration connection '{request.ConnectionId}' was not found.");
        var manifest = await scopedStore.GetManifestAsync(connection.ManifestId, ct).ConfigureAwait(false)
            ?? throw new KeyNotFoundException($"Provider integration manifest '{connection.ManifestId}' was not found.");
        var syncRuns = await scopedStore.ListSyncRunsAsync(connection.ConnectionId, ct).ConfigureAwait(false);

        var items = new List<ProviderIntegrationSyncPlanItemDto>(connection.EnabledCapabilities.Count);
        foreach (var capability in connection.EnabledCapabilities)
        {
            ct.ThrowIfCancellationRequested();
            items.Add(CreatePlanItem(manifest, connection, syncRuns, capability, request.EvaluatedAt));
        }

        return new ProviderIntegrationSyncPlanDto(
            connection.ConnectionId,
            manifest.ManifestId,
            manifest.ProviderId,
            connection.ConnectionName,
            connection.State,
            request.EvaluatedAt,
            items,
            DueCount: items.Count(item => item.IsDue),
            BlockedCount: items.Count(item => item.IsBlocked));
    }).ConfigureAwait(false);

    private IProviderIntegrationManifestStore ResolveStore(string? tenantId)
        => string.IsNullOrWhiteSpace(tenantId)
            ? store
            : store is IProviderIntegrationTenantManifestStoreFactory factory
                ? factory.ForTenant(tenantId)
                : store;

    private static ProviderIntegrationSyncPlanItemDto CreatePlanItem(
        ProviderIntegrationManifestDto manifest,
        ProviderConnectionDto connection,
        IReadOnlyList<ProviderIntegrationSyncRunDto> syncRuns,
        ProviderCapabilityKindDto capability,
        DateTimeOffset evaluatedAt)
    {
        var issues = new List<ValidationIssueDto>();
        var endpoint = manifest.Endpoints.FirstOrDefault(candidate => candidate.Capability == capability);
        var manifestCapability = manifest.Capabilities.FirstOrDefault(candidate => candidate.Capability == capability);
        var schedule = manifest.Sync;
        var lastSuccessfulSyncAt = syncRuns
            .Where(syncRun => syncRun.Capability == capability &&
                syncRun.CompletedAt.HasValue &&
                SuccessfulSyncStatuses.Contains(syncRun.Status))
            .OrderByDescending(syncRun => syncRun.CompletedAt)
            .Select(syncRun => syncRun.CompletedAt)
            .FirstOrDefault();

        if (manifestCapability is null || !manifestCapability.Enabled)
        {
            issues.Add(Critical(
                "sync-plan.capability-not-enabled",
                capability,
                $"The connection enables {capability}, but the manifest does not enable that capability.",
                "Disable the connection capability or update the manifest before scheduled sync."));
            return BuildItem(capability, endpoint, schedule, lastSuccessfulSyncAt, null, isDue: false, isBlocked: true, "capability-not-enabled", issues);
        }

        if (manifestCapability.RequiresCertifiedAdapter)
        {
            issues.Add(Critical(
                "sync-plan.certified-adapter-required",
                capability,
                $"{capability} requires a certified adapter and cannot be scheduled by the generic no-code runtime.",
                "Use a certified provider module with sandbox and approval evidence."));
            return BuildItem(capability, endpoint, schedule, lastSuccessfulSyncAt, null, isDue: false, isBlocked: true, "certified-adapter-required", issues);
        }

        if (connection.State != ProviderIntegrationActivationStateDto.Active ||
            manifest.State != ProviderIntegrationActivationStateDto.Active)
        {
            issues.Add(Critical(
                "sync-plan.activation-required",
                capability,
                "Scheduled sync requires both the provider manifest and connection to be active.",
                "Complete dry run, approval, and activation before enabling scheduled sync."));
            return BuildItem(capability, endpoint, schedule, lastSuccessfulSyncAt, null, isDue: false, isBlocked: true, "activation-required", issues);
        }

        if (IsApiIntegration(manifest.IntegrationType) && endpoint is null)
        {
            issues.Add(Critical(
                "sync-plan.endpoint-missing",
                capability,
                $"No endpoint is configured for {capability}.",
                "Map an endpoint for this capability before scheduled sync."));
            return BuildItem(capability, endpoint, schedule, lastSuccessfulSyncAt, null, isDue: false, isBlocked: true, "endpoint-missing", issues);
        }

        if (IsManualSchedule(schedule))
        {
            return BuildItem(capability, endpoint, schedule, lastSuccessfulSyncAt, null, isDue: false, isBlocked: false, "manual-only", issues);
        }

        if (!TryGetInterval(schedule.Frequency, out var interval))
        {
            issues.Add(new ValidationIssueDto(
                "sync-plan.frequency-unsupported",
                ProviderIntegrationIssueSeverityDto.Warning,
                $"Sync frequency '{schedule.Frequency}' cannot be planned by the first scheduler slice.",
                TargetField: null,
                SuggestedFix: "Use hourly, daily, weekly, or manual until custom schedule support is implemented."));
            return BuildItem(capability, endpoint, schedule, lastSuccessfulSyncAt, null, isDue: false, isBlocked: true, "unsupported-frequency", issues);
        }

        var nextEligibleSyncAt = lastSuccessfulSyncAt.HasValue
            ? lastSuccessfulSyncAt.Value.Add(interval)
            : evaluatedAt;
        var isDue = evaluatedAt >= nextEligibleSyncAt;
        return BuildItem(
            capability,
            endpoint,
            schedule,
            lastSuccessfulSyncAt,
            nextEligibleSyncAt,
            isDue,
            isBlocked: false,
            isDue ? "due" : "not-due",
            issues);
    }

    private static ProviderIntegrationSyncPlanItemDto BuildItem(
        ProviderCapabilityKindDto capability,
        EndpointDefinitionDto? endpoint,
        SyncScheduleDto schedule,
        DateTimeOffset? lastSuccessfulSyncAt,
        DateTimeOffset? nextEligibleSyncAt,
        bool isDue,
        bool isBlocked,
        string reason,
        IReadOnlyList<ValidationIssueDto> issues)
        => new(
            capability,
            endpoint?.EndpointKey,
            schedule.Mode,
            schedule.Frequency,
            schedule.Timezone,
            lastSuccessfulSyncAt,
            nextEligibleSyncAt,
            isDue,
            isBlocked,
            reason,
            issues);

    private static ValidationIssueDto Critical(
        string code,
        ProviderCapabilityKindDto capability,
        string message,
        string suggestedFix)
        => new(
            code,
            ProviderIntegrationIssueSeverityDto.Critical,
            message,
            capability.ToString(),
            suggestedFix);

    private static bool IsApiIntegration(IntegrationTypeDto integrationType)
        => integrationType is IntegrationTypeDto.Rest or IntegrationTypeDto.OpenApiRest or IntegrationTypeDto.Hybrid;

    private static bool IsManualSchedule(SyncScheduleDto schedule)
        => StringComparer.OrdinalIgnoreCase.Equals(schedule.Mode, "manual") ||
           StringComparer.OrdinalIgnoreCase.Equals(schedule.Frequency, "manual");

    private static bool TryGetInterval(string frequency, out TimeSpan interval)
    {
        if (StringComparer.OrdinalIgnoreCase.Equals(frequency, "hourly"))
        {
            interval = TimeSpan.FromHours(1);
            return true;
        }

        if (StringComparer.OrdinalIgnoreCase.Equals(frequency, "daily"))
        {
            interval = TimeSpan.FromDays(1);
            return true;
        }

        if (StringComparer.OrdinalIgnoreCase.Equals(frequency, "weekly"))
        {
            interval = TimeSpan.FromDays(7);
            return true;
        }

        interval = default;
        return false;
    }
}
