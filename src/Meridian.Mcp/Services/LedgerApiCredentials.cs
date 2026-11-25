using System.Text.Json.Serialization;

namespace Meridian.Mcp.Services;

/// <summary>Operator credentials are process configuration, never MCP tool arguments or output.</summary>
public sealed class LedgerApiCredentials(string? username = null, string? password = null, string? sessionToken = null, string? apiKey = null)
{
    internal string? Username { get; } = username;
    internal string? Password { get; } = password;
    internal string? SessionToken { get; } = sessionToken;
    internal string? ApiKey { get; } = apiKey;

    internal static LedgerApiCredentials FromEnvironment()
    {
        var username = Environment.GetEnvironmentVariable("MDC_LEDGER_USERNAME");
        var password = Environment.GetEnvironmentVariable("MDC_LEDGER_PASSWORD");
        var sessionToken = Environment.GetEnvironmentVariable("MDC_LEDGER_SESSION_TOKEN");
        if (string.IsNullOrEmpty(sessionToken) && (string.IsNullOrWhiteSpace(username) || string.IsNullOrEmpty(password)))
            throw new InvalidOperationException("Set MDC_LEDGER_SESSION_TOKEN to an issued Meridian operator session, or configure MDC_LEDGER_USERNAME and MDC_LEDGER_PASSWORD for sign-in. An API key does not establish the tenant-scoped session required by ledger reads.");
        return new(username, password, sessionToken, Environment.GetEnvironmentVariable("MDC_API_KEY"));
    }

    public override string ToString() => "Meridian ledger operator credentials (redacted)";
}

internal sealed record LedgerApiLoginRequest(string Username, string Password);

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(LedgerApiLoginRequest))]
internal partial class LedgerApiAuthenticationJsonContext : JsonSerializerContext;
