using Meridian.Application.Composition.Startup;

namespace Meridian;

internal static class DashboardServerBridge
{
    public static IHostDashboardServer Create(string configPath, int port)
        => new UiDashboardServer(new UiServer(configPath, port));

    private sealed class UiDashboardServer : IHostDashboardServer
    {
        private readonly UiServer _inner;

        public UiDashboardServer(UiServer inner)
        {
            _inner = inner;
        }

        public Task StartAsync(CancellationToken ct = default)
            => _inner.StartAsync(ct);

        public Task StopAsync(CancellationToken ct = default)
            => _inner.StopAsync(ct);

        public ValueTask DisposeAsync()
            => _inner.DisposeAsync();
    }
}
