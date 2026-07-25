using System.Net;
using FluentAssertions;
using Meridian.Ui.Services;
using Xunit;

namespace Meridian.Tests.UiServices;

/// <summary>
/// Coverage for the desktop API session seam (audit finding P8): the shared cookie jar
/// resolves the server-issued CSRF token per base URL, sign-out expires it, and mutating
/// requests built by <see cref="ApiClientService"/> echo it as the X-CSRF-Token header.
/// </summary>
public sealed class ApiClientSessionTests : IDisposable
{
    private const string BaseUrl = "http://localhost:8080";
    private const string AlternateBaseUrl = "http://localhost:9090";

    public void Dispose()
    {
        ApiClientSession.Clear(BaseUrl);
        ApiClientSession.Clear(AlternateBaseUrl);
        ApiClientService.Instance.Configure(BaseUrl);
    }

    [Fact]
    public void GetCsrfToken_ReturnsServerIssuedCookieForBaseUrl()
    {
        ApiClientSession.Cookies.Add(new Uri(BaseUrl), new Cookie(ApiClientSession.CsrfCookieName, "csrf-token-1"));

        ApiClientSession.GetCsrfToken(BaseUrl).Should().Be("csrf-token-1");
        ApiClientSession.GetCsrfToken("http://other-host:9999").Should().BeNull(
            "cookies must never leak across hosts");
        ApiClientSession.GetCsrfToken("not a url").Should().BeNull();
    }

    [Fact]
    public void Clear_ExpiresTheSessionCookies()
    {
        ApiClientSession.Cookies.Add(new Uri(BaseUrl), new Cookie(ApiClientSession.CsrfCookieName, "csrf-token-2"));
        ApiClientSession.Cookies.Add(new Uri(BaseUrl), new Cookie(ApiClientSession.SessionCookieName, "session-token"));

        ApiClientSession.Clear(BaseUrl);

        ApiClientSession.GetCsrfToken(BaseUrl).Should().BeNull(
            "desktop sign-out must expire the stored session cookies");
    }

    [Fact]
    public void Configure_RemovesSessionCookiesFromPreviousEndpoint()
    {
        ApiClientService.Instance.Configure(BaseUrl);
        ApiClientSession.Cookies.Add(new Uri(BaseUrl), new Cookie(ApiClientSession.CsrfCookieName, "csrf-token-4"));

        ApiClientService.Instance.Configure(AlternateBaseUrl);

        ApiClientSession.GetCsrfToken(AlternateBaseUrl).Should().BeNull(
            "cookies are scoped to hosts rather than ports and must not survive an endpoint switch");
    }

    [Fact]
    public async Task PostWithResponseAsync_EchoesCsrfCookieAsHeader()
    {
        ApiClientSession.Cookies.Add(new Uri(BaseUrl), new Cookie(ApiClientSession.CsrfCookieName, "csrf-token-3"));
        var handler = new RecordingHandler();
        using var client = new HttpClient(handler);

        var response = await ApiClientService.Instance.PostWithResponseAsync<ProbeDto>(
            "/api/probe", new { value = 1 }, CancellationToken.None, client);

        response.Success.Should().BeTrue();
        handler.LastRequest.Should().NotBeNull();
        handler.LastRequest!.Headers.TryGetValues(ApiClientSession.CsrfHeaderName, out var values)
            .Should().BeTrue("session-authenticated mutations must carry the CSRF header");
        values.Should().ContainSingle().Which.Should().Be("csrf-token-3");
    }

    [Fact]
    public async Task PostWithResponseAsync_OmitsCsrfHeaderBeforeLogin()
    {
        ApiClientSession.Clear(BaseUrl);
        var handler = new RecordingHandler();
        using var client = new HttpClient(handler);

        await ApiClientService.Instance.PostWithResponseAsync<ProbeDto>(
            "/api/probe", new { value = 1 }, CancellationToken.None, client);

        handler.LastRequest!.Headers.Contains(ApiClientSession.CsrfHeaderName).Should().BeFalse(
            "without a login session there is no CSRF cookie to echo");
    }

    private sealed class ProbeDto
    {
        public int Value { get; set; }
    }

    private sealed class RecordingHandler : HttpMessageHandler
    {
        public HttpRequestMessage? LastRequest { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            LastRequest = request;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("""{"value":1}""", System.Text.Encoding.UTF8, "application/json")
            });
        }
    }
}
