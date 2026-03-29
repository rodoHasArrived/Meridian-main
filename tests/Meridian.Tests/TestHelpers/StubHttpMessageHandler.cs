namespace Meridian.Tests.TestHelpers;

/// <summary>
/// A deterministic HttpMessageHandler stub for unit tests.
/// Responds with a caller-supplied factory function and tracks the number of calls.
/// </summary>
internal sealed class StubHttpMessageHandler : HttpMessageHandler
{
    private readonly Func<HttpRequestMessage, HttpResponseMessage> _responder;

    public StubHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> responder)
    {
        _responder = responder ?? throw new ArgumentNullException(nameof(responder));
    }

    public int CallCount { get; private set; }

    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        CallCount++;
        return Task.FromResult(_responder(request));
    }
}
