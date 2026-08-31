using FluentAssertions;
using Meridian.Identity.Auth;
using Meridian.Wpf.Services;

namespace Meridian.Wpf.Tests.Services;

/// <summary>
/// The desktop shell reaches Security Master mutation services in process, so no endpoint filter
/// stands between an operator and the golden record. These pin that the desktop refuses the same
/// mutations the browser lane does — including on a credential-free host that names an anonymous
/// role, where <see cref="DesktopAuthenticationSession.HasPermission"/> answers true to everything
/// and a gate built on it alone would authorize every write.
/// </summary>
[Collection("DesktopAuthenticationEnvironment")]
public sealed class DesktopMutationPermissionResolverTests
{
    [Fact]
    public void IsGranted_ForSignedInAdmin_GrantsModifySecurityMaster()
    {
        using var env = new DesktopAuthenticationSessionTests.EnvironmentVariableScope()
            .Set("MDC_USERS", DesktopAuthenticationSessionTests.HashedDesktopAdminUsersJson())
            .Set("MDC_USERNAME", null)
            .Set("MDC_PASSWORD_HASH", null)
            .Set("MDC_AUTH_MODE", null);

        var session = DesktopAuthenticationSessionTests.CreateSession("Production");
        session.SignIn("desktop-admin", "pw").Succeeded.Should().BeTrue();

        DesktopMutationPermissionResolver.IsGranted(session, UserPermission.ModifySecurityMaster)
            .Should().BeTrue();
    }

    [Fact]
    public void IsGranted_ForSignedInReadOnlyOperator_RefusesModifySecurityMaster()
    {
        using var env = new DesktopAuthenticationSessionTests.EnvironmentVariableScope()
            .Set("MDC_USERS", DesktopAuthenticationSessionTests.HashedDesktopReadOnlyUsersJson())
            .Set("MDC_USERNAME", null)
            .Set("MDC_PASSWORD_HASH", null)
            .Set("MDC_AUTH_MODE", null);

        var session = DesktopAuthenticationSessionTests.CreateSession("Production");
        session.SignIn("desktop-viewer", "pw").Succeeded.Should().BeTrue();

        // Over HTTP this operator is refused every golden-record mutation; the desktop lane must
        // refuse the same writes rather than being the second door.
        DesktopMutationPermissionResolver.IsGranted(session, UserPermission.ModifySecurityMaster)
            .Should().BeFalse();
    }

    [Fact]
    public void IsGranted_ForUnauthenticatedCredentialBackedHost_FailsClosed()
    {
        using var env = new DesktopAuthenticationSessionTests.EnvironmentVariableScope()
            .Set("MDC_USERS", DesktopAuthenticationSessionTests.HashedDesktopAdminUsersJson())
            .Set("MDC_USERNAME", null)
            .Set("MDC_PASSWORD_HASH", null)
            .Set("MDC_AUTH_MODE", null);

        var session = DesktopAuthenticationSessionTests.CreateSession("Production");

        DesktopMutationPermissionResolver.IsGranted(session, UserPermission.ModifySecurityMaster)
            .Should().BeFalse();
    }

    [Fact]
    public void IsGranted_WhenCredentialFreeHostNamesReadOnlyAnonymousRole_RefusesTheMutationTheSessionWouldAllow()
    {
        using var env = new DesktopAuthenticationSessionTests.EnvironmentVariableScope()
            .Set("MDC_USERS", null)
            .Set("MDC_USERNAME", null)
            .Set("MDC_PASSWORD_HASH", null)
            .Set("MDC_AUTH_MODE", "optional")
            .Set("MDC_ANONYMOUS_ROLE", "ReadOnly");

        var session = DesktopAuthenticationSessionTests.CreateSession("Development");

        // The defect this resolver exists to close: the session answers true to every permission on
        // a credential-free host, so a gate built on HasPermission alone would authorize every
        // mutation on a host explicitly declared read-only.
        session.HasPermission(UserPermission.ModifySecurityMaster).Should().BeTrue();
        DesktopMutationPermissionResolver.IsGranted(session, UserPermission.ModifySecurityMaster)
            .Should().BeFalse();
    }

    [Fact]
    public void IsGranted_WhenCredentialFreeHostNamesAdminAnonymousRole_GrantsWhatTheRoleGrants()
    {
        using var env = new DesktopAuthenticationSessionTests.EnvironmentVariableScope()
            .Set("MDC_USERS", null)
            .Set("MDC_USERNAME", null)
            .Set("MDC_PASSWORD_HASH", null)
            .Set("MDC_AUTH_MODE", "optional")
            .Set("MDC_ANONYMOUS_ROLE", "Admin");

        var session = DesktopAuthenticationSessionTests.CreateSession("Development");

        DesktopMutationPermissionResolver.IsGranted(session, UserPermission.ModifySecurityMaster)
            .Should().BeTrue();
    }

    [Fact]
    public void IsGranted_WhenCredentialFreeHostNamesUnrecognisedRole_FailsClosed()
    {
        using var env = new DesktopAuthenticationSessionTests.EnvironmentVariableScope()
            .Set("MDC_USERS", null)
            .Set("MDC_USERNAME", null)
            .Set("MDC_PASSWORD_HASH", null)
            .Set("MDC_AUTH_MODE", "optional")
            .Set("MDC_ANONYMOUS_ROLE", "NotARealRole");

        var session = DesktopAuthenticationSessionTests.CreateSession("Development");

        // A typo in a security setting must never grant everything: falling through to the session
        // here would authorize every write.
        DesktopMutationPermissionResolver.IsGranted(session, UserPermission.ModifySecurityMaster)
            .Should().BeFalse();
    }

    [Fact]
    public void IsGranted_WhenCredentialFreeHostNamesNoRole_KeepsTheShellFailOpenPosture()
    {
        using var env = new DesktopAuthenticationSessionTests.EnvironmentVariableScope()
            .Set("MDC_USERS", null)
            .Set("MDC_USERNAME", null)
            .Set("MDC_PASSWORD_HASH", null)
            .Set("MDC_AUTH_MODE", "optional")
            .Set("MDC_ANONYMOUS_ROLE", null);

        var session = DesktopAuthenticationSessionTests.CreateSession("Development");

        // Unset is not misconfigured: unconfigured local development keeps the shell's own
        // fail-open decision, exactly as HasPermission and the read-scope resolver already do.
        DesktopMutationPermissionResolver.IsGranted(session, UserPermission.ModifySecurityMaster)
            .Should().BeTrue();
    }

    [Fact]
    public void IsGranted_WithoutASessionAndNoAnonymousRole_KeepsTheShellFailOpenPosture()
    {
        using var env = new DesktopAuthenticationSessionTests.EnvironmentVariableScope()
            .Set("MDC_ANONYMOUS_ROLE", null);

        DesktopMutationPermissionResolver.IsGranted(null, UserPermission.ModifySecurityMaster)
            .Should().BeTrue();
    }

    [Fact]
    public void IsGranted_WithoutASessionAndReadOnlyAnonymousRole_Refuses()
    {
        using var env = new DesktopAuthenticationSessionTests.EnvironmentVariableScope()
            .Set("MDC_ANONYMOUS_ROLE", "ReadOnly");

        // A shell composed without authentication still honours a named anonymous role, matching
        // DesktopWorkflowReadScopeResolver's treatment of the same host for read scope.
        DesktopMutationPermissionResolver.IsGranted(null, UserPermission.ModifySecurityMaster)
            .Should().BeFalse();
    }
}
