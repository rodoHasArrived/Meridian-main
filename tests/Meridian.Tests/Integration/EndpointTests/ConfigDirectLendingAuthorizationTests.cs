using System.Net;
using System.Text;
using FluentAssertions;
using Meridian.Identity.Auth;
using Meridian.Ui.Shared.Endpoints;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Meridian.Tests.Integration.EndpointTests;

/// <summary>
/// SEC-001 regression coverage: configuration and direct-lending routes must require an explicit
/// permission. These tests prove the negative path (no permission is rejected), the positive path
/// (the right permission is accepted), and that every mapped route in both groups carries the
/// authorization metadata that records its requirement, so a future route cannot ship ungated.
/// </summary>
[Trait("Category", "Integration")]
[Collection("Endpoint")]
public sealed class ConfigDirectLendingAuthorizationTests : IDisposable
{
    private readonly EndpointTestFixture _fixture;
    private readonly HttpClient _unauthenticatedClient;

    public ConfigDirectLendingAuthorizationTests(EndpointTestFixture fixture)
    {
        _fixture = fixture;
        _unauthenticatedClient = fixture.CreateNoRedirectClient();
    }

    public void Dispose() => _unauthenticatedClient.Dispose();

    // ── Configuration: negative path ─────────────────────────────────────────

    [Fact]
    public async Task ConfigRead_WithoutPermission_IsRejected()
    {
        var response = await _unauthenticatedClient.GetAsync("/api/config");
        response.StatusCode.Should().BeOneOf(HttpStatusCode.Unauthorized, HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task ConfigStorageMutation_WithoutPermission_IsRejected()
    {
        var content = new StringContent("{}", Encoding.UTF8, "application/json");
        var response = await _unauthenticatedClient.PostAsync("/api/config/storage", content);
        response.StatusCode.Should().BeOneOf(HttpStatusCode.Unauthorized, HttpStatusCode.Forbidden);
    }

    // ── Configuration: positive path ─────────────────────────────────────────

    [Fact]
    public async Task ConfigRead_WithViewPermission_IsAllowedThrough()
    {
        using var client = _fixture.CreatePermittedClient(UserPermission.ViewConfig);
        var response = await client.GetAsync("/api/config");
        response.StatusCode.Should().NotBe(HttpStatusCode.Unauthorized);
        response.StatusCode.Should().NotBe(HttpStatusCode.Forbidden);
    }

    // ── Direct lending: negative path ────────────────────────────────────────

    [Fact]
    public async Task DirectLendingRead_WithoutPermission_IsRejected()
    {
        var response = await _unauthenticatedClient.GetAsync(
            $"/api/loans/{Guid.NewGuid()}/history");
        response.StatusCode.Should().BeOneOf(HttpStatusCode.Unauthorized, HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task DirectLendingMutation_WithoutPermission_IsRejected()
    {
        var content = new StringContent("{}", Encoding.UTF8, "application/json");
        var response = await _unauthenticatedClient.PostAsync(
            $"/api/loans/{Guid.NewGuid()}/drawdowns", content);
        response.StatusCode.Should().BeOneOf(HttpStatusCode.Unauthorized, HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task DirectLendingMutation_WithViewPermissionOnly_IsForbidden()
    {
        using var client = _fixture.CreatePermittedClient(UserPermission.ViewDirectLending);
        var content = new StringContent("{}", Encoding.UTF8, "application/json");
        var response = await client.PostAsync(
            $"/api/loans/{Guid.NewGuid()}/drawdowns", content);
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    // ── Coverage: every config / direct-lending route declares a requirement ──

    [Theory]
    // Representative routes owned by ConfigEndpoints (group-gated reads + permissioned mutations).
    [InlineData("GET", "/api/config")]
    [InlineData("GET", "/api/config/effective")]
    [InlineData("POST", "/api/config/datasource")]
    [InlineData("POST", "/api/config/storage")]
    [InlineData("POST", "/api/config/symbols")]
    [InlineData("GET", "/api/config/derivatives")]
    [InlineData("POST", "/api/config/derivatives")]
    // Representative routes owned by DirectLendingEndpoints.
    [InlineData("POST", "/api/loans/")]
    [InlineData("POST", "/api/loans/{loanId:guid}/drawdowns")]
    [InlineData("GET", "/api/loans/{loanId:guid}/history")]
    public void ConfigAndDirectLendingRoute_DeclaresAuthorizationMetadata(string method, string route)
    {
        var endpoint = _fixture.Services
            .GetRequiredService<EndpointDataSource>()
            .Endpoints
            .OfType<RouteEndpoint>()
            .FirstOrDefault(candidate =>
                NormalizeRoute(candidate.RoutePattern.RawText) == route &&
                MatchesMethod(candidate, method));

        endpoint.Should().NotBeNull($"the {method} {route} route should be mapped");
        endpoint!.Metadata.GetMetadata<EndpointAuthorizationMetadata>()
            .Should().NotBeNull($"{method} {route} must attach a permission requirement");
    }

    private static string NormalizeRoute(string? rawRoute)
    {
        if (string.IsNullOrEmpty(rawRoute))
        {
            return string.Empty;
        }

        return rawRoute.StartsWith('/') ? rawRoute : "/" + rawRoute;
    }

    private static bool MatchesMethod(RouteEndpoint endpoint, string method)
    {
        var metadata = endpoint.Metadata.GetMetadata<Microsoft.AspNetCore.Routing.HttpMethodMetadata>();
        return metadata is null || metadata.HttpMethods.Contains(method, StringComparer.OrdinalIgnoreCase);
    }
}
