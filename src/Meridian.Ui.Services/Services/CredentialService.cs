using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Meridian.Ui.Services;

/// <summary>
/// Default credential service for the shared UI services layer.
/// Platform-specific projects (WPF) override this with their own implementations
/// by setting the Instance property during app startup.
/// </summary>
public class CredentialService
{
    private static readonly Lazy<CredentialService> _instance = new(() => new CredentialService());

    /// <summary>
    /// Resource key prefix for OAuth tokens.
    /// </summary>
    public const string OAuthTokenResource = "oauth_token";

    public static CredentialService Instance => _instance.Value;

    public event EventHandler<CredentialExpirationEventArgs>? CredentialExpiring;

    public IReadOnlyList<CredentialWithMetadata> GetAllCredentialsWithMetadata()
        => Array.Empty<CredentialWithMetadata>();

    public Task<OAuthRefreshResult> RefreshOAuthTokenAsync(string providerId)
        => Task.FromResult(new OAuthRefreshResult { Success = false, ErrorMessage = "Not implemented" });

    public Task UpdateMetadataAsync(string resource, Action<CredentialMetadataUpdate> updateAction)
        => Task.CompletedTask;

    public CredentialMetadataInfo? GetMetadata(string resource)
        => null;

    protected void OnCredentialExpiring(CredentialExpirationEventArgs e)
        => CredentialExpiring?.Invoke(this, e);
}

/// <summary>
/// Event args for credential expiration notifications.
/// </summary>
public sealed class CredentialExpirationEventArgs : EventArgs
{
    public string Resource { get; }
    public DateTime ExpiresAt { get; }

    public CredentialExpirationEventArgs(string resource, DateTime expiresAt)
    {
        Resource = resource;
        ExpiresAt = expiresAt;
    }
}

/// <summary>
/// Credential with associated metadata.
/// </summary>
public sealed class CredentialWithMetadata
{
    public string Resource { get; set; } = string.Empty;
    public bool IsOAuthToken { get; set; }
    public DateTime? ExpiresAt { get; set; }
    public bool CanAutoRefresh { get; set; }
    public bool AutoRefreshEnabled { get; set; }
}

/// <summary>
/// Result of an OAuth token refresh operation.
/// </summary>
public sealed class OAuthRefreshResult
{
    public bool Success { get; set; }
    public string? ErrorMessage { get; set; }
    public DateTime? NewExpiration { get; set; }
}

/// <summary>
/// Mutable metadata for credential updates.
/// </summary>
public sealed class CredentialMetadataUpdate
{
    public bool AutoRefreshEnabled { get; set; }
}

/// <summary>
/// Read-only metadata about a credential.
/// </summary>
public sealed class CredentialMetadataInfo
{
    public bool AutoRefreshEnabled { get; set; }
    public DateTime? ExpiresAt { get; set; }
}
