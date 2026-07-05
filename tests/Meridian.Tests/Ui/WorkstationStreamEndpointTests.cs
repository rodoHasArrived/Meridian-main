using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using FluentAssertions;
using Meridian.Contracts.Domain.Models;
using Meridian.Domain.Collectors;
using Meridian.Tests.TestHelpers;
using Meridian.Ui.Shared.Endpoints;
using Meridian.Ui.Shared.Streaming;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Meridian.Tests.Ui;

public sealed class WorkstationStreamEndpointTests
{
    private static readonly JsonSerializerOptions ServerJsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter() }
    };

    [Fact]
    public async Task GetWorkstationStream_WithoutBroadcaster_Returns503()
    {
        await using var app = await CreateStreamAppAsync(registerStream: false);
        var client = app.GetTestClient();

        var response = await client.GetAsync("/api/workstation/stream");

        response.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
    }

    [Fact]
    public async Task GetWorkstationStream_WithTooManySymbols_Returns400()
    {
        await using var app = await CreateStreamAppAsync(registerStream: true);
        var client = app.GetTestClient();

        var symbols = string.Join(',', Enumerable.Range(0, 51).Select(index => $"SYM{index}"));
        var response = await client.GetAsync($"/api/workstation/stream?symbols={symbols}");

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task GetWorkstationStream_EmitsQuotesEventForFilteredSymbols()
    {
        await using var app = await CreateStreamAppAsync(registerStream: true, seedSymbols: ["SPY", "MSFT"]);
        var client = app.GetTestClient();

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        using var response = await client.GetAsync(
            "/api/workstation/stream?symbols=SPY",
            HttpCompletionOption.ResponseHeadersRead,
            cts.Token);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.ContentType!.MediaType.Should().Be("text/event-stream");

        var frame = await ReadFirstEventFrameAsync(response, cts.Token);
        frame.Should().StartWith("event: quotes");
        frame.Should().Contain("\"symbol\":\"SPY\"");
        frame.Should().NotContain("\"symbol\":\"MSFT\"");
    }

    [Fact]
    public async Task GetWorkstationStream_BeyondSessionCap_Returns429()
    {
        await using var app = await CreateStreamAppAsync(
            registerStream: true, seedSymbols: ["SPY"], maxConcurrentStreams: 1);
        var client = app.GetTestClient();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));

        // Hold the first stream open (its subscription keeps the session's single slot reserved).
        // Read past the first frame WITHOUT disposing the stream — disposing it would abort the
        // server request and release the slot, letting the second request succeed spuriously.
        using var first = await client.GetAsync(
            "/api/workstation/stream?symbols=SPY", HttpCompletionOption.ResponseHeadersRead, cts.Token);
        first.StatusCode.Should().Be(HttpStatusCode.OK);
        await using var firstStream = await first.Content.ReadAsStreamAsync(cts.Token);
        await ReadPastFirstFrameAsync(firstStream, cts.Token);

        using var second = await client.GetAsync(
            "/api/workstation/stream?symbols=MSFT", HttpCompletionOption.ResponseHeadersRead, cts.Token);

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

    // Reads until the first `\n\n` frame boundary but leaves the stream open so the caller keeps the
    // server request (and its reserved session slot) alive.
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
        bool registerStream,
        IReadOnlyList<string>? seedSymbols = null,
        int maxConcurrentStreams = 8)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            EnvironmentName = Environments.Development
        });
        builder.WebHost.UseTestServer();

        if (registerStream)
        {
            var collector = new QuoteCollector(new TestMarketEventPublisher());
            foreach (var symbol in seedSymbols ?? [])
            {
                collector.OnQuote(new MarketQuoteUpdate(
                    Timestamp: DateTimeOffset.UtcNow,
                    Symbol: symbol,
                    BidPrice: 450.00m,
                    BidSize: 100,
                    AskPrice: 450.05m,
                    AskSize: 200,
                    StreamId: "TEST",
                    Venue: "NYSE"));
            }

            builder.Services.AddSingleton(collector);
            builder.Services.AddSingleton(new QuoteStreamOptions
            {
                CoalesceIntervalMs = 0,
                MaxConcurrentStreamsPerSession = maxConcurrentStreams,
                SubscriberChannelCapacity = 1
            });
            builder.Services.AddSingleton(sp => new StreamConnectionRegistry(
                sp.GetRequiredService<QuoteStreamOptions>().MaxConcurrentStreamsPerSession));
            builder.Services.AddSingleton(sp => new QuoteStreamBroadcaster(
                sp,
                sp.GetRequiredService<StreamConnectionRegistry>(),
                sp.GetRequiredService<QuoteStreamOptions>()));
            builder.Services.AddSingleton<IQuoteStreamBroadcaster>(sp => sp.GetRequiredService<QuoteStreamBroadcaster>());
        }

        var app = builder.Build();
        app.Use(AddTestTenantContext);
        app.MapWorkstationEndpoints(ServerJsonOptions);
        await app.StartAsync();
        return app;
    }

    private static async Task AddTestTenantContext(HttpContext context, Func<Task> next)
    {
        context.Items[LoginSessionMiddleware.CurrentTenantIdKey] = "tenant-test";
        context.Items[LoginSessionMiddleware.CurrentUserCompanyIdKey] = "company-test";
        context.Items[LoginSessionMiddleware.CurrentUserKey] = "stream-test-operator";
        await next();
    }
}
