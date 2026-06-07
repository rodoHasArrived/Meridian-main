using Meridian.Domain.Events;

namespace Meridian.Application.Pipeline;

/// <summary>
/// Contract for retaining market events that require quarantine or dead-letter review.
/// </summary>
public interface IEventQuarantineSink
{
    /// <summary>
    /// Records an event with the reasons that caused it to be retained for review.
    /// </summary>
    ValueTask RecordAsync(MarketEvent evt, IReadOnlyList<string> errors, CancellationToken ct = default);
}
