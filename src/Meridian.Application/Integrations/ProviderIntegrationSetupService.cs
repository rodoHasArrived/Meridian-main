using Meridian.Contracts.Integrations;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Meridian.Application.Integrations;

public sealed class ProviderIntegrationSetupService
{
    private readonly IProviderIntegrationManifestStore store;
    private readonly ILogger<ProviderIntegrationSetupService> logger;

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
    {
        logger.LogDebug(
            "Provider integration operation {Operation} starting for manifest {ManifestId}, connection {ConnectionId}.",
            nameof(SaveDraftAsync),
            request?.Manifest?.ManifestId,
            request?.Connection?.ConnectionId);
        try
        {
            return await SaveDraftCoreAsync(tenantId, request, ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogError(
                ex,
                "Provider integration operation {Operation} failed for manifest {ManifestId}, connection {ConnectionId}.",
                nameof(SaveDraftAsync),
                request?.Manifest?.ManifestId,
                request?.Connection?.ConnectionId);
            throw;
        }
    }

    private async Task<ProviderIntegrationSetupSaveResultDto> SaveDraftCoreAsync(
        string? tenantId,
        ProviderIntegrationSetupSaveRequestDto request,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.Manifest);
        ArgumentNullException.ThrowIfNull(request.Connection);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.SavedBy);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.Manifest.ManifestId);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.Manifest.ProviderId);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.Connection.ConnectionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.Connection.ProviderId);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.Connection.ManifestId);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.Connection.CredentialSecretRef);

        ct.ThrowIfCancellationRequested();

        ValidateSetupScope(request.Manifest, request.Connection);
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
        return new ProviderIntegrationSetupSaveResultDto(
            Saved: true,
            savedManifest.ManifestId,
            savedConnection.ConnectionId,
            savedManifest.State,
            savedConnection.State,
            readiness,
            "Provider integration setup draft saved.");
    }

    private static void ValidateSetupScope(
        ProviderIntegrationManifestDto manifest,
        ProviderConnectionDto connection)
    {
        if (!StringComparer.Ordinal.Equals(connection.ManifestId, manifest.ManifestId))
        {
            throw new InvalidOperationException("Provider connection manifest id must match the manifest being saved.");
        }

        if (!StringComparer.Ordinal.Equals(connection.ProviderId, manifest.ProviderId))
        {
            throw new InvalidOperationException("Provider connection provider id must match the manifest provider id.");
        }

        if (!StringComparer.OrdinalIgnoreCase.Equals(connection.Environment, manifest.Environment))
        {
            throw new InvalidOperationException("Provider connection environment must match the manifest environment.");
        }

        var declaredCapabilities = manifest.Capabilities
            .Select(capability => capability.Capability)
            .ToHashSet();
        foreach (var capability in connection.EnabledCapabilities)
        {
            if (!declaredCapabilities.Contains(capability))
            {
                throw new InvalidOperationException(
                    $"Provider connection enables {capability}, but the manifest does not declare it.");
            }
        }
    }

    private static ProviderIntegrationActivationStateDto NormalizeSetupState(
        ProviderIntegrationActivationStateDto state)
        => state is ProviderIntegrationActivationStateDto.Active or ProviderIntegrationActivationStateDto.Retired
            ? ProviderIntegrationActivationStateDto.Draft
            : state;

    private IProviderIntegrationManifestStore ResolveStore(string? tenantId)
        => string.IsNullOrWhiteSpace(tenantId)
            ? store
            : store is IProviderIntegrationTenantManifestStoreFactory factory
                ? factory.ForTenant(tenantId)
                : store;
}
