namespace Meridian.DataIntegration.Monitoring;

/// <summary>
/// Process-wide hand-off of the running collector's <see cref="ConnectionHealthMonitor"/> to
/// the API host.
/// </summary>
/// <remarks>
/// The collector and the API host compose separate service graphs, even when one process runs
/// both (<c>--mode desktop</c>). The collector publishes its monitor here for as long as it
/// streams; the API host reads it per request and reports "not configured" while no collector
/// runs in the process. Mirrors <c>StreamingFailoverRegistry</c>.
/// </remarks>
public sealed class ConnectionHealthRegistry
{
    private ConnectionHealthMonitor? _monitor;

    /// <summary>The registry shared by everything in this process.</summary>
    public static ConnectionHealthRegistry Process { get; } = new();

    /// <summary>The monitor of the collector streaming in this process, if any.</summary>
    public ConnectionHealthMonitor? Monitor => Volatile.Read(ref _monitor);

    /// <summary>
    /// Publishes <paramref name="monitor"/> until the returned handle is disposed. Disposing
    /// clears the registry only if it still holds this monitor.
    /// </summary>
    public IDisposable Publish(ConnectionHealthMonitor monitor)
    {
        ArgumentNullException.ThrowIfNull(monitor);
        Volatile.Write(ref _monitor, monitor);
        return new Publication(this, monitor);
    }

    private sealed class Publication(ConnectionHealthRegistry registry, ConnectionHealthMonitor monitor) : IDisposable
    {
        private int _disposed;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 0)
                Interlocked.CompareExchange(ref registry._monitor, null, monitor);
        }
    }
}
