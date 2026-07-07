using FluentAssertions;
using Meridian.Identity;
using Meridian.Identity.Auth;
using Xunit;

namespace Meridian.Tests.Identity;

/// <summary>
/// Operator scenario: Meridian resolves who may sign in from either a governed account store or
/// hashed environment bootstrap records. These tests exercise the authentication decision itself
/// — credential matching, disabled accounts, store precedence, and permission resolution — rather
/// than the downstream authorization surfaces.
/// </summary>
[Collection("IdentityEnvironment")]
public sealed class UserProfileRegistryTests
{
    [Fact]
    public void Authenticate_WithCorrectCredentials_ReturnsProfile()
    {
        using var env = ClearAuthEnvironment()
            .Set("MDC_USERS", UsersJson(("trader", "pw-trader", UserRole.TradeDesk)));
        var registry = new UserProfileRegistry();

        var profile = registry.Authenticate("trader", "pw-trader");

        profile.Should().NotBeNull();
        profile!.Username.Should().Be("trader");
        profile.Role.Should().Be(UserRole.TradeDesk);
        profile.Permissions.Should().Be(RolePermissions.For(UserRole.TradeDesk));
    }

    [Fact]
    public void Authenticate_WithWrongPassword_ReturnsNull()
    {
        using var env = ClearAuthEnvironment()
            .Set("MDC_USERS", UsersJson(("trader", "pw-trader", UserRole.TradeDesk)));
        var registry = new UserProfileRegistry();

        registry.Authenticate("trader", "wrong-password").Should().BeNull();
    }

    [Fact]
    public void Authenticate_WithUnknownUser_ReturnsNull()
    {
        using var env = ClearAuthEnvironment()
            .Set("MDC_USERS", UsersJson(("trader", "pw-trader", UserRole.TradeDesk)));
        var registry = new UserProfileRegistry();

        registry.Authenticate("ghost", "pw-trader").Should().BeNull();
    }

    [Fact]
    public void Authenticate_UsernameComparisonIsCaseSensitive()
    {
        using var env = ClearAuthEnvironment()
            .Set("MDC_USERS", UsersJson(("Trader", "pw", UserRole.TradeDesk)));
        var registry = new UserProfileRegistry();

        registry.Authenticate("trader", "pw").Should().BeNull();
        registry.Authenticate("Trader", "pw").Should().NotBeNull();
    }

    [Fact]
    public void Authenticate_DisabledAccount_ReturnsNull()
    {
        var hash = PasswordHashing.HashPassword("pw");
        using var env = ClearAuthEnvironment()
            .Set("MDC_USERS", $$"""[{"username":"suspended","passwordHash":"{{hash}}","role":"Admin","disabled":true}]""");
        var registry = new UserProfileRegistry();

        registry.Authenticate("suspended", "pw").Should().BeNull();
        registry.GetProfile("suspended").Should().BeNull();
    }

    [Fact]
    public void IsConfigured_ReflectsWhetherUsableAccountsExist()
    {
        using (var empty = ClearAuthEnvironment())
        {
            new UserProfileRegistry().IsConfigured.Should().BeFalse();
        }

        using var env = ClearAuthEnvironment()
            .Set("MDC_USERS", UsersJson(("admin", "pw", UserRole.Admin)));
        new UserProfileRegistry().IsConfigured.Should().BeTrue();
    }

    [Fact]
    public void Authenticate_PrefersGovernedAccountStoreOverEnvironment()
    {
        // Environment provides a would-be admin, but the governed store is authoritative and
        // does not contain that account, so environment records must be ignored entirely.
        using var env = ClearAuthEnvironment()
            .Set("MDC_USERS", UsersJson(("env-admin", "pw", UserRole.Admin)));
        var store = new StubAccountStore(
            new UserAccountConfig("store-user", PasswordHashing.HashPassword("store-pw"), UserRole.Analysis));
        var registry = new UserProfileRegistry(roleProfileStore: null, accountStore: store);

        registry.Authenticate("env-admin", "pw").Should().BeNull("the governed store suppresses environment records");
        var stored = registry.Authenticate("store-user", "store-pw");
        stored.Should().NotBeNull();
        stored!.Role.Should().Be(UserRole.Analysis);
    }

    [Fact]
    public void Authenticate_WithPermissionOverride_UsesExplicitPermissions()
    {
        var hash = PasswordHashing.HashPassword("pw");
        using var env = ClearAuthEnvironment()
            .Set("MDC_USERS", $$"""[{"username":"scoped","passwordHash":"{{hash}}","role":"ReadOnly","permissions":["ViewTrades","ExportData"]}]""");
        var registry = new UserProfileRegistry();

        var profile = registry.Authenticate("scoped", "pw");

        profile.Should().NotBeNull();
        profile!.Permissions.Should().Be(UserPermission.ViewTrades | UserPermission.ExportData);
    }

    [Fact]
    public void Authenticate_MalformedMultiUserJson_ReturnsNullWithoutThrowing()
    {
        using var env = ClearAuthEnvironment()
            .Set("MDC_USERS", "{ this is not valid json");
        var registry = new UserProfileRegistry();

        registry.IsConfigured.Should().BeFalse();
        registry.Authenticate("anyone", "pw").Should().BeNull();
    }

    private static EnvironmentVariableScope ClearAuthEnvironment()
        => new EnvironmentVariableScope()
            .Set("MDC_USERS", null)
            .Set("MDC_DEMO_USERS", null)
            .Set("MDC_USERNAME", null)
            .Set("MDC_PASSWORD_HASH", null);

    private static string UsersJson(params (string Username, string Password, UserRole Role)[] users)
    {
        var entries = users.Select(user =>
            $$"""{"username":"{{user.Username}}","passwordHash":"{{PasswordHashing.HashPassword(user.Password)}}","role":"{{user.Role}}"}""");
        return $"[{string.Join(",", entries)}]";
    }

    private sealed class StubAccountStore(params UserAccountConfig[] accounts) : IUserAccountStore
    {
        public bool HasAccounts => accounts.Length > 0;

        public IReadOnlyList<UserAccountConfig> LoadAccounts() => accounts;

        public Task<IReadOnlyList<UserAccountDto>> GetAccountsAsync(CancellationToken ct = default)
            => throw new NotSupportedException();

        public Task<IReadOnlyList<UserAccountAuditEventDto>> GetAuditEventsAsync(int? limit = null, CancellationToken ct = default)
            => throw new NotSupportedException();

        public Task<UserAccountMutationResultDto> UpsertAsync(UserAccountUpsertRequestDto request, string actor, CancellationToken ct = default)
            => throw new NotSupportedException();

        public Task<UserAccountMutationResultDto> ResetPasswordAsync(UserPasswordResetRequestDto request, string actor, int revokedSessionCount = 0, CancellationToken ct = default)
            => throw new NotSupportedException();

        public Task<UserAccountMutationResultDto> SetDisabledAsync(UserAccountDisableRequestDto request, string actor, int revokedSessionCount = 0, CancellationToken ct = default)
            => throw new NotSupportedException();

        public Task<UserSessionRevokeResultDto> RecordSessionRevocationAsync(UserSessionRevokeRequestDto request, string actor, int revokedSessionCount, CancellationToken ct = default)
            => throw new NotSupportedException();
    }
}
