using System.Net.Http;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Runtime.CompilerServices;
using Meridian.Ui.Services;

namespace Meridian.Wpf.Tests.Support;

/// <summary>
/// Reports "connection refused" for a loopback port nobody is listening on without opening a socket.
/// </summary>
/// <remarks>
/// The desktop services under test call the backend at <c>http://localhost:8080</c>, and no backend
/// runs during this suite. Windows takes about two seconds to report a refused loopback connection,
/// per address family, and the named clients then retry with backoff, so a single view model
/// refresh cost tens of seconds of pure waiting. The outcome is unchanged: the caller still gets an
/// <see cref="HttpRequestException"/> wrapping <see cref="SocketError.ConnectionRefused"/>. A request
/// to a loopback port that does have a listener, or to any other host, goes to the real handler.
/// </remarks>
internal sealed class LoopbackFastRefusalHandler : DelegatingHandler
{
    public LoopbackFastRefusalHandler()
    {
    }

    public LoopbackFastRefusalHandler(HttpMessageHandler innerHandler)
        : base(innerHandler)
    {
    }

    [ModuleInitializer]
    internal static void InstallForCompatibilityClients()
        => HttpClientFactoryProvider.PrimaryHandlerDecoratorForTests =
            static handler => new LoopbackFastRefusalHandler(handler);

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        if (cancellationToken.IsCancellationRequested)
        {
            return Task.FromCanceled<HttpResponseMessage>(cancellationToken);
        }

        return TryCreateRefusal(request) is { } refusal
            ? Task.FromException<HttpResponseMessage>(refusal)
            : base.SendAsync(request, cancellationToken);
    }

    protected override HttpResponseMessage Send(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (TryCreateRefusal(request) is { } refusal)
        {
            throw refusal;
        }

        return base.Send(request, cancellationToken);
    }

    internal static HttpRequestException? TryCreateRefusal(HttpRequestMessage request)
    {
        if (request.RequestUri is not { IsAbsoluteUri: true, IsLoopback: true } uri || HasListener(uri.Port))
        {
            return null;
        }

        return new HttpRequestException(
            HttpRequestError.ConnectionError,
            $"No connection could be made because the target machine actively refused it. ({uri.Host}:{uri.Port})",
            new SocketException((int)SocketError.ConnectionRefused));
    }

    private static bool HasListener(int port)
    {
        try
        {
            foreach (var endpoint in IPGlobalProperties.GetIPGlobalProperties().GetActiveTcpListeners())
            {
                if (endpoint.Port == port)
                {
                    return true;
                }
            }

            return false;
        }
        catch (NetworkInformationException)
        {
            // Cannot tell: let the real handler decide.
            return true;
        }
    }
}
