using System.Net;
using System.Text.Json;
using FluentAssertions;
using Meridian.Contracts.Api;
using Meridian.Identity.Auth;
using Meridian.Storage;
using Meridian.Ui.Shared.Endpoints;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Meridian.Tests.Ui;

public sealed class ReplayPreviewEndpointsTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"replay-preview-{Guid.NewGuid():N}");

    [Theory]
    [InlineData("{not-json}\n")]
    [InlineData("{}\n")]
    public async Task Preview_MalformedCapture_ReturnsExplicitEmptyErrorResponse(string content)
    {
        Directory.CreateDirectory(_root);
        var path = Path.Combine(_root, "malformed.jsonl");
        await File.WriteAllTextAsync(path, content);
        await using var app = await CreateAppAsync();
        using var response = await app.GetTestClient().GetAsync($"{UiApiRoutes.ReplayPreview}?filePath={Uri.EscapeDataString(path)}");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        body.RootElement.GetProperty("events").GetArrayLength().Should().Be(0);
        body.RootElement.GetProperty("total").GetInt32().Should().Be(0);
        body.RootElement.GetProperty("error").GetString().Should().Be("Malformed replay data");
        body.RootElement.GetProperty("detail").GetString().Should().Contain("line 1");
    }

    [Fact]
    public async Task Preview_ActivePartialCapture_ReportsUnavailableNotMalformed()
    {
        Directory.CreateDirectory(_root);
        var path = Path.Combine(_root, "active.jsonl");
        await File.WriteAllTextAsync(path, "{\"timestamp\":");
        await using var writer = new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.Read | FileShare.Delete);
        await using var app = await CreateAppAsync();
        using var response = await app.GetTestClient().GetAsync($"{UiApiRoutes.ReplayPreview}?filePath={Uri.EscapeDataString(path)}");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        body.RootElement.GetProperty("error").GetString().Should().Contain("Close the capture writer");
        body.RootElement.GetProperty("total").GetInt32().Should().Be(0);
    }

    private async Task<WebApplication> CreateAppAsync()
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Testing" });
        builder.WebHost.UseTestServer();
        builder.Services.AddSingleton(new StorageOptions { RootPath = _root });
        var app = builder.Build();
        app.Use(async (context, next) =>
        {
            context.Items[LoginSessionMiddleware.CurrentUserPermissionsKey] = UserPermission.ViewHistoricalData;
            await next();
        });
        app.MapReplayEndpoints(new JsonSerializerOptions(JsonSerializerDefaults.Web));
        await app.StartAsync();
        return app;
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }
}
