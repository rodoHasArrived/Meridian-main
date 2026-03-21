namespace Meridian.Application.Config.Credentials;

/// <summary>
/// Represents the current authentication status of a provider credential.
/// </summary>
public enum CredentialAuthStatus : byte
{
    /// <summary>
    /// Credential status has not been tested yet.
    /// </summary>
    Unknown,

    /// <summary>
    /// Credential is valid and authentication succeeded.
    /// </summary>
    Valid,

    /// <summary>
    /// Credential is invalid (wrong key/secret).
    /// </summary>
    Invalid,

    /// <summary>
    /// Credential has expired (for time-limited tokens).
    /// </summary>
    Expired,

    /// <summary>
    /// Credential is expiring soon (warning threshold reached).
    /// </summary>
    ExpiringSoon,

    /// <summary>
    /// Authentication test failed due to network or service issues.
    /// </summary>
    TestFailed,

    /// <summary>
    /// Credential is being refreshed (OAuth flow in progress).
    /// </summary>
    Refreshing,

    /// <summary>
    /// Credential configuration is missing or incomplete.
    /// </summary>
    NotConfigured
}

/// <summary>
/// Represents the result of testing a provider's credentials.
/// </summary>
/// <param name="ProviderName">Name of the provider (e.g., "Alpaca", "Polygon").</param>
/// <param name="Status">Current authentication status.</param>
/// <param name="Message">Human-readable status message.</param>
/// <param name="TestedAt">Timestamp when the test was performed.</param>
/// <param name="LastSuccessfulAuth">Timestamp of last successful authentication.</param>
/// <param name="ExpiresAt">When the credential expires (null if non-expiring).</param>
/// <param name="ResponseTimeMs">Time taken for the authentication test in milliseconds.</param>
/// <param name="CanAutoRefresh">Whether the credential supports auto-refresh (OAuth).</param>
/// <param name="CredentialSource">Source of the credential (Environment, File, Config).</param>
/// <param name="CredentialMasked">Masked version of the credential for display.</param>
public sealed record CredentialTestResult(
    string ProviderName,
    CredentialAuthStatus Status,
    string Message,
    DateTimeOffset TestedAt,
    DateTimeOffset? LastSuccessfulAuth = null,
    DateTimeOffset? ExpiresAt = null,
    long? ResponseTimeMs = null,
    bool CanAutoRefresh = false,
    string? CredentialSource = null,
    string? CredentialMasked = null
)
{
    /// <summary>
    /// Indicates whether the credential test was successful.
    /// </summary>
    public bool IsSuccess => Status is CredentialAuthStatus.Valid or CredentialAuthStatus.ExpiringSoon;

    /// <summary>
    /// Time until expiration, or null if not applicable.
    /// </summary>
    public TimeSpan? TimeUntilExpiration => ExpiresAt.HasValue ? ExpiresAt.Value - DateTimeOffset.UtcNow : null;

    /// <summary>
    /// Percentage of token lifetime remaining (0-100).
    /// </summary>
    public double? LifetimeRemainingPercent { get; init; }
}

/// <summary>
/// Aggregated status of all provider credentials.
/// </summary>
/// <param name="Results">Individual test results for each provider.</param>
/// <param name="AllValid">True if all configured providers have valid credentials.</param>
/// <param name="TestedAt">When the aggregate test was performed.</param>
/// <param name="Warnings">List of warning messages (expiring credentials, etc.).</param>
public sealed record CredentialStatusSummary(
    IReadOnlyList<CredentialTestResult> Results,
    bool AllValid,
    DateTimeOffset TestedAt,
    IReadOnlyList<string> Warnings
);

/// <summary>
/// Stored credential status for persistence and caching.
/// </summary>
/// <param name="ProviderName">Name of the provider.</param>
/// <param name="LastSuccessfulAuth">Timestamp of last successful authentication.</param>
/// <param name="LastTestResult">Status from the last test.</param>
/// <param name="LastTestedAt">When the credential was last tested.</param>
/// <param name="ConsecutiveFailures">Number of consecutive authentication failures.</param>
/// <param name="ExpiresAt">When the credential expires.</param>
public sealed record StoredCredentialStatus(
    string ProviderName,
    DateTimeOffset? LastSuccessfulAuth,
    CredentialAuthStatus LastTestResult,
    DateTimeOffset LastTestedAt,
    int ConsecutiveFailures,
    DateTimeOffset? ExpiresAt = null
);

/// <summary>
/// Configuration for credential expiration warnings.
/// </summary>
/// <param name="WarnDaysBeforeExpiration">Days before expiration to show warning (default: 7).</param>
/// <param name="CriticalDaysBeforeExpiration">Days before expiration to show critical warning (default: 1).</param>
/// <param name="AutoRefreshEnabled">Whether to automatically refresh expiring OAuth tokens.</param>
/// <param name="AutoRefreshDaysBeforeExpiration">Days before expiration to trigger auto-refresh (default: 3).</param>
public sealed record CredentialExpirationConfig(
    int WarnDaysBeforeExpiration = 7,
    int CriticalDaysBeforeExpiration = 1,
    bool AutoRefreshEnabled = true,
    int AutoRefreshDaysBeforeExpiration = 3
);
