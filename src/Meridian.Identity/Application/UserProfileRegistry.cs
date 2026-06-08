using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Meridian.Identity.Auth;

namespace Meridian.Identity;

/// <summary>
/// Manages the set of user accounts available for authentication.
/// </summary>
public sealed class UserProfileRegistry
{
    internal const string MultiUserEnvVar = "MDC_USERS";
    internal const string DemoUserEnvVar = "MDC_DEMO_USERS";
    internal const string LegacyUsernameEnvVar = "MDC_USERNAME";
    internal const string LegacyPasswordHashEnvVar = "MDC_PASSWORD_HASH";

    private readonly IRolePermissionProfileStore? _roleProfileStore;
    private readonly IUserAccountStore? _accountStore;

    public UserProfileRegistry()
        : this(null, null)
    {
    }

    public UserProfileRegistry(IRolePermissionProfileStore? roleProfileStore)
        : this(roleProfileStore, null)
    {
    }

    public UserProfileRegistry(IRolePermissionProfileStore? roleProfileStore, IUserAccountStore? accountStore)
    {
        _roleProfileStore = roleProfileStore;
        _accountStore = accountStore;
    }

    /// <summary>
    /// Returns <see langword="true"/> when at least one user account is configured.
    /// </summary>
    public bool IsConfigured => LoadAccounts().Length > 0;

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
        var account = LoadAccounts()
            .FirstOrDefault(candidate => CryptographicEquals(username, candidate.Username));
        if (account is null ||
            account.IsDisabled ||
            !PasswordHashing.VerifyPassword(password, account.PasswordHash))
        {
            return null;
        }

        return CreateProfile(account);
    }

    public UserProfile? GetProfile(string username)
    {
        if (string.IsNullOrWhiteSpace(username))
        {
            return null;
        }

        var account = LoadAccounts()
            .FirstOrDefault(candidate => CryptographicEquals(username, candidate.Username));
        return account is null || account.IsDisabled ? null : CreateProfile(account);
    }

    // ── Private helpers ──────────────────────────────────────────────────────

    private UserAccountConfig[] LoadAccounts()
    {
        var storedAccounts = _accountStore?.LoadAccounts() ?? Array.Empty<UserAccountConfig>();
        if (storedAccounts.Count > 0)
        {
            return storedAccounts
                .Where(IsUsableAccountConfig)
                .ToArray();
        }

        var multiUserJson = Environment.GetEnvironmentVariable(MultiUserEnvVar);
        if (!string.IsNullOrWhiteSpace(multiUserJson))
        {
            try
            {
                var parsed = JsonSerializer.Deserialize(
                    multiUserJson,
                    UserAccountConfigArrayJsonContext.Default.UserAccountConfigArray);
                return (parsed ?? [])
                    .Where(IsUsableAccountConfig)
                    .ToArray();
            }
            catch (JsonException)
            {
                // Fall through to legacy fallback
            }
        }

        if (IsDevelopmentLikeEnvironment())
        {
            var demoUserJson = Environment.GetEnvironmentVariable(DemoUserEnvVar);
            if (!string.IsNullOrWhiteSpace(demoUserJson))
            {
                try
                {
                    var parsed = JsonSerializer.Deserialize(
                        demoUserJson,
                        UserAccountConfigArrayJsonContext.Default.UserAccountConfigArray);
                    return (parsed ?? [])
                        .Where(IsUsableAccountConfig)
                        .ToArray();
                }
                catch (JsonException)
                {
                    // Fall through to legacy fallback
                }
            }
        }

        var legacyUsername = Environment.GetEnvironmentVariable(LegacyUsernameEnvVar);
        var legacyPasswordHash = Environment.GetEnvironmentVariable(LegacyPasswordHashEnvVar);

        if (!string.IsNullOrWhiteSpace(legacyUsername) && PasswordHashing.IsSupportedHash(legacyPasswordHash))
            return [new UserAccountConfig(legacyUsername, legacyPasswordHash!, UserRole.Admin)];

        return [];
    }

    private UserProfile? CreateProfile(UserAccountConfig account)
    {
        if (account.Permissions is { Count: > 0 })
        {
            if (!RolePermissions.TryParsePermissionNames(
                    account.Permissions,
                    out var permissions,
                    out var invalidPermissionNames))
            {
                return null;
            }

            return new UserProfile(
                account.Username,
                account.Role,
                PermissionOverride: permissions,
                RoleProfileName: account.RoleProfileName,
                InvalidPermissionNames: invalidPermissionNames,
                PasswordResetRequired: account.PasswordResetRequired);
        }

        if (!string.IsNullOrWhiteSpace(account.RoleProfileName) &&
            _roleProfileStore is not null &&
            _roleProfileStore.TryGetProfile(account.RoleProfileName, out var profile) &&
            RolePermissions.TryParsePermissionNames(
                profile.Permissions,
                out var roleProfilePermissions,
                out var roleProfileInvalidPermissions))
        {
            return new UserProfile(
                account.Username,
                account.Role,
                PermissionOverride: roleProfilePermissions,
                RoleProfileName: profile.Role,
                InvalidPermissionNames: roleProfileInvalidPermissions,
                PasswordResetRequired: account.PasswordResetRequired);
        }

        return new UserProfile(
            account.Username,
            account.Role,
            RoleProfileName: account.RoleProfileName,
            PasswordResetRequired: account.PasswordResetRequired);
    }

    private static bool IsUsableAccountConfig(UserAccountConfig account)
        => !string.IsNullOrWhiteSpace(account.Username) &&
           PasswordHashing.IsSupportedHash(account.PasswordHash);

    private static bool IsDevelopmentLikeEnvironment()
    {
        var dotnet = Environment.GetEnvironmentVariable("DOTNET_ENVIRONMENT");
        var aspNetCore = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT");
        return IsDevelopmentLike(dotnet) || IsDevelopmentLike(aspNetCore);
    }

    private static bool IsDevelopmentLike(string? environment)
        => environment is not null &&
           (environment.Equals("Development", StringComparison.OrdinalIgnoreCase) ||
            environment.Equals("Test", StringComparison.OrdinalIgnoreCase));

    /// <summary>Constant-time string comparison to prevent timing attacks.</summary>
    private static bool CryptographicEquals(string a, string b) =>
        CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(a),
            Encoding.UTF8.GetBytes(b));
}

/// <summary>
/// Represents an authenticated user's identity and role within a session.
/// </summary>
public sealed record UserProfile(
    string Username,
    UserRole Role,
    UserPermission? PermissionOverride = null,
    string? RoleProfileName = null,
    IReadOnlyList<string>? InvalidPermissionNames = null,
    bool PasswordResetRequired = false)
{
    /// <summary>The permissions granted to this user based on their <see cref="Role"/>.</summary>
    public UserPermission Permissions => PermissionOverride ?? RolePermissions.For(Role);
}

/// <summary>
/// Configuration shape for a single user account loaded from the <c>MDC_USERS</c> environment variable.
/// </summary>
public sealed record UserAccountConfig(
    [property: JsonPropertyName("username")] string Username,
    [property: JsonPropertyName("passwordHash")] string PasswordHash,
    [property: JsonPropertyName("role")] UserRole Role,
    [property: JsonPropertyName("roleProfileName")] string? RoleProfileName = null,
    [property: JsonPropertyName("permissions")] IReadOnlyList<string>? Permissions = null,
    [property: JsonPropertyName("disabled")] bool IsDisabled = false,
    [property: JsonPropertyName("passwordResetRequired")] bool PasswordResetRequired = false);

/// <summary>AOT-safe JSON context for deserializing <see cref="UserAccountConfig"/> arrays.</summary>
[JsonSerializable(typeof(UserAccountConfig[]))]
[JsonSourceGenerationOptions(
    PropertyNameCaseInsensitive = true,
    UseStringEnumConverter = true)]
internal sealed partial class UserAccountConfigArrayJsonContext : JsonSerializerContext;
