namespace Meridian.Core.Contracts;

/// <summary>
/// Stores and retrieves provider credentials outside of appsettings.json.
/// Credentials are persisted as plaintext in a restricted-access sidecar file
/// ({DataRoot}/provider-credentials.json) and are never returned to browser clients.
/// </summary>
public interface IProviderCredentialStore
{
    /// <summary>Persists credential key/value pairs for a provider module.</summary>
    Task SaveCredentialsAsync(string moduleId, IReadOnlyDictionary<string, string> values, CancellationToken ct = default);

    /// <summary>Returns all credential key/value pairs stored for a provider module.</summary>
    Task<IReadOnlyDictionary<string, string>> GetCredentialsAsync(string moduleId, CancellationToken ct = default);

    /// <summary>Returns the set of credential key names that have stored (non-empty) values.</summary>
    Task<IReadOnlySet<string>> GetStoredKeyNamesAsync(string moduleId, CancellationToken ct = default);

    /// <summary>Removes all stored credentials for a provider module.</summary>
    Task DeleteCredentialsAsync(string moduleId, CancellationToken ct = default);
}
