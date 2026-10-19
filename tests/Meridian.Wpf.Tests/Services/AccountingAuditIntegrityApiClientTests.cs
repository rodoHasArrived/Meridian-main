using System.Net;
using System.Net.Http;
using System.Text.Json;
using Meridian.Contracts.Api;
using Meridian.Contracts.Ledger;
using Meridian.Ui.Services;
using Meridian.Wpf.Services;

namespace Meridian.Wpf.Tests.Services;

public sealed class AccountingAuditIntegrityApiClientTests
{
    [Fact]
    public async Task Verify_ReadsTheReportFromTheAuditIntegrityRoute()
    {
        string? path = null;
        var report = new AccountingAuditIntegrityReportDto(DateTimeOffset.UnixEpoch, true,
            [new(AuditChainIdentifiers.AccountingActions, AuditChainIntegrityStatus.Verified, 4, 0)]);
        using var api = new ApiClientService(new Factory(new Handler(request =>
        {
            path = request.RequestUri!.AbsolutePath;
            request.Method.Should().Be(HttpMethod.Get);
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(JsonSerializer.Serialize(report, new JsonSerializerOptions(JsonSerializerDefaults.Web)),
                    System.Text.Encoding.UTF8, "application/json")
            };
        })));

        var result = await new AccountingAuditIntegrityApiClient(api).VerifyAsync();

        path.Should().Be(UiApiRoutes.LedgerAuditIntegrity);
        result.Outcome.Should().Be(AuditIntegrityCheckOutcome.Completed);
        result.Report!.AllVerified.Should().BeTrue();
        result.Report.Chains.Should().ContainSingle().Which.LinksChecked.Should().Be(4);
    }

    [Theory]
    [InlineData(HttpStatusCode.Forbidden, AuditIntegrityCheckOutcome.Forbidden)]
    [InlineData(HttpStatusCode.TooManyRequests, AuditIntegrityCheckOutcome.Busy)]
    [InlineData(HttpStatusCode.ServiceUnavailable, AuditIntegrityCheckOutcome.Failed)]
    public async Task Verify_ReturnsRefusalsAsOutcomesWithoutAReport(HttpStatusCode status, AuditIntegrityCheckOutcome expected)
    {
        using var api = new ApiClientService(new Factory(new Handler(_ => new HttpResponseMessage(status)
        { Content = new StringContent("refused") })));

        var result = await new AccountingAuditIntegrityApiClient(api).VerifyAsync();

        result.Outcome.Should().Be(expected);
        result.Report.Should().BeNull();
    }

    private sealed class Factory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler, disposeHandler: false);
    }

    private sealed class Handler(Func<HttpRequestMessage, HttpResponseMessage> responder) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(responder(request));
    }
}
