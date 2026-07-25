using FluentAssertions;
using Meridian.Identity;
using Meridian.Identity.Auth;
using System.Reflection;
using Xunit;

namespace Meridian.Tests.Identity;

/// <summary>
/// Operator scenario: the desktop and web shells trade credentials for an in-memory session token
/// and then re-validate that token on every request. These tests exercise the token lifecycle —
/// creation, validation, profile resolution, logout, and revocation — plus the environment-driven
/// authentication mode that governs anonymous access.
/// </summary>
[Collection("IdentityEnvironment")]
public sealed class LoginSessionServiceTests
{
    [Fact]
    public void CreateSession_WithValidCredentials_IssuesValidatableToken()
    {
        using var env = ConfigureUsers(("operator", "pw-operator", UserRole.Accounting));
        var service = CreateService("Production");

        var token = service.CreateSession("operator", "pw-operator");

        token.Should().NotBeNullOrWhiteSpace();
        service.ValidateSession(token!).Should().BeTrue();
        var profile = service.GetSessionProfile(token!);
        profile.Should().NotBeNull();
        profile!.Username.Should().Be("operator");
        profile.Role.Should().Be(UserRole.Accounting);
    }

    [Fact]
    public void CreateSession_WithInvalidCredentials_ReturnsNull()
    {
        using var env = ConfigureUsers(("operator", "pw-operator", UserRole.Accounting));
        var service = CreateService("Production");

        service.CreateSession("operator", "wrong").Should().BeNull();
        service.CreateSession("nobody", "pw-operator").Should().BeNull();
    }

    [Fact]
    public void ValidateSession_UnknownToken_ReturnsFalse()
    {
        using var env = ConfigureUsers(("operator", "pw-operator", UserRole.Accounting));
        var service = CreateService("Production");

        service.ValidateSession("deadbeef").Should().BeFalse();
        service.GetSessionProfile("deadbeef").Should().BeNull();
    }

    [Fact]
    public void RemoveSession_InvalidatesToken()
    {
        using var env = ConfigureUsers(("operator", "pw-operator", UserRole.Accounting));
        var service = CreateService("Production");
        var token = service.CreateSession("operator", "pw-operator")!;

        service.RemoveSession(token);

        service.ValidateSession(token).Should().BeFalse();
        service.GetSessionProfile(token).Should().BeNull();
    }

    [Fact]
    public void RevokeSessionsForUser_RemovesOnlyThatUsersSessions()
    {
        using var env = ConfigureUsers(
            ("alice", "pw-alice", UserRole.Accounting),
            ("bob", "pw-bob", UserRole.Analysis));
        var service = CreateService("Production");
        var aliceOne = service.CreateSession("alice", "pw-alice")!;
        var aliceTwo = service.CreateSession("alice", "pw-alice")!;
        var bob = service.CreateSession("bob", "pw-bob")!;

        var revoked = service.RevokeSessionsForUser("alice");

        revoked.Should().Be(2);
        service.ValidateSession(aliceOne).Should().BeFalse();
        service.ValidateSession(aliceTwo).Should().BeFalse();
        service.ValidateSession(bob).Should().BeTrue();
    }

    [Fact]
    public void RevokeAllSessions_ClearsEveryToken()
    {
        using var env = ConfigureUsers(("operator", "pw-operator", UserRole.Accounting));
        var service = CreateService("Production");
        var token = service.CreateSession("operator", "pw-operator")!;

        service.RevokeAllSessions().Should().Be(1);

        service.ValidateSession(token).Should().BeFalse();
    }

    [Fact]
    public void AuthenticationMode_ProductionUnconfigured_RequiresCredentials()
    {
        using var env = new EnvironmentVariableScope()
            .Set("MDC_USERS", null)
            .Set("MDC_USERNAME", null)
            .Set("MDC_PASSWORD_HASH", null)
            .Set("MDC_AUTH_MODE", null);
        var service = CreateService("Production");

        service.IsConfigured.Should().BeFalse();
        service.AllowAnonymousWhenUnconfigured.Should().BeFalse();
    }

    [Fact]
    public void AuthenticationMode_DevelopmentDefault_AllowsAnonymous()
    {
        using var env = new EnvironmentVariableScope()
            .Set("MDC_USERS", null)
            .Set("MDC_USERNAME", null)
            .Set("MDC_PASSWORD_HASH", null)
            .Set("MDC_AUTH_MODE", null)
            .Set("MDC_PACKAGED_BUILD", null)
            .Set("MERIDIAN_CUSTOMER_BUILD", null);
        var service = CreateService("Development");

        service.AllowAnonymousWhenUnconfigured.Should().BeTrue();
    }

    [Fact]
    public void AuthenticationMode_InvalidConfiguredValue_FailsClosed()
    {
        using var env = new EnvironmentVariableScope()
            .Set("MDC_AUTH_MODE", "sometimes");
        var service = CreateService("Production");

        var act = () => service.AllowAnonymousWhenUnconfigured;

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*MDC_AUTH_MODE*sometimes*");
    }

    [Fact]
    public void TryCreateSession_FifthFailureLocksClientWithDeterministicRetryWindow()
    {
        using var env = ConfigureUsers(("operator", "pw-operator", UserRole.Accounting));
        var service = CreateService("Production");

        for (var attempt = 1; attempt < 5; attempt++)
        {
            var result = service.TryCreateSession("operator", "wrong", "127.0.0.1");
            result.Status.Should().Be(LoginAttemptStatus.InvalidCredentials);
            result.RemainingAttempts.Should().Be(5 - attempt);
        }

        var locked = service.TryCreateSession("operator", "wrong", "127.0.0.1");

        locked.Status.Should().Be(LoginAttemptStatus.LockedOut);
        locked.RemainingAttempts.Should().Be(0);
        locked.RetryAfter.Should().BeGreaterThan(TimeSpan.FromMinutes(14));
        service.TryCreateSession("operator", "pw-operator", "127.0.0.1").Status
            .Should().Be(LoginAttemptStatus.LockedOut);
    }

    [Fact]
    public void TryCreateSession_DistinctInvalidUsernames_BoundsTrackedAttemptWindows()
    {
        using var env = new EnvironmentVariableScope()
            .Set("MDC_USERS", null)
            .Set("MDC_DEMO_USERS", null)
            .Set("MDC_USERNAME", null)
            .Set("MDC_PASSWORD_HASH", null)
            .Set("MDC_AUTH_MODE", null);
        var service = CreateService("Production");

        for (var index = 0; index < 1_100; index++)
        {
            service.TryCreateSession($"unknown-{index}", "wrong", "127.0.0.1")
                .Status.Should().Be(LoginAttemptStatus.InvalidCredentials);
        }

        GetTrackedFailedAttemptWindowCount(service).Should().BeLessThanOrEqualTo(1_024);
    }

    [Fact]
    public void DurableSessionStore_RestartAndRevocationPreserveAuthoritativeStateWithoutRawToken()
    {
        using var env = ConfigureUsers(("operator", "pw-operator", UserRole.Accounting));
        var root = Path.Combine(Path.GetTempPath(), $"meridian-session-{Guid.NewGuid():N}");
        var path = Path.Combine(root, "sessions.json");
        try
        {
            var first = new LoginSessionService(
                new FakeHostEnvironment("Production"),
                new UserProfileRegistry(),
                new LoginSessionStoreOptions(path));
            var token = first.CreateSession("operator", "pw-operator")!;

            File.ReadAllText(path).Should().NotContain(token);
            var restarted = new LoginSessionService(
                new FakeHostEnvironment("Production"),
                new UserProfileRegistry(),
                new LoginSessionStoreOptions(path));
            restarted.ValidateSession(token).Should().BeTrue();

            restarted.RevokeSessionsForUser("operator").Should().Be(1);
            var afterRevocation = new LoginSessionService(
                new FakeHostEnvironment("Production"),
                new UserProfileRegistry(),
                new LoginSessionStoreOptions(path));
            afterRevocation.ValidateSession(token).Should().BeFalse();
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    private static LoginSessionService CreateService(string environmentName)
        => new(new FakeHostEnvironment(environmentName), new UserProfileRegistry());

    private static int GetTrackedFailedAttemptWindowCount(LoginSessionService service)
    {
        var field = typeof(LoginSessionService).GetField("_failedAttempts", BindingFlags.Instance | BindingFlags.NonPublic);
        field.Should().NotBeNull();
        return (int)field!.GetValue(service)!.GetType().GetProperty("Count")!.GetValue(field.GetValue(service))!;
    }

    private static EnvironmentVariableScope ConfigureUsers(params (string Username, string Password, UserRole Role)[] users)
    {
        var entries = users.Select(user =>
            $$"""{"username":"{{user.Username}}","passwordHash":"{{PasswordHashing.HashPassword(user.Password)}}","role":"{{user.Role}}"}""");
        return new EnvironmentVariableScope()
            .Set("MDC_USERS", $"[{string.Join(",", entries)}]")
            .Set("MDC_DEMO_USERS", null)
            .Set("MDC_USERNAME", null)
            .Set("MDC_PASSWORD_HASH", null)
            .Set("MDC_AUTH_MODE", null);
    }
}
