namespace Meridian.Contracts.Credentials;

/// <summary>
/// Abstraction for retrieving secrets from external stores such as
/// AWS Secrets Manager, Azure Key Vault, HashiCorp Vault, or environment variables.
/// Implementations are plugged in via DI to replace the environment-variable bridge
/// in <c>CredentialConfig</c>.
/// </summary>
public interface ISecretProvider
{
    /// <summary>
    /// Retrieves a secret value for the given path and key.
    /// </summary>
    /// <param name="path">Vault path or secret group (e.g., "marketdata/alpaca/prod").</param>
    /// <param name="key">Individual key within the secret (e.g., "keyId", "secretKey").</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The secret value, or null if not found.</returns>
    Task<string?> GetSecretAsync(string path, string key, CancellationToken ct = default);

    /// <summary>
    /// Checks whether this secret provider is configured and reachable.
    /// </summary>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>True if secrets can be retrieved from this provider.</returns>
    Task<bool> IsAvailableAsync(CancellationToken ct = default);

    /// <summary>
    /// Gets the display name of this provider (e.g., "AWS Secrets Manager", "Environment Variables").
    /// </summary>
    string Name { get; }
}

/// <summary>
/// Default <see cref="ISecretProvider"/> that resolves secrets from environment variables.
/// This bridges the gap between the vault abstraction and local/test environments where
/// secrets are supplied as MDC_VAULT__{PATH}__{KEY} environment variables.
/// </summary>
public sealed class EnvironmentSecretProvider : ISecretProvider
{
    /// <inheritdoc />
    public string Name => "Environment Variables";

    /// <inheritdoc />
    public Task<string?> GetSecretAsync(string path, string key, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentException.ThrowIfNullOrWhiteSpace(key);

        var normalizedPath = path.Trim()
            .Replace('/', '_')
            .Replace(':', '_')
            .Replace('-', '_')
            .ToUpperInvariant();

        // Single-value mode: MDC_VAULT__{normalizedPath}__{key}
        var envName = $"MDC_VAULT__{normalizedPath}__{key.ToUpperInvariant()}";
        var raw = Environment.GetEnvironmentVariable(envName);
        if (!string.IsNullOrWhiteSpace(raw))
        {
            return Task.FromResult<string?>(raw);
        }

        // JSON payload mode: MDC_VAULT_JSON__{normalizedPath}
        var jsonEnvName = $"MDC_VAULT_JSON__{normalizedPath}";
        var payload = Environment.GetEnvironmentVariable(jsonEnvName);
        if (string.IsNullOrWhiteSpace(payload))
        {
            return Task.FromResult<string?>(null);
        }

        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(payload);
            if (doc.RootElement.ValueKind == System.Text.Json.JsonValueKind.Object &&
                doc.RootElement.TryGetProperty(key, out var property) &&
                property.ValueKind == System.Text.Json.JsonValueKind.String)
            {
                return Task.FromResult(property.GetString());
            }
        }
        catch (System.Text.Json.JsonException)
        {
            // Malformed JSON payload — treat as not found
        }

        return Task.FromResult<string?>(null);
    }

    /// <inheritdoc />
    public Task<bool> IsAvailableAsync(CancellationToken ct = default)
    {
        // Environment variables are always available
        return Task.FromResult(true);
    }
}
