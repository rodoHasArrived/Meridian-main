using System.Net;
using FluentAssertions;
using Meridian.Ui.Shared.Endpoints;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Hosting;

namespace Meridian.Tests.Ui;

public sealed class SecureReportingDistributionEndpointTests
{
    [Fact]
    public async Task RecipientLanding_ClearsFragmentAndPostsBearerOnlyInBody_WithStrictBrowserHeaders()
    {
        await using var app = await CreateAppAsync();
        using var client = app.GetTestClient();

        using var response = await client.GetAsync(
            "/portal/reporting/access-grants/grant-1/exchange");
        var html = await response.Content.ReadAsStringAsync();

        response.StatusCode.Should().Be(HttpStatusCode.OK, html);
        response.Headers.CacheControl!.NoStore.Should().BeTrue();
        response.Headers.GetValues("Referrer-Policy").Should().ContainSingle("no-referrer");
        response.Headers.GetValues("X-Content-Type-Options").Should().ContainSingle("nosniff");
        response.Headers.GetValues("X-Frame-Options").Should().ContainSingle("DENY");
        response.Headers.GetValues("Content-Security-Policy").Single()
            .Should().Contain("default-src 'none'")
            .And.Contain("connect-src 'self'")
            .And.Contain("frame-ancestors 'none'");
        html.Should().Contain("location.hash.slice(1)")
            .And.Contain("history.replaceState(null, document.title, location.pathname)")
            .And.Contain("JSON.stringify({ bearerToken, artifactId")
            .And.Contain("method: 'POST'")
            .And.Contain("credentials: 'omit'")
            .And.NotContain("grant-secret-value");
    }

    [Fact]
    public async Task RecipientLanding_RejectsQueryOrAuthorizationBearer_AndWorkerPumpHasNoPublicRoute()
    {
        await using var app = await CreateAppAsync();
        using var client = app.GetTestClient();

        using var query = await client.GetAsync(
            "/portal/reporting/access-grants/grant-1/exchange?token=grant-secret-value");
        var queryBody = await query.Content.ReadAsStringAsync();
        using var authorizationRequest = new HttpRequestMessage(
            HttpMethod.Get,
            "/portal/reporting/access-grants/grant-1/exchange");
        authorizationRequest.Headers.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", "grant-secret-value");
        using var authorization = await client.SendAsync(authorizationRequest);
        using var workerPump = await client.PostAsync(
            "/api/fund-structure/reporting/distribution/deliveries/process-due",
            content: null);

        query.StatusCode.Should().Be(HttpStatusCode.BadRequest, queryBody);
        authorization.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        workerPump.StatusCode.Should().BeOneOf(
            HttpStatusCode.NotFound,
            HttpStatusCode.MethodNotAllowed);
        ((IEndpointRouteBuilder)app).DataSources
            .SelectMany(static source => source.Endpoints)
            .OfType<RouteEndpoint>()
            .Select(static endpoint => endpoint.RoutePattern.RawText)
            .Should().NotContain(route =>
                route != null && route.Contains("process-due", StringComparison.Ordinal));
    }

    private static async Task<WebApplication> CreateAppAsync()
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            EnvironmentName = Environments.Development
        });
        builder.WebHost.UseTestServer();
        var app = builder.Build();
        app.MapSecureReportingDistributionEndpoints();
        await app.StartAsync();
        return app;
    }
}
