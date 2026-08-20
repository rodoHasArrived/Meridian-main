using Meridian.Identity.Auth;
using Meridian.Ui.Shared.Services;
using Meridian.Wpf.Services;

namespace Meridian.Wpf.Tests.Services;

/// <summary>
/// The desktop shell reaches the shared workflow-summary service in process, so no endpoint filter
/// stands between a restricted operator and the composed summary. These pin that the desktop
/// withholds the same workspace cards the browser lane does, rather than inheriting a permissive
/// in-process default.
/// </summary>
[Collection("DesktopAuthenticationEnvironment")]
public sealed class DesktopWorkflowReadScopeResolverTests
{
    [Fact]
    public void Resolve_ForReadOnlyDesktopOperator_ShouldGrantOnlyTheFamiliesItsPermissionsName()
    {
        using var env = new DesktopAuthenticationSessionTests.EnvironmentVariableScope()
            .Set("MDC_USERS", DesktopAuthenticationSessionTests.HashedDesktopReadOnlyUsersJson())
            .Set("MDC_USERNAME", null)
            .Set("MDC_PASSWORD_HASH", null)
            .Set("MDC_AUTH_MODE", null);

        var session = DesktopAuthenticationSessionTests.CreateSession("Production");
        session.SignIn("desktop-viewer", "pw").Succeeded.Should().BeTrue();

        var scope = DesktopWorkflowReadScopeResolver.Resolve(session);

        // ReadOnly is ViewMarketData | ViewHistoricalData | ViewAnalytics | ViewStrategies. Two of
        // those name a family: ViewStrategies the strategy card, and ViewHistoricalData the data card
        // once that family was aligned with the Data workspace's own permissions. The trading and
        // accounting cards carry records this role holds nothing for.
        scope.Strategy.Should().BeTrue();
        scope.Data.Should().BeTrue();
        scope.Trading.Should().BeFalse();
        scope.Accounting.Should().BeFalse();
    }

    [Fact]
    public void Resolve_ForAdminDesktopOperator_ShouldReadEveryFamily()
    {
        using var env = new DesktopAuthenticationSessionTests.EnvironmentVariableScope()
            .Set("MDC_USERS", DesktopAuthenticationSessionTests.HashedDesktopAdminUsersJson())
            .Set("MDC_USERNAME", null)
            .Set("MDC_PASSWORD_HASH", null)
            .Set("MDC_AUTH_MODE", null);

        var session = DesktopAuthenticationSessionTests.CreateSession("Production");
        session.SignIn("desktop-admin", "pw").Succeeded.Should().BeTrue();

        var scope = DesktopWorkflowReadScopeResolver.Resolve(session);

        scope.Should().Be(WorkstationWorkflowReadScope.All);
    }

    [Fact]
    public void Resolve_ForUnauthenticatedProductionSession_ShouldWithholdEveryRecordBackedFamily()
    {
        using var env = new DesktopAuthenticationSessionTests.EnvironmentVariableScope()
            .Set("MDC_USERS", DesktopAuthenticationSessionTests.HashedDesktopAdminUsersJson())
            .Set("MDC_USERNAME", null)
            .Set("MDC_PASSWORD_HASH", null)
            .Set("MDC_AUTH_MODE", null);

        // Accounts are configured and nobody has signed in, so HasPermission fails closed. A shell
        // that composed the summary before sign-in must not show it as a fully privileged operator.
        var session = DesktopAuthenticationSessionTests.CreateSession("Production");

        var scope = DesktopWorkflowReadScopeResolver.Resolve(session);

        scope.Trading.Should().BeFalse();
        scope.Accounting.Should().BeFalse();
        scope.Strategy.Should().BeFalse();
        scope.Data.Should().BeFalse();
    }

    [Fact]
    public void Resolve_WithoutASession_ShouldReadEveryFamily()
    {
        // No session at all means the shell was composed without authentication -- the unconfigured
        // local-development posture that DesktopAuthenticationSession.HasPermission already lets
        // through, rather than a restricted operator whose grants were dropped.
        DesktopWorkflowReadScopeResolver.Resolve(null).Should().Be(WorkstationWorkflowReadScope.All);
    }
}
