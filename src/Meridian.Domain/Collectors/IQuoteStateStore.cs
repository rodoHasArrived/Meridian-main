using System.Collections.Generic;
using Meridian.Contracts.Domain.Models;
using Meridian.Domain.Events;
using Meridian.Domain.Models;

namespace Meridian.Domain.Collectors;

/// <summary>
/// Provides access to the latest Best-Bid/Offer quote per symbol for downstream inference (e.g., aggressor side).
/// </summary>
public interface IQuoteStateStore
{
    /// <summary>Try get the latest BBO for a symbol.</summary>
    bool TryGet(string symbol, out BboQuotePayload? quote);

    /// <summary>
    /// Try get the latest BBO for a symbol only when it came from <paramref name="source"/>
    /// (the canonical provider id, compared case-insensitively). Returns <see langword="false"/>
    /// when the latest quote is from another provider, so one feed's trades are never classified
    /// against another feed's book. Stores that do not track the quoting source return
    /// <see langword="false"/>.
    /// </summary>
    bool TryGetFromSource(string symbol, string source, out BboQuotePayload? quote)
    {
        quote = null;
        return false;
    }

    /// <summary>
    /// Upsert a BBO update and return the resulting payload that callers can reuse for publishing/logging.
    /// </summary>
    /// <remarks>
    /// Implementations should treat the incoming update as authoritative, replacing any existing entry for the symbol.
    /// </remarks>
    BboQuotePayload Upsert(MarketQuoteUpdate update);

    /// <summary>
    /// Remove cached state for a symbol. Returns <c>true</c> if the symbol existed.
    /// </summary>
    bool TryRemove(string symbol, out BboQuotePayload? removed);

    /// <summary>
    /// Snapshot the current cache for inspection/monitoring without exposing internal mutability.
    /// </summary>
    IReadOnlyDictionary<string, BboQuotePayload> Snapshot();
}
