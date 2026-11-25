using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Meridian.Ui.Services;

namespace Meridian.Ui.Services.Contracts;

/// <summary>
/// Interface for credential management services used by shared UI services.
/// Implemented by platform-specific credential services (WPF).
/// </summary>
public interface ICredentialService
{
    /// <summary>
    /// Resource-name prefix under which this host stores OAuth tokens. A token for provider
    /// <c>x</c> is stored as <c>{OAuthTokenResourcePrefix}.x</c>.
    /// </summary>
    string OAuthTokenResourcePrefix { get; }

    event EventHandler<CredentialExpirationEventArgs>? CredentialExpiring;
    IReadOnlyList<CredentialWithMetadata> GetAllCredentialsWithMetadata();
    Task<OAuthRefreshResult> RefreshOAuthTokenAsync(string providerId, CancellationToken ct = default);
    Task UpdateMetadataAsync(string resource, Action<CredentialMetadataUpdate> updateAction, CancellationToken ct = default);
    CredentialMetadataInfo? GetMetadata(string resource);
}
