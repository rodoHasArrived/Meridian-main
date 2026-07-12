using Meridian.Contracts.Integrations;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Meridian.Application.Integrations;

public sealed class ProviderIntegrationSetupService
{
    private readonly ILogger<ProviderIntegrationSetupService> logger;
    private readonly IProviderIntegrationManifestStore store;

    public ProviderIntegrationSetupService(
        IProviderIntegrationManifestStore store,
        ILogger<ProviderIntegrationSetupService>? logger = null)
    {
        this.store = store ?? throw new ArgumentNullException(nameof(store));
        this.logger = logger ?? NullLogger<ProviderIntegrationSetupService>.Instance;
    }

    public async Task<ProviderIntegrationSetupSaveResultDto> SaveDraftAsync(
        ProviderIntegrationSetupSaveRequestDto request,
        CancellationToken ct = default)
        => await SaveDraftAsync(null, request, ct).ConfigureAwait(false);

    public async Task<ProviderIntegrationSetupSaveResultDto> SaveDraftAsync(
        string? tenantId,
        ProviderIntegrationSetupSaveRequestDto request,
        CancellationToken ct = default)
        => await ProviderIntegrationServiceBoundary.RunAsync(
            logger,
            "setup-save-draft",
            new ProviderIntegrationBoundaryContext(
                TenantId: tenantId,
                ManifestId: request?.Manifest?.ManifestId,
                ConnectionId: request?.Connection?.ConnectionId),
            () => SaveDraftCoreAsync(tenantId, request, ct)).ConfigureAwait(false);

    private async Task<ProviderIntegrationSetupSaveResultDto> SaveDraftCoreAsync(
        string? tenantId,
        ProviderIntegrationSetupSaveRequestDto request,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        ct.ThrowIfCancellationRequested();

        var validationIssues = ProviderIntegrationSetupValidator.Validate(request);
        if (validationIssues.Count > 0)
        {
            throw new ProviderIntegrationSetupValidationException(validationIssues);
        }

        var manifestStateNormalized = IsNormalizedSetupState(request.Manifest.State);
        var connectionStateNormalized = IsNormalizedSetupState(request.Connection.State);
        var savedManifest = request.Manifest with
        {
            State = NormalizeSetupState(request.Manifest.State),
            ChangeReason = request.ChangeReason ?? request.Manifest.ChangeReason
        };
        var savedConnection = request.Connection with
        {
            State = NormalizeSetupState(request.Connection.State),
            UpdatedAt = request.SavedAt
        };

        var scopedStore = ResolveStore(tenantId);
        await scopedStore.SaveManifestAsync(savedManifest, ct).ConfigureAwait(false);
        await scopedStore.SaveConnectionAsync(savedConnection, ct).ConfigureAwait(false);

        var readiness = ProviderIntegrationActivationReadinessService.Evaluate(
            savedManifest,
            savedConnection);
        logger.LogInformation(
            "Provider integration setup draft saved for TenantId {TenantId}, ManifestId {ManifestId}, ConnectionId {ConnectionId}: ManifestState {ManifestState}, ConnectionState {ConnectionState}, ActivationReady {ActivationReady}, ReadinessIssueCount {ReadinessIssueCount}.",
            string.IsNullOrWhiteSpace(tenantId) ? "(default)" : tenantId,
            savedManifest.ManifestId,
            savedConnection.ConnectionId,
            savedManifest.State,
            savedConnection.State,
            readiness.IsReady,
            readiness.Issues.Count);
        return new ProviderIntegrationSetupSaveResultDto(
            Saved: true,
            savedManifest.ManifestId,
            savedConnection.ConnectionId,
            savedManifest.State,
            savedConnection.State,
            readiness,
            BuildSaveMessage(request, manifestStateNormalized, connectionStateNormalized));
    }

    private static string BuildSaveMessage(
        ProviderIntegrationSetupSaveRequestDto request,
        bool manifestStateNormalized,
        bool connectionStateNormalized)
    {
        if (!manifestStateNormalized && !connectionStateNormalized)
        {
            return "Provider integration setup draft saved.";
        }

        var normalized = (manifestStateNormalized, connectionStateNormalized) switch
        {
            (true, true) =>
                $"Manifest state {request.Manifest.State} and connection state {request.Connection.State} were reset to Draft",
            (true, false) => $"Manifest state {request.Manifest.State} was reset to Draft",
            _ => $"Connection state {request.Connection.State} was reset to Draft"
        };
        return $"Provider integration setup draft saved. {normalized}; setup drafts re-enter the activation workflow from Draft.";
    }

    private static bool IsNormalizedSetupState(ProviderIntegrationActivationStateDto state)
        => state is ProviderIntegrationActivationStateDto.Active or ProviderIntegrationActivationStateDto.Retired;

    private static ProviderIntegrationActivationStateDto NormalizeSetupState(
        ProviderIntegrationActivationStateDto state)
        => IsNormalizedSetupState(state)
            ? ProviderIntegrationActivationStateDto.Draft
            : state;

    private IProviderIntegrationManifestStore ResolveStore(string? tenantId)
        => string.IsNullOrWhiteSpace(tenantId)
            ? store
            : store is IProviderIntegrationTenantManifestStoreFactory factory
                ? factory.ForTenant(tenantId)
                : store;
}
