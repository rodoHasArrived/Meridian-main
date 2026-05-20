using System.Text.RegularExpressions;

namespace Meridian.Application.Services;

/// <summary>
/// Redacts sensitive values before runtime diagnostics are exported to logs, bundles, or support endpoints.
/// </summary>
public static class RuntimeDiagnosticRedactor
{
    private static readonly string[] SensitiveKeys =
    [
        "password", "secret", "key", "token", "apikey", "api_key", "api-key",
        "connectionstring", "connection_string", "credential", "auth", "authorization",
        "clientsecret", "client_secret", "refresh", "session", "accountkey"
    ];

    public static string SanitizeText(string? content)
    {
        if (string.IsNullOrEmpty(content))
            return string.Empty;

        var patterns = new[]
        {
            (@"Bearer\s+[A-Za-z0-9\-_]+", "Bearer [REDACTED]"),
            (@"Basic\s+[A-Za-z0-9+/=]+", "Basic [REDACTED]"),
            (@"(?<key>authorization)\s*[:=]\s*(?<value>Bearer\s+[^\s,;}\]]+|Basic\s+[^\s,;}\]]+|[^\s,;}\]]+)", "${key}=[REDACTED]"),
            (@"(?<key>password|secret|secretKey|key|apiKey|api_key|api-key|token|accessToken|refreshToken|clientSecret|connectionString|credential)\s*[:=]\s*(?<value>[^\s,;}\]]+)", "${key}=[REDACTED]"),
            (@"""(?<key>password|secret|secretKey|key|apiKey|api_key|api-key|token|accessToken|refreshToken|clientSecret|connectionString|authorization|credential)""\s*:\s*""(?<value>[^""]*)""", "\"${key}\":\"[REDACTED]\""),
            (@"(?<key>accountNumber|account_number|accountNo|account_no|acctNumber|acct_number|accountId|account_id|externalAccountId|external_account_id|custodianAccountId|custodian_account_id)\s*[:=]\s*(?<value>[A-Za-z0-9\-]{4,})", "${key}=[REDACTED]"),
            (@"""(?<key>accountNumber|account_number|accountNo|account_no|acctNumber|acct_number|accountId|account_id|externalAccountId|external_account_id|custodianAccountId|custodian_account_id)""\s*:\s*""(?<value>[^""]*)""", "\"${key}\":\"[REDACTED]\""),
            (@"(?<separator>[?&])(?<key>api_key|apikey|access_token|token|secret|accountNumber|account_number|accountId|account_id|externalAccountId|external_account_id)=[^&\s]+", "${separator}${key}=[REDACTED]")
        };

        var result = content;
        foreach (var (pattern, replacement) in patterns)
        {
            result = Regex.Replace(
                result,
                pattern,
                replacement,
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        }

        return result;
    }

    public static string SanitizeEnvValue(string key, string value) =>
        IsSensitiveKey(key) || Meridian.Application.Config.SensitiveValueMasker.IsSensitiveEnvVar(key) ? "[REDACTED]" :
        key.Equals("PATH", StringComparison.OrdinalIgnoreCase) ? "[PATH variable - omitted for brevity]" :
        SanitizeText(value);

    public static bool IsSensitiveKey(string key)
    {
        return SensitiveKeys.Any(s =>
            key.IndexOf(s, StringComparison.OrdinalIgnoreCase) >= 0);
    }
}
