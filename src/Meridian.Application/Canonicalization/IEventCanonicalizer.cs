using Meridian.Domain.Events;

namespace Meridian.Application.Canonicalization;

/// <summary>
/// Transforms a raw <see cref="MarketEvent"/> into a canonicalized event by resolving symbols,
/// mapping condition codes, and normalizing venue identifiers.
/// Runs <b>before</b> <c>EventPipeline.PublishAsync()</c> to avoid adding latency to the
/// high-throughput sink path.
/// </summary>
public interface IEventCanonicalizer
{
    /// <summary>
    /// Canonicalizes a raw market event. Returns the enriched event with canonical fields populated.
    /// The original <see cref="MarketEvent.Symbol"/> is never mutated.
    /// </summary>
    /// <param name="raw">The raw event from a provider adapter.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The canonicalized event with <see cref="MarketEvent.Tier"/> set to <c>Enriched</c>.</returns>
    MarketEvent Canonicalize(MarketEvent raw, CancellationToken ct = default);
}
