using System.Net;
using System.Text.Json;
using FluentAssertions;
using Xunit;

namespace Meridian.Tests.Integration.EndpointTests;

/// <summary>
/// Integration tests for status, health, and monitoring endpoints.
/// Tests actual HTTP request/response cycles through the full middleware pipeline.
/// </summary>
[Trait("Category", "Integration")]
[Collection("Endpoint")]
public sealed class StatusEndpointTests
{
    private readonly HttpClient _client;

    public StatusEndpointTests(EndpointTestFixture fixture)
    {
        _client = fixture.Client;
    }

    #region Health Endpoints

    [Fact]
    public async Task Health_ReturnsJsonWithChecks()
    {
        var response = await _client.GetAsync("/health");

        response.StatusCode.Should().BeOneOf(HttpStatusCode.OK, HttpStatusCode.ServiceUnavailable);
        response.Content.Headers.ContentType!.MediaType.Should().Be("application/json");

        var json = await DeserializeAsync(response);
        json.Should().ContainKey("status");
        json.Should().ContainKey("checks");
    }

    [Fact]
    public async Task Healthz_ReturnsOk()
    {
        var response = await _client.GetAsync("/healthz");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Ready_ReturnsOkOr503()
    {
        var response = await _client.GetAsync("/ready");

        response.StatusCode.Should().BeOneOf(HttpStatusCode.OK, HttpStatusCode.ServiceUnavailable);
    }

    [Fact]
    public async Task Readyz_ReturnsOkOr503()
    {
        var response = await _client.GetAsync("/readyz");

        response.StatusCode.Should().BeOneOf(HttpStatusCode.OK, HttpStatusCode.ServiceUnavailable);
    }

    [Fact]
    public async Task Live_ReturnsOk()
    {
        var response = await _client.GetAsync("/live");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var content = await response.Content.ReadAsStringAsync();
        content.Should().Contain("alive");
    }

    [Fact]
    public async Task Livez_ReturnsOk()
    {
        var response = await _client.GetAsync("/livez");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    #endregion

    #region Metrics Endpoint

    [Fact]
    public async Task Metrics_ReturnsPrometheusFormat()
    {
        var response = await _client.GetAsync("/metrics");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.ContentType!.MediaType.Should().Be("text/plain");
    }

    #endregion

    #region Status Endpoint

    [Fact]
    public async Task Status_ReturnsJsonWithExpectedFields()
    {
        var response = await _client.GetAsync("/api/status");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.ContentType!.MediaType.Should().Be("application/json");

        var json = await DeserializeAsync(response);
        json.Should().ContainKey("uptime");
    }

    #endregion

    #region Errors Endpoint

    [Fact]
    public async Task Errors_ReturnsJsonArray()
    {
        var response = await _client.GetAsync("/api/errors");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.ContentType!.MediaType.Should().Be("application/json");
    }

    [Fact]
    public async Task Errors_AcceptsCountParameter()
    {
        var response = await _client.GetAsync("/api/errors?count=5");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Errors_AcceptsLevelFilter()
    {
        var response = await _client.GetAsync("/api/errors?level=error");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    #endregion

    #region Backpressure Endpoint

    [Fact]
    public async Task Backpressure_ReturnsJson()
    {
        var response = await _client.GetAsync("/api/backpressure");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.ContentType!.MediaType.Should().Be("application/json");
    }

    #endregion

    #region Provider Latency Endpoint

    [Fact]
    public async Task ProviderLatency_ReturnsJson()
    {
        var response = await _client.GetAsync("/api/providers/latency");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.ContentType!.MediaType.Should().Be("application/json");
    }

    #endregion

    #region Connections Endpoint

    [Fact]
    public async Task Connections_ReturnsJson()
    {
        var response = await _client.GetAsync("/api/connections");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.ContentType!.MediaType.Should().Be("application/json");
    }

    #endregion

    #region Dashboard Endpoint

    [Fact]
    public async Task Dashboard_ReturnsHtml()
    {
        var response = await _client.GetAsync("/");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.ContentType!.MediaType.Should().Be("text/html");
    }

    #endregion

    private static async Task<Dictionary<string, JsonElement>> DeserializeAsync(HttpResponseMessage response)
    {
        var content = await response.Content.ReadAsStringAsync();
        return JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(content)!;
    }
}
