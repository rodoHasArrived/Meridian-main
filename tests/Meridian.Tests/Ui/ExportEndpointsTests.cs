using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using FluentAssertions;
using Meridian.Contracts.Api;
using Meridian.Contracts.Export;
using Meridian.Ui.Shared.Endpoints;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Hosting;

namespace Meridian.Tests.Ui;

public sealed class ExportEndpointsTests
{
    [Fact]
    public async Task MapExportEndpoints_Preview_ShouldReturnReadOnlyProfileScope()
    {
        await using var app = await CreateAppAsync();
        var client = app.GetTestClient();

        using var preview = await ReadJsonAsync(
            client,
            "/api/export/preview?profile=audit-pack&symbols=SPY,QQQ&eventTypes=Trade&sampleSize=999");

        preview.RootElement.GetProperty("previewOnly").GetBoolean().Should().BeTrue();
        preview.RootElement.GetProperty("profileId").GetString().Should().Be("audit-pack");
        preview.RootElement.GetProperty("symbols").EnumerateArray()
            .Select(symbol => symbol.GetString())
            .Should()
            .Equal("SPY", "QQQ");
        preview.RootElement.GetProperty("eventTypes")[0].GetString().Should().Be("Trade");
        preview.RootElement.GetProperty("sampleSize").GetInt32().Should().Be(500);
        preview.RootElement.GetProperty("canRunExport").GetBoolean().Should().BeFalse();
    }

    [Fact]
    public async Task MapExportEndpoints_AnalysisWithoutService_ShouldReturnUnavailable()
    {
        await using var app = await CreateAppAsync();
        var client = app.GetTestClient();

        var response = await client.PostAsJsonAsync("/api/export/analysis", new
        {
            profileId = " audit-pack ",
            symbols = new[] { " SPY ", "", "spy", "QQQ" }
        });

        response.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
        var payload = await response.Content.ReadFromJsonAsync<ExportAnalysisApiResponse>(JsonOptions);
        payload.Should().NotBeNull();
        payload!.Success.Should().BeFalse();
        payload.Status.Should().Be("unavailable");
        payload.ProfileId.Should().Be("audit-pack");
        payload.Error.Should().Be("Export service not available");
        payload.Files.Should().BeEmpty();
        payload.Warnings.Should().BeEmpty();
    }

    [Fact]
    public async Task MapExportEndpoints_StrategyPackageRoute_ShouldRetainResearchCompatibilityAlias()
    {
        await using var app = await CreateAppAsync();
        var client = app.GetTestClient();

        var strategyResponse = await client.PostAsJsonAsync(UiApiRoutes.ExportStrategyPackage, new
        {
            symbols = new[] { "SPY" },
            includeMetadata = true
        });
        var researchResponse = await client.PostAsJsonAsync(UiApiRoutes.ExportResearchPackage, new
        {
            symbols = new[] { "SPY" },
            includeMetadata = true
        });

        strategyResponse.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
        researchResponse.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);

        using var strategyPayload = await JsonDocument.ParseAsync(await strategyResponse.Content.ReadAsStreamAsync());
        using var researchPayload = await JsonDocument.ParseAsync(await researchResponse.Content.ReadAsStreamAsync());
        strategyPayload.RootElement.GetProperty("error").GetString().Should().Be("Export service not available");
        researchPayload.RootElement.GetProperty("error").GetString().Should().Be("Export service not available");
    }

    private static async Task<WebApplication> CreateAppAsync()
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            EnvironmentName = Environments.Development
        });
        builder.WebHost.UseTestServer();

        var app = builder.Build();
        app.MapExportEndpoints(JsonOptions);

        await app.StartAsync();
        return app;
    }

    private static async Task<JsonDocument> ReadJsonAsync(HttpClient client, string path)
    {
        var response = await client.GetAsync(path);
        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync();
        return await JsonDocument.ParseAsync(stream);
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter() }
    };
}
