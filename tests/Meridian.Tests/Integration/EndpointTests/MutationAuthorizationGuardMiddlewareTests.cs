using System.Net;
using System.Text;
using FluentAssertions;
using Meridian.Identity;
using Meridian.Identity.Auth;
using Meridian.Ui.Shared.Endpoints;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Meridian.Tests.Integration.EndpointTests;

/// <summary>
/// Proves the pre-binding contract of <see cref="MutationAuthorizationGuardMiddleware"/>: a
/// mutating route's declared authorization is decided before argument binding can answer with a
/// validation 400 or trip handler service resolution, an undeclared mutating route is refused
/// fail-closed, and the explicit permissionless declarations are the only ways past the guard
/// without a permission.
/// </summary>
[Trait("Category", "Integration")]
public sealed class MutationAuthorizationGuardMiddlewareTests
{
    private const string PermissionsHeader = "X-Guard-Test-Permissions";
    private const string ActorHeader = "X-Guard-Test-Actor";

    [Fact]
    public async Task DeclaredRoute_PermissionlessCaller_IsUnauthorizedBeforeBindingCanAnswer400()
    {
        await using var app = await CreateHostAsync(host =>
            host.MapPost("/guarded", (List<int> body) => Results.Ok(body.Count))
                .RequirePermission(UserPermission.ModifySecurityMaster));
        using var client = app.GetTestClient();

        // "{}" cannot bind to List<int>, so a binding-first pipeline would answer 400 and leak
        // the validation outcome to a caller that was never authorized to learn it.
        using var response = await client.PostAsync("/guarded", JsonBody("{}"));

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task DeclaredRoute_WrongPermission_IsForbiddenBeforeBinding()
    {
        await using var app = await CreateHostAsync(host =>
            host.MapPost("/guarded", (List<int> body) => Results.Ok(body.Count))
                .RequirePermission(UserPermission.ModifySecurityMaster));
        using var client = app.GetTestClient();

        using var request = new HttpRequestMessage(HttpMethod.Post, "/guarded")
        {
            Content = JsonBody("{}")
        };
        request.Headers.Add(PermissionsHeader, nameof(UserPermission.ViewSecurityMaster));

        using var response = await client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task DeclaredRoute_HeldPermission_MalformedBody_StillReachesBindingAnd400s()
    {
        await using var app = await CreateHostAsync(host =>
            host.MapPost("/guarded", (List<int> body) => Results.Ok(body.Count))
                .RequirePermission(UserPermission.ModifySecurityMaster));
        using var client = app.GetTestClient();

        using var request = new HttpRequestMessage(HttpMethod.Post, "/guarded")
        {
            Content = JsonBody("{}")
        };
        request.Headers.Add(PermissionsHeader, nameof(UserPermission.ModifySecurityMaster));

        using var response = await client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task DeclaredRoute_HeldPermission_ValidBody_ReachesHandler()
    {
        await using var app = await CreateHostAsync(host =>
            host.MapPost("/guarded", (List<int> body) => Results.Ok(body.Count))
                .RequirePermission(UserPermission.ModifySecurityMaster));
        using var client = app.GetTestClient();

        using var request = new HttpRequestMessage(HttpMethod.Post, "/guarded")
        {
            Content = JsonBody("[1, 2, 3]")
        };
        request.Headers.Add(PermissionsHeader, nameof(UserPermission.ModifySecurityMaster));

        using var response = await client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await response.Content.ReadAsStringAsync()).Should().Be("3");
    }

    [Fact]
    public async Task UndeclaredMutatingRoute_IsRefusedFailClosed_EvenForAnAdminSnapshot()
    {
        await using var app = await CreateHostAsync(host =>
            host.MapPost("/undeclared", () => Results.Ok("mutated")));
        using var client = app.GetTestClient();

        using var request = new HttpRequestMessage(HttpMethod.Post, "/undeclared")
        {
            Content = JsonBody("{}")
        };
        request.Headers.Add(PermissionsHeader, nameof(UserPermission.AdminMaintenance));

        using var response = await client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task PostToGetOnlyRoute_KeepsIts405_InsteadOfBeingRefusedAsUndeclared()
    {
        await using var app = await CreateHostAsync(host =>
            host.MapGet("/read", () => Results.Ok("read")));
        using var client = app.GetTestClient();

        using var response = await client.PostAsync("/read", JsonBody("{}"));

        response.StatusCode.Should().Be(HttpStatusCode.MethodNotAllowed);
    }

    [Fact]
    public async Task UndeclaredReadRoute_IsOutOfScope()
    {
        await using var app = await CreateHostAsync(host =>
            host.MapGet("/read", () => Results.Ok("read")));
        using var client = app.GetTestClient();

        using var response = await client.GetAsync("/read");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task DeclaredPermissionlessMutation_PassesWithoutAnyPermission()
    {
        await using var app = await CreateHostAsync(host =>
            host.MapPost("/tombstone", () => Results.Ok("gone"))
                .DeclarePermissionlessMutation("Test tombstone that performs no action."));
        using var client = app.GetTestClient();

        using var response = await client.PostAsync("/tombstone", JsonBody("{}"));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task IndependentlyAuthenticatedMutation_IsJudgedByItsOwnCheck_NotByTheGuard()
    {
        await using var app = await CreateHostAsync(host =>
            host.MapPost("/webhook", (HttpContext context) =>
                    context.Request.Headers.ContainsKey("X-Webhook-Signature")
                        ? Results.Accepted()
                        : ApiProblemDetails.Unauthorized(context))
                .DeclareIndependentAuthentication("Test webhook authenticated by its signature header."));
        using var client = app.GetTestClient();

        using var unsigned = await client.PostAsync("/webhook", JsonBody("{}"));
        unsigned.StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        using var request = new HttpRequestMessage(HttpMethod.Post, "/webhook")
        {
            Content = JsonBody("{}")
        };
        request.Headers.Add("X-Webhook-Signature", "signed");
        using var signed = await client.SendAsync(request);
        signed.StatusCode.Should().Be(HttpStatusCode.Accepted);
    }

    [Fact]
    public async Task RequireAllDeclaration_RequiresEveryDeclaredPermission()
    {
        // Declaration-only on purpose: no endpoint filter is attached, so a pass here proves the
        // middleware enforces the metadata itself rather than riding on a filter's decision.
        await using var app = await CreateHostAsync(host =>
            host.MapPost("/require-all", () => Results.Ok("mutated"))
                .WithMetadata(new EndpointAuthorizationMetadata(
                    new[] { UserPermission.ManageProviders, UserPermission.AdminMaintenance },
                    requireAll: true)));
        using var client = app.GetTestClient();

        using var partial = new HttpRequestMessage(HttpMethod.Post, "/require-all")
        {
            Content = JsonBody("{}")
        };
        partial.Headers.Add(PermissionsHeader, nameof(UserPermission.ManageProviders));
        using var partialResponse = await client.SendAsync(partial);
        partialResponse.StatusCode.Should().Be(HttpStatusCode.Forbidden);

        using var complete = new HttpRequestMessage(HttpMethod.Post, "/require-all")
        {
            Content = JsonBody("{}")
        };
        complete.Headers.Add(
            PermissionsHeader,
            $"{nameof(UserPermission.ManageProviders)},{nameof(UserPermission.AdminMaintenance)}");
        using var completeResponse = await client.SendAsync(complete);
        completeResponse.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task EmptyPermissionDeclaration_IsDeferredToItsOwnSessionFilter()
    {
        await using var app = await CreateHostAsync(host =>
            host.MapPost("/session-owned", () => Results.Ok("mutated"))
                .RequireAuthenticatedSession());
        using var client = app.GetTestClient();

        // A permission snapshot with no actor satisfies the guard's snapshot check but must still
        // be refused by the session filter the empty declaration defers to.
        using var snapshotOnly = new HttpRequestMessage(HttpMethod.Post, "/session-owned")
        {
            Content = JsonBody("{}")
        };
        snapshotOnly.Headers.Add(PermissionsHeader, nameof(UserPermission.AdminMaintenance));
        using var snapshotResponse = await client.SendAsync(snapshotOnly);
        snapshotResponse.StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        using var session = new HttpRequestMessage(HttpMethod.Post, "/session-owned")
        {
            Content = JsonBody("{}")
        };
        session.Headers.Add(PermissionsHeader, nameof(UserPermission.AdminMaintenance));
        session.Headers.Add(ActorHeader, "session-operator");
        using var sessionResponse = await client.SendAsync(session);
        sessionResponse.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    private static StringContent JsonBody(string json)
        => new(json, Encoding.UTF8, "application/json");

    private static async Task<WebApplication> CreateHostAsync(Action<WebApplication> mapEndpoints)
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddLogging();

        var app = builder.Build();
        app.Use(async (context, next) =>
        {
            if (context.Request.Headers.TryGetValue(PermissionsHeader, out var rawPermissions))
            {
                RolePermissions.TryParsePermissionNames(
                    rawPermissions.ToString().Split(
                        ',',
                        StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries),
                    out var permissions,
                    out _);
                if (permissions != UserPermission.None)
                {
                    context.Items[LoginSessionMiddleware.CurrentUserPermissionsKey] = permissions;
                }
            }

            if (context.Request.Headers.TryGetValue(ActorHeader, out var actor) &&
                !string.IsNullOrWhiteSpace(actor.ToString()))
            {
                context.Items[LoginSessionMiddleware.CurrentUserKey] = actor.ToString();
            }

            await next(context);
        });
        app.UseMutationAuthorizationGuard();
        mapEndpoints(app);

        await app.StartAsync();
        return app;
    }
}
