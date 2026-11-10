using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Meridian.Identity.Auth;
using Xunit;

namespace Meridian.Tests.Integration.EndpointTests;

/// <summary>
/// Input bounds and disclosure guards on the data sampling endpoints: a zero or oversized sample
/// request, a zero margin of error, an invented population when no data exists, an unbounded
/// in-memory sample cache, and the storage root echoed back in responses.
/// </summary>
[Trait("Category", "Integration")]
public sealed class SamplingEndpointTests : IDisposable, IClassFixture<EndpointTestFixture>
{
    private const string MissingSymbol = "NO_SUCH_SAMPLING_SYMBOL";
    private readonly EndpointTestFixture _fixture;
    private readonly HttpClient _client;

    public SamplingEndpointTests(EndpointTestFixture fixture)
    {
        _fixture = fixture;
        _client = fixture.CreatePermittedClient(UserPermission.ViewHistoricalData);
    }

    public void Dispose() => _client.Dispose();

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    [InlineData(100_001)]
    public async Task CreateSample_SizeOutOfRange_ReturnsBadRequest(int sampleSize)
    {
        // Zero used to divide by zero in systematic sampling and surface as a 500.
        using var response = await _client.PostAsJsonAsync(
            "/api/sampling/create", new { Symbol = MissingSymbol, Strategy = "systematic", SampleSize = sampleSize });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task CreateSample_NoData_DoesNotEchoStorageRoot()
    {
        using var response = await _client.PostAsJsonAsync(
            "/api/sampling/create", new { Symbol = MissingSymbol, SampleSize = 10 });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadAsStringAsync();
        body.Should().Contain("no_data");
        body.Should().NotContain(Path.GetFileName(Path.GetDirectoryName(_fixture.DataRoot))!,
            "the server's storage layout must not be disclosed");
    }

    [Theory]
    [InlineData(0.95, 0.0)]
    [InlineData(0.95, 1.5)]
    [InlineData(1.0, 0.05)]
    public async Task EstimateSample_InvalidFractions_ReturnsBadRequest(double confidence, double margin)
    {
        // A zero margin of error used to divide by zero in Cochran's formula.
        using var response = await _client.GetAsync(
            $"/api/sampling/estimate?symbol={MissingSymbol}&confidence={confidence}&marginOfError={margin}");

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task EstimateSample_NoData_ReportsNoPopulationInsteadOfInventingOne()
    {
        using var response = await _client.GetAsync($"/api/sampling/estimate?symbol={MissingSymbol}");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        doc.RootElement.GetProperty("estimatedPopulation").GetInt64().Should().Be(0,
            "a population of 100,000 used to be invented when no data existed");
        doc.RootElement.GetProperty("recommendedSampleSize").GetInt32().Should().Be(0);
        doc.RootElement.GetProperty("status").GetString().Should().Be("no_data");
    }

    [Fact]
    public async Task CreateSample_Repeatedly_SavedSamplesStayBounded()
    {
        for (var i = 0; i < 60; i++)
        {
            using var response = await _client.PostAsJsonAsync(
                "/api/sampling/create", new { Symbol = MissingSymbol, SampleSize = 1 });
            response.StatusCode.Should().Be(HttpStatusCode.OK);
        }

        using var saved = await _client.GetAsync("/api/sampling/saved");
        using var doc = JsonDocument.Parse(await saved.Content.ReadAsStringAsync());
        doc.RootElement.GetProperty("total").GetInt32().Should().BeLessThanOrEqualTo(50,
            "the in-memory sample cache evicts the oldest entries beyond its cap");
    }
}
