using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Xunit;

namespace Meridian.Tests.Integration.EndpointTests;

/// <summary>
/// Base class for HTTP endpoint integration tests.
/// Uses EndpointTestFixture for in-process testing without real network calls.
/// Implements improvement B2/#7 from the structural improvements analysis.
/// </summary>
[Collection("Endpoint")]
public abstract class EndpointIntegrationTestBase
{
    protected readonly HttpClient Client;
    protected readonly EndpointTestFixture Fixture;

    protected static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };

    protected EndpointIntegrationTestBase(EndpointTestFixture fixture)
    {
        Fixture = fixture;
        Client = fixture.Client;
    }

    protected async Task<HttpResponseMessage> GetAsync(string url, CancellationToken ct = default)
    {
        return await Client.GetAsync(url, ct);
    }

    protected async Task<T?> GetJsonAsync<T>(string url, CancellationToken ct = default)
    {
        var response = await Client.GetAsync(url, ct);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<T>(JsonOptions, ct);
    }

    protected async Task AssertEndpointReturnsOk(string url)
    {
        var response = await GetAsync(url);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    protected async Task AssertEndpointReturnsJson(string url)
    {
        var response = await GetAsync(url);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var contentType = response.Content.Headers.ContentType?.ToString() ?? string.Empty;
        Assert.Contains("application/json", contentType);
    }

    protected async Task AssertEndpointReturnsNotImplemented(string url)
    {
        var response = await GetAsync(url);
        Assert.Equal(HttpStatusCode.NotImplemented, response.StatusCode);
    }
}
