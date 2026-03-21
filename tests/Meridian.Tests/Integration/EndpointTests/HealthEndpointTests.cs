using System.Net;
using System.Net.Http.Json;
using Xunit;

namespace Meridian.Tests.Integration.EndpointTests;

/// <summary>
/// Integration tests for health and status endpoints.
/// Verifies that core health probes respond correctly.
/// Part of B2/#7 endpoint integration test suite.
/// </summary>
[Trait("Category", "Integration")]
[Collection("Endpoint")]
public sealed class HealthEndpointTests : EndpointIntegrationTestBase
{
    public HealthEndpointTests(EndpointTestFixture fixture) : base(fixture)
    {
    }

    [Theory]
    [InlineData("/healthz")]
    [InlineData("/livez")]
    public async Task HealthProbe_ReturnsOk(string endpoint)
    {
        var response = await GetAsync(endpoint);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task ReadinessProbe_ReturnsExpectedStatusCode()
    {
        var response = await GetAsync("/readyz");
        // Should be either 200 (ready) or 503 (not ready) — both are valid
        Assert.True(
            response.StatusCode == HttpStatusCode.OK ||
            response.StatusCode == HttpStatusCode.ServiceUnavailable,
            $"Expected 200 or 503 but got {(int)response.StatusCode}");
    }

    [Fact]
    public async Task HealthEndpoint_ReturnsJson()
    {
        var response = await GetAsync("/health");
        // Health endpoint should return JSON regardless of health status
        var contentType = response.Content.Headers.ContentType?.ToString() ?? "";
        Assert.Contains("json", contentType, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task MetricsEndpoint_ReturnsPrometheusFormat()
    {
        var response = await GetAsync("/metrics");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var contentType = response.Content.Headers.ContentType?.ToString() ?? "";
        Assert.Contains("text/plain", contentType);
    }

    [Fact]
    public async Task StatusEndpoint_ReturnsJson()
    {
        await AssertEndpointReturnsJson("/api/status");
    }

    [Fact]
    public async Task ErrorsEndpoint_ReturnsJson()
    {
        await AssertEndpointReturnsJson("/api/errors");
    }

    [Fact]
    public async Task BackpressureEndpoint_ReturnsJson()
    {
        await AssertEndpointReturnsJson("/api/backpressure");
    }
}
