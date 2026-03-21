using System.Text.Json.Serialization;

namespace Meridian.Contracts.Credentials;

/// <summary>
/// Types of credentials supported by the system.
/// </summary>
public enum CredentialType : byte
{
    /// <summary>
    /// Simple API key (non-expiring).
    /// </summary>
    ApiKey,

    /// <summary>
    /// API key with secret (e.g., Alpaca KeyId + SecretKey).
    /// </summary>
    ApiKeyWithSecret,

    /// <summary>
    /// OAuth 2.0 access token (typically time-limited).
    /// </summary>
    OAuth2Token,

    /// <summary>
    /// Bearer token for API authentication.
    /// </summary>
    BearerToken
}

/// <summary>
/// Status of the last credential test.
/// </summary>
public enum CredentialTestStatus : byte
{
    /// <summary>
    /// Credential has not been tested.
    /// </summary>
    Unknown,

    /// <summary>
    /// Credential test is in progress.
    /// </summary>
    Testing,

    /// <summary>
    /// Credential test succeeded.
    /// </summary>
    Success,

    /// <summary>
    /// Credential test failed.
    /// </summary>
    Failed,

    /// <summary>
    /// Credential has expired.
    /// </summary>
    Expired
}

/// <summary>
/// Extended credential information with metadata for tracking expiration and authentication status.
/// </summary>
public sealed class CredentialInfo
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("resource")]
    public string Resource { get; set; } = string.Empty;

    [JsonPropertyName("status")]
    public string Status { get; set; } = string.Empty;

    [JsonPropertyName("credentialType")]
    public CredentialType CredentialType { get; set; } = CredentialType.ApiKey;

    [JsonPropertyName("expiresAt")]
    public DateTime? ExpiresAt { get; set; }

    [JsonPropertyName("lastAuthenticatedAt")]
    public DateTime? LastAuthenticatedAt { get; set; }

    [JsonPropertyName("lastTestedAt")]
    public DateTime? LastTestedAt { get; set; }

    [JsonPropertyName("testStatus")]
    public CredentialTestStatus TestStatus { get; set; } = CredentialTestStatus.Unknown;

    [JsonPropertyName("isExpiringSoon")]
    public bool IsExpiringSoon => ExpiresAt.HasValue &&
        ExpiresAt.Value <= DateTime.UtcNow.AddDays(7) &&
        ExpiresAt.Value > DateTime.UtcNow;

    [JsonPropertyName("isExpired")]
    public bool IsExpired => ExpiresAt.HasValue && ExpiresAt.Value <= DateTime.UtcNow;

    [JsonPropertyName("canAutoRefresh")]
    public bool CanAutoRefresh { get; set; }

    [JsonPropertyName("refreshToken")]
    public string? RefreshToken { get; set; }

    /// <summary>
    /// Gets a friendly display string for the expiration status.
    /// </summary>
    [JsonIgnore]
    public string ExpirationDisplay
    {
        get
        {
            if (!ExpiresAt.HasValue)
                return "No expiration";

            if (IsExpired)
                return "Expired";

            var remaining = ExpiresAt.Value - DateTime.UtcNow;
            if (remaining.TotalDays > 30)
                return $"Expires in {(int)remaining.TotalDays} days";
            if (remaining.TotalDays > 1)
                return $"Expires in {(int)remaining.TotalDays} days";
            if (remaining.TotalHours > 1)
                return $"Expires in {(int)remaining.TotalHours} hours";
            return $"Expires in {(int)remaining.TotalMinutes} minutes";
        }
    }

    /// <summary>
    /// Gets a friendly display string for the last authentication time.
    /// </summary>
    [JsonIgnore]
    public string LastAuthDisplay
    {
        get
        {
            if (!LastAuthenticatedAt.HasValue)
                return "Never authenticated";

            var elapsed = DateTime.UtcNow - LastAuthenticatedAt.Value;
            if (elapsed.TotalDays > 30)
                return $"Last auth {(int)elapsed.TotalDays} days ago";
            if (elapsed.TotalDays > 1)
                return $"Last auth {(int)elapsed.TotalDays} days ago";
            if (elapsed.TotalHours > 1)
                return $"Last auth {(int)elapsed.TotalHours} hours ago";
            if (elapsed.TotalMinutes > 1)
                return $"Last auth {(int)elapsed.TotalMinutes} min ago";
            return "Just authenticated";
        }
    }
}

/// <summary>
/// Result of a credential test operation.
/// </summary>
public sealed class CredentialTestResult
{
    [JsonPropertyName("success")]
    public bool Success { get; set; }

    [JsonPropertyName("message")]
    public string Message { get; set; } = string.Empty;

    [JsonPropertyName("testedAt")]
    public DateTime TestedAt { get; set; } = DateTime.UtcNow;

    [JsonPropertyName("responseTimeMs")]
    public long ResponseTimeMs { get; set; }

    [JsonPropertyName("serverInfo")]
    public string? ServerInfo { get; set; }

    [JsonPropertyName("expiresAt")]
    public DateTime? ExpiresAt { get; set; }

    [JsonPropertyName("scopes")]
    public string[]? Scopes { get; set; }

    /// <summary>
    /// Creates a successful test result.
    /// </summary>
    public static CredentialTestResult CreateSuccess(string message, long responseTimeMs = 0)
    {
        return new CredentialTestResult
        {
            Success = true,
            Message = message,
            ResponseTimeMs = responseTimeMs
        };
    }

    /// <summary>
    /// Creates a failed test result.
    /// </summary>
    public static CredentialTestResult CreateFailure(string message)
    {
        return new CredentialTestResult
        {
            Success = false,
            Message = message
        };
    }
}

/// <summary>
/// Metadata stored alongside credentials for tracking expiration and auth status.
/// </summary>
public sealed class CredentialMetadata
{
    [JsonPropertyName("resource")]
    public string Resource { get; set; } = string.Empty;

    [JsonPropertyName("credentialType")]
    public CredentialType CredentialType { get; set; } = CredentialType.ApiKey;

    [JsonPropertyName("createdAt")]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    [JsonPropertyName("expiresAt")]
    public DateTime? ExpiresAt { get; set; }

    [JsonPropertyName("lastAuthenticatedAt")]
    public DateTime? LastAuthenticatedAt { get; set; }

    [JsonPropertyName("lastTestedAt")]
    public DateTime? LastTestedAt { get; set; }

    [JsonPropertyName("testStatus")]
    public CredentialTestStatus TestStatus { get; set; } = CredentialTestStatus.Unknown;

    [JsonPropertyName("refreshToken")]
    public string? RefreshToken { get; set; }

    [JsonPropertyName("tokenEndpoint")]
    public string? TokenEndpoint { get; set; }

    [JsonPropertyName("clientId")]
    public string? ClientId { get; set; }

    [JsonPropertyName("autoRefreshEnabled")]
    public bool AutoRefreshEnabled { get; set; }

    [JsonPropertyName("lastRefreshAttemptAt")]
    public DateTime? LastRefreshAttemptAt { get; set; }

    [JsonPropertyName("refreshFailureCount")]
    public int RefreshFailureCount { get; set; }
}

/// <summary>
/// OAuth token response from a token endpoint.
/// </summary>
public sealed class OAuthTokenResponse
{
    [JsonPropertyName("access_token")]
    public string AccessToken { get; set; } = string.Empty;

    [JsonPropertyName("token_type")]
    public string TokenType { get; set; } = "Bearer";

    [JsonPropertyName("expires_in")]
    public int ExpiresInSeconds { get; set; }

    [JsonPropertyName("refresh_token")]
    public string? RefreshToken { get; set; }

    [JsonPropertyName("scope")]
    public string? Scope { get; set; }

    /// <summary>
    /// Calculates the absolute expiration time from the expires_in value.
    /// </summary>
    public DateTime GetExpirationTime()
    {
        return DateTime.UtcNow.AddSeconds(ExpiresInSeconds);
    }
}

/// <summary>
/// Configuration for OAuth-based credential providers.
/// </summary>
public sealed class OAuthProviderConfig
{
    [JsonPropertyName("providerId")]
    public string ProviderId { get; set; } = string.Empty;

    [JsonPropertyName("displayName")]
    public string DisplayName { get; set; } = string.Empty;

    [JsonPropertyName("tokenEndpoint")]
    public string TokenEndpoint { get; set; } = string.Empty;

    [JsonPropertyName("clientId")]
    public string ClientId { get; set; } = string.Empty;

    [JsonPropertyName("clientSecret")]
    public string? ClientSecret { get; set; }

    [JsonPropertyName("scopes")]
    public string[]? Scopes { get; set; }

    [JsonPropertyName("autoRefreshThresholdMinutes")]
    public int AutoRefreshThresholdMinutes { get; set; } = 5;
}
