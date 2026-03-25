using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Meridian.Contracts.Auth;

namespace Meridian.Ui.Shared;

/// <summary>
/// Manages the set of user accounts available for authentication.
///
/// <para>User accounts are sourced from environment variables using one of two approaches:</para>
/// <list type="bullet">
///   <item>
///     <b>Multi-user (recommended):</b> Set <c>MDC_USERS</c> to a JSON array of
///     <see cref="UserAccountConfig"/> objects, e.g.:
///     <code>
///     MDC_USERS=[{"username":"alice","password":"s3cr3t","role":"TradeDesk"},
///                {"username":"bob","password":"p@ss","role":"Accounting"}]
///     </code>
///   </item>
///   <item>
///     <b>Single-user legacy (backward-compatible):</b> Set <c>MDC_USERNAME</c> and
///     <c>MDC_PASSWORD</c>. These are mapped to a single <see cref="UserRole.Admin"/> account.
///   </item>
/// </list>
///
/// <para>When both are set, <c>MDC_USERS</c> takes precedence.</para>
/// </summary>
public sealed class UserProfileRegistry
{
    internal const string MultiUserEnvVar = "MDC_USERS";
    internal const string LegacyUsernameEnvVar = "MDC_USERNAME";
    internal const string LegacyPasswordEnvVar = "MDC_PASSWORD";

    /// <summary>
    /// Returns <see langword="true"/> when at least one user account is configured.
    /// </summary>
    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(MultiUserEnvVar)) ||
        (!string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(LegacyUsernameEnvVar)) &&
         !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(LegacyPasswordEnvVar)));

    /// <summary>
    /// Validates <paramref name="username"/> and <paramref name="password"/> against the
    /// configured user accounts.
    /// </summary>
    /// <returns>
    /// The <see cref="UserProfile"/> for the matching account, or <see langword="null"/> when
    /// the credentials are invalid or no accounts are configured.
    /// </returns>
    public UserProfile? Authenticate(string username, string password)
    {
        var accounts = LoadAccounts();
        if (accounts.Length == 0)
            return null;

        foreach (var account in accounts)
        {
            if (CryptographicEquals(username, account.Username) &&
                CryptographicEquals(password, account.Password))
            {
                return new UserProfile(account.Username, account.Role);
            }
        }

        return null;
    }

    // ── Private helpers ──────────────────────────────────────────────────────

    private static UserAccountConfig[] LoadAccounts()
    {
        var multiUserJson = Environment.GetEnvironmentVariable(MultiUserEnvVar);
        if (!string.IsNullOrWhiteSpace(multiUserJson))
        {
            try
            {
                var parsed = JsonSerializer.Deserialize(
                    multiUserJson,
                    UserAccountConfigArrayJsonContext.Default.UserAccountConfigArray);
                return parsed ?? [];
            }
            catch (JsonException)
            {
                // Fall through to legacy fallback
            }
        }

        var legacyUsername = Environment.GetEnvironmentVariable(LegacyUsernameEnvVar);
        var legacyPassword = Environment.GetEnvironmentVariable(LegacyPasswordEnvVar);

        if (!string.IsNullOrWhiteSpace(legacyUsername) && !string.IsNullOrWhiteSpace(legacyPassword))
            return [new UserAccountConfig(legacyUsername, legacyPassword, UserRole.Admin)];

        return [];
    }

    /// <summary>Constant-time string comparison to prevent timing attacks.</summary>
    private static bool CryptographicEquals(string a, string b) =>
        CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(a),
            Encoding.UTF8.GetBytes(b));
}

/// <summary>
/// Represents an authenticated user's identity and role within a session.
/// </summary>
public sealed record UserProfile(string Username, UserRole Role)
{
    /// <summary>The permissions granted to this user based on their <see cref="Role"/>.</summary>
    public UserPermission Permissions => RolePermissions.For(Role);
}

/// <summary>
/// Configuration shape for a single user account loaded from the <c>MDC_USERS</c> environment variable.
/// </summary>
public sealed record UserAccountConfig(
    [property: JsonPropertyName("username")] string Username,
    [property: JsonPropertyName("password")] string Password,
    [property: JsonPropertyName("role")] UserRole Role);

/// <summary>AOT-safe JSON context for deserializing <see cref="UserAccountConfig"/> arrays.</summary>
[JsonSerializable(typeof(UserAccountConfig[]))]
[JsonSourceGenerationOptions(
    PropertyNameCaseInsensitive = true,
    UseStringEnumConverter = true)]
internal sealed partial class UserAccountConfigArrayJsonContext : JsonSerializerContext;
