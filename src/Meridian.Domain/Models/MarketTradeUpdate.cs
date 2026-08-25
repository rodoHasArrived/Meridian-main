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
    bool SequenceIsContiguous = true
);
