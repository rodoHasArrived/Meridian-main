using FluentAssertions;
using Meridian.Identity;
using Meridian.Identity.Auth;
using Meridian.Contracts.Tenancy;
using NSubstitute;
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
    public void MissingSelectedRoleProfile_DoesNotRestoreTheBaseAdminRole()
    {
        using var env = ClearAuthEnvironment();
        var store = new StubAccountStore(new UserAccountConfig(
            "scoped-admin", PasswordHashing.HashPassword("pw"), UserRole.Admin, RoleProfileName: "missing-profile"));
        var registry = new UserProfileRegistry(null, store);

        registry.Authenticate("scoped-admin", "pw").Should().BeNull();
        registry.GetProfile("scoped-admin").Should().BeNull();
    }

    [Fact]
    public void InvalidSelectedRoleProfile_DoesNotRestoreTheBaseAdminRole()
    {
        using var env = ClearAuthEnvironment();
        var store = new StubAccountStore(new UserAccountConfig(
            "scoped-admin", PasswordHashing.HashPassword("pw"), UserRole.Admin, RoleProfileName: "broken-profile"));
        var profiles = Substitute.For<IRolePermissionProfileStore>();
        profiles.TryGetProfile("broken-profile", out Arg.Any<RolePermissionProfileDto>()).Returns(call =>
        {
            call[1] = new RolePermissionProfileDto("broken-profile", "Broken", "Invalid permission", false, ["not-a-permission"], 0);
            return true;
        });
        var registry = new UserProfileRegistry(profiles, store);

        registry.Authenticate("scoped-admin", "pw").Should().BeNull();
        registry.GetProfile("scoped-admin").Should().BeNull();
    }

    [Fact]
    public void AddingASecondCompanyAfterStartup_RefusesExistingSessionResolution()
    {
        using var env = ClearAuthEnvironment();
        var hash = PasswordHashing.HashPassword("pw");
        UserAccountConfig[] accounts =
        [
            new("alpha", hash, UserRole.Admin, CompanyId: "alpha"),
            new("second", hash, UserRole.Admin, CompanyId: "alpha")
        ];
        var registry = new UserProfileRegistry(null, new StubAccountStore(accounts));
        registry.GetProfile("alpha").Should().NotBeNull();
        accounts[1] = accounts[1] with { CompanyId = "beta" };

        Action read = () => registry.GetProfile("alpha");
        read.Should().Throw<InvalidOperationException>().WithMessage("*fail-closed*");
    }

    [Fact]
    public void MultipleCompanies_RequireStrictTenantEnforcementForLoginAndExistingSessions()
    {
        using var env = ClearAuthEnvironment();
        var hash = PasswordHashing.HashPassword("pw");
        var store = new StubAccountStore(
            new UserAccountConfig("alpha", hash, UserRole.Admin, CompanyId: "alpha"),
            new UserAccountConfig("beta", hash, UserRole.Admin, CompanyId: "beta"));
        var boundary = new UserProfileRegistry(null, store, TenantScopeEnforcementOptions.DeploymentBoundary);
        Action login = () => boundary.Authenticate("alpha", "pw");
        Action existingSession = () => boundary.GetProfile("alpha");
        login.Should().Throw<InvalidOperationException>().WithMessage("*fail-closed*");
        existingSession.Should().Throw<InvalidOperationException>().WithMessage("*fail-closed*");

        var strict = new UserProfileRegistry(null, store, TenantScopeEnforcementOptions.FailClosed);
        strict.Authenticate("alpha", "pw")!.CompanyId.Should().Be("alpha");
        strict.GetProfile("beta")!.CompanyId.Should().Be("beta");
    }

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
