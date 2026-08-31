using System.Security.Claims;
using FluentAssertions;
using Meridian.Contracts.Configuration;
using Meridian.Identity;
using Meridian.Identity.Auth;
using Meridian.Tests.Identity;
using Meridian.Ui.Shared.Endpoints;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Meridian.Tests.Integration.EndpointTests;

/// <summary>
/// The ASP.NET rate limiter partitions the direct-lending mutation policy by
/// <c>HttpContext.User.Identity?.Name</c>, but framework components never see the session items the
/// middleware stamps — so until the middleware also set <see cref="HttpContext.User"/>, every
/// authenticated session fell into the shared per-IP bucket and per-user rate limiting was dead.
/// These tests pin the contract: a validated login session stamps a minimal authenticated
/// principal, while the non-session postures (optional-mode anonymous, API-key) deliberately leave
/// <see cref="HttpContext.User"/> anonymous so nothing downstream mistakes them for a signed-in
/// operator.
/// </summary>
[Collection("IdentityEnvironment")]
public sealed class LoginSessionMiddlewarePrincipalTests
{
    [Fact]
    public async Task ValidatedSession_StampsAuthenticatedClaimsPrincipal()
    {
        using var env = ConfigureUsers(("operator", "pw-operator", UserRole.Accounting));
        var service = CreateService("Production");
        var token = service.CreateSession("operator", "pw-operator")!;

        var nextCalled = false;
        // The validated-session branch issues the CSRF cookie, which resolves the deployment
        // posture off the request, so the context needs a (possibly empty) service provider.
        var context = new DefaultHttpContext
        {
            RequestServices = new ServiceCollection().BuildServiceProvider()
        };
        context.Request.Path = "/api/workstation/session";
        context.Request.Headers.Cookie = $"{LoginSessionMiddleware.SessionCookieName}={token}";
        var middleware = new LoginSessionMiddleware(nextContext =>
        {
            nextCalled = true;
            nextContext.Items[LoginSessionMiddleware.CurrentUserKey].Should().Be("operator");
            nextContext.User.Identity.Should().NotBeNull();
            nextContext.User.Identity!.IsAuthenticated.Should().BeTrue(
                "a validated login session is an authenticated caller in the framework's terms too");
            nextContext.User.Identity.AuthenticationType
                .Should().Be(LoginSessionMiddleware.SessionAuthenticationType);
            nextContext.User.Identity.Name.Should().Be(
                "operator",
                "the rate limiter partitions the direct-lending policy by this exact value");
            nextContext.User.HasClaim(ClaimTypes.Role, nameof(UserRole.Accounting)).Should().BeTrue();
            return Task.CompletedTask;
        });

        await middleware.InvokeAsync(context, service);

        nextCalled.Should().BeTrue("a valid session cookie should reach the downstream handler");
    }

    [Fact]
    public async Task AnonymousOptionalModePrincipal_DoesNotStampUser()
    {
        using var env = new EnvironmentVariableScope()
            .Set("MDC_USERS", null)
            .Set("MDC_DEMO_USERS", null)
            .Set("MDC_USERNAME", null)
            .Set("MDC_PASSWORD_HASH", null)
            .Set("MDC_AUTH_MODE", null)
            .Set("MDC_PACKAGED_BUILD", null)
            .Set("MERIDIAN_CUSTOMER_BUILD", null)
            .Set("MDC_ANONYMOUS_ROLE", nameof(UserRole.Admin))
            .Set("MDC_ANONYMOUS_TENANT", null)
            .Set(DemoWorkspaceLayout.DemoModeEnvironmentVariable, null);
        var service = CreateService("Development");

        var nextCalled = false;
        var context = new DefaultHttpContext();
        context.Request.Path = "/api/workstation/session";
        var middleware = new LoginSessionMiddleware(nextContext =>
        {
            nextCalled = true;
            nextContext.Items[LoginSessionMiddleware.CurrentUserKey]
                .Should().Be(LoginSessionMiddleware.AnonymousLocalActor);
            (nextContext.User.Identity?.IsAuthenticated ?? false).Should().BeFalse(
                "an optional-mode anonymous caller is not a validated login session, so it must "
                + "not carry an authenticated framework principal");
            nextContext.User.Identity?.Name.Should().BeNull();
            return Task.CompletedTask;
        });

        await middleware.InvokeAsync(context, service);

        nextCalled.Should().BeTrue("optional mode should establish the configured anonymous principal");
    }

    [Fact]
    public async Task ApiKeyCandidate_PassesThroughWithoutStampingUser()
    {
        using var env = new EnvironmentVariableScope()
            .Set("MDC_USERS", null)
            .Set("MDC_DEMO_USERS", null)
            .Set("MDC_USERNAME", null)
            .Set("MDC_PASSWORD_HASH", null)
            .Set("MDC_AUTH_MODE", null)
            .Set("MDC_API_KEY", "principal-stamp-key");
        var service = CreateService("Production");

        var nextCalled = false;
        var context = new DefaultHttpContext();
        context.Request.Path = "/api/data/quotes-snapshot";
        context.Request.Headers["X-Api-Key"] = "principal-stamp-key";
        var middleware = new LoginSessionMiddleware(nextContext =>
        {
            nextCalled = true;
            nextContext.Items.Should().NotContainKey(
                LoginSessionMiddleware.CurrentUserKey,
                "the session middleware defers judgment on API-key candidates to ApiKeyMiddleware");
            (nextContext.User.Identity?.IsAuthenticated ?? false).Should().BeFalse(
                "an API-key caller is not a login session and must keep the anonymous framework principal");
            return Task.CompletedTask;
        });

        await middleware.InvokeAsync(context, service);

        nextCalled.Should().BeTrue("API-key candidates authenticate downstream, not here");
    }

    private static LoginSessionService CreateService(string environmentName)
        => new(new FakeHostEnvironment(environmentName), new UserProfileRegistry());

    private static EnvironmentVariableScope ConfigureUsers(
        params (string Username, string Password, UserRole Role)[] users)
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
