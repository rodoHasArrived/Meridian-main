using Meridian.Contracts.Catalog;
using Meridian.Contracts.Domain.Enums;
using Meridian.Contracts.Domain.Models;
using Meridian.Domain.Events;

using ContractPayload = Meridian.Contracts.Domain.Events.MarketEventPayload;

namespace Meridian.DataIntegration.Canonicalization;

/// <summary>
/// Default canonicalization implementation that resolves symbols, maps condition codes,
/// and normalizes venue identifiers using in-memory lookup tables.
/// Follows the <c>with</c> expression pattern established by <see cref="Domain.Events.MarketEvent.StampReceiveTime"/>.
/// </summary>
public sealed class EventCanonicalizer : IEventCanonicalizer
{
    private readonly ICanonicalSymbolRegistry _symbols;
    private readonly ConditionCodeMapper _conditions;
    private readonly VenueMicMapper _venues;
    private readonly byte _version;
    private readonly ICanonicalSecurityIdLookup? _securityIdLookup;

    public EventCanonicalizer(
        ICanonicalSymbolRegistry symbols,
        ConditionCodeMapper conditions,
        VenueMicMapper venues,
        byte version = 1,
        ICanonicalSecurityIdLookup? securityIdLookup = null)
    {
        _symbols = symbols ?? throw new ArgumentNullException(nameof(symbols));
        _conditions = conditions ?? throw new ArgumentNullException(nameof(conditions));
        _venues = venues ?? throw new ArgumentNullException(nameof(venues));
        _version = version;
        _securityIdLookup = securityIdLookup;
    }

    /// <inheritdoc />
    public MarketEvent Canonicalize(MarketEvent raw, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(raw);

        // Skip heartbeats and already-canonicalized events
        if (raw.Type == MarketEventType.Heartbeat || raw.CanonicalizationVersion > 0)
            return raw;

        // Symbol resolution: use provider-aware resolution first, fall back to generic
        var providerCanonical = _symbols.TryResolve(raw.Symbol, raw.Source);
        var canonicalSymbol = string.IsNullOrWhiteSpace(providerCanonical)
            ? _symbols.ResolveToCanonical(raw.Symbol)
            : providerCanonical;

        // Security Master cross-reference: tag with security_id from the seed lookup
        // (cache hit only — no DB round-trip on the hot path).
        var securityId = canonicalSymbol is not null
            ? _securityIdLookup?.TryGetSecurityId(canonicalSymbol)
            : null;

        // Venue normalization
        var rawVenue = ExtractVenue(raw.Payload);
        var canonicalVenue = _venues.TryMapVenue(rawVenue, raw.Source);

        var result = raw with
        {
            CanonicalSymbol = canonicalSymbol,
            SecurityId = securityId,
            CanonicalVenue = canonicalVenue,
            CanonicalizationVersion = _version,
            Tier = raw.Tier < MarketEventTier.Enriched ? MarketEventTier.Enriched : raw.Tier
        };

        // Condition code mapping: apply for Trade payloads that carry raw conditions.
        if (raw.Payload is Trade trade && trade.RawConditions is { Length: > 0 })
        {
            var (canonical, _) = _conditions.MapConditions(raw.Source, trade.RawConditions);
            result = result with { Payload = trade with { CanonicalConditions = canonical } };
        }

        return result;
    }

    /// <summary>
    /// Extracts the venue string from a market event payload, if present.
    /// </summary>
    private static string? ExtractVenue(ContractPayload payload) => payload switch
    {
        Trade trade => trade.Venue,
        BboQuotePayload bbo => bbo.Venue,
        LOBSnapshot lob => lob.Venue,
        L2SnapshotPayload l2 => l2.Venue,
        OrderFlowStatistics ofs => ofs.Venue,
        IntegrityEvent integrity => integrity.Venue,
        _ => null
    };
}
