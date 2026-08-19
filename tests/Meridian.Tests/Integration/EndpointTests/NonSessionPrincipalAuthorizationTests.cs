using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Meridian.Contracts.Configuration;
using Meridian.Contracts.Workstation;
using Meridian.Identity;
using Meridian.Identity.Auth;
using Meridian.Ui.Shared.Endpoints;
using Meridian.Ui.Shared.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
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
            context.Items[LoginSessionMiddleware.CurrentUserKey] = LoginSessionMiddleware.AnonymousLocalActor;
            context.Items[LoginSessionMiddleware.AnonymousPrincipalKey] = true;
            context.Items[LoginSessionMiddleware.DemoLocalOperatorPrincipalKey] = true;
            context.Items[LoginSessionMiddleware.CurrentTenantIdKey] = "anon-tenant";
            context.Items[LoginSessionMiddleware.CurrentUserCompanyIdKey] = "anon-tenant";
            context.Items[LoginSessionMiddleware.CurrentUserRoleProfileNameKey] = "Anonymous profile";

            var middleware = new ApiKeyMiddleware(nextContext =>
            {
                nextCalled = true;
                nextContext.Items[LoginSessionMiddleware.CurrentUserKey].Should().Be(ApiKeyMiddleware.ApiKeyActor);
                nextContext.Items.Should().NotContainKey(LoginSessionMiddleware.CurrentTenantIdKey);
                nextContext.Items.Should().NotContainKey(LoginSessionMiddleware.CurrentUserCompanyIdKey);
                nextContext.Items.Should().NotContainKey(LoginSessionMiddleware.AnonymousPrincipalKey);
                nextContext.Items.Should().NotContainKey(LoginSessionMiddleware.DemoLocalOperatorPrincipalKey);
                nextContext.Items.Should().NotContainKey(LoginSessionMiddleware.CurrentUserRoleProfileNameKey);
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
    public async Task SessionPayload_WithheldFromAStrategylessCaller_LeaksNoPromotionStateViaTheWorkspace()
    {
        var originalRole = Environment.GetEnvironmentVariable("MDC_ANONYMOUS_ROLE");
        var originalTenant = Environment.GetEnvironmentVariable("MDC_ANONYMOUS_TENANT");
        // FundAccountant holds no strategy permission, so the run digest is withheld -- and the
        // workspace must not hand the same promotion state back one field over. MapWorkspace turns
        // LiveManaged into accounting and CandidateForLive into trading, which is exactly the
        // restricted state the rest of the payload is withholding.
        Environment.SetEnvironmentVariable("MDC_ANONYMOUS_ROLE", nameof(UserRole.FundAccountant));
        Environment.SetEnvironmentVariable("MDC_ANONYMOUS_TENANT", "workspace-leak-tenant");
        try
        {
            using var response = await Client.GetAsync("/api/workstation/session");
            response.StatusCode.Should().Be(HttpStatusCode.OK);

            var payload = await response.Content.ReadFromJsonAsync<WorkstationSessionPayload>(JsonOptions);

            payload!.LatestRun.Should().BeNull();
            payload.ActiveWorkspace.Should().Be(
                "strategy",
                "the redacted payload returns the same landing workspace a deployment with no run "
                + "service returns, so it discloses nothing about the latest run");
        }
        finally
        {
            Environment.SetEnvironmentVariable("MDC_ANONYMOUS_ROLE", originalRole);
            Environment.SetEnvironmentVariable("MDC_ANONYMOUS_TENANT", originalTenant);
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
            payload!.Role.Should().Be(
                nameof(UserRole.Compliance),
                "the masthead and role catalog match the caller's authority, not a strategy-run label");
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
        var originalTenant = Environment.GetEnvironmentVariable("MDC_ANONYMOUS_TENANT");
        var originalDemoMode = Environment.GetEnvironmentVariable(DemoWorkspaceLayout.DemoModeEnvironmentVariable);
        Environment.SetEnvironmentVariable("MDC_ANONYMOUS_ROLE", nameof(UserRole.Admin));
        Environment.SetEnvironmentVariable("MDC_ANONYMOUS_TENANT", null);
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
                nextContext.Items.Should().NotContainKey(LoginSessionMiddleware.DemoLocalOperatorPrincipalKey);
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
            Environment.SetEnvironmentVariable("MDC_ANONYMOUS_TENANT", originalTenant);
            Environment.SetEnvironmentVariable(DemoWorkspaceLayout.DemoModeEnvironmentVariable, originalDemoMode);
        }
    }

    [Fact]
    public async Task DemoAnonymousPrincipal_CarriesTheSeededTenantScope()
    {
        var originalRole = Environment.GetEnvironmentVariable("MDC_ANONYMOUS_ROLE");
        var originalTenant = Environment.GetEnvironmentVariable("MDC_ANONYMOUS_TENANT");
        var originalDemoMode = Environment.GetEnvironmentVariable(DemoWorkspaceLayout.DemoModeEnvironmentVariable);
        Environment.SetEnvironmentVariable("MDC_ANONYMOUS_ROLE", nameof(UserRole.Admin));
        Environment.SetEnvironmentVariable("MDC_ANONYMOUS_TENANT", null);
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
                nextContext.Items[LoginSessionMiddleware.DemoLocalOperatorPrincipalKey].Should().Be(true);
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
            Environment.SetEnvironmentVariable("MDC_ANONYMOUS_TENANT", originalTenant);
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

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task AnonymousRolePrincipal_CannotMutateSessionOwnedWorkflowPresets(bool demoMode)
    {
        var originalRole = Environment.GetEnvironmentVariable("MDC_ANONYMOUS_ROLE");
        var originalTenant = Environment.GetEnvironmentVariable("MDC_ANONYMOUS_TENANT");
        var originalDemoMode = Environment.GetEnvironmentVariable(DemoWorkspaceLayout.DemoModeEnvironmentVariable);
        Environment.SetEnvironmentVariable("MDC_ANONYMOUS_ROLE", nameof(UserRole.Admin));
        Environment.SetEnvironmentVariable("MDC_ANONYMOUS_TENANT", demoMode ? null : "session-write-test");
        Environment.SetEnvironmentVariable(
            DemoWorkspaceLayout.DemoModeEnvironmentVariable,
            demoMode ? "true" : null);
        try
        {
            var request = new WorkflowPresetSaveRequest(
                PresetId: null,
                Name: "Anonymous preset",
                Description: null,
                WorkflowId: "data-provider-recovery",
                ActionId: null,
                Tags: null,
                IsPinned: false);

            using var response = await Client.PostAsJsonAsync(
                "/api/workstation/workflows/presets",
                request);

            response.StatusCode.Should().Be(
                HttpStatusCode.Unauthorized,
                "an optional-auth principal is not a validated login session, including in demo mode");
        }
        finally
        {
            Environment.SetEnvironmentVariable("MDC_ANONYMOUS_ROLE", originalRole);
            Environment.SetEnvironmentVariable("MDC_ANONYMOUS_TENANT", originalTenant);
            Environment.SetEnvironmentVariable(DemoWorkspaceLayout.DemoModeEnvironmentVariable, originalDemoMode);
        }
    }

    [Fact]
    public async Task PermissionSnapshotWithoutAnActor_DoesNotSatisfySessionOnlyAuthorization()
    {
        using var client = Fixture.CreatePermittedClient(UserPermission.AdminMaintenance);
        using var response = await client.PostAsJsonAsync(
            "/api/workstation/first-run/outcomes/complete",
            new CompleteActivationOutcomeRequestDto("workspace-opened"));

        response.StatusCode.Should().Be(
            HttpStatusCode.Unauthorized,
            "permissions alone do not prove that a validated session principal exists");
    }

    [Fact]
    public async Task UnscopedNonDemoAnonymousRolePrincipal_CannotUseTheLocalReadException()
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        await using var app = builder.Build();
        app.Use(async (context, next) =>
        {
            context.Items[LoginSessionMiddleware.CurrentUserKey] = LoginSessionMiddleware.AnonymousLocalActor;
            context.Items[LoginSessionMiddleware.CurrentUserPermissionsKey] = RolePermissions.For(UserRole.Admin);
            context.Items[LoginSessionMiddleware.AnonymousPrincipalKey] = true;
            await next(context);
        });
        app.MapGet("/optional-read", static () => Results.Ok())
            .RequireAuthenticatedSessionOrScopedLocalOperatorRead();
        await app.StartAsync();

        using var response = await app.GetTestClient().GetAsync("/optional-read");

        response.StatusCode.Should().Be(
            HttpStatusCode.Unauthorized,
            "optional mode must name a tenant or use the seeded demo scope before receiving the read-only bootstrap exception");
    }

    [Fact]
    public async Task AnonymousReadOnlyRole_DoesNotRefuseAMutationCarryingAValidApiKey()
    {
        var originalAnonRole = Environment.GetEnvironmentVariable("MDC_ANONYMOUS_ROLE");
        var originalKey = Environment.GetEnvironmentVariable("MDC_API_KEY");
        var originalKeyRole = Environment.GetEnvironmentVariable("MDC_API_KEY_ROLE");
        Environment.SetEnvironmentVariable("MDC_ANONYMOUS_ROLE", nameof(UserRole.ReadOnly));
        Environment.SetEnvironmentVariable("MDC_API_KEY", "deferred-judgement-key");
        Environment.SetEnvironmentVariable("MDC_API_KEY_ROLE", nameof(UserRole.Admin));
        try
        {
            // LoginSessionMiddleware runs before ApiKeyMiddleware, so judging this request by the
            // anonymous role would settle it before the key's own role is ever read -- a read-only
            // anonymous posture would silently disable every API-key mutation in the deployment.
            using var request = new HttpRequestMessage(HttpMethod.Post, "/api/replay/start")
            {
                Content = new StringContent("{}", System.Text.Encoding.UTF8, "application/json")
            };
            request.Headers.Add("X-Api-Key", "deferred-judgement-key");

            using var response = await Client.SendAsync(request);

            response.StatusCode.Should().NotBe(
                HttpStatusCode.Forbidden,
                "the key carries Admin, and its own role is what decides a request presenting it");
        }
        finally
        {
            Environment.SetEnvironmentVariable("MDC_ANONYMOUS_ROLE", originalAnonRole);
            Environment.SetEnvironmentVariable("MDC_API_KEY", originalKey);
            Environment.SetEnvironmentVariable("MDC_API_KEY_ROLE", originalKeyRole);
        }
    }

    [Fact]
    public async Task AnonymousReadOnlyRole_DoesNotBlockTheInitialAccountBootstrap()
    {
        var originalAnonRole = Environment.GetEnvironmentVariable("MDC_ANONYMOUS_ROLE");
        Environment.SetEnvironmentVariable("MDC_ANONYMOUS_ROLE", nameof(UserRole.ReadOnly));
        try
        {
            // The bootstrap surface carries its own loopback and one-use token checks, which are
            // stronger than a role check. Refusing it on method alone would leave a fresh install
            // that named an anonymous role unable to ever create its first governed account.
            using var request = new HttpRequestMessage(HttpMethod.Post, "/api/auth/bootstrap")
            {
                Content = new StringContent("{}", System.Text.Encoding.UTF8, "application/json")
            };

            using var response = await Client.SendAsync(request);

            // The endpoint may still refuse on its own terms -- no bootstrap token is configured in
            // this fixture -- so the status alone cannot tell the two refusals apart. What must not
            // happen is the middleware answering first, which its message identifies.
            var body = await response.Content.ReadAsStringAsync();
            body.Should().NotContain(
                "allows only GET, HEAD, and OPTIONS",
                "the bootstrap request must reach its own loopback and one-use token checks rather "
                + "than be refused by the anonymous role's read-only rule");
        }
        finally
        {
            Environment.SetEnvironmentVariable("MDC_ANONYMOUS_ROLE", originalAnonRole);
        }
    }

    [Fact]
    public async Task AnonymousReadOnlyPrincipal_CannotReachAViewPermissionMutation()
    {
        var originalRole = Environment.GetEnvironmentVariable("MDC_ANONYMOUS_ROLE");
        Environment.SetEnvironmentVariable("MDC_ANONYMOUS_ROLE", nameof(UserRole.ReadOnly));
        try
        {
            // /api/replay/start declares ViewHistoricalData, which ReadOnly holds, so the route would
            // admit this caller and let an unauthenticated request drive replay session state. The
            // API-key principal has been refused this since the read-only rule landed; the anonymous
            // principal must be refused for the same reason and by the same shared rule.
            using var request = new HttpRequestMessage(HttpMethod.Post, "/api/replay/start")
            {
                Content = new StringContent("{}", System.Text.Encoding.UTF8, "application/json")
            };

            using var response = await Client.SendAsync(request);

            response.StatusCode.Should().Be(
                HttpStatusCode.Forbidden,
                "ReadOnly means read-only whichever posture established the principal");
        }
        finally
        {
            Environment.SetEnvironmentVariable("MDC_ANONYMOUS_ROLE", originalRole);
        }
    }

    [Fact]
    public async Task ExplicitReadOnlyApiKeyRole_CannotReachAViewPermissionMutation()
    {
        var originalKey = Environment.GetEnvironmentVariable("MDC_API_KEY");
        var originalRole = Environment.GetEnvironmentVariable("MDC_API_KEY_ROLE");
        Environment.SetEnvironmentVariable("MDC_API_KEY", "explicit-read-key");
        Environment.SetEnvironmentVariable("MDC_API_KEY_ROLE", nameof(UserRole.ReadOnly));
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, "/api/sampling/create");
            request.Headers.Add("X-Api-Key", "explicit-read-key");
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

    [Theory]
    [InlineData(nameof(UserRole.Analysis))]
    [InlineData(nameof(UserRole.Executive))]
    public async Task ReadOnlyApiKeyRoleHoldingExportData_ReachesAnExportRoute(string roleName)
    {
        var originalKey = Environment.GetEnvironmentVariable("MDC_API_KEY");
        var originalRole = Environment.GetEnvironmentVariable("MDC_API_KEY_ROLE");
        Environment.SetEnvironmentVariable("MDC_API_KEY", "export-grant-key");
        Environment.SetEnvironmentVariable("MDC_API_KEY_ROLE", roleName);
        try
        {
            // Analysis and Executive are read-only in the sense the method rule means -- no Manage,
            // Modify, Execute or Admin permission -- but both are granted ExportData outright, and
            // the export routes are POSTs because they take a request body rather than because they
            // mutate governed state. Refusing them by method alone would take away a capability the
            // configured role names, which is the one thing the rule was never meant to do.
            using var request = new HttpRequestMessage(HttpMethod.Post, "/api/export/analysis");
            request.Headers.Add("X-Api-Key", "export-grant-key");
            request.Content = JsonContent.Create(new { profileId = "default" });

            using var response = await Client.SendAsync(request);

            // The export service may still decline on its own terms in this fixture, so the status
            // cannot separate the two refusals. What must not happen is the middleware answering
            // first, and its message is what identifies it.
            var body = await response.Content.ReadAsStringAsync();
            body.Should().NotContain(
                "allows only GET, HEAD, and OPTIONS",
                $"{roleName} holds ExportData and this route declares it, so the endpoint's own "
                + "declaration must decide the request rather than the method rule");
        }
        finally
        {
            Environment.SetEnvironmentVariable("MDC_API_KEY", originalKey);
            Environment.SetEnvironmentVariable("MDC_API_KEY_ROLE", originalRole);
        }
    }

    [Theory]
    [InlineData(nameof(UserRole.Analysis))]
    [InlineData(nameof(UserRole.Executive))]
    public async Task ReadOnlyApiKeyRoleHoldingExportData_IsStillRefusedAViewPermissionMutation(string roleName)
    {
        var originalKey = Environment.GetEnvironmentVariable("MDC_API_KEY");
        var originalRole = Environment.GetEnvironmentVariable("MDC_API_KEY_ROLE");
        Environment.SetEnvironmentVariable("MDC_API_KEY", "export-grant-scope-key");
        Environment.SetEnvironmentVariable("MDC_API_KEY_ROLE", roleName);
        try
        {
            // The export exemption is keyed on the permission the endpoint declares, not on the
            // role, so it must not widen into the legacy view-grade mutations the rule exists for.
            // /api/replay/start declares ViewHistoricalData, which both roles hold.
            using var request = new HttpRequestMessage(HttpMethod.Post, "/api/replay/start")
            {
                Content = new StringContent("{}", System.Text.Encoding.UTF8, "application/json")
            };
            request.Headers.Add("X-Api-Key", "export-grant-scope-key");

            using var response = await Client.SendAsync(request);

            response.StatusCode.Should().Be(
                HttpStatusCode.Forbidden,
                $"{roleName} holds no ExportData claim on this route, so the method rule still applies");
        }
        finally
        {
            Environment.SetEnvironmentVariable("MDC_API_KEY", originalKey);
            Environment.SetEnvironmentVariable("MDC_API_KEY_ROLE", originalRole);
        }
    }

    [Fact]
    public async Task AnonymousReadOnlyRoleHoldingExportData_ReachesAnExportRoute()
    {
        var originalRole = Environment.GetEnvironmentVariable("MDC_ANONYMOUS_ROLE");
        Environment.SetEnvironmentVariable("MDC_ANONYMOUS_ROLE", nameof(UserRole.Analysis));
        try
        {
            // The two postures share one rule, so the exemption has to reach both. Pinned on the
            // anonymous side as well rather than assumed equivalent -- that assumption is exactly
            // how the two paths diverged the first time.
            using var request = new HttpRequestMessage(HttpMethod.Post, "/api/export/analysis");
            request.Content = JsonContent.Create(new { profileId = "default" });

            using var response = await Client.SendAsync(request);

            var body = await response.Content.ReadAsStringAsync();
            body.Should().NotContain(
                "allows only GET, HEAD, and OPTIONS",
                "the anonymous posture must honour the same export grant the key posture does");
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
