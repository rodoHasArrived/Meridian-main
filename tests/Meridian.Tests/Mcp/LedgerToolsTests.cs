using System.Net;
using FluentAssertions;
using Meridian.Mcp.Tools;
using Xunit;

namespace Meridian.Tests.Mcp;

public sealed class LedgerToolsTests
{
    [Fact]
    public async Task TrialBalance_UsesAuthorizedApiReadAndEscapesRunIdentifier()
    {
        var handler = new Handler(HttpStatusCode.OK, "{\"lines\":[]}");
        using var client = new HttpClient(handler) { BaseAddress = new Uri("http://localhost:8080/") };
        var tool = new LedgerTools(new Factory(client));
        var result = await tool.GetRunTrialBalanceAsync("run?x=y", DateTimeOffset.Parse("2026-01-31T23:59:59Z"));
        result.Should().Be("{\"lines\":[]}");
        handler.LastUri!.AbsolutePath.Should().Contain("run%3Fx%3Dy/ledger/trial-balance");
        handler.LastUri.Query.Should().StartWith("?asOf=");
    }

    [Theory]
    [InlineData(HttpStatusCode.Forbidden, "refused")]
    [InlineData(HttpStatusCode.NotFound, "unavailable")]
    [InlineData(HttpStatusCode.InternalServerError, "HTTP 500")]
    public async Task TrialBalance_RefusalDoesNotDiscloseResponseBody(HttpStatusCode status, string expected)
    {
        using var client = new HttpClient(new Handler(status, "sensitive backend details")) { BaseAddress = new Uri("http://localhost/") };
        var result = await new LedgerTools(new Factory(client)).GetRunTrialBalanceAsync("run", DateTimeOffset.UtcNow);
        result.Should().Contain(expected).And.NotContain("sensitive");
    }

    private sealed class Factory(HttpClient client) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => client;
    }

    private sealed class Handler(HttpStatusCode status, string content) : HttpMessageHandler
    {
        public Uri? LastUri { get; private set; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            LastUri = request.RequestUri;
            return Task.FromResult(new HttpResponseMessage(status) { Content = new StringContent(content) });
        }
    }
}
