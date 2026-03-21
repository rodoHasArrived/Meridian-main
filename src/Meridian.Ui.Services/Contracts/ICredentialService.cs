using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Meridian.Ui.Services;

namespace Meridian.Ui.Services.Contracts;

/// <summary>
/// Interface for credential management services used by shared UI services.
/// Implemented by platform-specific credential services (WPF).
/// </summary>
public interface ICredentialService
{
    event EventHandler<CredentialExpirationEventArgs>? CredentialExpiring;
    IReadOnlyList<CredentialWithMetadata> GetAllCredentialsWithMetadata();
    Task<OAuthRefreshResult> RefreshOAuthTokenAsync(string providerId);
    Task UpdateMetadataAsync(string resource, Action<CredentialMetadataUpdate> updateAction);
    CredentialMetadataInfo? GetMetadata(string resource);
}
