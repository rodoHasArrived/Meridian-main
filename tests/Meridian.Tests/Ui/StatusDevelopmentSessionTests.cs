using System.Net;
using System.Text.Json;
using Meridian.Application.UI;
using Meridian.Contracts.Pipeline;
using Meridian.Ui.Shared.Endpoints;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace Meridian.Tests.Ui;

public sealed class StatusDevelopmentSessionTests
{
    private const string Session = "df788df2-6388-49b9-a3bd-963820848245";
    private const string SessionHeader = "x-meridian-dev-session";

    [Theory]
    [InlineData(true, HttpStatusCode.OK)]
    [InlineData(false, HttpStatusCode.ServiceUnavailable)]
    public async Task Readyz_IdentifiesSeededSessionWithoutChangingReadiness(bool ready, HttpStatusCode expectedStatus)
    {
        await using var app = await CreateAppAsync("true", Session, ready);
        using var client = app.GetTestClient();

        using var response = await client.GetAsync("/readyz");

        Assert.Equal(expectedStatus, response.StatusCode);
        Assert.Equal(Session, Assert.Single(response.Headers.GetValues(SessionHeader)));
    }

    [Theory]
    [InlineData(null, Session)]
    [InlineData("false", Session)]
    [InlineData("true", null)]
    [InlineData("true", " ")]
    public async Task Readyz_DoesNotIdentifyOrdinaryOrUnmarkedHosts(string? demo, string? session)
    {
        await using var app = await CreateAppAsync(demo, session);
        using var client = app.GetTestClient();

        using var response = await client.GetAsync("/readyz");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.False(response.Headers.Contains(SessionHeader));
    }

    [Fact]
    public async Task Readyz_KeepsTheSessionCapturedWhenRoutesWereRegistered()
    {
        await using var app = await CreateAppAsync("true", Session);
        app.Configuration["MERIDIAN_DEMO"] = "false";
        app.Configuration["MERIDIAN_DEV_SESSION"] = Guid.NewGuid().ToString();
        using var client = app.GetTestClient();

        using var response = await client.GetAsync("/readyz");

        Assert.Equal(Session, Assert.Single(response.Headers.GetValues(SessionHeader)));
    }

    [Theory]
    [InlineData("/ready")]
    [InlineData("/healthz")]
    [InlineData("/livez")]
    public async Task OtherProbes_DoNotExposeTheDevelopmentSession(string endpoint)
    {
        await using var app = await CreateAppAsync("true", Session);
        using var client = app.GetTestClient();

        using var response = await client.GetAsync(endpoint);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.False(response.Headers.Contains(SessionHeader));
    }

    private static async Task<WebApplication> CreateAppAsync(string? demo, string? session, bool ready = true)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Testing" });
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["MERIDIAN_DEMO"] = demo,
            ["MERIDIAN_DEV_SESSION"] = session
        });
        builder.WebHost.UseTestServer();
        var app = builder.Build();
        var handlers = new StatusEndpointHandlers(
            () => throw new InvalidOperationException("These probes do not read metrics."),
            () => new PipelineStatistics(
                PublishedCount: 0, DroppedCount: 0, ConsumedCount: 0,
                CurrentQueueSize: 0, PeakQueueSize: 0, QueueCapacity: 100,
                QueueUtilization: ready ? 0 : 100, AverageProcessingTimeUs: 0,
                TimeSinceLastFlush: TimeSpan.Zero, Timestamp: DateTimeOffset.UtcNow),
            () => []);
        app.MapStatusEndpoints(handlers, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        await app.StartAsync();
        return app;
    }
}
