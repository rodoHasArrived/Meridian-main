using System.Net;
using System.Net.Http;
using FluentAssertions;
using Meridian.Mcp.Services;
using Meridian.Mcp.Tools;
using Xunit;

namespace Meridian.Tests.Mcp;

public sealed class LedgerToolsTests
{
    [Fact]
    public async Task ReadTools_CallOnlyScopedLedgerGetRoutesWithEncodedQueryValues()
    {
        var handler = new RecordingHandler(HttpStatusCode.OK);
        using var client = new HttpClient(handler) { BaseAddress = new Uri("http://localhost:8080/") };
        var tools = new LedgerTools(new LedgerApiClient(client, new("synthetic-operator", "synthetic-password")));
        await tools.GetRunningBalanceAsync("run-1", "Cash&other=value", financialAccountId: "");
        handler.Request!.Method.Should().Be(HttpMethod.Get);
        handler.Request.RequestUri!.AbsolutePath.Should().Be("/api/workstation/runs/run-1/ledger/running-balance");
        handler.Request.RequestUri.Query.Should().Contain("accountName=Cash%26other%3Dvalue").And.Contain("financialAccountId=");
        await tools.GetTrialBalanceAsync("run-1", "2026-01-02T00:00:00Z");
        handler.Request!.RequestUri!.AbsolutePath.Should().EndWith("trial-balance/as-of");
    }

    [Fact]
    public async Task ReadTools_PropagateAuthorizationRefusalAndRejectTraversal()
    {
        using var client = new HttpClient(new RecordingHandler(HttpStatusCode.Forbidden)) { BaseAddress = new Uri("http://localhost/") };
        var tools = new LedgerTools(new LedgerApiClient(client, new("synthetic-operator", "synthetic-password")));
        await Assert.ThrowsAsync<HttpRequestException>(() => tools.GetAccountsAsync("run-1"));
        await Assert.ThrowsAsync<ArgumentException>(() => tools.GetAccountsAsync("../other"));
        await Assert.ThrowsAsync<ArgumentException>(() => tools.GetSymbolLedgerAsync("run-1", ".."));
    }

    [Fact]
    public async Task TrialBalance_UsesIssuedSessionApiReadAndEscapesRunIdentifier()
    {
        var handler = new RecordingHandler(HttpStatusCode.OK, "{\"lines\":[]}");
        using var client = new HttpClient(handler) { BaseAddress = new Uri("http://localhost:8080/") };
        var tools = new LedgerTools(new LedgerApiClient(client, new("synthetic-operator", "synthetic-password")));

        var result = await tools.GetRunTrialBalanceAsync("run?x=y", DateTimeOffset.Parse("2026-01-31T23:59:59Z"));

        result.Should().Be("{\"lines\":[]}");
        handler.Request!.RequestUri!.AbsolutePath.Should().Contain("run%3Fx%3Dy/ledger/trial-balance");
        handler.Request.RequestUri.Query.Should().StartWith("?asOf=");
        handler.Request.Headers.GetValues("Cookie").Single().Should().Contain("mdc-session=");
        handler.Request.Headers.Authorization.Should().BeNull();
    }

    [Theory]
    [InlineData(HttpStatusCode.Forbidden, "refused")]
    [InlineData(HttpStatusCode.NotFound, "unavailable")]
    [InlineData(HttpStatusCode.InternalServerError, "HTTP 500")]
    public async Task TrialBalance_RefusalDoesNotDiscloseResponseBody(HttpStatusCode status, string expected)
    {
        using var client = new HttpClient(new RecordingHandler(status, "sensitive backend details")) { BaseAddress = new Uri("http://localhost/") };
        var tools = new LedgerTools(new LedgerApiClient(client, new("synthetic-operator", "synthetic-password")));

        var result = await tools.GetRunTrialBalanceAsync("run", DateTimeOffset.UtcNow);

        result.Should().Contain(expected).And.NotContain("sensitive");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task TrialBalance_RefusesResponseAboveTheMcpSizeLimit(bool unknownLength)
    {
        using var client = new HttpClient(new RecordingHandler(HttpStatusCode.OK, new string('x', 1_048_577), unknownLength))
        {
            BaseAddress = new Uri("http://localhost/")
        };
        var tools = new LedgerTools(new LedgerApiClient(client, new("synthetic-operator", "synthetic-password")));

        var result = await tools.GetRunTrialBalanceAsync("run", DateTimeOffset.UtcNow);

        result.Should().Contain("exceeds the MCP response size limit");
    }

    [Fact]
    public async Task TrialBalance_ResponseExactlyAtTheMcpSizeLimitIsReturned()
    {
        var content = new string('x', 1_048_576);
        using var client = new HttpClient(new RecordingHandler(HttpStatusCode.OK, content, unknownLength: true))
        {
            BaseAddress = new Uri("http://localhost/")
        };
        var tools = new LedgerTools(new LedgerApiClient(client, new("synthetic-operator", "synthetic-password")));

        (await tools.GetRunTrialBalanceAsync("run", DateTimeOffset.UtcNow)).Should().Be(content);
    }

    [Fact]
    public async Task ReadTools_ExistingViewsRetainUncappedResponses()
    {
        var content = new string('x', 1_048_577);
        using var client = new HttpClient(new RecordingHandler(HttpStatusCode.OK, content, unknownLength: true))
        {
            BaseAddress = new Uri("http://localhost/")
        };
        var tools = new LedgerTools(new LedgerApiClient(client, new("synthetic-operator", "synthetic-password")));

        (await tools.GetAccountsAsync("run")).Should().Be(content);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1_048_577)]
    public async Task ReadTools_CallerCannotDisableOrRaiseTheResponseLimit(int limit)
    {
        var handler = new RecordingHandler(HttpStatusCode.OK);
        using var client = new HttpClient(handler) { BaseAddress = new Uri("http://localhost/") };
        var api = new LedgerApiClient(client, new("synthetic-operator", "synthetic-password"));

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => api.ReadAsync("run", "accounts", null,
            CancellationToken.None, maxResponseCharacters: limit));
        handler.Request.Should().BeNull();
    }

    [Fact]
    public async Task TrialBalance_StillBoundsTheResponseAfterSessionRenewal()
    {
        var handler = new RenewalHandler();
        using var client = new HttpClient(handler) { BaseAddress = new Uri("http://localhost/") };
        var tools = new LedgerTools(new LedgerApiClient(client, new("synthetic-operator", "synthetic-password")));

        var result = await tools.GetRunTrialBalanceAsync("run", DateTimeOffset.UtcNow);

        result.Should().Contain("exceeds the MCP response size limit");
        handler.LoginCount.Should().Be(2);
        handler.ReadCount.Should().Be(2);
        handler.SessionCookies.Should().OnlyHaveUniqueItems();
    }

    private sealed class RecordingHandler(HttpStatusCode status, string content = "[]", bool unknownLength = false) : HttpMessageHandler
    {
        public HttpRequestMessage? Request { get; private set; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            if (request.RequestUri!.AbsolutePath == "/api/auth/login")
            {
                var login = new HttpResponseMessage(HttpStatusCode.OK);
                login.Headers.Add("Set-Cookie", $"mdc-session={new string('A', 64)}; Path=/; HttpOnly");
                return Task.FromResult(login);
            }
            Request = request;
            return Task.FromResult(new HttpResponseMessage(status)
            {
                Content = unknownLength ? new UnknownLengthContent(content) : new StringContent(content)
            });
        }
    }

    private sealed class UnknownLengthContent(string value) : HttpContent
    {
        private readonly byte[] _bytes = System.Text.Encoding.UTF8.GetBytes(value);

        protected override bool TryComputeLength(out long length)
        {
            length = 0;
            return false;
        }

        protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context) =>
            stream.WriteAsync(_bytes).AsTask();

        protected override Task<Stream> CreateContentReadStreamAsync() =>
            Task.FromResult<Stream>(new MemoryStream(_bytes, writable: false));
    }

    private sealed class RenewalHandler : HttpMessageHandler
    {
        public int LoginCount { get; private set; }
        public int ReadCount { get; private set; }
        public List<string> SessionCookies { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            if (request.RequestUri!.AbsolutePath == "/api/auth/login")
            {
                var login = new HttpResponseMessage(HttpStatusCode.OK);
                login.Headers.Add("Set-Cookie", $"mdc-session={new string((char)('A' + LoginCount++), 64)}; Path=/; HttpOnly");
                return Task.FromResult(login);
            }
            SessionCookies.Add(request.Headers.GetValues("Cookie").Single());
            var first = ReadCount++ == 0;
            return Task.FromResult(new HttpResponseMessage(first ? HttpStatusCode.Unauthorized : HttpStatusCode.OK)
            {
                Content = first ? new StringContent("expired") : new UnknownLengthContent(new string('x', 1_048_577))
            });
        }
    }
}
