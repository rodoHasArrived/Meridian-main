using Meridian.Infrastructure.Contracts;

namespace Meridian.Application.Credentials;

/// <summary>
/// Credential types supported by the store.
/// </summary>
public enum CredentialType : byte
{
    /// <summary>Simple API key.</summary>
    ApiKey,

    /// <summary>Key/secret pair (e.g., Alpaca).</summary>
    KeySecretPair,

    /// <summary>OAuth token with refresh capability.</summary>
    OAuthToken,

    /// <summary>Bearer token.</summary>
    BearerToken,

    /// <summary>Basic auth (username/password).</summary>
    BasicAuth,

    /// <summary>Certificate-based authentication.</summary>
    Certificate
}

/// <summary>
/// Credential metadata for discovery and validation.
/// </summary>
/// <param name="Provider">Provider name (e.g., "alpaca", "polygon").</param>
/// <param name="Key">Credential key name.</param>
/// <param name="CredentialType">Type of credential.</param>
/// <param name="IsRequired">Whether credential is required for provider to function.</param>
/// <param name="Description">Human-readable description.</param>
/// <param name="EnvironmentVariable">Primary environment variable name.</param>
/// <param name="AlternateEnvVars">Alternate environment variable names.</param>
public sealed record CredentialMetadata(
    string Provider,
    string Key,
    CredentialType CredentialType,
    bool IsRequired,
    string Description,
    string? EnvironmentVariable = null,
    IReadOnlyList<string>? AlternateEnvVars = null);

/// <summary>
/// Result of a credential retrieval operation.
/// </summary>
/// <param name="Value">Credential value (null if not found).</param>
/// <param name="Source">Where the credential was found.</param>
/// <param name="IsExpired">Whether the credential is expired (for tokens).</param>
/// <param name="ExpiresAt">When the credential expires (for tokens).</param>
public sealed record CredentialResult(
    string? Value,
    CredentialSource Source,
    bool IsExpired = false,
    DateTimeOffset? ExpiresAt = null)
{
    /// <summary>
    /// Whether a credential was found.
    /// </summary>
    public bool HasValue => !string.IsNullOrEmpty(Value);

    /// <summary>
    /// Whether the credential is valid (found and not expired).
    /// </summary>
    public bool IsValid => HasValue && !IsExpired;

    /// <summary>
    /// Empty result indicating credential not found.
    /// </summary>
    public static readonly CredentialResult NotFound = new(null, CredentialSource.None);
}

/// <summary>
/// Sources where credentials can be loaded from.
/// </summary>
public enum CredentialSource : byte
{
    /// <summary>No credential found.</summary>
    None,

    /// <summary>Loaded from environment variable.</summary>
    EnvironmentVariable,

    /// <summary>Loaded from configuration file.</summary>
    Configuration,

    /// <summary>Loaded from secure store (e.g., Azure Key Vault).</summary>
    SecureStore,

    /// <summary>Loaded from in-memory cache.</summary>
    Cache,

    /// <summary>Refreshed via OAuth flow.</summary>
    OAuthRefresh
}

/// <summary>
/// Centralized credential store providing unified access to API credentials
/// across all providers with caching, refresh, and validation capabilities.
/// </summary>
/// <remarks>
/// The credential store addresses scattered credential management by providing:
/// - Single interface for all credential types (API keys, OAuth, etc.)
/// - Automatic loading from environment variables with fallback
/// - Caching with configurable TTL
/// - Token refresh for OAuth providers
/// - Validation and health checking
/// - Metadata for discovery and documentation
///
/// Replaces scattered patterns:
/// - Direct Environment.GetEnvironmentVariable calls
/// - CredentialProvider inconsistent implementations
/// - OAuthTokenRefreshService OAuth-only handling
/// </remarks>
[ImplementsAdr("ADR-001", "Centralized credential management")]
public interface ICredentialStore
{
    /// <summary>
    /// Gets a credential value for a provider.
    /// </summary>
    /// <param name="provider">Provider name (e.g., "alpaca").</param>
    /// <param name="key">Credential key (e.g., "apiKey", "secretKey").</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Credential result with value and metadata.</returns>
    Task<CredentialResult> GetCredentialAsync(string provider, string key, CancellationToken ct = default);

    /// <summary>
    /// Sets a credential value (for runtime configuration).
    /// </summary>
    /// <param name="provider">Provider name.</param>
    /// <param name="key">Credential key.</param>
    /// <param name="value">Credential value.</param>
    /// <param name="ct">Cancellation token.</param>
    Task SetCredentialAsync(string provider, string key, string value, CancellationToken ct = default);

    /// <summary>
    /// Checks if a credential exists and is valid.
    /// </summary>
    /// <param name="provider">Provider name.</param>
    /// <param name="key">Credential key.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>True if credential exists and is valid.</returns>
    Task<bool> HasValidCredentialAsync(string provider, string key, CancellationToken ct = default);

    /// <summary>
    /// Refreshes a credential (for OAuth tokens).
    /// </summary>
    /// <param name="provider">Provider name.</param>
    /// <param name="key">Credential key.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Refreshed credential result.</returns>
    Task<CredentialResult> RefreshCredentialAsync(string provider, string key, CancellationToken ct = default);

    /// <summary>
    /// Validates all credentials for a provider.
    /// </summary>
    /// <param name="provider">Provider name.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Validation result with details.</returns>
    Task<CredentialValidationResult> ValidateProviderCredentialsAsync(string provider, CancellationToken ct = default);

    /// <summary>
    /// Gets metadata for all registered credentials.
    /// </summary>
    /// <returns>List of credential metadata.</returns>
    IReadOnlyList<CredentialMetadata> GetRegisteredCredentials();

    /// <summary>
    /// Gets metadata for a specific provider's credentials.
    /// </summary>
    /// <param name="provider">Provider name.</param>
    /// <returns>List of credential metadata for the provider.</returns>
    IReadOnlyList<CredentialMetadata> GetProviderCredentials(string provider);

    /// <summary>
    /// Registers credential metadata for a provider.
    /// </summary>
    /// <param name="metadata">Credential metadata to register.</param>
    void RegisterCredential(CredentialMetadata metadata);

    /// <summary>
    /// Clears cached credentials (forces reload on next access).
    /// </summary>
    /// <param name="provider">Optional provider to clear (null = all).</param>
    void ClearCache(string? provider = null);
}

/// <summary>
/// Result of credential validation for a provider.
/// </summary>
/// <param name="Provider">Provider name.</param>
/// <param name="IsValid">Whether all required credentials are valid.</param>
/// <param name="MissingCredentials">List of missing required credentials.</param>
/// <param name="ExpiredCredentials">List of expired credentials.</param>
/// <param name="ValidCredentials">List of valid credentials.</param>
/// <param name="Message">Summary message.</param>
public sealed record CredentialValidationResult(
    string Provider,
    bool IsValid,
    IReadOnlyList<string> MissingCredentials,
    IReadOnlyList<string> ExpiredCredentials,
    IReadOnlyList<string> ValidCredentials,
    string Message);

/// <summary>
/// Extension methods for common credential patterns.
/// </summary>
public static class CredentialStoreExtensions
{
    /// <summary>
    /// Gets an API key for a provider.
    /// </summary>
    public static Task<CredentialResult> GetApiKeyAsync(
        this ICredentialStore store, string provider, CancellationToken ct = default)
        => store.GetCredentialAsync(provider, "apiKey", ct);

    /// <summary>
    /// Gets a key/secret pair for a provider (e.g., Alpaca).
    /// </summary>
    public static async Task<(string? KeyId, string? SecretKey)> GetKeySecretPairAsync(
        this ICredentialStore store, string provider, CancellationToken ct = default)
    {
        var keyResult = await store.GetCredentialAsync(provider, "keyId", ct).ConfigureAwait(false);
        var secretResult = await store.GetCredentialAsync(provider, "secretKey", ct).ConfigureAwait(false);
        return (keyResult.Value, secretResult.Value);
    }

    /// <summary>
    /// Checks if a provider has all required credentials configured.
    /// </summary>
    public static async Task<bool> IsProviderConfiguredAsync(
        this ICredentialStore store, string provider, CancellationToken ct = default)
    {
        var validation = await store.ValidateProviderCredentialsAsync(provider, ct).ConfigureAwait(false);
        return validation.IsValid;
    }

    /// <summary>
    /// Registers standard API key credential for a provider.
    /// </summary>
    public static void RegisterApiKey(
        this ICredentialStore store,
        string provider,
        string envVar,
        bool required = true,
        params string[] alternateEnvVars)
    {
        store.RegisterCredential(new CredentialMetadata(
            Provider: provider,
            Key: "apiKey",
            CredentialType: CredentialType.ApiKey,
            IsRequired: required,
            Description: $"API key for {provider}",
            EnvironmentVariable: envVar,
            AlternateEnvVars: alternateEnvVars));
    }

    /// <summary>
    /// Registers key/secret pair credentials for a provider (e.g., Alpaca).
    /// </summary>
    public static void RegisterKeySecretPair(
        this ICredentialStore store,
        string provider,
        string keyIdEnvVar,
        string secretKeyEnvVar,
        bool required = true)
    {
        store.RegisterCredential(new CredentialMetadata(
            Provider: provider,
            Key: "keyId",
            CredentialType: CredentialType.KeySecretPair,
            IsRequired: required,
            Description: $"Key ID for {provider}",
            EnvironmentVariable: keyIdEnvVar));

        store.RegisterCredential(new CredentialMetadata(
            Provider: provider,
            Key: "secretKey",
            CredentialType: CredentialType.KeySecretPair,
            IsRequired: required,
            Description: $"Secret key for {provider}",
            EnvironmentVariable: secretKeyEnvVar));
    }
}
