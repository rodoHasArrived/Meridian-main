namespace Meridian.Infrastructure.Adapters.Failover;

/// <summary>
/// Singleton registry that holds the runtime <see cref="StreamingFailoverService"/> instance.
/// Allows API endpoints (in the Ui.Shared layer) to access the failover service
/// without requiring a direct project reference to the runtime.
/// </summary>
/// <remarks>
/// Register this as a singleton in DI. Set <see cref="Service"/> from Program.cs
/// after creating the failover service. API endpoints can then resolve it to
/// query live failover state and trigger manual failovers.
/// </remarks>
public sealed class StreamingFailoverRegistry
{
    /// <summary>
    /// Process-wide instance. Desktop mode runs the UI server and the collector in separate DI
    /// containers; both register this instance so the UI reads the collector's live coordinator.
    /// </summary>
    public static StreamingFailoverRegistry Process { get; } = new();

    /// <summary>
    /// The runtime failover service, or null if failover is disabled.
    /// </summary>
    public StreamingFailoverService? Service { get => _service; set => _service = value; }

    private volatile StreamingFailoverService? _service;

    /// <summary>
    /// Whether a runtime failover service is available.
    /// </summary>
    public bool IsAvailable => Service is not null;
}
