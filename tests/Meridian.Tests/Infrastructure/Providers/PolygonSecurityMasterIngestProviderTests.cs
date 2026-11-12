using FluentAssertions;
using System.Net;
using Meridian.Infrastructure.Adapters.Core;
using Meridian.Infrastructure.Adapters.Polygon;
using Microsoft.Extensions.Logging.Abstractions;

namespace Meridian.Tests.Infrastructure.Providers;

[Collection("Sequential")]
public sealed class PolygonSecurityMasterIngestProviderTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task FetchAllAsync_FailedPageNeverReturnsAnApparentlyCompletePartialResult(bool afterFirstPage)
    {
        using var environment = new EnvironmentVariableScope().Set("POLYGON_API_KEY", "synthetic-key");
        using var handler = new StubHandler((_, index) => afterFirstPage && index == 1
            ? Json("""{"status":"OK","results":[{"ticker":"SPY"}],"next_url":"https://api.polygon.io/v3/reference/tickers?cursor=next"}""")
            : new HttpResponseMessage(HttpStatusCode.Unauthorized) { Content = new StringContent("synthetic-secret-body") });
        using var client = new HttpClient(handler);
        using var limiter = new RateLimiter(1000, TimeSpan.FromHours(1));
        using var provider = new PolygonSecurityMasterIngestProvider(NullLogger<PolygonSecurityMasterIngestProvider>.Instance, client, limiter);

        var failure = await Assert.ThrowsAsync<HttpRequestException>(() => provider.FetchAllAsync());

        failure.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        failure.Message.Should().NotContain("synthetic-secret-body").And.NotContain("synthetic-key");
        handler.Requests.Should().Be(afterFirstPage ? 2 : 1);
        handler.Contents.Should().OnlyContain(content => content.IsDisposed);
    }

    [Fact]
    public async Task FetchAllAsync_RepeatedCursorStopsBeforeRequestingItAgain()
    {
        using var environment = new EnvironmentVariableScope().Set("POLYGON_API_KEY", "synthetic-key");
        using var handler = new StubHandler((_, _) => Json("""{"status":"OK","results":[],"next_url":"https://api.polygon.io/v3/reference/tickers?cursor=repeat"}"""));
        using var client = new HttpClient(handler);
        using var limiter = new RateLimiter(1000, TimeSpan.FromHours(1));
        using var provider = new PolygonSecurityMasterIngestProvider(NullLogger<PolygonSecurityMasterIngestProvider>.Instance, client, limiter);

        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await Assert.ThrowsAsync<InvalidOperationException>(() => provider.FetchAllAsync(ct: deadline.Token));

        handler.Requests.Should().Be(2);
        handler.Contents.Should().OnlyContain(content => content.IsDisposed);
    }

    [Theory]
    [InlineData("https://untrusted.example/tickers")]
    [InlineData("http://api.polygon.io/v3/reference/tickers")]
    public async Task FetchAllAsync_UntrustedContinuationNeverReceivesARequest(string nextUrl)
    {
        using var environment = new EnvironmentVariableScope().Set("POLYGON_API_KEY", "synthetic-key");
        using var handler = new StubHandler((_, _) => Json(System.Text.Json.JsonSerializer.Serialize(new { status = "OK", results = Array.Empty<object>(), next_url = nextUrl })));
        using var client = new HttpClient(handler);
        using var limiter = new RateLimiter(1000, TimeSpan.FromHours(1));
        using var provider = new PolygonSecurityMasterIngestProvider(NullLogger<PolygonSecurityMasterIngestProvider>.Instance, client, limiter);

        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await Assert.ThrowsAsync<InvalidOperationException>(() => provider.FetchAllAsync(ct: deadline.Token));

        handler.Requests.Should().Be(1);
    }

    private static HttpResponseMessage Json(string json) => new(HttpStatusCode.OK) { Content = new TrackingContent(json) };

    private sealed class TrackingContent(string json) : StringContent(json)
    {
        public bool IsDisposed { get; private set; }
        protected override void Dispose(bool disposing) { IsDisposed = true; base.Dispose(disposing); }
    }

    private sealed class StubHandler(Func<HttpRequestMessage, int, HttpResponseMessage> respond) : HttpMessageHandler
    {
        public int Requests { get; private set; }
        public List<TrackingContent> Contents { get; } = [];
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var response = respond(request, ++Requests);
            if (response.Content is TrackingContent content) Contents.Add(content);
            return Task.FromResult(response);
        }
    }

    [Fact]
    public async Task FetchAllAsync_WhenHttpFetchIsCancelled_PropagatesCancellation()
    {
        using var environment = new EnvironmentVariableScope()
            .Set("POLYGON_API_KEY", "test-key")
            .Set("POLYGON__APIKEY", null);
        using var cts = new CancellationTokenSource();
        using var handler = new CancelingHttpMessageHandler(cts);
        using var httpClient = new HttpClient(handler);
        using var provider = new PolygonSecurityMasterIngestProvider(
            NullLogger<PolygonSecurityMasterIngestProvider>.Instance,
            httpClient);

        var fetch = async () => await provider.FetchAllAsync(ct: cts.Token);

        await fetch.Should().ThrowAsync<OperationCanceledException>();
        handler.Requests.Should().Be(1);
    }

    private sealed class CancelingHttpMessageHandler(CancellationTokenSource cancellation) : HttpMessageHandler
    {
        public int Requests { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Requests++;
            cancellation.Cancel();
            return Task.FromCanceled<HttpResponseMessage>(cancellation.Token);
        }
    }

    private sealed class EnvironmentVariableScope : IDisposable
    {
        private readonly Dictionary<string, string?> _originalValues = new(StringComparer.Ordinal);

        public EnvironmentVariableScope Set(string name, string? value)
        {
            _originalValues.TryAdd(name, Environment.GetEnvironmentVariable(name));
            Environment.SetEnvironmentVariable(name, value);
            return this;
        }

        public void Dispose()
        {
            foreach (var (name, value) in _originalValues)
            {
                Environment.SetEnvironmentVariable(name, value);
            }
        }
    }
}
