using System.Net;
using FluentAssertions;
using Meridian.Application.Integrations;
using Meridian.Contracts.Integrations;

namespace Meridian.Tests.Application.Integrations;

public sealed class ProviderIntegrationHttpClientTransportTests
{
    [Theory]
    [InlineData("fc00::1")]
    [InlineData("fd12:3456::1")]
    [InlineData("::ffff:10.1.2.3")]
    public async Task SendAsync_RejectsPrivateIpv6BeforeSending(string address)
    {
        var handler = new RecordingHandler(new HttpResponseMessage(HttpStatusCode.OK));
        using var client = new HttpClient(handler);
        var transport = new ProviderIntegrationHttpClientTransport(client,
            hostResolver: new StaticHostResolver(IPAddress.Parse(address)));
        await FluentActions.Awaiting(() => transport.SendAsync(CreateRequest("/v1/data")))
            .Should().ThrowAsync<InvalidOperationException>().WithMessage("*non-public address*");
        handler.Requests.Should().BeEmpty();
    }

    [Theory]
    [InlineData("127.0.0.1")]
    [InlineData("169.254.169.254")]
    [InlineData("fd12:3456::1")]
    public async Task SendAsync_DnsRebindsAfterPreflight_ProductionHandlerRefusesConnection(string reboundAddress)
    {
        var resolver = new RebindingHostResolver(IPAddress.Parse(reboundAddress));
        using var client = ProviderIntegrationHttpClientTransport.CreateHttpClient(resolver);
        var transport = new ProviderIntegrationHttpClientTransport(client, hostResolver: resolver);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var failure = await FluentActions.Awaiting(() => transport.SendAsync(CreateRequest("/v1/data"), timeout.Token))
            .Should().ThrowAsync<HttpRequestException>();
        failure.Which.ToString().Should().Contain("non-public address");
        resolver.Calls.Should().Be(2, "preflight and connection must each validate DNS before any socket connects");
    }

    private sealed class RebindingHostResolver(IPAddress reboundAddress) : IProviderIntegrationHostResolver
    {
        public int Calls { get; private set; }
        public ValueTask<IReadOnlyList<IPAddress>> ResolveAsync(string host, CancellationToken ct = default)
            => ValueTask.FromResult<IReadOnlyList<IPAddress>>([++Calls == 1 ? IPAddress.Parse("203.0.113.10") : reboundAddress]);
    }

    [Fact]
    public async Task SendAsync_SameOriginRedirectRebinds_RejectsBeforeSecondRequest()
    {
        var handler = new RecordingHandler(new HttpResponseMessage(HttpStatusCode.Redirect)
        {
            Headers = { Location = new Uri("/redirected", UriKind.Relative) }
        });
        using var client = new HttpClient(handler);
        var resolver = new RebindingHostResolver(IPAddress.Loopback);
        var transport = new ProviderIntegrationHttpClientTransport(client, hostResolver: resolver);
        await FluentActions.Awaiting(() => transport.SendAsync(CreateRequest("/v1/data")))
            .Should().ThrowAsync<InvalidOperationException>().WithMessage("*non-public address*");
        handler.Requests.Should().ContainSingle();
    }

    [Fact]
    public async Task SendAsync_UnknownLengthBodyExceedsLimit_RejectsWhileReading()
    {
        var content = new StreamContent(new NonSeekableMemoryStream(new byte[(8 * 1024 * 1024) + 1]));
        content.Headers.ContentLength.Should().BeNull();
        var handler = new RecordingHandler(new HttpResponseMessage(HttpStatusCode.OK) { Content = content });
        using var client = new HttpClient(handler);
        var transport = new ProviderIntegrationHttpClientTransport(client,
            hostResolver: new StaticHostResolver(IPAddress.Parse("203.0.113.10")));
        await FluentActions.Awaiting(() => transport.SendAsync(CreateRequest("/v1/data")))
            .Should().ThrowAsync<InvalidOperationException>().WithMessage("*exceeds*");
    }

    private sealed class NonSeekableMemoryStream(byte[] buffer) : MemoryStream(buffer)
    {
        public override bool CanSeek => false;
    }

    [Fact]
    public async Task SendAsync_SendsRequestWithQueryAndReturnsBodyAndHeaders()
    {
        var handler = new RecordingHandler(
            new HttpResponseMessage(HttpStatusCode.Accepted)
            {
                Content = new StringContent("""{"ok":true}""")
            });
        handler.Response.Headers.Add("X-Provider-Cursor", "cursor-2");
        using var httpClient = new HttpClient(handler)
        {
            BaseAddress = new Uri("https://provider.example.test")
        };
        var transport = new ProviderIntegrationHttpClientTransport(
            httpClient,
            hostResolver: new StaticHostResolver(IPAddress.Parse("203.0.113.10")));

        var response = await transport.SendAsync(
            new ProviderIntegrationHttpRequest(
                ProviderIntegrationHttpMethodDto.Get,
                "/v1/accounts/A-100/positions",
                new Dictionary<string, string> { ["Accept"] = "application/json" },
                new Dictionary<string, string>
                {
                    ["asOf"] = "2026-06-16",
                    ["cursor"] = "next page"
                },
                BodyTemplate: null,
                ApprovedBaseUri: "https://provider.example.test"));

        response.StatusCode.Should().Be(202);
        response.Body.Should().Be("""{"ok":true}""");
        response.Headers.Should().Contain("X-Provider-Cursor", "cursor-2");
        handler.Requests.Should().ContainSingle();
        var request = handler.Requests.Single();
        request.Method.Should().Be(HttpMethod.Get);
        request.RequestUri!.AbsoluteUri.Should().Be("https://provider.example.test/v1/accounts/A-100/positions?asOf=2026-06-16&cursor=next%20page");
        request.Headers.Accept.ToString().Should().Be("application/json");
    }

    [Fact]
    public async Task SendAsync_RejectsTargetOutsideApprovedOriginBeforeSending()
    {
        var handler = new RecordingHandler(new HttpResponseMessage(HttpStatusCode.OK));
        using var httpClient = new HttpClient(handler);
        var transport = new ProviderIntegrationHttpClientTransport(
            httpClient,
            hostResolver: new StaticHostResolver(IPAddress.Parse("203.0.113.10")));

        var act = () => transport.SendAsync(CreateRequest("https://attacker.example/metadata"));

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*outside the approved HTTPS origin*");
        handler.Requests.Should().BeEmpty();
    }

    [Fact]
    public async Task SendAsync_RejectsNetworkPathOutsideApprovedOriginBeforeSending()
    {
        var handler = new RecordingHandler(new HttpResponseMessage(HttpStatusCode.OK));
        using var httpClient = new HttpClient(handler);
        var transport = new ProviderIntegrationHttpClientTransport(
            httpClient,
            hostResolver: new StaticHostResolver(IPAddress.Parse("203.0.113.10")));

        var act = () => transport.SendAsync(CreateRequest("//attacker.example/metadata"));

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*outside the approved HTTPS origin*");
        handler.Requests.Should().BeEmpty();
    }

    [Fact]
    public async Task SendAsync_RejectsApprovedHostResolvingToPrivateAddress()
    {
        var handler = new RecordingHandler(new HttpResponseMessage(HttpStatusCode.OK));
        using var httpClient = new HttpClient(handler);
        var transport = new ProviderIntegrationHttpClientTransport(
            httpClient,
            hostResolver: new StaticHostResolver(IPAddress.Parse("169.254.169.254")));

        var act = () => transport.SendAsync(CreateRequest("/v1/data"));

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*non-public address*");
        handler.Requests.Should().BeEmpty();
    }

    [Fact]
    public async Task SendAsync_RejectsRedirectToUnapprovedOrigin()
    {
        var redirect = new HttpResponseMessage(HttpStatusCode.Redirect)
        {
            Headers = { Location = new Uri("https://169.254.169.254/latest/meta-data") }
        };
        var handler = new RecordingHandler(redirect);
        using var httpClient = new HttpClient(handler);
        var transport = new ProviderIntegrationHttpClientTransport(
            httpClient,
            hostResolver: new StaticHostResolver(IPAddress.Parse("203.0.113.10")));

        var act = () => transport.SendAsync(CreateRequest("/v1/data"));

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*outside the approved HTTPS origin*");
        handler.Requests.Should().ContainSingle();
    }

    [Fact]
    public async Task SendAsync_RejectsResponseLargerThanBoundedLimit()
    {
        var handler = new RecordingHandler(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new ByteArrayContent(new byte[(8 * 1024 * 1024) + 1])
        });
        using var httpClient = new HttpClient(handler);
        var transport = new ProviderIntegrationHttpClientTransport(
            httpClient,
            hostResolver: new StaticHostResolver(IPAddress.Parse("203.0.113.10")));

        var act = () => transport.SendAsync(CreateRequest("/v1/data"));

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*exceeds*");
    }

    private static ProviderIntegrationHttpRequest CreateRequest(string path)
        => new(
            ProviderIntegrationHttpMethodDto.Get,
            path,
            new Dictionary<string, string>(),
            new Dictionary<string, string>(),
            BodyTemplate: null,
            ApprovedBaseUri: "https://provider.example.test");

    private sealed class RecordingHandler : HttpMessageHandler
    {
        public RecordingHandler(HttpResponseMessage response)
        {
            Response = response;
        }

        public HttpResponseMessage Response { get; }

        public List<HttpRequestMessage> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Requests.Add(request);
            return Task.FromResult(Response);
        }
    }

    private sealed class StaticHostResolver(params IPAddress[] addresses) : IProviderIntegrationHostResolver
    {
        public ValueTask<IReadOnlyList<IPAddress>> ResolveAsync(string host, CancellationToken ct = default)
            => ValueTask.FromResult<IReadOnlyList<IPAddress>>(addresses);
    }
}
