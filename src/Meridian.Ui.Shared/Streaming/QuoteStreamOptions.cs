namespace Meridian.Ui.Shared.Streaming;

/// <summary>
/// Tuning knobs for the workstation quote-stream fan-out. Bindable from configuration
/// under <see cref="SectionName"/>.
/// </summary>
public sealed class QuoteStreamOptions
{
    /// <summary>Configuration section name.</summary>
    public const string SectionName = "Workstation:QuoteStream";

    /// <summary>
    /// Maximum push cadence: dirty topics are rebuilt and fanned out at most this often.
    /// Also the floor for how quickly a change reaches subscribers.
    /// </summary>
    public int CoalesceIntervalMs { get; set; } = 250;

    /// <summary>Maximum concurrent stream connections per session.</summary>
    public int MaxConcurrentStreamsPerSession { get; set; } = 4;

    /// <summary>
    /// Per-subscriber channel capacity. 1 means latest-snapshot-wins (drop-oldest), so a
    /// slow client can never block the broadcaster — it only drops intermediate snapshots.
    /// </summary>
    public int SubscriberChannelCapacity { get; set; } = 1;
}
