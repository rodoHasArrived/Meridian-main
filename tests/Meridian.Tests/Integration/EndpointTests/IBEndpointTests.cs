using System.Net;
using System.Text.Json;
using FluentAssertions;
using Xunit;

namespace Meridian.Tests.Integration.EndpointTests;

/// <summary>
/// Integration tests for Interactive Brokers endpoints (/api/providers/ib/*).
/// </summary>
[Trait("Category", "Integration")]
[Collection("Endpoint")]
public sealed class IBEndpointTests
{
    private readonly HttpClient _client;

    public IBEndpointTests(EndpointTestFixture fixture)
    {
        _client = fixture.Client;
    }

    [Fact]
    public async Task IBStatus_ReturnsJson()
    {
        var response = await _client.GetAsync("/api/providers/ib/status");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.ContentType!.MediaType.Should().Be("application/json");

        await using var body = await response.Content.ReadAsStreamAsync();
        using var json = await JsonDocument.ParseAsync(body);

        json.RootElement.GetProperty("buildMode").GetString().Should().NotBeNullOrWhiteSpace();
        json.RootElement.GetProperty("runtimeTarget").GetString().Should().BeOneOf("paper", "live");
        json.RootElement.GetProperty("socket").GetProperty("configured").ValueKind.Should().BeOneOf(JsonValueKind.True, JsonValueKind.False);
        json.RootElement.GetProperty("clientPortal").GetProperty("enabled").ValueKind.Should().BeOneOf(JsonValueKind.True, JsonValueKind.False);
    }

    [Fact]
    public async Task IBErrorCodes_ReturnsJson()
    {
        var response = await _client.GetAsync("/api/providers/ib/error-codes");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.ContentType!.MediaType.Should().Be("application/json");
    }

    [Fact]
    public async Task IBLimits_ReturnsJson()
    {
        var response = await _client.GetAsync("/api/providers/ib/limits");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.ContentType!.MediaType.Should().Be("application/json");
    }
}
