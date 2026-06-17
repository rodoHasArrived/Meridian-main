using System.Net;
using FluentAssertions;
using Meridian.Application.Integrations;
using Meridian.Contracts.Integrations;

namespace Meridian.Tests.Application.Integrations;

public sealed class ProviderIntegrationHttpClientTransportTests
{
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
        var transport = new ProviderIntegrationHttpClientTransport(httpClient);

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
                BodyTemplate: null));

        response.StatusCode.Should().Be(202);
        response.Body.Should().Be("""{"ok":true}""");
        response.Headers.Should().Contain("X-Provider-Cursor", "cursor-2");
        handler.Requests.Should().ContainSingle();
        var request = handler.Requests.Single();
        request.Method.Should().Be(HttpMethod.Get);
        request.RequestUri!.AbsoluteUri.Should().Be("https://provider.example.test/v1/accounts/A-100/positions?asOf=2026-06-16&cursor=next%20page");
        request.Headers.Accept.ToString().Should().Be("application/json");
    }

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
}
