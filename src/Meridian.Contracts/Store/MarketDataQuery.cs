using Meridian.Contracts.Domain;
using Meridian.Contracts.Domain.Enums;

namespace Meridian.Contracts.Store;

/// <summary>
/// Immutable query specification for <c>IMarketDataStore</c>.
/// All parameters are optional — an empty query returns all stored events up to
/// <see cref="Limit"/>.
/// </summary>
/// <param name="Symbol">Restrict results to a single instrument.  When <see langword="null"/>,
/// events for all symbols are returned.</param>
/// <param name="From">Inclusive lower bound on the event timestamp.
/// When <see langword="null"/>, there is no lower bound.</param>
/// <param name="To">Exclusive upper bound on the event timestamp.
/// When <see langword="null"/>, there is no upper bound.</param>
/// <param name="EventType">Restrict results to a specific <see cref="MarketEventType"/>.
/// When <see langword="null"/>, all event types are returned.</param>
/// <param name="Source">Restrict results to a named data source (e.g. <c>"alpaca"</c>).
/// When <see langword="null"/>, all sources are returned.</param>
/// <param name="Limit">Maximum number of events to return.  When <see langword="null"/>,
/// all matching events are returned.</param>
public sealed record MarketDataQuery(
    SymbolId? Symbol = null,
    DateTimeOffset? From = null,
    DateTimeOffset? To = null,
    MarketEventType? EventType = null,
    string? Source = null,
    int? Limit = null)
{
    /// <summary>Returns a new query that adds a symbol filter.</summary>
    public MarketDataQuery WithSymbol(SymbolId symbol) => this with { Symbol = symbol };

    /// <summary>Returns a new query restricted to a time window.</summary>
    public MarketDataQuery WithTimeRange(DateTimeOffset from, DateTimeOffset to)
        => this with { From = from, To = to };

    /// <summary>Returns a new query restricted to a specific event type.</summary>
    public MarketDataQuery WithEventType(MarketEventType type) => this with { EventType = type };

    /// <summary>Returns a new query restricted to a specific data source.</summary>
    public MarketDataQuery WithSource(string source) => this with { Source = source };

    /// <summary>Returns a new query with a maximum result count.</summary>
    public MarketDataQuery WithLimit(int limit) => this with { Limit = limit };
}
