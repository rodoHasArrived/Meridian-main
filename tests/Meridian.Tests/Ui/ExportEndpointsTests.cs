using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using FluentAssertions;
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

        var response = await client.PostAsJsonAsync("/api/export/analysis", new { profileId = "audit-pack" });

        response.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
    }

    private static async Task<WebApplication> CreateAppAsync()
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            EnvironmentName = Environments.Development
        });
        builder.WebHost.UseTestServer();

        var app = builder.Build();
        app.MapExportEndpoints(new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            Converters = { new JsonStringEnumConverter() }
        });

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
}
