using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Meridian.Contracts.Configuration;
using Meridian.Contracts.Workstation;
using Meridian.Identity;
using Meridian.Identity.Auth;
using Meridian.Ui.Shared.Endpoints;
using Meridian.Ui.Shared.Services;
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
            var context = new DefaultHttpContext
            {
                RequestServices = Fixture.Services
            };
            context.Request.Path = DeclaredRoute;
            context.Request.Headers["X-Api-Key"] = "snapshot-key";
            var middleware = new ApiKeyMiddleware(nextContext =>
            {
                nextCalled = true;
                nextContext.Items[LoginSessionMiddleware.CurrentUserKey].Should().Be(ApiKeyMiddleware.ApiKeyActor);
                nextContext.Items[LoginSessionMiddleware.CurrentUserRoleKey].Should().Be(UserRole.Admin);
                nextContext.Items[LoginSessionMiddleware.CurrentUserPermissionsKey]
                    .Should().Be(RolePermissions.For(UserRole.Admin));
                nextContext.Items[ApiKeyMiddleware.ApiKeyPrincipalKey].Should().Be(true);
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
    public async Task ApiKeyPrincipal_DoesNotInheritTheAnonymousTenantScope()
    {
        var originalKey = Environment.GetEnvironmentVariable("MDC_API_KEY");
        var originalKeyRole = Environment.GetEnvironmentVariable("MDC_API_KEY_ROLE");
        var originalAnonRole = Environment.GetEnvironmentVariable("MDC_ANONYMOUS_ROLE");
        var originalAnonTenant = Environment.GetEnvironmentVariable("MDC_ANONYMOUS_TENANT");
        Environment.SetEnvironmentVariable("MDC_API_KEY", "scope-inherit-key");
        Environment.SetEnvironmentVariable("MDC_API_KEY_ROLE", nameof(UserRole.Admin));
        Environment.SetEnvironmentVariable("MDC_ANONYMOUS_ROLE", nameof(UserRole.Admin));
        Environment.SetEnvironmentVariable("MDC_ANONYMOUS_TENANT", "anon-tenant");
        try
        {
            var nextCalled = false;
            var context = new DefaultHttpContext { RequestServices = Fixture.Services };
            context.Request.Path = DeclaredRoute;
            context.Request.Headers["X-Api-Key"] = "scope-inherit-key";
            // The login middleware runs first and, in this configuration, hands the request the
            // anonymous operator's tenant. Promoting it to a key principal must not leave that scope
            // behind: authority comes from one posture, and a key was granted no tenant at all.
            context.Items[LoginSessionMiddleware.CurrentUserKey] = LoginSessionMiddleware.AnonymousLocalActor;
            context.Items[LoginSessionMiddleware.AnonymousPrincipalKey] = true;
            context.Items[LoginSessionMiddleware.CurrentTenantIdKey] = "anon-tenant";
            context.Items[LoginSessionMiddleware.CurrentUserCompanyIdKey] = "anon-tenant";

            var middleware = new ApiKeyMiddleware(nextContext =>
            {
                nextCalled = true;
                nextContext.Items[LoginSessionMiddleware.CurrentUserKey].Should().Be(ApiKeyMiddleware.ApiKeyActor);
                nextContext.Items.Should().NotContainKey(LoginSessionMiddleware.CurrentTenantIdKey);
                nextContext.Items.Should().NotContainKey(LoginSessionMiddleware.CurrentUserCompanyIdKey);
                nextContext.Items.Should().NotContainKey(LoginSessionMiddleware.AnonymousPrincipalKey);
                return Task.CompletedTask;
            });

            await middleware.InvokeAsync(context);

            nextCalled.Should().BeTrue();
        }
        finally
        {
            Environment.SetEnvironmentVariable("MDC_API_KEY", originalKey);
            Environment.SetEnvironmentVariable("MDC_API_KEY_ROLE", originalKeyRole);
            Environment.SetEnvironmentVariable("MDC_ANONYMOUS_ROLE", originalAnonRole);
            Environment.SetEnvironmentVariable("MDC_ANONYMOUS_TENANT", originalAnonTenant);
        }
    }

    [Fact]
    public async Task SessionPayload_ReportsTheCallersOwnRoleNotTheLatestRunsPosture()
    {
        var originalRole = Environment.GetEnvironmentVariable("MDC_ANONYMOUS_ROLE");
        var originalTenant = Environment.GetEnvironmentVariable("MDC_ANONYMOUS_TENANT");
        Environment.SetEnvironmentVariable("MDC_ANONYMOUS_ROLE", nameof(UserRole.Compliance));
        Environment.SetEnvironmentVariable("MDC_ANONYMOUS_TENANT", "role-label-tenant");
        try
        {
            using var response = await Client.GetAsync("/api/workstation/session");
            response.StatusCode.Should().Be(HttpStatusCode.OK);

            var payload = await response.Content.ReadFromJsonAsync<WorkstationSessionPayload>(JsonOptions);

            // The browser matches this against the role catalog to name the active authority profile,
            // and the run-derived labels ("Strategy Lead", "Live Operations") are not role names, so
            // that match failed for every operator rather than only the redacted ones.
            payload!.Role.Should().Be(nameof(UserRole.Compliance));
        }
        finally
        {
            Environment.SetEnvironmentVariable("MDC_ANONYMOUS_ROLE", originalRole);
            Environment.SetEnvironmentVariable("MDC_ANONYMOUS_TENANT", originalTenant);
        }
    }

    [Fact]
    public async Task NonDemoAnonymousPrincipal_CarriesTheTenantTheDeploymentNames()
    {
        var originalRole = Environment.GetEnvironmentVariable("MDC_ANONYMOUS_ROLE");
        var originalTenant = Environment.GetEnvironmentVariable("MDC_ANONYMOUS_TENANT");
        var originalDemoMode = Environment.GetEnvironmentVariable(DemoWorkspaceLayout.DemoModeEnvironmentVariable);
        Environment.SetEnvironmentVariable("MDC_ANONYMOUS_ROLE", nameof(UserRole.Admin));
        Environment.SetEnvironmentVariable("MDC_ANONYMOUS_TENANT", "acme-local");
        // Explicitly not the demo host: optional mode is also the plain local-development posture, and
        // such a deployment must be able to open the tenant-scoped workstation on its own book rather
        // than being offered the seeded demo tenant or nothing at all.
        Environment.SetEnvironmentVariable(DemoWorkspaceLayout.DemoModeEnvironmentVariable, null);
        try
        {
            var nextCalled = false;
            var context = new DefaultHttpContext();
            context.Request.Path = "/api/workstation/session";
            var middleware = new LoginSessionMiddleware(nextContext =>
            {
                nextCalled = true;
                nextContext.Items[LoginSessionMiddleware.CurrentUserCompanyIdKey].Should().Be("acme-local");
                nextContext.Items[LoginSessionMiddleware.CurrentTenantIdKey].Should().Be("acme-local");
                return Task.CompletedTask;
            });

            await middleware.InvokeAsync(
                context,
                Fixture.Services.GetRequiredService<LoginSessionService>());

            nextCalled.Should().BeTrue();
        }
        finally
        {
            Environment.SetEnvironmentVariable("MDC_ANONYMOUS_ROLE", originalRole);
            Environment.SetEnvironmentVariable("MDC_ANONYMOUS_TENANT", originalTenant);
            Environment.SetEnvironmentVariable(DemoWorkspaceLayout.DemoModeEnvironmentVariable, originalDemoMode);
        }
    }

    [Fact]
    public async Task UnscopedAnonymousPrincipal_CarriesNoTenantAtAll()
    {
        var originalRole = Environment.GetEnvironmentVariable("MDC_ANONYMOUS_ROLE");
        var originalTenant = Environment.GetEnvironmentVariable("MDC_ANONYMOUS_TENANT");
        var originalDemoMode = Environment.GetEnvironmentVariable(DemoWorkspaceLayout.DemoModeEnvironmentVariable);
        Environment.SetEnvironmentVariable("MDC_ANONYMOUS_ROLE", nameof(UserRole.Admin));
        Environment.SetEnvironmentVariable("MDC_ANONYMOUS_TENANT", null);
        Environment.SetEnvironmentVariable(DemoWorkspaceLayout.DemoModeEnvironmentVariable, null);
        try
        {
            var context = new DefaultHttpContext();
            context.Request.Path = "/api/workstation/session";
            var middleware = new LoginSessionMiddleware(nextContext =>
            {
                // Naming a role does not invent a book to work on: the tenant-scoped workstation group
                // keeps refusing this caller rather than reading whichever tenant's records are on disk.
                nextContext.Items.Should().NotContainKey(LoginSessionMiddleware.CurrentTenantIdKey);
                nextContext.Items.Should().NotContainKey(LoginSessionMiddleware.CurrentUserCompanyIdKey);
                return Task.CompletedTask;
            });

            await middleware.InvokeAsync(
                context,
                Fixture.Services.GetRequiredService<LoginSessionService>());
        }
        finally
        {
            Environment.SetEnvironmentVariable("MDC_ANONYMOUS_ROLE", originalRole);
            Environment.SetEnvironmentVariable("MDC_ANONYMOUS_TENANT", originalTenant);
            Environment.SetEnvironmentVariable(DemoWorkspaceLayout.DemoModeEnvironmentVariable, originalDemoMode);
        }
    }

    [Theory]
    [InlineData("POST")]
    [InlineData("PUT")]
    [InlineData("PATCH")]
    [InlineData("DELETE")]
    [InlineData("PROPFIND")]
    public async Task DefaultApiKeyRole_RejectsEveryMethodOutsideTheSafeAllowlist(string method)
    {
        var originalKey = Environment.GetEnvironmentVariable("MDC_API_KEY");
        var originalRole = Environment.GetEnvironmentVariable("MDC_API_KEY_ROLE");
        Environment.SetEnvironmentVariable("MDC_API_KEY", "default-read-key");
        Environment.SetEnvironmentVariable("MDC_API_KEY_ROLE", null);
        try
        {
            var nextCalled = false;
            var context = new DefaultHttpContext
            {
                RequestServices = Fixture.Services
            };
            context.Request.Path = "/api/sampling/create";
            context.Request.Method = method;
            context.Request.Headers["X-Api-Key"] = "default-read-key";
            var middleware = new ApiKeyMiddleware(_ =>
            {
                nextCalled = true;
                return Task.CompletedTask;
            });

            await middleware.InvokeAsync(context);

            context.Response.StatusCode.Should().Be(StatusCodes.Status403Forbidden);
            nextCalled.Should().BeFalse();
        }
        finally
        {
            Environment.SetEnvironmentVariable("MDC_API_KEY", originalKey);
            Environment.SetEnvironmentVariable("MDC_API_KEY_ROLE", originalRole);
        }
    }

    [Fact]
    public async Task DefaultApiKeyRole_AllowsGetWithReadOnlyPermissionSnapshot()
    {
        var originalKey = Environment.GetEnvironmentVariable("MDC_API_KEY");
        var originalRole = Environment.GetEnvironmentVariable("MDC_API_KEY_ROLE");
        Environment.SetEnvironmentVariable("MDC_API_KEY", "default-read-key");
        Environment.SetEnvironmentVariable("MDC_API_KEY_ROLE", null);
        try
        {
            var nextCalled = false;
            var context = new DefaultHttpContext();
            context.Request.Path = DeclaredRoute;
            context.Request.Method = HttpMethods.Get;
            context.Request.Headers["X-Api-Key"] = "default-read-key";
            var middleware = new ApiKeyMiddleware(nextContext =>
            {
                nextCalled = true;
                nextContext.Items[LoginSessionMiddleware.CurrentUserRoleKey].Should().Be(UserRole.ReadOnly);
                nextContext.Items[LoginSessionMiddleware.CurrentUserPermissionsKey]
                    .Should().Be(RolePermissions.For(UserRole.ReadOnly));
                return Task.CompletedTask;
            });

            await middleware.InvokeAsync(context);

            nextCalled.Should().BeTrue();
        }
        finally
        {
            Environment.SetEnvironmentVariable("MDC_API_KEY", originalKey);
            Environment.SetEnvironmentVariable("MDC_API_KEY_ROLE", originalRole);
        }
    }

    [Fact]
    public async Task DefaultApiKeyRole_CannotReachAViewPermissionMutation()
    {
        var originalKey = Environment.GetEnvironmentVariable("MDC_API_KEY");
        var originalRole = Environment.GetEnvironmentVariable("MDC_API_KEY_ROLE");
        Environment.SetEnvironmentVariable("MDC_API_KEY", "default-read-key");
        Environment.SetEnvironmentVariable("MDC_API_KEY_ROLE", null);
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, "/api/sampling/create");
            request.Headers.Add("X-Api-Key", "default-read-key");
            request.Content = JsonContent.Create(new { strategy = "random" });

            using var response = await Client.SendAsync(request);

            response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        }
        finally
        {
            Environment.SetEnvironmentVariable("MDC_API_KEY", originalKey);
            Environment.SetEnvironmentVariable("MDC_API_KEY_ROLE", originalRole);
        }
    }

    [Fact]
    public async Task AnonymousRolePrincipal_CarriesThePermissionSnapshotHandlersReadDirectly()
    {
        var originalRole = Environment.GetEnvironmentVariable("MDC_ANONYMOUS_ROLE");
        var originalDemoMode = Environment.GetEnvironmentVariable(DemoWorkspaceLayout.DemoModeEnvironmentVariable);
        Environment.SetEnvironmentVariable("MDC_ANONYMOUS_ROLE", nameof(UserRole.Admin));
        Environment.SetEnvironmentVariable(DemoWorkspaceLayout.DemoModeEnvironmentVariable, null);
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
                nextContext.Items.Should().NotContainKey(LoginSessionMiddleware.CurrentUserCompanyIdKey);
                nextContext.Items.Should().NotContainKey(LoginSessionMiddleware.CurrentTenantIdKey);
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
            Environment.SetEnvironmentVariable(DemoWorkspaceLayout.DemoModeEnvironmentVariable, originalDemoMode);
        }
    }

    [Fact]
    public async Task DemoAnonymousPrincipal_CarriesTheSeededTenantScope()
    {
        var originalRole = Environment.GetEnvironmentVariable("MDC_ANONYMOUS_ROLE");
        var originalDemoMode = Environment.GetEnvironmentVariable(DemoWorkspaceLayout.DemoModeEnvironmentVariable);
        Environment.SetEnvironmentVariable("MDC_ANONYMOUS_ROLE", nameof(UserRole.Admin));
        Environment.SetEnvironmentVariable(DemoWorkspaceLayout.DemoModeEnvironmentVariable, "true");
        try
        {
            var nextCalled = false;
            var context = new DefaultHttpContext();
            context.Request.Path = "/api/workstation/session";
            var middleware = new LoginSessionMiddleware(nextContext =>
            {
                nextCalled = true;
                nextContext.Items[LoginSessionMiddleware.CurrentUserCompanyIdKey]
                    .Should().Be(DemoTenantBlueprint.CompanyId);
                nextContext.Items[LoginSessionMiddleware.CurrentTenantIdKey]
                    .Should().Be(DemoTenantBlueprint.TenantId);
                return Task.CompletedTask;
            });

            await middleware.InvokeAsync(
                context,
                Fixture.Services.GetRequiredService<LoginSessionService>());

            nextCalled.Should().BeTrue("the configured demo principal should reach the workstation pipeline");
        }
        finally
        {
            Environment.SetEnvironmentVariable("MDC_ANONYMOUS_ROLE", originalRole);
            Environment.SetEnvironmentVariable(DemoWorkspaceLayout.DemoModeEnvironmentVariable, originalDemoMode);
        }
    }

    [Fact]
    public async Task ApiKeyPrincipal_DoesNotSatisfySessionOnlyAuthorization()
    {
        var originalKey = Environment.GetEnvironmentVariable("MDC_API_KEY");
        var originalRole = Environment.GetEnvironmentVariable("MDC_API_KEY_ROLE");
        Environment.SetEnvironmentVariable("MDC_API_KEY", "session-only-key");
        Environment.SetEnvironmentVariable("MDC_API_KEY_ROLE", nameof(UserRole.Admin));
        try
        {
            using var request = new HttpRequestMessage(
                HttpMethod.Post,
                "/api/workstation/first-run/outcomes/complete");
            request.Headers.Add("X-Api-Key", "session-only-key");
            request.Content = JsonContent.Create(new CompleteActivationOutcomeRequestDto("workspace-opened"));

            using var response = await Client.SendAsync(request);

            response.StatusCode.Should().Be(
                HttpStatusCode.Unauthorized,
                "a validated API key is not a browser login session and must not mutate session-owned state");
        }
        finally
        {
            Environment.SetEnvironmentVariable("MDC_API_KEY", originalKey);
            Environment.SetEnvironmentVariable("MDC_API_KEY_ROLE", originalRole);
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
            using var request = new HttpRequestMessage(HttpMethod.Post, DeclaredRoute);
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
