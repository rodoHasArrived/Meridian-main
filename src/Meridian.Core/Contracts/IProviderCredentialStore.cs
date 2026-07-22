namespace Meridian.Core.Contracts;

/// <summary>
/// Compatibility contract for provider-module setup. Production implementations must delegate to
/// the encrypted, audited credential vault; no implementation may persist plaintext sidecars.
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
