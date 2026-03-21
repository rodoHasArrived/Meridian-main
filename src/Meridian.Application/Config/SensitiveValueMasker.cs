using System.Text.RegularExpressions;

namespace Meridian.Application.Config;

/// <summary>
/// Utility for masking sensitive values in logs, diagnostics, and configuration displays.
/// Part of the data quality framework (QW-78) to prevent accidental exposure of credentials.
/// </summary>
/// <remarks>
/// Masks API keys, secret keys, passwords, tokens, and other sensitive values
/// while preserving enough information for debugging (first/last few characters).
/// </remarks>
public static partial class SensitiveValueMasker
{
    /// <summary>
    /// Default number of characters to show at the start of a masked value.
    /// </summary>
    public const int DefaultPrefixLength = 4;

    /// <summary>
    /// Default number of characters to show at the end of a masked value.
    /// </summary>
    public const int DefaultSuffixLength = 4;

    /// <summary>
    /// The mask character used to replace hidden characters.
    /// </summary>
    public const char MaskChar = '*';

    /// <summary>
    /// Minimum length for partial masking. Values shorter than this are fully masked.
    /// </summary>
    public const int MinLengthForPartialMask = 12;

    /// <summary>
    /// Common environment variable names that contain sensitive values.
    /// </summary>
    private static readonly HashSet<string> SensitiveEnvVarNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "ALPACA_KEY_ID", "ALPACA_SECRET_KEY", "ALPACA__KEYID", "ALPACA__SECRETKEY",
        "MDC_ALPACA_KEY_ID", "MDC_ALPACA_SECRET_KEY",
        "POLYGON_API_KEY", "POLYGON__APIKEY", "MDC_POLYGON_API_KEY",
        "NYSE_API_KEY", "NYSE__APIKEY", "MDC_NYSE_API_KEY",
        "TIINGO_TOKEN", "TIINGO__TOKEN", "MDC_TIINGO_TOKEN",
        "FINNHUB_API_KEY", "FINNHUB__APIKEY", "MDC_FINNHUB_API_KEY",
        "ALPHA_VANTAGE_API_KEY", "ALPHAVANTAGE__APIKEY", "MDC_ALPHA_VANTAGE_API_KEY",
        "NASDAQ_API_KEY", "NASDAQ__APIKEY", "MDC_NASDAQ_API_KEY",
        "IB_PASSWORD", "IB__PASSWORD", "MDC_IB_PASSWORD",
        "DATABASE_CONNECTION_STRING", "CONNECTION_STRING",
        "AWS_SECRET_ACCESS_KEY", "AWS_ACCESS_KEY_ID",
        "AZURE_CLIENT_SECRET", "AZURE_STORAGE_KEY",
        "GCP_SERVICE_ACCOUNT_KEY",
        "API_KEY", "API_SECRET", "SECRET_KEY", "ACCESS_TOKEN", "BEARER_TOKEN",
        "PASSWORD", "CREDENTIALS"
    };

    /// <summary>
    /// Common configuration property names that contain sensitive values.
    /// </summary>
    private static readonly HashSet<string> SensitivePropertyNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "KeyId", "SecretKey", "ApiKey", "Token", "Password", "Secret",
        "Credentials", "ConnectionString", "AccessKey", "BearerToken",
        "ClientSecret", "PrivateKey", "Certificate"
    };

    /// <summary>
    /// Masks a sensitive value, showing only the first and last few characters.
    /// </summary>
    /// <param name="value">The value to mask.</param>
    /// <param name="prefixLength">Number of characters to show at the start.</param>
    /// <param name="suffixLength">Number of characters to show at the end.</param>
    /// <returns>The masked value.</returns>
    public static string Mask(string? value, int prefixLength = DefaultPrefixLength, int suffixLength = DefaultSuffixLength)
    {
        if (string.IsNullOrEmpty(value))
            return "[empty]";

        if (value.Length < MinLengthForPartialMask)
            return new string(MaskChar, value.Length);

        // Ensure we don't reveal too much
        var maxReveal = Math.Min(prefixLength + suffixLength, value.Length / 3);
        prefixLength = Math.Min(prefixLength, maxReveal / 2);
        suffixLength = Math.Min(suffixLength, maxReveal - prefixLength);

        var prefix = value[..prefixLength];
        var suffix = value[^suffixLength..];
        var maskLength = value.Length - prefixLength - suffixLength;

        return $"{prefix}{new string(MaskChar, maskLength)}{suffix}";
    }

    /// <summary>
    /// Masks a value completely, showing only the length.
    /// </summary>
    public static string MaskCompletely(string? value)
    {
        if (string.IsNullOrEmpty(value))
            return "[empty]";

        return $"[{value.Length} chars]";
    }

    /// <summary>
    /// Checks if a property name is considered sensitive.
    /// </summary>
    public static bool IsSensitiveProperty(string propertyName)
    {
        if (string.IsNullOrEmpty(propertyName))
            return false;

        // Direct match
        if (SensitivePropertyNames.Contains(propertyName))
            return true;

        // Partial match (e.g., "AlpacaSecretKey" contains "SecretKey")
        foreach (var sensitive in SensitivePropertyNames)
        {
            if (propertyName.Contains(sensitive, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    /// <summary>
    /// Checks if an environment variable name is considered sensitive.
    /// </summary>
    public static bool IsSensitiveEnvVar(string envVarName)
    {
        if (string.IsNullOrEmpty(envVarName))
            return false;

        // Direct match
        if (SensitiveEnvVarNames.Contains(envVarName))
            return true;

        // Check for common sensitive patterns
        var upperName = envVarName.ToUpperInvariant();
        return upperName.Contains("SECRET") ||
               upperName.Contains("PASSWORD") ||
               upperName.Contains("TOKEN") ||
               upperName.Contains("API_KEY") ||
               upperName.Contains("APIKEY") ||
               upperName.Contains("CREDENTIAL") ||
               upperName.Contains("PRIVATE_KEY") ||
               upperName.Contains("ACCESS_KEY");
    }

    /// <summary>
    /// Masks a value if the property name is sensitive.
    /// </summary>
    /// <param name="propertyName">The property name.</param>
    /// <param name="value">The value to potentially mask.</param>
    /// <returns>The original or masked value.</returns>
    public static string MaskIfSensitive(string propertyName, string? value)
    {
        if (IsSensitiveProperty(propertyName))
            return Mask(value);

        return value ?? "[null]";
    }

    /// <summary>
    /// Masks sensitive values in a dictionary.
    /// </summary>
    public static Dictionary<string, string> MaskDictionary(IDictionary<string, string?> values)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var kvp in values)
        {
            result[kvp.Key] = MaskIfSensitive(kvp.Key, kvp.Value);
        }

        return result;
    }

    /// <summary>
    /// Masks sensitive values in a connection string.
    /// </summary>
    public static string MaskConnectionString(string? connectionString)
    {
        if (string.IsNullOrEmpty(connectionString))
            return "[empty]";

        // Common patterns to mask in connection strings
        var result = connectionString;

        // Password=xxx; or Pwd=xxx;
        result = PasswordRegex().Replace(result, "$1" + new string(MaskChar, 8) + ";");

        // Secret=xxx;
        result = SecretRegex().Replace(result, "$1" + new string(MaskChar, 8) + ";");

        // AccountKey=xxx;
        result = AccountKeyRegex().Replace(result, "$1" + new string(MaskChar, 16) + ";");

        return result;
    }

    /// <summary>
    /// Creates a masked summary of AlpacaOptions for display.
    /// </summary>
    public static object MaskAlpacaOptions(string? keyId, string? secretKey, string? feed, bool useSandbox)
    {
        return new
        {
            KeyId = Mask(keyId),
            SecretKey = MaskCompletely(secretKey),
            Feed = feed ?? "iex",
            UseSandbox = useSandbox
        };
    }

    /// <summary>
    /// Creates a masked summary of provider credentials for logging.
    /// </summary>
    public static string FormatMaskedCredential(string providerName, string? credential)
    {
        if (string.IsNullOrEmpty(credential))
            return $"{providerName}: [not configured]";

        return $"{providerName}: {Mask(credential)}";
    }

    /// <summary>
    /// Gets all environment variables with sensitive values masked.
    /// </summary>
    public static Dictionary<string, string> GetMaskedEnvironmentVariables(string prefix = "MDC_")
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (System.Collections.DictionaryEntry entry in Environment.GetEnvironmentVariables())
        {
            var name = entry.Key?.ToString() ?? string.Empty;
            var value = entry.Value?.ToString();

            // Only include variables with the specified prefix
            if (!string.IsNullOrEmpty(prefix) && !name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                continue;

            result[name] = IsSensitiveEnvVar(name) ? Mask(value) : (value ?? "[null]");
        }

        return result;
    }

    [GeneratedRegex(@"(Password|Pwd)\s*=\s*[^;]+;", RegexOptions.IgnoreCase)]
    private static partial Regex PasswordRegex();

    [GeneratedRegex(@"(Secret)\s*=\s*[^;]+;", RegexOptions.IgnoreCase)]
    private static partial Regex SecretRegex();

    [GeneratedRegex(@"(AccountKey)\s*=\s*[^;]+;", RegexOptions.IgnoreCase)]
    private static partial Regex AccountKeyRegex();
}
