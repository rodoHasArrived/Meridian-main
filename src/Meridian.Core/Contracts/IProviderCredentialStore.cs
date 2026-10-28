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

    /// <summary>
    /// Reads one selected record, preserving an empty retained record; null proves absence.
    /// Compatibility stores that cannot prove absence conservatively select their returned
    /// record, so missing fields cannot borrow ambient credentials.
    /// </summary>
    async Task<IReadOnlyDictionary<string, string>?> ReadCredentialRecordAsync(string moduleId, CancellationToken ct = default) =>
        await GetCredentialsAsync(moduleId, ct).ConfigureAwait(false);

    /// <summary>Returns the set of credential key names that have stored (non-empty) values.</summary>
    Task<IReadOnlySet<string>> GetStoredKeyNamesAsync(string moduleId, CancellationToken ct = default);

    /// <summary>Removes all stored credentials for a provider module.</summary>
    Task DeleteCredentialsAsync(string moduleId, CancellationToken ct = default);
}
