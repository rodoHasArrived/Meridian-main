using System.Net;
using System.Net.Http.Json;
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

namespace Meridian.Tests.Ui;

public sealed class ReplayCapabilityEndpointsTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"replay-capabilities-{Guid.NewGuid():N}");

    [Theory]
    [InlineData(UiApiRoutes.AlignmentCreate)]
    [InlineData(UiApiRoutes.AlignmentPreview)]
    public async Task Alignment_ReturnsUnavailableWithoutInventingJobOrEstimate(string route)
    {
        await using var app = await CreateAppAsync();
        using var response = await app.GetTestClient().PostAsJsonAsync(route, new { symbols = new[] { "AAPL" } });

        response.StatusCode.Should().Be(HttpStatusCode.NotImplemented);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        body.RootElement.GetProperty("detail").GetString().Should().Contain("No job was created");
        body.RootElement.TryGetProperty("jobId", out _).Should().BeFalse();
        body.RootElement.TryGetProperty("estimatedOutputRows", out _).Should().BeFalse();
    }

    [Theory]
    [InlineData("pause")]
    [InlineData("resume")]
    [InlineData("seek")]
    [InlineData("speed")]
    public async Task PlaybackControls_ReturnUnavailableAndPreserveScanStatus(string action)
    {
        await using var app = await CreateAppAsync();
        using var client = app.GetTestClient();
        var sessionId = await StartCompletedScanAsync(client);

        using var response = await client.PostAsJsonAsync($"/api/replay/{sessionId}/{action}",
            new { positionMs = 5000, speedMultiplier = 2.0 });

        response.StatusCode.Should().Be(HttpStatusCode.NotImplemented);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        body.RootElement.GetProperty("detail").GetString().Should().Contain("No playback position");
        using var status = await client.GetFromJsonAsync<JsonDocument>($"/api/replay/{sessionId}/status");
        status!.RootElement.GetProperty("status").GetString().Should().Be("completed");
        status.RootElement.GetProperty("mode").GetString().Should().Be("event_count");
        status.RootElement.GetProperty("playbackSupported").GetBoolean().Should().BeFalse();
        status.RootElement.TryGetProperty("speedMultiplier", out _).Should().BeFalse();
    }

    [Theory]
    [InlineData("0")]
    [InlineData("-1")]
    [InlineData("\"NaN\"")]
    [InlineData("1e400")]
    public async Task StartAndSpeed_RejectInvalidMultiplier(string value)
    {
        await using var app = await CreateAppAsync();
        using var client = app.GetTestClient();
        var pathJson = JsonSerializer.Serialize(Path.Combine(_root, "empty.jsonl"));
        using var startBody = new StringContent($"{{\"filePath\":{pathJson},\"speedMultiplier\":{value}}}",
            System.Text.Encoding.UTF8, "application/json");
        using var start = await client.PostAsync(UiApiRoutes.ReplayStart, startBody);
        start.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        using var speedBody = new StringContent($"{{\"speedMultiplier\":{value}}}",
            System.Text.Encoding.UTF8, "application/json");
        using var speed = await client.PostAsync("/api/replay/session/speed", speedBody);
        speed.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Start_RequestedPlaybackSpeed_ReturnsUnavailableWithoutRetainingScan()
    {
        await using var app = await CreateAppAsync();
        using var client = app.GetTestClient();
        using var response = await client.PostAsJsonAsync(UiApiRoutes.ReplayStart,
            new { filePath = Path.Combine(_root, "empty.jsonl"), speedMultiplier = 2.0 });
        response.StatusCode.Should().Be(HttpStatusCode.NotImplemented);
        using var stats = await client.GetFromJsonAsync<JsonDocument>(UiApiRoutes.ReplayStats);
        stats!.RootElement.GetProperty("retainedSessions").GetInt32().Should().Be(0);
    }

    [Fact]
    public async Task ScanHistory_IsBoundedAndEvictsOldestFinishedScan()
    {
        await using var app = await CreateAppAsync();
        using var client = app.GetTestClient();
        var oldest = await StartCompletedScanAsync(client);
        string newest = oldest;
        for (var i = 0; i < 64; i++)
            newest = await StartCompletedScanAsync(client);

        using var stats = await client.GetFromJsonAsync<JsonDocument>(UiApiRoutes.ReplayStats);
        stats!.RootElement.GetProperty("retainedSessions").GetInt32().Should().Be(64);
        stats.RootElement.GetProperty("activeSessions").GetInt32().Should().Be(0);
        using var oldestResponse = await client.GetAsync($"/api/replay/{oldest}/status");
        oldestResponse.StatusCode.Should().Be(HttpStatusCode.NotFound);
        using var newestResponse = await client.GetAsync($"/api/replay/{newest}/status");
        newestResponse.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    private async Task<string> StartCompletedScanAsync(HttpClient client)
    {
        using var response = await client.PostAsJsonAsync(UiApiRoutes.ReplayStart,
            new { filePath = Path.Combine(_root, "empty.jsonl") });
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        body.RootElement.GetProperty("mode").GetString().Should().Be("event_count");
        body.RootElement.GetProperty("playbackSupported").GetBoolean().Should().BeFalse();
        var sessionId = body.RootElement.GetProperty("sessionId").GetString()!;

        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        while (true)
        {
            using var status = await client.GetFromJsonAsync<JsonDocument>(
                $"/api/replay/{sessionId}/status", timeout.Token);
            if (status!.RootElement.GetProperty("status").GetString() == "completed")
                return sessionId;
            await Task.Delay(10, timeout.Token);
        }
    }

    private async Task<WebApplication> CreateAppAsync()
    {
        Directory.CreateDirectory(_root);
        await File.WriteAllTextAsync(Path.Combine(_root, "empty.jsonl"), string.Empty);
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Testing" });
        builder.WebHost.UseTestServer();
        builder.Services.AddSingleton(new StorageOptions { RootPath = _root });
        builder.Services.AddSingleton<ReplayScanLifetime>();
        builder.Services.AddHostedService(sp => sp.GetRequiredService<ReplayScanLifetime>());
        var app = builder.Build();
        app.Use(async (context, next) =>
        {
            context.Items[LoginSessionMiddleware.CurrentUserPermissionsKey] = UserPermission.ViewHistoricalData;
            await next();
        });
        var json = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        app.MapReplayEndpoints(json);
        app.MapAlignmentEndpoints(json);
        await app.StartAsync();
        return app;
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }
}
