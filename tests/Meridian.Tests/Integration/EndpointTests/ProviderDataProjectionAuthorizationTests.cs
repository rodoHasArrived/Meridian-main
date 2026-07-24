using System.Net;
using FluentAssertions;
using Meridian.Identity.Auth;
using Meridian.Ui.Shared.Endpoints;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Meridian.Tests.Integration.EndpointTests;

[Trait("Category", "Integration")]
[Collection("Endpoint")]
public sealed class ProviderDataProjectionAuthorizationTests : IDisposable
{
    private const string Route = "/api/providers/data-projection";
    private readonly EndpointTestFixture _fixture;
    private readonly HttpClient _unauthenticatedClient;

    public ProviderDataProjectionAuthorizationTests(EndpointTestFixture fixture)
    {
        _fixture = fixture;
        _unauthenticatedClient = fixture.CreateNoRedirectClient();
    }

    public void Dispose() => _unauthenticatedClient.Dispose();

    [Fact]
    public async Task ProviderDataProjection_WithoutViewTradesPermission_IsRejected()
    {
        var response = await _unauthenticatedClient.GetAsync(Route);

        response.StatusCode.Should().BeOneOf(HttpStatusCode.Unauthorized, HttpStatusCode.Forbidden);
    }

    [Fact]
    public void ProviderDataProjection_RequiresTenantScopeAndViewTradesPermission()
    {
        var endpoint = _fixture.Services
            .GetRequiredService<EndpointDataSource>()
            .Endpoints
            .OfType<RouteEndpoint>()
            .Single(candidate =>
                NormalizeRoute(candidate.RoutePattern.RawText) == Route &&
                candidate.Metadata.GetMetadata<HttpMethodMetadata>()!.HttpMethods.Contains("GET"));

        endpoint.Metadata.GetMetadata<WorkstationTenantScopeMetadata>().Should().NotBeNull();
        var authorization = endpoint.Metadata.GetMetadata<EndpointAuthorizationMetadata>();
        authorization.Should().NotBeNull();
        authorization!.Permissions.Should().ContainSingle().Which.Should().Be(UserPermission.ViewTrades);
    }

    private static string NormalizeRoute(string? rawRoute) =>
        string.IsNullOrEmpty(rawRoute) || rawRoute.StartsWith('/') ? rawRoute ?? string.Empty : "/" + rawRoute;
}
