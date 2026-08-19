using System.Net;
using FluentAssertions;
using Meridian.Identity;
using Meridian.Identity.Auth;
using Meridian.Ui.Shared.Endpoints;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Meridian.Tests.Integration.EndpointTests;

/// <summary>
/// The governed surface resolves permissions from the session items, so any authentication posture
/// that authenticates a caller without populating them refuses every declared route. Two such
/// postures exist beside the browser session -- the out-of-band API key and optional mode, where a
/// deployment has no accounts at all -- and both silently lost access to roughly two hundred routes
/// as this wave declared them, because the only coverage either had pointed at routes that were
/// still undeclared.
/// <para>
/// These tests pair each posture with a route that genuinely carries a permission, so the class of
/// regression is caught at the first declaration rather than the two-hundredth.
/// </para>
/// </summary>
[Trait("Category", "Integration")]
[Collection("Endpoint")]
public sealed class NonSessionPrincipalAuthorizationTests : EndpointIntegrationTestBase
{
    // Declared ViewMarketData when the live-data family was gated; the quote stream's polling
    // fallback, and so exactly the kind of route an out-of-band script calls.
    private const string DeclaredRoute = "/api/data/quotes-snapshot";

    public NonSessionPrincipalAuthorizationTests(EndpointTestFixture fixture)
        : base(fixture)
    {
    }

    [Fact]
    public async Task ValidApiKey_ReachesADeclaredRoute()
    {
        var original = Environment.GetEnvironmentVariable("MDC_API_KEY");
        var originalRole = Environment.GetEnvironmentVariable("MDC_API_KEY_ROLE");
        Environment.SetEnvironmentVariable("MDC_API_KEY", "declared-route-key");
        Environment.SetEnvironmentVariable("MDC_API_KEY_ROLE", nameof(UserRole.TradeDesk));
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, DeclaredRoute);
            request.Headers.Add("X-Api-Key", "declared-route-key");

            using var response = await Client.SendAsync(request);

            response.StatusCode.Should().NotBe(
                HttpStatusCode.Unauthorized,
                "a validated key carries its configured role, so the permission filter must resolve");
            response.StatusCode.Should().NotBe(
                HttpStatusCode.Forbidden,
                "TradeDesk holds ViewMarketData, which is what this route declares");
        }
        finally
        {
            Environment.SetEnvironmentVariable("MDC_API_KEY", original);
            Environment.SetEnvironmentVariable("MDC_API_KEY_ROLE", originalRole);
        }
    }

    [Fact]
    public async Task ApiKeyWithANarrowerRole_IsRefusedByThePermissionRatherThanTheKey()
    {
        var original = Environment.GetEnvironmentVariable("MDC_API_KEY");
        var originalRole = Environment.GetEnvironmentVariable("MDC_API_KEY_ROLE");
        Environment.SetEnvironmentVariable("MDC_API_KEY", "narrow-role-key");
        // Accounting holds no ViewMarketData, so the key authenticates but the route still refuses.
        Environment.SetEnvironmentVariable("MDC_API_KEY_ROLE", nameof(UserRole.Accounting));
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, DeclaredRoute);
            request.Headers.Add("X-Api-Key", "narrow-role-key");

            using var response = await Client.SendAsync(request);

            response.StatusCode.Should().Be(
                HttpStatusCode.Forbidden,
                "the key is valid, so this is an authorization refusal and not an authentication one -- "
                + "which is what makes MDC_API_KEY_ROLE a real control rather than a formality");
        }
        finally
        {
            Environment.SetEnvironmentVariable("MDC_API_KEY", original);
            Environment.SetEnvironmentVariable("MDC_API_KEY_ROLE", originalRole);
        }
    }

    [Theory]
    [InlineData("0")]
    [InlineData("3")]
    public async Task ApiKeyWithANumericRole_FailsClosedRatherThanResolvingByOrdinal(string numericRole)
    {
        var original = Environment.GetEnvironmentVariable("MDC_API_KEY");
        var originalRole = Environment.GetEnvironmentVariable("MDC_API_KEY_ROLE");
        Environment.SetEnvironmentVariable("MDC_API_KEY", "numeric-role-key");
        Environment.SetEnvironmentVariable("MDC_API_KEY_ROLE", numericRole);
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, DeclaredRoute);
            request.Headers.Add("X-Api-Key", "numeric-role-key");

            using var response = await Client.SendAsync(request);

            response.StatusCode.Should().Be(
                HttpStatusCode.ServiceUnavailable,
                "Admin is the zero value of UserRole, so ordinal parsing would silently promote \"0\" to "
                + "full administrator -- only role names may resolve");
        }
        finally
        {
            Environment.SetEnvironmentVariable("MDC_API_KEY", original);
            Environment.SetEnvironmentVariable("MDC_API_KEY_ROLE", originalRole);
        }
    }

    [Fact]
    public async Task ApiKeyPrincipal_CarriesThePermissionSnapshotHandlersReadDirectly()
    {
        var original = Environment.GetEnvironmentVariable("MDC_API_KEY");
        var originalRole = Environment.GetEnvironmentVariable("MDC_API_KEY_ROLE");
        Environment.SetEnvironmentVariable("MDC_API_KEY", "snapshot-key");
        Environment.SetEnvironmentVariable("MDC_API_KEY_ROLE", nameof(UserRole.Admin));
        try
        {
            var nextCalled = false;
            var context = new DefaultHttpContext();
            context.Request.Path = DeclaredRoute;
            context.Request.Headers["X-Api-Key"] = "snapshot-key";
            var middleware = new ApiKeyMiddleware(nextContext =>
            {
                nextCalled = true;
                nextContext.Items[LoginSessionMiddleware.CurrentUserKey].Should().Be(ApiKeyMiddleware.ApiKeyActor);
                nextContext.Items[LoginSessionMiddleware.CurrentUserRoleKey].Should().Be(UserRole.Admin);
                nextContext.Items[LoginSessionMiddleware.CurrentUserPermissionsKey]
                    .Should().Be(RolePermissions.For(UserRole.Admin));
                return Task.CompletedTask;
            });

            await middleware.InvokeAsync(context);

            nextCalled.Should().BeTrue("a valid key should reach the downstream handler");
        }
        finally
        {
            Environment.SetEnvironmentVariable("MDC_API_KEY", original);
            Environment.SetEnvironmentVariable("MDC_API_KEY_ROLE", originalRole);
        }
    }

    [Fact]
    public async Task AnonymousRolePrincipal_CarriesThePermissionSnapshotHandlersReadDirectly()
    {
        var originalRole = Environment.GetEnvironmentVariable("MDC_ANONYMOUS_ROLE");
        Environment.SetEnvironmentVariable("MDC_ANONYMOUS_ROLE", nameof(UserRole.Admin));
        try
        {
            var nextCalled = false;
            var context = new DefaultHttpContext();
            context.Request.Path = DeclaredRoute;
            var middleware = new LoginSessionMiddleware(nextContext =>
            {
                nextCalled = true;
                nextContext.Items[LoginSessionMiddleware.CurrentUserKey]
                    .Should().Be(LoginSessionMiddleware.AnonymousLocalActor);
                nextContext.Items[LoginSessionMiddleware.CurrentUserRoleKey].Should().Be(UserRole.Admin);
                nextContext.Items[LoginSessionMiddleware.CurrentUserPermissionsKey]
                    .Should().Be(RolePermissions.For(UserRole.Admin));
                nextContext.Items[LoginSessionMiddleware.AnonymousPrincipalKey].Should().Be(true);
                return Task.CompletedTask;
            });

            await middleware.InvokeAsync(
                context,
                Fixture.Services.GetRequiredService<LoginSessionService>());

            nextCalled.Should().BeTrue("optional mode should establish the explicitly configured anonymous principal");
        }
        finally
        {
            Environment.SetEnvironmentVariable("MDC_ANONYMOUS_ROLE", originalRole);
        }
    }

    [Fact]
    public async Task AnonymousRolePrincipal_DoesNotBypassConfiguredApiKey()
    {
        var originalKey = Environment.GetEnvironmentVariable("MDC_API_KEY");
        var originalRole = Environment.GetEnvironmentVariable("MDC_ANONYMOUS_ROLE");
        Environment.SetEnvironmentVariable("MDC_API_KEY", "still-required-key");
        Environment.SetEnvironmentVariable("MDC_ANONYMOUS_ROLE", nameof(UserRole.Admin));
        try
        {
            using var response = await Client.GetAsync(DeclaredRoute);

            response.StatusCode.Should().Be(
                HttpStatusCode.Unauthorized,
                "an optional-mode principal is not a validated login session and must not suppress API-key enforcement");
        }
        finally
        {
            Environment.SetEnvironmentVariable("MDC_API_KEY", originalKey);
            Environment.SetEnvironmentVariable("MDC_ANONYMOUS_ROLE", originalRole);
        }
    }

    [Fact]
    public async Task ApiKeyWithAnUnknownRole_FailsClosed()
    {
        var original = Environment.GetEnvironmentVariable("MDC_API_KEY");
        var originalRole = Environment.GetEnvironmentVariable("MDC_API_KEY_ROLE");
        Environment.SetEnvironmentVariable("MDC_API_KEY", "bad-role-key");
        Environment.SetEnvironmentVariable("MDC_API_KEY_ROLE", "NotARole");
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, DeclaredRoute);
            request.Headers.Add("X-Api-Key", "bad-role-key");

            using var response = await Client.SendAsync(request);

            response.StatusCode.Should().Be(
                HttpStatusCode.ServiceUnavailable,
                "a misconfigured role must surface rather than quietly applying a different permission set");
        }
        finally
        {
            Environment.SetEnvironmentVariable("MDC_API_KEY", original);
            Environment.SetEnvironmentVariable("MDC_API_KEY_ROLE", originalRole);
        }
    }
}
