namespace Meridian.Domain.Events;

/// <summary>
/// Exposes pipeline backpressure state so upstream producers can self-throttle
/// instead of having events silently dropped.
/// </summary>
/// <remarks>
/// Producers (adapters, collectors) should query <see cref="IsUnderPressure"/> before
/// publishing a new event and pause or reduce their publishing rate when the signal is active.
/// The <see cref="QueueUtilization"/> property can be used for more nuanced throttling
/// (e.g. introduce delay when utilization exceeds a threshold).
/// </remarks>
public interface IBackpressureSignal
{
    /// <summary>
    /// Returns <see langword="true"/> when the downstream queue utilization has reached or
    /// exceeded the warning threshold (typically 80 %).
    /// Producers should pause or slow down when this returns <see langword="true"/>.
    /// </summary>
    bool IsUnderPressure { get; }

    /// <summary>
    /// Current queue fill level as a value between 0.0 and 1.0 (0 % – 100 %).
    /// Useful for graduated throttling: e.g. skip non-critical events above 0.9.
    /// </summary>
    double QueueUtilization { get; }
}
