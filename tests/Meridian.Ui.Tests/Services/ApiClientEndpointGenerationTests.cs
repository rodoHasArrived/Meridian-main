using System.Collections.Concurrent;
using System.Net;
using FluentAssertions;
using Meridian.Ui.Services;

namespace Meridian.Ui.Tests.Services;

/// <summary>
/// Deterministic coverage for endpoint-generation swaps. Requests are held inside the handler so
/// Configure can run at the exact point that previously disposed their retained HttpClient.
/// </summary>
[Collection("ApiClientService singleton serial")]
public sealed class ApiClientEndpointGenerationTests : IDisposable
{
    private const string OldBaseUrl = "https://old-api.example";
    private const string NewBaseUrl = "https://new-api.example";

    public void Dispose()
    {
        ApiClientSession.Clear(OldBaseUrl);
        ApiClientSession.Clear(NewBaseUrl);
    }

    [Fact]
    public async Task Configure_DuringRetainedUiApiRequest_KeepsEachEndpointAndAuthGenerationCoherent()
    {
        var handler = new EndpointGateHandler(OldBaseUrl);
        using var service = new ApiClientService(new TestHttpClientFactory(handler));
        service.Configure(OldBaseUrl);
        var retainedUiApi = service.UiApi;
        ApiClientSession.Cookies.Add(
            new Uri(OldBaseUrl),
            new Cookie(ApiClientSession.CsrfCookieName, "old-csrf"));

        var oldRequest = retainedUiApi.PostWithResponseAsync<ProbeDto>(
            "/api/probe",
            new { value = 1 });
        await handler.OldRequestStarted.Task;

        service.Configure(NewBaseUrl);
        ApiClientSession.Cookies.Add(
            new Uri(NewBaseUrl),
            new Cookie(ApiClientSession.CsrfCookieName, "new-csrf"));
        var newResponse = await retainedUiApi.PostWithResponseAsync<ProbeDto>(
            "/api/probe",
            new { value = 2 });

        handler.ReleaseOldRequest.TrySetResult();
        var oldResponse = await oldRequest;

        oldResponse.Success.Should().BeTrue();
        newResponse.Success.Should().BeTrue();
        retainedUiApi.BaseUrl.Should().Be(NewBaseUrl);
        handler.Observations.Should().ContainSingle(observation =>
            observation.Uri == $"{OldBaseUrl}/api/probe"
            && observation.CsrfToken == "old-csrf");
        handler.Observations.Should().ContainSingle(observation =>
            observation.Uri == $"{NewBaseUrl}/api/probe"
            && observation.CsrfToken == "new-csrf");
    }

    [Fact]
    public async Task Configure_WithConcurrentConsumers_DrainsTheCapturedGenerationBeforeDisposal()
    {
        var handler = new ConcurrentGateHandler(expectedRequests: 2);
        using var service = new ApiClientService(new TestHttpClientFactory(handler));
        service.Configure(OldBaseUrl);

        var first = service.GetWithResponseAsync<ProbeDto>("/api/one");
        var second = service.GetWithResponseAsync<ProbeDto>("/api/two");
        await handler.AllRequestsStarted.Task;

        service.Configure(NewBaseUrl);
        handler.ReleaseRequests.TrySetResult();

        var responses = await Task.WhenAll(first, second);
        responses.Should().OnlyContain(response => response.Success);
        handler.RequestUris.Should().BeEquivalentTo(
            [$"{OldBaseUrl}/api/one", $"{OldBaseUrl}/api/two"]);
        service.Configuration.Revision.Should().BeGreaterThan(1);
        service.BaseUrl.Should().Be(NewBaseUrl);
    }

    private sealed record ProbeDto(int Value);

    private sealed record RequestObservation(string Uri, string? CsrfToken);

    private sealed class TestHttpClientFactory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name)
            => new(handler, disposeHandler: false);
    }

    private sealed class EndpointGateHandler(string blockedBaseUrl) : HttpMessageHandler
    {
        public TaskCompletionSource OldRequestStarted { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource ReleaseOldRequest { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        public ConcurrentQueue<RequestObservation> Observations { get; } = new();

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            request.Headers.TryGetValues(ApiClientSession.CsrfHeaderName, out var csrfValues);
            Observations.Enqueue(new RequestObservation(
                request.RequestUri!.AbsoluteUri,
                csrfValues?.SingleOrDefault()));

            if (request.RequestUri.AbsoluteUri.StartsWith(blockedBaseUrl, StringComparison.Ordinal))
            {
                OldRequestStarted.TrySetResult();
                await ReleaseOldRequest.Task;
            }

            return JsonResponse();
        }
    }

    private sealed class ConcurrentGateHandler(int expectedRequests) : HttpMessageHandler
    {
        private int _started;
        public TaskCompletionSource AllRequestsStarted { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource ReleaseRequests { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        public ConcurrentQueue<string> RequestUris { get; } = new();

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            RequestUris.Enqueue(request.RequestUri!.AbsoluteUri);
            if (Interlocked.Increment(ref _started) == expectedRequests)
                AllRequestsStarted.TrySetResult();
            await ReleaseRequests.Task;
            return JsonResponse();
        }
    }

    private static HttpResponseMessage JsonResponse()
        => new(HttpStatusCode.OK)
        {
            Content = new StringContent(
                """{"value":1}""",
                System.Text.Encoding.UTF8,
                "application/json")
        };
}
