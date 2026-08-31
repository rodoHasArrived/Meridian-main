using System.Net;
using System.Text;
using FluentAssertions;
using Meridian.FinancialOperations.Reconciliation.Connectors.IbFlex;
using Xunit;

namespace Meridian.Tests.Reconciliation.Connectors;

public sealed class IbFlexWebServiceClientTests
{
    [Fact]
    public async Task FetchStatementAsync_GenerationInProgress_PollsAndReturnsFlexReport()
    {
        var requests = new List<Uri>();
        var responses = new Queue<string>(
        [
            "<FlexStatementResponse><Status>Success</Status><ReferenceCode>ref-42</ReferenceCode><Url>https://gdcdyn.interactivebrokers.com/Universal/servlet/FlexStatementService.GetStatement</Url></FlexStatementResponse>",
            "<FlexStatementResponse><Status>Warn</Status><ErrorCode>1019</ErrorCode><ErrorMessage>Statement generation in progress. Please try again shortly.</ErrorMessage></FlexStatementResponse>",
            "<FlexQueryResponse queryName=\"Meridian\"><FlexStatements count=\"0\" /></FlexQueryResponse>"
        ]);
        using var http = new HttpClient(new StubHandler(request =>
        {
            requests.Add(request.RequestUri!);
            return Xml(responses.Dequeue());
        }));
        var sut = new IbFlexWebServiceClient(http, TimeSpan.Zero, maxRetrieveAttempts: 3);

        var content = await sut.FetchStatementAsync("secret-token", "query-7");

        Encoding.UTF8.GetString(content.Span).Should().Contain("FlexQueryResponse");
        requests.Should().HaveCount(3);
        requests[0].AbsolutePath.Should().EndWith("/SendRequest");
        requests[0].Query.Should().Contain("q=query-7").And.Contain("v=3");
        requests[1].Host.Should().Be("gdcdyn.interactivebrokers.com");
        requests[1].Query.Should().Contain("q=ref-42").And.Contain("v=3");
    }

    [Fact]
    public async Task FetchStatementAsync_UntrustedRetrievalHost_FailsClosed()
    {
        using var http = new HttpClient(new StubHandler(_ => Xml(
            "<FlexStatementResponse><Status>Success</Status><ReferenceCode>ref-42</ReferenceCode><Url>https://attacker.example/statement</Url></FlexStatementResponse>")));
        var sut = new IbFlexWebServiceClient(http, TimeSpan.Zero);

        var act = () => sut.FetchStatementAsync("secret-token", "query-7");

        await act.Should().ThrowAsync<InvalidDataException>().WithMessage("*untrusted*");
    }

    [Fact]
    public async Task FetchStatementAsync_ServiceError_PreservesProviderCodeWithoutReturningPartialData()
    {
        using var http = new HttpClient(new StubHandler(_ => Xml(
            "<FlexStatementResponse><Status>Fail</Status><ErrorCode>1012</ErrorCode><ErrorMessage>Token has expired.</ErrorMessage></FlexStatementResponse>")));
        var sut = new IbFlexWebServiceClient(http, TimeSpan.Zero);

        var act = () => sut.FetchStatementAsync("expired-token", "query-7");

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*1012*Token has expired*");
    }

    private static HttpResponseMessage Xml(string value) => new(HttpStatusCode.OK)
    {
        Content = new StringContent(value, Encoding.UTF8, "application/xml")
    };

    private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> responder) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            return Task.FromResult(responder(request));
        }
    }
}
