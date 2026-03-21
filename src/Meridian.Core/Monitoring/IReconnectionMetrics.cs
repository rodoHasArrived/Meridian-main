namespace Meridian.Application.Monitoring;

/// <summary>
/// Abstraction for recording WebSocket reconnection metrics.
/// Defined in Core so that Infrastructure-layer helpers can record
/// reconnection outcomes without depending on the Application layer.
/// The default implementation delegates to <see cref="NullReconnectionMetrics"/>
/// (no-op); the real Prometheus-backed implementation is registered in DI
/// by the Application composition root.
/// </summary>
public interface IReconnectionMetrics
{
    /// <summary>
    /// Records a provider reconnection attempt with its outcome.
    /// </summary>
    /// <param name="provider">Provider name (e.g., "Polygon", "NYSE").</param>
    /// <param name="success">Whether the reconnection attempt succeeded.</param>
    void RecordAttempt(string provider, bool success);
}

/// <summary>
/// No-op implementation used when no metrics sink is configured.
/// </summary>
public sealed class NullReconnectionMetrics : IReconnectionMetrics
{
    public static readonly NullReconnectionMetrics Instance = new();

    public void RecordAttempt(string provider, bool success) { }
}
