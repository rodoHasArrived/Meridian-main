using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using Meridian.Ui.Services;

namespace Meridian.Wpf.Tests.Support;

public sealed class LoopbackFastRefusalHandlerTests
{
    [Fact]
    public async Task CompatibilityClient_RefusesAnUnusedLoopbackPortAsAConnectionFailure()
    {
        var port = ReserveUnusedLoopbackPort();
        using var client = HttpClientFactoryProvider.CreateClient(HttpClientNames.Default);

        var exception = await Record.ExceptionAsync(() => client.GetAsync($"http://localhost:{port}/api/status"));

        var refusal = exception.Should().BeOfType<HttpRequestException>().Subject;
        refusal.HttpRequestError.Should().Be(HttpRequestError.ConnectionError);
        refusal.InnerException.Should().BeOfType<SocketException>()
            .Which.SocketErrorCode.Should().Be(SocketError.ConnectionRefused);
    }

    [Fact]
    public void Refusal_IsLimitedToLoopbackPortsWithoutAListener()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        try
        {
            var livePort = ((IPEndPoint)listener.LocalEndpoint).Port;

            LoopbackFastRefusalHandler.TryCreateRefusal(Get($"http://127.0.0.1:{livePort}/")).Should().BeNull();
            LoopbackFastRefusalHandler.TryCreateRefusal(Get("https://backend.example.test/")).Should().BeNull();
        }
        finally
        {
            listener.Stop();
        }

        LoopbackFastRefusalHandler.TryCreateRefusal(Get($"http://127.0.0.1:{ReserveUnusedLoopbackPort()}/"))
            .Should().NotBeNull();
    }

    [Fact]
    public async Task CanceledRequest_StaysCanceledRatherThanRefused()
    {
        using var client = HttpClientFactoryProvider.CreateClient(HttpClientNames.Default);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        var exception = await Record.ExceptionAsync(
            () => client.GetAsync($"http://localhost:{ReserveUnusedLoopbackPort()}/", cancellation.Token));

        exception.Should().BeAssignableTo<OperationCanceledException>();
    }

    private static HttpRequestMessage Get(string uri) => new(HttpMethod.Get, uri);

    private static int ReserveUnusedLoopbackPort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }
}
