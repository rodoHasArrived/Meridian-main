using System.Net;
using System.Net.Http;
using FluentAssertions;
using Meridian.Ui.Services;

namespace Meridian.Ui.Tests.Services;

public sealed class ApiClientArtifactTests
{
    [Fact]
    public async Task PostArtifactAsync_PreservesBytesAndUsesCapturedEndpointCsrf()
    {
        byte[] expected = [0xEF, 0xBB, 0xBF, 0x31, 0x2C, 0x32, 0x0D, 0x0A];
        using var handler = new ArtifactHandler(async request =>
        {
            request.Method.Should().Be(HttpMethod.Post);
            request.RequestUri!.AbsoluteUri.Should().Be("http://artifact-export.test/api/ledger/periods/period-1/journal-export");
            request.Headers.GetValues(ApiClientSession.CsrfHeaderName).Should().ContainSingle().Which.Should().Be("artifact-csrf");
            (await request.Content!.ReadAsStringAsync()).Should().Be("{\"format\":\"csv\"}");
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(expected) };
        });
        using var service = new ApiClientService(new ArtifactFactory(handler));
        service.Configure("http://artifact-export.test");
        ApiClientSession.Cookies.Add(new Uri(service.BaseUrl), new Cookie(ApiClientSession.CsrfCookieName, "artifact-csrf", "/"));
        try
        {
            var response = await service.PostArtifactAsync("/api/ledger/periods/period-1/journal-export", new { format = "csv" });
            response.Success.Should().BeTrue();
            response.Data.Should().Equal(expected);
        }
        finally { ApiClientSession.Clear(service.BaseUrl); }
    }

    [Fact]
    public async Task PostArtifactAsync_AuthorizationFailureCannotBecomeDownloadableBytes()
    {
        using var handler = new ArtifactHandler(_ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.Forbidden)
            { Content = new StringContent("Export permission is required.") }));
        using var service = new ApiClientService(new ArtifactFactory(handler));

        var response = await service.PostArtifactAsync("/api/ledger/periods/period-1/journal-export", new { format = "csv" });

        response.Success.Should().BeFalse();
        response.StatusCode.Should().Be(403);
        response.Data.Should().BeNull();
        response.ErrorMessage.Should().Contain("Export permission");
    }

    private sealed class ArtifactFactory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler, disposeHandler: false);
    }

    private sealed class ArtifactHandler(Func<HttpRequestMessage, Task<HttpResponseMessage>> send) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => send(request);
    }
}
