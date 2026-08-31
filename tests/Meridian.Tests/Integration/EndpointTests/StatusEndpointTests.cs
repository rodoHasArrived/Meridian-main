using System.Net;
using System.Text.Json;
using FluentAssertions;
using Meridian.Identity.Auth;
using Xunit;

namespace Meridian.Tests.Integration.EndpointTests;

/// <summary>
/// Integration tests for status, health, and monitoring endpoints.
/// Tests actual HTTP request/response cycles through the full middleware pipeline.
/// </summary>
[Trait("Category", "Integration")]
[Collection("Endpoint")]
public sealed class StatusEndpointTests : IClassFixture<EndpointTestFixture>
{
    private readonly HttpClient _client;
    // W9-GOV-008: /api/providers/latency is a platform read and now requires a permission.
    private readonly HttpClient _providerReadClient;
    // Configuration authority without diagnostics authority, for the error-buffer boundary below.
    private readonly HttpClient _configOnlyClient;

    public StatusEndpointTests(EndpointTestFixture fixture)
    {
        _client = fixture.Client;
        _providerReadClient = fixture.CreateSessionClient(UserPermission.ViewDiagnostics);
        _configOnlyClient = fixture.CreateSessionClient(UserPermission.ViewConfig);
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
        var response = await _providerReadClient.GetAsync("/api/status");

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
        var response = await _providerReadClient.GetAsync("/api/errors");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.ContentType!.MediaType.Should().Be("application/json");
    }

    [Fact]
    public async Task Errors_AcceptsCountParameter()
    {
        var response = await _providerReadClient.GetAsync("/api/errors?count=5");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Errors_AcceptsLevelFilter()
    {
        var response = await _providerReadClient.GetAsync("/api/errors?level=error");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    #endregion

    #region Backpressure Endpoint

    [Fact]
    public async Task Backpressure_ReturnsJson()
    {
        var response = await _providerReadClient.GetAsync("/api/backpressure");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.ContentType!.MediaType.Should().Be("application/json");
    }

    #endregion

    #region Provider Latency Endpoint

    [Fact]
    public async Task ProviderLatency_ReturnsJson()
    {
        var response = await _providerReadClient.GetAsync("/api/providers/latency");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.ContentType!.MediaType.Should().Be("application/json");
    }

    #endregion

    #region Connections Endpoint

    [Fact]
    public async Task Connections_ReturnsJson()
    {
        var response = await _providerReadClient.GetAsync("/api/connections");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.ContentType!.MediaType.Should().Be("application/json");
    }

    #endregion

    #region Event Stream Endpoint

    [Fact]
    public async Task ErrorBufferReads_AreRefusedToConfigurationOnlyOperators()
    {
        // ViewConfig reads platform configuration; ErrorEntryDto carries exception type, context and
        // message. The stream republishes GetErrors, so narrowing the route alone would leave the same
        // detail reachable through it -- the check has to cover both doors.
        var errors = await _configOnlyClient.GetAsync("/api/errors");
        errors.StatusCode.Should().Be(HttpStatusCode.Forbidden);

        using var streamRequest = new HttpRequestMessage(HttpMethod.Get, "/api/events/stream");
        using var stream = await _configOnlyClient.SendAsync(streamRequest, HttpCompletionOption.ResponseHeadersRead);
        stream.StatusCode.Should().Be(HttpStatusCode.Forbidden);

        // The configuration-bearing reads in the same family stay available to that operator.
        var status = await _configOnlyClient.GetAsync("/api/status");
        status.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task EventsStream_ReturnsSsePayloadWithStatusData()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/events/stream");
        using var response = await _providerReadClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cts.Token);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.ContentType!.MediaType.Should().Be("text/event-stream");

        await using var stream = await response.Content.ReadAsStreamAsync(cts.Token);
        using var reader = new StreamReader(stream);
        var dataLine = await ReadFirstDataLineAsync(reader, cts.Token);

        dataLine.Should().StartWith("data: ");

        using var document = JsonDocument.Parse(dataLine["data: ".Length..]);
        document.RootElement.TryGetProperty("status", out _).Should().BeTrue();
        document.RootElement.TryGetProperty("backpressure", out _).Should().BeTrue();
        document.RootElement.TryGetProperty("providerLatency", out _).Should().BeTrue();
        document.RootElement.TryGetProperty("recentErrors", out _).Should().BeTrue();
    }

    #endregion

    #region Root Endpoint

    [Fact]
    public async Task Root_ReturnsNotFound()
    {
        var response = await _client.GetAsync("/");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    #endregion

    private static async Task<Dictionary<string, JsonElement>> DeserializeAsync(HttpResponseMessage response)
    {
        var content = await response.Content.ReadAsStringAsync();
        return JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(content)!;
    }

    private static async Task<string> ReadFirstDataLineAsync(StreamReader reader, CancellationToken ct)
    {
        while (true)
        {
            ct.ThrowIfCancellationRequested();

            var line = await reader.ReadLineAsync(ct);
            if (line is null)
            {
                break;
            }

            if (line.StartsWith("data: ", StringComparison.Ordinal))
            {
                return line;
            }
        }

        throw new InvalidOperationException("Did not receive a server-sent event data payload.");
    }
}
