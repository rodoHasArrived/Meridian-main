using System.Net;
using System.Text;
using FluentAssertions;
using Meridian.Contracts.Api;

namespace Meridian.Tests.Contracts.Api;

public sealed class UiApiClientTests
{
    [Fact]
    public async Task GetOptionsTrackedUnderlyingsAsync_ParsesWrappedResponse()
    {
        using var httpClient = new HttpClient(new StubHttpMessageHandler(
            HttpStatusCode.OK,
            """
            {
              "underlyings": ["AAPL", "SPY"],
              "count": 2,
              "timestamp": "2026-04-07T00:00:00Z"
            }
            """));
        var sut = new UiApiClient(httpClient, "http://localhost:8080");

        var underlyings = await sut.GetOptionsTrackedUnderlyingsAsync();

        underlyings.Should().Equal("AAPL", "SPY");
    }

    private sealed class StubHttpMessageHandler : HttpMessageHandler
    {
        private readonly HttpStatusCode _statusCode;
        private readonly string _content;

        public StubHttpMessageHandler(HttpStatusCode statusCode, string content)
        {
            _statusCode = statusCode;
            _content = content;
        }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(new HttpResponseMessage(_statusCode)
            {
                Content = new StringContent(_content, Encoding.UTF8, "application/json")
            });
        }
    }
}
