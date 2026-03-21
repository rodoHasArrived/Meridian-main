using Meridian.Domain.Events;

namespace Meridian.Application.Filters;

public sealed class MarketEventFilter
{
    public string? Symbol { get; init; }
    public MarketEventType? Type { get; init; }
    public MarketEventTier? Tier { get; init; }

    public bool Matches(in MarketEvent evt)
    {
        if (!string.IsNullOrWhiteSpace(Symbol) &&
            !string.Equals(evt.Symbol, Symbol, StringComparison.OrdinalIgnoreCase))
            return false;

        if (Type.HasValue && evt.Type != Type.Value)
            return false;
        if (Tier.HasValue && evt.Tier != Tier.Value)
            return false;

        return true;
    }

    public static MarketEventFilter All => new();
}
