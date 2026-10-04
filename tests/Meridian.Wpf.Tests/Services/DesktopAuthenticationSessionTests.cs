using FluentAssertions;
using Meridian.Identity;
using Meridian.Identity.Auth;
using Meridian.Wpf.Services;
using Meridian.Wpf.Tests.Support;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;

namespace Meridian.Wpf.Tests.Services;

[Collection("DesktopAuthenticationEnvironment")]
public sealed class DesktopAuthenticationSessionTests
{
    [Fact]
    public void SignIn_WithConfiguredUser_ShouldSetCurrentDesktopUser()
    {
        using var env = new EnvironmentVariableScope()
            .Set("MDC_USERS", HashedDesktopAdminUsersJson())
            .Set("MDC_USERNAME", null)
            .Set("MDC_PASSWORD_HASH", null)
            .Set("MDC_AUTH_MODE", null);

        var session = CreateSession("Production");

        var result = session.SignIn("desktop-admin", "pw");

        result.Succeeded.Should().BeTrue();
        result.Profile.Should().NotBeNull();
        session.IsAuthenticated.Should().BeTrue();
        session.CurrentActor.Should().Be("desktop-admin");
        session.CurrentRole.Should().Be(UserRole.Admin);
        session.CurrentPermissions.Should().Be(RolePermissions.For(UserRole.Admin));
        session.IsAnonymousDevelopmentSession.Should().BeFalse();
    }

    [Fact]
    public void SignIn_WithWrongPassword_ShouldLeaveSessionUnauthenticated()
    {
        using var env = new EnvironmentVariableScope()
            .Set("MDC_USERS", HashedDesktopAdminUsersJson())
            .Set("MDC_USERNAME", null)
            .Set("MDC_PASSWORD_HASH", null)
            .Set("MDC_AUTH_MODE", null);

        var session = CreateSession("Production");

        var result = session.SignIn("desktop-admin", "wrong");

        result.Succeeded.Should().BeFalse();
        result.Message.Should().Contain("does not match");
        session.IsAuthenticated.Should().BeFalse();
        session.CurrentUser.Should().BeNull();
        session.CurrentActor.Should().BeEmpty();
    }

    [Fact]
    public void HasPermission_WhenAdminSignedIn_EnforcesGrantedPermissions()
    {
        using var env = new EnvironmentVariableScope()
            .Set("MDC_USERS", HashedDesktopAdminUsersJson())
            .Set("MDC_USERNAME", null)
            .Set("MDC_PASSWORD_HASH", null)
            .Set("MDC_AUTH_MODE", null);

        var session = CreateSession("Production");
        session.SignIn("desktop-admin", "pw").Succeeded.Should().BeTrue();

        session.HasPermission(UserPermission.ManageProviders).Should().BeTrue();
        session.HasPermission(UserPermission.ManageUsers).Should().BeTrue();
    }

    [Fact]
    public void HasPermission_WhenReadOnlySignedIn_DeniesPrivilegedPermission()
    {
        using var env = new EnvironmentVariableScope()
            .Set("MDC_USERS", HashedDesktopReadOnlyUsersJson())
            .Set("MDC_USERNAME", null)
            .Set("MDC_PASSWORD_HASH", null)
            .Set("MDC_AUTH_MODE", null);

        var session = CreateSession("Production");
        session.SignIn("desktop-viewer", "pw").Succeeded.Should().BeTrue();

        session.HasPermission(UserPermission.ManageProviders).Should().BeFalse();
        session.HasPermission(UserPermission.ViewMarketData).Should().BeTrue();
    }

    [Fact]
    public void HasPermission_WhenProductionUnauthenticated_FailsClosed()
    {
        using var env = new EnvironmentVariableScope()
            .Set("MDC_USERS", null)
            .Set("MDC_USERNAME", null)
            .Set("MDC_PASSWORD_HASH", null)
            .Set("MDC_AUTH_MODE", "required");

        var session = CreateSession("Production");

        // No signed-in profile and credentials are required: gating must fail closed.
        session.HasPermission(UserPermission.ManageProviders).Should().BeFalse();
        session.HasPermission(UserPermission.ViewMarketData).Should().BeFalse();
    }

    [Fact]
    public void HasPermission_WhenNoResolvedProfile_DefersToAuthenticationGates()
    {
        using var env = new EnvironmentVariableScope()
            .Set("MDC_USERS", null)
            .Set("MDC_USERNAME", null)
            .Set("MDC_PASSWORD_HASH", null)
            .Set("MDC_AUTH_MODE", "optional")
            .Set("MDC_ANONYMOUS_ROLE", null);

        var session = CreateSession("Development");

        // No signed-in operator profile: gating defers (returns true) so local development is not blocked.
        session.HasPermission(UserPermission.ManageProviders).Should().BeTrue();
    }

    [Fact]
    public void HasPermission_WhenAnonymousReadOnlyRoleIsConfigured_UsesThatRolesPermissions()
    {
        using var env = new EnvironmentVariableScope()
            .Set("MDC_USERS", null)
            .Set("MDC_USERNAME", null)
            .Set("MDC_PASSWORD_HASH", null)
            .Set("MDC_AUTH_MODE", "optional")
            .Set("MDC_ANONYMOUS_ROLE", "ReadOnly");

        var session = CreateSession("Development");
        session.ContinueWithoutCredentials().Succeeded.Should().BeTrue();

        session.HasPermission(UserPermission.ViewMarketData).Should().BeTrue();
        session.HasPermission(UserPermission.ModifySecurityMaster).Should().BeFalse();
    }

    [Fact]
    public void HasPermission_WhenAnonymousRoleIsInvalid_FailsClosed()
    {
        using var env = new EnvironmentVariableScope()
            .Set("MDC_USERS", null)
            .Set("MDC_USERNAME", null)
            .Set("MDC_PASSWORD_HASH", null)
            .Set("MDC_AUTH_MODE", "optional")
            .Set("MDC_ANONYMOUS_ROLE", "0");

        var session = CreateSession("Development");
        session.ContinueWithoutCredentials().Succeeded.Should().BeTrue();

        session.HasPermission(UserPermission.ViewSecurityMaster).Should().BeFalse();
        session.HasPermission(UserPermission.ModifySecurityMaster).Should().BeFalse();
    }

    [Fact]
    public void HasPermission_WhenSignedInSessionIsRevoked_FailsClosed()
    {
        using var env = new EnvironmentVariableScope()
            .Set("MDC_USERS", HashedDesktopAdminUsersJson())
            .Set("MDC_USERNAME", null)
            .Set("MDC_PASSWORD_HASH", null)
            .Set("MDC_AUTH_MODE", null)
            .Set("MDC_ANONYMOUS_ROLE", null);

        var session = CreateSession("Production");
        session.SignIn("desktop-admin", "pw").Succeeded.Should().BeTrue();
        session.SignOut();

        session.HasPermission(UserPermission.ModifySecurityMaster).Should().BeFalse();
        session.TryGetAuthenticatedActor(out _).Should().BeFalse();
        session.TryAuthorize(UserPermission.ModifySecurityMaster, out _).Should().BeFalse();
    }

    [Fact]
    public void ContinueWithoutCredentials_WhenDevelopmentAndUnconfigured_ShouldCreateAnonymousDevelopmentSession()
    {
        using var env = new EnvironmentVariableScope()
            .Set("MDC_USERS", null)
            .Set("MDC_USERNAME", null)
            .Set("MDC_PASSWORD_HASH", null)
            .Set("MDC_AUTH_MODE", "optional");

        var session = CreateSession("Development");

        var result = session.ContinueWithoutCredentials();

        result.Succeeded.Should().BeTrue();
        result.IsAnonymousDevelopmentSession.Should().BeTrue();
        session.IsAnonymousDevelopmentSession.Should().BeTrue();
        session.CurrentActor.Should().Be("local-development");
        session.IsAuthenticated.Should().BeFalse();
    }

    [Fact]
    public void ContinueWithoutCredentials_WhenProductionAndUnconfigured_ShouldFailClosed()
    {
        using var env = new EnvironmentVariableScope()
            .Set("MDC_USERS", null)
            .Set("MDC_USERNAME", null)
            .Set("MDC_PASSWORD_HASH", null)
            .Set("MDC_AUTH_MODE", "required");

        var session = CreateSession("Production");

        var result = session.ContinueWithoutCredentials();

        result.Succeeded.Should().BeFalse();
        result.Message.Should().Contain("requires");
        session.IsAnonymousDevelopmentSession.Should().BeFalse();
        session.CurrentActor.Should().BeEmpty();
    }

    internal static DesktopAuthenticationSession CreateSession(string environmentName)
        => new(new LoginSessionService(
            new FakeHostEnvironment(environmentName),
            new UserProfileRegistry()));

    internal static string HashedDesktopAdminUsersJson()
        => $$"""[{"username":"desktop-admin","passwordHash":"{{PasswordHashing.HashPassword("pw")}}","role":"Admin","companyId":"company-alpha"}]""";

    internal static string HashedDesktopReadOnlyUsersJson()
        => $$"""[{"username":"desktop-viewer","passwordHash":"{{PasswordHashing.HashPassword("pw")}}","role":"ReadOnly"}]""";

    internal sealed class EnvironmentVariableScope : IDisposable
    {
        private readonly Dictionary<string, string?> _originalValues = new(StringComparer.Ordinal);

        public EnvironmentVariableScope Set(string name, string? value)
        {
            if (!_originalValues.ContainsKey(name))
            {
                _originalValues[name] = Environment.GetEnvironmentVariable(name);
            }

            Environment.SetEnvironmentVariable(name, value);
            return this;
        }

        public void Dispose()
        {
            foreach (var entry in _originalValues)
            {
                Environment.SetEnvironmentVariable(entry.Key, entry.Value);
            }
        }
    }

    internal sealed class FakeHostEnvironment(string environmentName) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = environmentName;

        public string ApplicationName { get; set; } = "Meridian.Wpf.Tests";

        public string ContentRootPath { get; set; } = RunMatUiAutomationFacade.ResolveRepoRoot();

        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
