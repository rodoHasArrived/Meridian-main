using Meridian.Contracts.Integrations;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Meridian.Application.Integrations;

public sealed class ProviderIntegrationActivationService
{
    private readonly ILogger<ProviderIntegrationActivationService> logger;
    private readonly IProviderIntegrationManifestStore store;

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
            async () =>
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.ManifestId);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.ConnectionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.ApprovedBy);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.ApprovalEvidenceId);

        ct.ThrowIfCancellationRequested();

        var scopedStore = ResolveStore(tenantId);
        var manifest = await scopedStore.GetManifestAsync(request.ManifestId, ct).ConfigureAwait(false)
            ?? throw new KeyNotFoundException($"Provider integration manifest '{request.ManifestId}' was not found.");
        var connection = await scopedStore.GetConnectionAsync(request.ConnectionId, ct).ConfigureAwait(false)
            ?? throw new KeyNotFoundException($"Provider integration connection '{request.ConnectionId}' was not found.");

        var activationManifest = manifest with
        {
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

        await scopedStore.SaveManifestAsync(activationManifest, ct).ConfigureAwait(false);
        await scopedStore.SaveConnectionAsync(activationConnection, ct).ConfigureAwait(false);
        return new ProviderIntegrationActivationResultDto(
            Activated: true,
            activationManifest.ManifestId,
            activationConnection.ConnectionId,
            activationManifest.State,
            activationConnection.State,
            readiness,
            activationConnection.ApprovalEvidenceId,
            "Provider integration connection activated.");
    }).ConfigureAwait(false);

    private IProviderIntegrationManifestStore ResolveStore(string? tenantId)
        => string.IsNullOrWhiteSpace(tenantId)
            ? store
            : store is IProviderIntegrationTenantManifestStoreFactory factory
                ? factory.ForTenant(tenantId)
                : store;
}
