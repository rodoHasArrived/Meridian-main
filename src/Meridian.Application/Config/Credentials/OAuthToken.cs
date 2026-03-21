using System.Text.Json.Serialization;

namespace Meridian.Application.Config.Credentials;

/// <summary>
/// Represents an OAuth 2.0 token for providers that support OAuth authentication.
/// </summary>
/// <param name="AccessToken">The access token used for API authentication.</param>
/// <param name="TokenType">Token type (typically "Bearer").</param>
/// <param name="ExpiresAt">When the access token expires.</param>
/// <param name="RefreshToken">The refresh token for obtaining new access tokens.</param>
/// <param name="RefreshTokenExpiresAt">When the refresh token expires (if applicable).</param>
/// <param name="Scope">OAuth scopes granted to this token.</param>
/// <param name="IssuedAt">When the token was issued.</param>
public sealed record OAuthToken(
    string AccessToken,
    string TokenType,
    DateTimeOffset ExpiresAt,
    string? RefreshToken = null,
    DateTimeOffset? RefreshTokenExpiresAt = null,
    string? Scope = null,
    DateTimeOffset? IssuedAt = null
)
{
    /// <summary>
    /// Indicates whether the access token has expired.
    /// </summary>
    [JsonIgnore]
    public bool IsExpired => DateTimeOffset.UtcNow >= ExpiresAt;

    /// <summary>
    /// Indicates whether the access token is expiring soon (within 5 minutes).
    /// </summary>
    [JsonIgnore]
    public bool IsExpiringSoon => DateTimeOffset.UtcNow >= ExpiresAt.AddMinutes(-5);

    /// <summary>
    /// Time remaining until the access token expires.
    /// </summary>
    [JsonIgnore]
    public TimeSpan TimeUntilExpiration => ExpiresAt - DateTimeOffset.UtcNow;

    /// <summary>
    /// Indicates whether this token can be refreshed.
    /// </summary>
    [JsonIgnore]
    public bool CanRefresh => !string.IsNullOrEmpty(RefreshToken) &&
                              (!RefreshTokenExpiresAt.HasValue || DateTimeOffset.UtcNow < RefreshTokenExpiresAt.Value);

    /// <summary>
    /// Percentage of token lifetime remaining (0-100).
    /// </summary>
    [JsonIgnore]
    public double LifetimeRemainingPercent
    {
        get
        {
            if (!IssuedAt.HasValue)
                return IsExpired ? 0 : 100;
            var totalLifetime = ExpiresAt - IssuedAt.Value;
            var remaining = ExpiresAt - DateTimeOffset.UtcNow;
            if (remaining <= TimeSpan.Zero)
                return 0;
            return Math.Min(100, (remaining.TotalSeconds / totalLifetime.TotalSeconds) * 100);
        }
    }
}

/// <summary>
/// OAuth provider configuration for providers that support OAuth 2.0.
/// </summary>
/// <param name="ProviderName">Name of the provider.</param>
/// <param name="ClientId">OAuth client ID.</param>
/// <param name="ClientSecret">OAuth client secret.</param>
/// <param name="AuthorizationEndpoint">OAuth authorization endpoint URL.</param>
/// <param name="TokenEndpoint">OAuth token endpoint URL.</param>
/// <param name="Scopes">Required OAuth scopes.</param>
/// <param name="RedirectUri">OAuth redirect URI for authorization code flow.</param>
public sealed record OAuthProviderConfig(
    string ProviderName,
    string ClientId,
    string? ClientSecret = null,
    string? AuthorizationEndpoint = null,
    string? TokenEndpoint = null,
    string[]? Scopes = null,
    string? RedirectUri = null
);

/// <summary>
/// Result of an OAuth token refresh operation.
/// </summary>
/// <param name="Success">Whether the refresh was successful.</param>
/// <param name="Token">The new token if successful.</param>
/// <param name="Error">Error message if refresh failed.</param>
/// <param name="RefreshedAt">When the refresh was performed.</param>
/// <param name="NextRefreshAt">Suggested time for next refresh.</param>
public sealed record OAuthRefreshResult(
    bool Success,
    OAuthToken? Token = null,
    string? Error = null,
    DateTimeOffset? RefreshedAt = null,
    DateTimeOffset? NextRefreshAt = null
);
