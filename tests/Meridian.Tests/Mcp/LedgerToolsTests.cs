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
        var tools = new LedgerTools(new LedgerApiClient(client));
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
        var tools = new LedgerTools(new LedgerApiClient(client));
        await Assert.ThrowsAsync<HttpRequestException>(() => tools.GetAccountsAsync("run-1"));
        await Assert.ThrowsAsync<ArgumentException>(() => tools.GetAccountsAsync("../other"));
        await Assert.ThrowsAsync<ArgumentException>(() => tools.GetSymbolLedgerAsync("run-1", ".."));
    }

    private sealed class RecordingHandler(HttpStatusCode status) : HttpMessageHandler
    {
        public HttpRequestMessage? Request { get; private set; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Request = request;
            return Task.FromResult(new HttpResponseMessage(status) { Content = new StringContent("[]") });
        }
    }
}
