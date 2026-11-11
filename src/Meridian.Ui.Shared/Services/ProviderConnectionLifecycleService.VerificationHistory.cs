using Meridian.Contracts.Configuration;
using Meridian.DataIntegration.Credentials;

namespace Meridian.Ui.Shared.Services;

public sealed partial class ProviderConnectionLifecycleService
{
    public Task<ProviderVerificationHistoryDto> GetVerificationHistoryForTenantAsync(
        string connectionId, string tenantId, int limit = 100, CancellationToken ct = default)
    {
        var connection = RequireOwnedConnection(connectionId, tenantId);
        var descriptor = RequireDescriptor(connection.ProviderFamilyId);
        var scope = new ProviderCredentialScope(connection.TenantId!, connection.ConnectionId,
            connection.ExternalAccountId!, connection.CredentialEnvironment!);
        if (_rootCredentialStore is not IProviderVerificationHistoryReader history)
            throw new NotSupportedException("Retained provider verification history is not configured.");
        return history.GetVerificationHistoryAsync(descriptor.ProviderId, scope, limit, ct);
    }
}
