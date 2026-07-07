using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using FluentAssertions;
using Meridian.Identity.Auth;
using Meridian.Reporting;
using Meridian.Ui.Shared.Endpoints;
using Meridian.Ui.Shared.Streaming;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Meridian.Tests.Ui;

public sealed class ReportingRunStreamEndpointTests
{
    private static readonly JsonSerializerOptions ServerJsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter() }
    };

    private static readonly DateTimeOffset FixedNow = new(2026, 5, 1, 12, 0, 0, TimeSpan.Zero);
    private const string SeededRunId = "job-stream-20260501";

    [Fact]
    public async Task WithoutReadPermission_Returns403()
    {
        await using var app = await CreateStreamAppAsync(grantPermission: false);
        var client = app.GetTestClient();

        var response = await client.GetAsync($"/api/fund-structure/reporting/runs/{SeededRunId}/stream");

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task UnknownRun_Returns404()
    {
        await using var app = await CreateStreamAppAsync();
        var client = app.GetTestClient();

        var response = await client.GetAsync("/api/fund-structure/reporting/runs/no-such-run/stream");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task WithoutBroadcaster_Returns503()
    {
        await using var app = await CreateStreamAppAsync(registerBroadcaster: false);
        var client = app.GetTestClient();

        var response = await client.GetAsync($"/api/fund-structure/reporting/runs/{SeededRunId}/stream");

        response.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
    }

    [Fact]
    public async Task EmitsReportRunEventForSeededRun()
    {
        await using var app = await CreateStreamAppAsync();
        var client = app.GetTestClient();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));

        using var response = await client.GetAsync(
            $"/api/fund-structure/reporting/runs/{SeededRunId}/stream",
            HttpCompletionOption.ResponseHeadersRead,
            cts.Token);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.ContentType!.MediaType.Should().Be("text/event-stream");

        var frame = await ReadFirstEventFrameAsync(response, cts.Token);
        frame.Should().StartWith("event: report-run");
        frame.Should().Contain(SeededRunId);
    }

    [Fact]
    public async Task BeyondSessionCap_Returns429()
    {
        await using var app = await CreateStreamAppAsync(maxConcurrentStreams: 1);
        var client = app.GetTestClient();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));

        // Hold the first stream open (its subscription keeps the session's single slot reserved).
        using var first = await client.GetAsync(
            $"/api/fund-structure/reporting/runs/{SeededRunId}/stream",
            HttpCompletionOption.ResponseHeadersRead,
            cts.Token);
        first.StatusCode.Should().Be(HttpStatusCode.OK);
        await using var firstStream = await first.Content.ReadAsStreamAsync(cts.Token);
        await ReadPastFirstFrameAsync(firstStream, cts.Token);

        using var second = await client.GetAsync(
            $"/api/fund-structure/reporting/runs/{SeededRunId}/stream",
            HttpCompletionOption.ResponseHeadersRead,
            cts.Token);

        second.StatusCode.Should().Be(HttpStatusCode.TooManyRequests);
        second.Headers.RetryAfter.Should().NotBeNull();
    }

    private static async Task<string> ReadFirstEventFrameAsync(HttpResponseMessage response, CancellationToken ct)
    {
        await using var stream = await response.Content.ReadAsStreamAsync(ct);
        var buffer = new byte[4096];
        var builder = new StringBuilder();
        while (!ct.IsCancellationRequested)
        {
            var read = await stream.ReadAsync(buffer, ct);
            if (read <= 0)
            {
                break;
            }

            builder.Append(Encoding.UTF8.GetString(buffer, 0, read));
            var text = builder.ToString();
            var frameEnd = text.IndexOf("\n\n", StringComparison.Ordinal);
            if (frameEnd >= 0)
            {
                return text[..frameEnd];
            }
        }

        throw new TimeoutException("No SSE frame arrived before the read deadline.");
    }

    private static async Task ReadPastFirstFrameAsync(Stream stream, CancellationToken ct)
    {
        var buffer = new byte[1024];
        while (!ct.IsCancellationRequested)
        {
            var read = await stream.ReadAsync(buffer, ct);
            if (read <= 0)
            {
                break;
            }

            if (Encoding.UTF8.GetString(buffer, 0, read).Contains("\n\n", StringComparison.Ordinal))
            {
                return;
            }
        }
    }

    private static async Task<WebApplication> CreateStreamAppAsync(
        bool grantPermission = true,
        bool registerBroadcaster = true,
        int maxConcurrentStreams = 8)
    {
        var orchestration = new ReportingOrchestrationService(
            new DefaultReportingTemplateCatalog(), new DeterministicReportingSectionRenderer(), () => FixedNow);
        await orchestration.ExecuteAsync(
            new ReportingJobContract("job-stream", "investor-monthly-statement", new DateOnly(2026, 5, 1), ReportingRunTrigger.AdHoc, 0, "op", FixedNow),
            CancellationToken.None);

        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            EnvironmentName = Environments.Development
        });
        builder.WebHost.UseTestServer();

        builder.Services.AddSingleton<IReportingOrchestrationService>(orchestration);
        builder.Services.AddSingleton(new QuoteStreamOptions
        {
            CoalesceIntervalMs = 0,
            MaxConcurrentStreamsPerSession = maxConcurrentStreams,
            SubscriberChannelCapacity = 1
        });
        builder.Services.AddSingleton(sp => new StreamConnectionRegistry(
            sp.GetRequiredService<QuoteStreamOptions>().MaxConcurrentStreamsPerSession));
        if (registerBroadcaster)
        {
            builder.Services.AddSingleton(sp => new ReportRunStreamBroadcaster(
                sp,
                sp.GetRequiredService<StreamConnectionRegistry>(),
                sp.GetRequiredService<QuoteStreamOptions>()));
        }

        var app = builder.Build();
        app.Use(async (context, next) =>
        {
            context.Items[LoginSessionMiddleware.CurrentUserKey] = "reporting-op";
            if (grantPermission)
            {
                context.Items[LoginSessionMiddleware.CurrentUserPermissionsKey] = UserPermission.ViewReporting;
            }

            await next();
        });
        app.MapReportingRunStreamEndpoints(ServerJsonOptions);
        await app.StartAsync();
        return app;
    }
}
