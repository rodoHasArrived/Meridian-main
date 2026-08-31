using Meridian.Contracts.Domain.Enums;

namespace Meridian.Domain.Models;

/// <summary>
/// Normalized tick-by-tick trade update (adapter input into TradeDataCollector).
/// This is NOT the stored Trade; it's the raw-ish input model.
/// </summary>
/// <remarks>
/// <paramref name="Source"/> is the canonical provider identity (see
/// <see cref="Meridian.Contracts.Domain.MarketDataSources"/>). Collectors are shared
/// singletons that serve every concurrently active adapter, so provenance must be stamped
/// per event at the adapter origin; a sourceless update is rejected at the collector seam
/// with a missing-source integrity event instead of being silently attributed to a default
/// vendor. A <paramref name="SequenceNumber"/> of 0 means the provider does not sequence
/// this stream; continuity checks are skipped rather than fabricating a sequence.
/// <paramref name="SequenceIsContiguous"/> declares whether the provider's sequence domain
/// is dense (every integer occupied), which is what makes gap inference meaningful. Feeds
/// like Polygon supply sequences that are unique and increasing per ticker but explicitly
/// non-contiguous; they pass <see langword="false"/> so out-of-order/duplicate detection
/// still runs while gaps are not reported as data loss.
/// <paramref name="SequenceStreamId"/> can override the published <paramref name="StreamId"/>
/// and <paramref name="Venue"/> for the collector state that owns continuity and rolling order-flow
/// calculations. This keeps provider trade identity and venue provenance on the event while allowing
/// a sequence documented as per-ticker to share one comparison stream across changing trade IDs and
/// execution venues.
/// <paramref name="SequenceSessionDate"/> further scopes that explicit continuity stream when the
/// provider resets its sequence each trading session. The adapter supplies the provider-market date,
/// not a UTC date inferred by the collector.
/// </remarks>
public sealed record MarketTradeUpdate(
    DateTimeOffset Timestamp,
    string Symbol,
    decimal Price,
    long Size,
    AggressorSide Aggressor,
    long SequenceNumber,
    string? StreamId = null,
    string? Venue = null,
    string[]? RawConditions = null,
    string? Source = null,
    bool SequenceIsContiguous = true,
    string? SequenceStreamId = null,
    DateOnly? SequenceSessionDate = null
);
