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
    /// The runtime failover service, or null if failover is disabled.
    /// </summary>
    public StreamingFailoverService? Service { get; set; }

    /// <summary>
    /// Whether a runtime failover service is available.
    /// </summary>
    public bool IsAvailable => Service is not null;
}
