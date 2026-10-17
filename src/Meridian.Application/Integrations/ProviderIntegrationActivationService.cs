using Meridian.Contracts.Integrations;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Meridian.Application.Integrations;

public sealed class ProviderIntegrationActivationService
{
    private readonly IProviderIntegrationManifestStore store;
    private readonly ILogger<ProviderIntegrationActivationService> logger;

    public ProviderIntegrationActivationService(
        IProviderIntegrationManifestStore store,
        ILogger<ProviderIntegrationActivationService>? logger = null)
    {
        this.store = store ?? throw new ArgumentNullException(nameof(store));
        this.logger = logger ?? NullLogger<ProviderIntegrationActivationService>.Instance;
    }

    public async Task<ProviderIntegrationActivationResultDto> ActivateAsync(
        ProviderIntegrationActivationRequestDto request,
        CancellationToken ct = default)
        => await ActivateAsync(null, request, ct).ConfigureAwait(false);

    public async Task<ProviderIntegrationActivationResultDto> ActivateAsync(
        string? tenantId,
        ProviderIntegrationActivationRequestDto request,
        CancellationToken ct = default)
        => await ProviderIntegrationServiceBoundary.RunAsync(
            logger,
            "activation-activate",
            new ProviderIntegrationBoundaryContext(
                TenantId: tenantId,
                ManifestId: request?.ManifestId,
                ConnectionId: request?.ConnectionId),
            () => ActivateCoreAsync(tenantId, request, ct)).ConfigureAwait(false);

    private async Task<ProviderIntegrationActivationResultDto> ActivateCoreAsync(
        string? tenantId,
        ProviderIntegrationActivationRequestDto request,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.ManifestId);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.ConnectionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.ApprovedBy);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.ApprovalEvidenceId);

        ct.ThrowIfCancellationRequested();

        var scopedStore = ResolveStore(tenantId);
        if (request.ExpectedManifestReference is null)
        {
            throw new InvalidOperationException("Activation requires the manifest reference that was reviewed. Reload the current version before approving.");
        }

        var manifest = await scopedStore.GetManifestAsync(request.ManifestId, ct).ConfigureAwait(false)
            ?? throw new KeyNotFoundException($"Provider integration manifest '{request.ManifestId}' was not found.");
        var connection = await scopedStore.GetConnectionAsync(request.ConnectionId, ct).ConfigureAwait(false)
            ?? throw new KeyNotFoundException($"Provider integration connection '{request.ConnectionId}' was not found.");
        ProviderIntegrationManifestPromotion.ValidateExpected(manifest, request.ExpectedManifestReference);

        var activationManifest = manifest with
        {
            ManifestVersion = checked(manifest.ManifestVersion + 1),
            State = ProviderIntegrationActivationStateDto.Active,
            ApprovedBy = request.ApprovedBy,
            ApprovedAt = request.ApprovedAt,
            ChangeReason = request.ChangeReason ?? manifest.ChangeReason
        };
        var activationConnection = connection with
        {
            State = ProviderIntegrationActivationStateDto.Active,
            UpdatedAt = request.ApprovedAt,
            ApprovalEvidenceId = request.ApprovalEvidenceId
        };
        var readiness = ProviderIntegrationActivationReadinessService.Evaluate(
            activationManifest,
            activationConnection);

        if (!readiness.IsReady)
        {
            return new ProviderIntegrationActivationResultDto(
                Activated: false,
                manifest.ManifestId,
                connection.ConnectionId,
                manifest.State,
                connection.State,
                readiness,
                connection.ApprovalEvidenceId,
                "Provider integration activation is blocked by readiness issues.");
        }

        activationManifest = await ProviderIntegrationManifestPromotion.SelectAvailableVersionAsync(scopedStore, activationManifest, ct).ConfigureAwait(false);
        await ProviderIntegrationManifestPromotion.SaveAsync(
            scopedStore, activationManifest, ProviderIntegrationManifestIdentity.Create(manifest), ct).ConfigureAwait(false);
        await scopedStore.SaveConnectionAsync(activationConnection, ct).ConfigureAwait(false);
        return new ProviderIntegrationActivationResultDto(
            Activated: true,
            activationManifest.ManifestId,
            activationConnection.ConnectionId,
            activationManifest.State,
            activationConnection.State,
            readiness,
            activationConnection.ApprovalEvidenceId,
            "Provider integration connection activated.")
        {
            ManifestReference = ProviderIntegrationManifestIdentity.Create(activationManifest)
        };
    }

    private IProviderIntegrationManifestStore ResolveStore(string? tenantId)
        => string.IsNullOrWhiteSpace(tenantId)
            ? store
            : store is IProviderIntegrationTenantManifestStoreFactory factory
                ? factory.ForTenant(tenantId)
                : store;
}
