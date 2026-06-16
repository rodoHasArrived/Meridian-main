using Meridian.Contracts.Integrations;

namespace Meridian.Application.Integrations;

public sealed class ProviderIntegrationActivationService
{
    private readonly IProviderIntegrationManifestStore store;

    public ProviderIntegrationActivationService(IProviderIntegrationManifestStore store)
    {
        this.store = store ?? throw new ArgumentNullException(nameof(store));
    }

    public async Task<ProviderIntegrationActivationResultDto> ActivateAsync(
        ProviderIntegrationActivationRequestDto request,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.ManifestId);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.ConnectionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.ApprovedBy);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.ApprovalEvidenceId);

        ct.ThrowIfCancellationRequested();

        var manifest = await store.GetManifestAsync(request.ManifestId, ct).ConfigureAwait(false)
            ?? throw new KeyNotFoundException($"Provider integration manifest '{request.ManifestId}' was not found.");
        var connection = await store.GetConnectionAsync(request.ConnectionId, ct).ConfigureAwait(false)
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

        await store.SaveManifestAsync(activationManifest, ct).ConfigureAwait(false);
        await store.SaveConnectionAsync(activationConnection, ct).ConfigureAwait(false);
        return new ProviderIntegrationActivationResultDto(
            Activated: true,
            activationManifest.ManifestId,
            activationConnection.ConnectionId,
            activationManifest.State,
            activationConnection.State,
            readiness,
            activationConnection.ApprovalEvidenceId,
            "Provider integration connection activated.");
    }
}
