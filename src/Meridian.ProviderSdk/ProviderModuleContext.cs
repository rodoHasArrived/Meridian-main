namespace Meridian.Infrastructure.Adapters.Core;

/// <summary>
/// Resolved runtime context passed to an <see cref="IProviderModule"/> before registration.
/// Carries already-resolved credential values and operational settings so modules remain
/// decoupled from any specific config schema or credential resolution strategy.
/// </summary>
/// <remarks>
/// Values in <see cref="Credentials"/> are resolved values (e.g. the actual API key string),
/// not variable names. Resolution from environment variables, vaults, or config files happens
/// in the Application layer before this context is built.
///
/// Conventional credential keys: "apiKey", "keyId", "secretKey", "username", "password".
/// Providers may define additional keys specific to their authentication model.
/// </remarks>
public sealed record ProviderModuleContext
{
    /// <summary>
    /// Whether the provider is enabled. Disabled modules are skipped during registration.
    /// </summary>
    public bool Enabled { get; init; } = true;

    /// <summary>
    /// Routing priority. Lower values are preferred. Defaults to 100.
    /// </summary>
    public int Priority { get; init; } = 100;

    /// <summary>
    /// Resolved credential values keyed by conventional name.
    /// Keys are lower-camel-case: "apiKey", "keyId", "secretKey", "username", "password".
    /// </summary>
    public IReadOnlyDictionary<string, string?> Credentials { get; init; }
        = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Optional provider-specific settings (connection strings, timeouts, feature flags, etc.).
    /// Keys are lower-camel-case strings; values are JSON-compatible primitives.
    /// </summary>
    public IReadOnlyDictionary<string, string?> Settings { get; init; }
        = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Retrieves a resolved credential value by conventional key name.
    /// Returns null when the key is absent or the value is null/empty.
    /// </summary>
    public string? GetCredential(string key)
    {
        if (Credentials.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value))
            return value;
        return null;
    }

    /// <summary>
    /// Retrieves a string setting by key.
    /// Returns null when the key is absent or the value is null/empty.
    /// </summary>
    public string? GetSetting(string key)
    {
        if (Settings.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value))
            return value;
        return null;
    }

    /// <summary>
    /// Creates a context with a flat set of already-resolved credentials.
    /// Convenience factory for tests and manual wiring.
    /// </summary>
    public static ProviderModuleContext FromCredentials(
        IReadOnlyDictionary<string, string?> credentials,
        bool enabled = true,
        int priority = 100)
        => new() { Enabled = enabled, Priority = priority, Credentials = credentials };
}
