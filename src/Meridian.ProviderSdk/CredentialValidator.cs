using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Meridian.Infrastructure.Utilities;

/// <summary>
/// Centralized credential validation utilities to eliminate duplicate validation code
/// across providers. Each provider was implementing similar credential checks.
/// </summary>
public static class CredentialValidator
{
    /// <summary>
    /// Validate that a single credential (API key) is configured.
    /// </summary>
    public static bool ValidateApiKey(string? apiKey, string providerName, ILogger? log = null)
    {
        if (string.IsNullOrEmpty(apiKey))
        {
            var logger = log ?? NullLogger.Instance;
            logger.LogDebug("{Provider} API key not configured", providerName);
            return false;
        }
        return true;
    }

    /// <summary>
    /// Validate that a key/secret pair is configured.
    /// </summary>
    public static bool ValidateKeySecretPair(
        string? keyId,
        string? secretKey,
        string providerName,
        ILogger? log = null)
    {
        if (string.IsNullOrEmpty(keyId) || string.IsNullOrEmpty(secretKey))
        {
            var logger = log ?? NullLogger.Instance;
            logger.LogDebug("{Provider} API credentials not configured (missing key or secret)", providerName);
            return false;
        }
        return true;
    }

    /// <summary>
    /// Throw if API key is not configured.
    /// </summary>
    public static void ThrowIfApiKeyMissing(string? apiKey, string providerName, string envVarName)
    {
        if (string.IsNullOrEmpty(apiKey))
        {
            throw new InvalidOperationException(
                $"{providerName} API key is required. Set {envVarName} environment variable or provide it in configuration.");
        }
    }

    /// <summary>
    /// Throw if key/secret pair is not configured.
    /// </summary>
    public static void ThrowIfCredentialsMissing(
        string? keyId,
        string? secretKey,
        string providerName,
        string keyEnvVar,
        string secretEnvVar)
    {
        if (string.IsNullOrEmpty(keyId) || string.IsNullOrEmpty(secretKey))
        {
            throw new InvalidOperationException(
                $"{providerName} API credentials are required. Set {keyEnvVar} and {secretEnvVar} environment variables or provide them in configuration.");
        }
    }

    /// <summary>
    /// Get credential from parameter or environment variable.
    /// </summary>
    public static string? GetCredential(string? paramValue, string envVarName)
    {
        return !string.IsNullOrEmpty(paramValue)
            ? paramValue
            : Environment.GetEnvironmentVariable(envVarName);
    }

    /// <summary>
    /// Get credential from parameter or multiple possible environment variables.
    /// Tries each env var in order until one is found.
    /// </summary>
    public static string? GetCredential(string? paramValue, params string[] envVarNames)
    {
        if (!string.IsNullOrEmpty(paramValue))
            return paramValue;

        foreach (var envVar in envVarNames)
        {
            var value = Environment.GetEnvironmentVariable(envVar);
            if (!string.IsNullOrEmpty(value))
                return value;
        }

        return null;
    }
}
