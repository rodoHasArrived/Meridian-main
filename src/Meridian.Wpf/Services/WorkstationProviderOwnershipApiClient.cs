using Meridian.Contracts.Api;
using Meridian.Core.Config;
using Meridian.DataIntegration.Credentials;
using Meridian.Ui.Services;

namespace Meridian.Wpf.Services;

/// <summary>
/// Provider credential ownership administration over the shared provider-routing commands. The
/// server resolves the tenant and acting operator; the desktop never sends or reads a secret.
/// </summary>
public interface IWorkstationProviderOwnershipApiClient
{
    Task<ApiResponse<ProviderConnectionDto[]>> GetConnectionsAsync(CancellationToken ct = default);
    Task<ApiResponse<ProviderConnectionDto>> CreateOwnedConnectionAsync(CreateOwnedProviderConnectionRequest request, CancellationToken ct = default);
    Task<ApiResponse<ProviderRuntimeCredentialBinding>> BindRuntimeCredentialAsync(string connectionId, BindProviderRuntimeCredentialRequest request, CancellationToken ct = default);
    /// <summary>Whether the retained binding has reached running instances of the host.</summary>
    Task<ApiResponse<ProviderRuntimeCredentialApplicationStatus>> GetRuntimeCredentialStatusAsync(string connectionId, string providerId, CancellationToken ct = default);
    /// <summary>Opt-in, never hot: the default host reports the binding as pending a deliberate restart.</summary>
    Task<ApiResponse<ProviderRuntimeCredentialApplicationStatus>> ApplyRuntimeCredentialAsync(string connectionId, ApplyProviderRuntimeCredentialRequest request, CancellationToken ct = default);
    /// <summary>Non-secret preview; its <c>AuditMetadata["credentialGeneration"]</c> is the token a migrate command must echo.</summary>
    Task<ApiResponse<ProviderCredentialStoreStatus>> PreviewMigrationAsync(string connectionId, string providerId, CancellationToken ct = default);
    Task<ApiResponse<ProviderCredentialMigrationReceipt>> MigrateAsync(string connectionId, ProviderCredentialMigrationRequest request, CancellationToken ct = default);
    Task<ApiResponse<ProviderCredentialMigrationReceipt>> RollbackAsync(string connectionId, ProviderCredentialMigrationRequest request, CancellationToken ct = default);
    /// <summary>Retires a verified migration: rollback ends and the provider-wide record is released; the receipt stays in audit.</summary>
    Task<ApiResponse<ProviderCredentialMigrationReceipt>> FinalizeAsync(string connectionId, ProviderCredentialMigrationRequest request, CancellationToken ct = default);
}

public sealed class WorkstationProviderOwnershipApiClient : IWorkstationProviderOwnershipApiClient
{
    private readonly ApiClientService _apiClient;

    public WorkstationProviderOwnershipApiClient(ApiClientService apiClient)
    {
        _apiClient = apiClient ?? throw new ArgumentNullException(nameof(apiClient));
    }

    public Task<ApiResponse<ProviderConnectionDto[]>> GetConnectionsAsync(CancellationToken ct = default)
        => _apiClient.GetWithResponseAsync<ProviderConnectionDto[]>(UiApiRoutes.ProviderRoutingConnections, ct);

    public Task<ApiResponse<ProviderConnectionDto>> CreateOwnedConnectionAsync(CreateOwnedProviderConnectionRequest request, CancellationToken ct = default)
        => _apiClient.PostWithResponseAsync<ProviderConnectionDto>(UiApiRoutes.ProviderRoutingConnections, request, ct);

    public Task<ApiResponse<ProviderRuntimeCredentialBinding>> BindRuntimeCredentialAsync(string connectionId, BindProviderRuntimeCredentialRequest request, CancellationToken ct = default)
        => _apiClient.PutWithResponseAsync<ProviderRuntimeCredentialBinding>(Route(UiApiRoutes.ProviderRoutingConnectionRuntimeCredentials, connectionId), request, ct);

    public Task<ApiResponse<ProviderRuntimeCredentialApplicationStatus>> GetRuntimeCredentialStatusAsync(string connectionId, string providerId, CancellationToken ct = default)
        => _apiClient.GetWithResponseAsync<ProviderRuntimeCredentialApplicationStatus>(
            Route(UiApiRoutes.ProviderRoutingConnectionRuntimeCredentialStatus, connectionId) + "?providerId=" + Uri.EscapeDataString(providerId), ct);

    public Task<ApiResponse<ProviderRuntimeCredentialApplicationStatus>> ApplyRuntimeCredentialAsync(string connectionId, ApplyProviderRuntimeCredentialRequest request, CancellationToken ct = default)
        => _apiClient.PostWithResponseAsync<ProviderRuntimeCredentialApplicationStatus>(Route(UiApiRoutes.ProviderRoutingConnectionRuntimeCredentialApply, connectionId), request, ct);

    public Task<ApiResponse<ProviderCredentialStoreStatus>> PreviewMigrationAsync(string connectionId, string providerId, CancellationToken ct = default)
        => _apiClient.GetWithResponseAsync<ProviderCredentialStoreStatus>(
            Route(UiApiRoutes.ProviderRoutingConnectionCredentialMigrationPreview, connectionId) + "?providerId=" + Uri.EscapeDataString(providerId), ct);

    public Task<ApiResponse<ProviderCredentialMigrationReceipt>> MigrateAsync(string connectionId, ProviderCredentialMigrationRequest request, CancellationToken ct = default)
        => _apiClient.PostWithResponseAsync<ProviderCredentialMigrationReceipt>(Route(UiApiRoutes.ProviderRoutingConnectionCredentialMigrate, connectionId), request, ct);

    public Task<ApiResponse<ProviderCredentialMigrationReceipt>> RollbackAsync(string connectionId, ProviderCredentialMigrationRequest request, CancellationToken ct = default)
        => _apiClient.PostWithResponseAsync<ProviderCredentialMigrationReceipt>(Route(UiApiRoutes.ProviderRoutingConnectionCredentialRollback, connectionId), request, ct);

    public Task<ApiResponse<ProviderCredentialMigrationReceipt>> FinalizeAsync(string connectionId, ProviderCredentialMigrationRequest request, CancellationToken ct = default)
        => _apiClient.PostWithResponseAsync<ProviderCredentialMigrationReceipt>(Route(UiApiRoutes.ProviderRoutingConnectionCredentialFinalize, connectionId), request, ct);

    private static string Route(string template, string connectionId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionId);
        return template.Replace("{connectionId}", Uri.EscapeDataString(connectionId.Trim()), StringComparison.Ordinal);
    }
}
