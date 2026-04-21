using System.Net;
using System.Reflection;
using FluentAssertions;
using Meridian.Domain.Collectors;
using Meridian.Domain.Events;
using Meridian.Infrastructure.Adapters.Robinhood;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Meridian.Tests.Infrastructure.Providers;

/// <summary>
/// Unit tests for <see cref="RobinhoodMarketDataClient"/>.
/// All tests use a stub HTTP handler — no real network calls are made.
/// </summary>
public sealed class RobinhoodMarketDataClientTests : IDisposable
{
    private readonly string? _originalAccessToken;

    public RobinhoodMarketDataClientTests()
    {
        _originalAccessToken = Environment.GetEnvironmentVariable("ROBINHOOD_ACCESS_TOKEN");
    }

    public void Dispose()
    {
        Environment.SetEnvironmentVariable("ROBINHOOD_ACCESS_TOKEN", _originalAccessToken);
    }

    // ── Helpers ──────────────────────────────────────────────────────────

    private static RobinhoodMarketDataClient CreateSut(
        HttpMessageHandler handler,
        out CapturingPublisher publisher,
        string? accessToken = "test-token")
    {
        publisher = new CapturingPublisher();
        var quoteCollector = new QuoteCollector(publisher);

        return new RobinhoodMarketDataClient(
            new StubHttpClientFactory(handler),
            quoteCollector,
            NullLogger<RobinhoodMarketDataClient>.Instance,
            accessToken: accessToken);
    }

    // ── Provider metadata ────────────────────────────────────────────────

    [Fact]
    public void Name_ReturnsRobinhoodLive()
    {
        var sut = CreateSut(new StubHttpHandler(HttpStatusCode.OK, new StringContent("{}")), out _);
        ((Meridian.Infrastructure.Adapters.Core.IProviderMetadata)sut).ProviderId.Should().Be("robinhood-live");
    }

    [Fact]
    public void IsEnabled_WithToken_ReturnsTrue()
    {
        var sut = CreateSut(new StubHttpHandler(HttpStatusCode.OK, new StringContent("{}")),
            out _, accessToken: "some-token");
        sut.IsEnabled.Should().BeTrue();
    }

    [Fact]
    public void IsEnabled_WithoutToken_ReturnsFalse()
    {
        var sut = CreateSut(new StubHttpHandler(HttpStatusCode.OK, new StringContent("{}")),
            out _, accessToken: null);
        sut.IsEnabled.Should().BeFalse();
    }

    // ── ConnectAsync ──────────────────────────────────────────────────────

    [Fact]
    public async Task ConnectAsync_NoToken_ThrowsConnectionException()
    {
        var sut = CreateSut(new StubHttpHandler(HttpStatusCode.OK, new StringContent("{}")),
            out _, accessToken: null);
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));

        var act = () => sut.ConnectAsync(cts.Token);

        await act.Should().ThrowAsync<Meridian.Application.Exceptions.ConnectionException>()
            .WithMessage("*ROBINHOOD_ACCESS_TOKEN*");
    }

    [Fact]
    public async Task ConnectAsync_ValidToken_Connects()
    {
        var sut = CreateSut(new StubHttpHandler(HttpStatusCode.OK, new StringContent("{}")), out _);
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));

        var act = () => sut.ConnectAsync(cts.Token);

        await act.Should().NotThrowAsync();
        await sut.DisconnectAsync(cts.Token);
    }

    // ── Subscription stubs ────────────────────────────────────────────────

    [Fact]
    public void SubscribeTrades_ReturnsMinusOne()
    {
        var sut = CreateSut(new StubHttpHandler(HttpStatusCode.OK, new StringContent("{}")), out _);
        var cfg = new Meridian.Contracts.Configuration.SymbolConfig("AAPL");
        sut.SubscribeTrades(cfg).Should().Be(-1);
    }

    [Fact]
    public void SubscribeMarketDepth_ReturnsMinusOne()
    {
        var sut = CreateSut(new StubHttpHandler(HttpStatusCode.OK, new StringContent("{}")), out _);
        var cfg = new Meridian.Contracts.Configuration.SymbolConfig("AAPL");
        sut.SubscribeMarketDepth(cfg).Should().Be(-1);
    }

    [Fact]
    public void SubscribeQuotes_ReturnsPositiveId()
    {
        var sut = CreateSut(new StubHttpHandler(HttpStatusCode.OK, new StringContent("{}")), out _);
        var cfg = new Meridian.Contracts.Configuration.SymbolConfig("AAPL");
        sut.SubscribeQuotes(cfg).Should().BeGreaterThan(0);
    }

    [Fact]
    public void SubscribeQuotes_MultipleSymbols_ReturnsUniqueIds()
    {
        var sut = CreateSut(new StubHttpHandler(HttpStatusCode.OK, new StringContent("{}")), out _);
        var id1 = sut.SubscribeQuotes(new Meridian.Contracts.Configuration.SymbolConfig("AAPL"));
        var id2 = sut.SubscribeQuotes(new Meridian.Contracts.Configuration.SymbolConfig("MSFT"));
        id1.Should().NotBe(id2);
    }

    // ── Dispose / DisposeAsync ────────────────────────────────────────────

    [Fact]
    public async Task DisposeAsync_WhenConnected_DisconnectsCleanly()
    {
        var sut = CreateSut(new StubHttpHandler(HttpStatusCode.OK, new StringContent("{}")), out _);
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await sut.ConnectAsync(cts.Token);

        var act = () => sut.DisposeAsync().AsTask();
        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task PollBatchAsync_WhenRateLimited_DoesNotPublishQuotes()
    {
        var sut = CreateSut(
            new StubHttpHandler(HttpStatusCode.TooManyRequests, new StringContent("{\"detail\":\"rate limited\"}")),
            out var publisher);

        var act = () => InvokePollBatchAsync(sut, ["AAPL"]);

        await act.Should().NotThrowAsync();
        publisher.Published.Should().BeEmpty();
    }

    [Fact]
    public async Task PollBatchAsync_WhenCancelled_PropagatesOperationCanceledException()
    {
        var sut = CreateSut(new CancelledHttpHandler(), out _);
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        var act = () => InvokePollBatchAsync(sut, ["AAPL"], cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    // ── Stub infrastructure ──────────────────────────────────────────────

    private sealed class StubHttpHandler : HttpMessageHandler
    {
        private readonly HttpStatusCode _statusCode;
        private readonly HttpContent _content;

        public StubHttpHandler(HttpStatusCode statusCode, HttpContent content)
        {
            _statusCode = statusCode;
            _content = content;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            return Task.FromResult(new HttpResponseMessage(_statusCode) { Content = _content });
        }
    }

    private sealed class StubHttpClientFactory : IHttpClientFactory
    {
        private readonly HttpMessageHandler _handler;

        public StubHttpClientFactory(HttpMessageHandler handler) => _handler = handler;

        public HttpClient CreateClient(string name) =>
            new HttpClient(_handler, disposeHandler: false) { BaseAddress = new Uri("https://api.robinhood.com/") };
    }

    private sealed class CancelledHttpHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            return Task.FromCanceled<HttpResponseMessage>(ct);
        }
    }

    private sealed class CapturingPublisher : IMarketEventPublisher
    {
        public List<MarketEvent> Published { get; } = new();

        public bool TryPublish(in MarketEvent evt)
        {
            Published.Add(evt);
            return true;
        }
    }

    private static Task InvokePollBatchAsync(
        RobinhoodMarketDataClient sut,
        IReadOnlyList<string> symbols,
        CancellationToken cancellationToken = default)
    {
        var method = typeof(RobinhoodMarketDataClient).GetMethod(
            "PollBatchAsync",
            BindingFlags.Instance | BindingFlags.NonPublic);

        method.Should().NotBeNull("Wave 1 provider hardening exercises the actual Robinhood polling seam");
        return (Task)method!.Invoke(sut, [symbols, cancellationToken])!;
    }
}
