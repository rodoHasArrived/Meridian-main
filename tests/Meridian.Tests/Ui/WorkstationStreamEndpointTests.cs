using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using FluentAssertions;
using Meridian.Contracts.Domain.Models;
using Meridian.Domain.Collectors;
using Meridian.Tests.TestHelpers;
using Meridian.Ui.Shared.Endpoints;
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
    public async Task GetWorkstationStream_WithoutQuoteCollector_Returns503()
    {
        await using var app = await CreateStreamAppAsync(registerCollector: false);
        var client = app.GetTestClient();

        var response = await client.GetAsync("/api/workstation/stream");

        response.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
    }

    [Fact]
    public async Task GetWorkstationStream_WithTooManySymbols_Returns400()
    {
        await using var app = await CreateStreamAppAsync(registerCollector: true);
        var client = app.GetTestClient();

        var symbols = string.Join(',', Enumerable.Range(0, 51).Select(index => $"SYM{index}"));
        var response = await client.GetAsync($"/api/workstation/stream?symbols={symbols}");

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task GetWorkstationStream_EmitsQuotesEventForFilteredSymbols()
    {
        await using var app = await CreateStreamAppAsync(registerCollector: true, seedSymbols: ["SPY", "MSFT"]);
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

    private static async Task<WebApplication> CreateStreamAppAsync(
        bool registerCollector,
        IReadOnlyList<string>? seedSymbols = null)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            EnvironmentName = Environments.Development
        });
        builder.WebHost.UseTestServer();

        if (registerCollector)
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
