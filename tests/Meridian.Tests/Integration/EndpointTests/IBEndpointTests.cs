using System.Net;
using System.Text.Json;
using FluentAssertions;
using Meridian.Identity.Auth;
using Xunit;

namespace Meridian.Tests.Integration.EndpointTests;

/// <summary>
/// Integration tests for Interactive Brokers endpoints (/api/providers/ib/*).
/// </summary>
[Trait("Category", "Integration")]
[Collection("Endpoint")]
public sealed class IBEndpointTests : IDisposable, IClassFixture<EndpointTestFixture>
{
    private readonly HttpClient _client;
    // /api/providers/ib/status reports this deployment's IB configuration and readiness, so it
    // carries a permission. The error-code and limit references below are vendor constants and
    // are declared open, which is why they stay on the plain client.
    private readonly HttpClient _providerReadClient;

    public IBEndpointTests(EndpointTestFixture fixture)
    {
        _client = fixture.CreateNoRedirectClient();
        _providerReadClient = fixture.CreatePermittedClient(UserPermission.ViewDiagnostics);
    }

    public void Dispose()
    {
        _client.Dispose();
        _providerReadClient.Dispose();
    }

    [Fact]
    public async Task IBStatus_ReturnsJson()
    {
        var response = await _providerReadClient.GetAsync("/api/providers/ib/status");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.ContentType!.MediaType.Should().Be("application/json");

        await using var body = await response.Content.ReadAsStreamAsync();
        using var json = await JsonDocument.ParseAsync(body);

        json.RootElement.GetProperty("provider").GetString().Should().Be("Interactive Brokers");
        json.RootElement.GetProperty("buildMode").GetString().Should().BeOneOf("guidance", "smoke", "vendor");
        json.RootElement.GetProperty("ibApiAvailable").ValueKind.Should().BeOneOf(JsonValueKind.True, JsonValueKind.False);
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
