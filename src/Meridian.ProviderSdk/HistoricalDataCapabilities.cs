namespace Meridian.Infrastructure.Adapters.Core;

/// <summary>
/// Consolidated capability flags for historical data providers.
/// Instead of checking individual boolean properties, consumers can check
/// a single Capabilities object for all supported features.
/// </summary>
/// <remarks>
/// Common capability combinations can be created using static factory methods:
/// - <see cref="BarsOnly"/> for basic daily bar providers
/// - <see cref="FullFeatured"/> for providers supporting all data types
/// </remarks>
public sealed record HistoricalDataCapabilities
{
    /// <summary>Whether the provider returns split/dividend adjusted prices.</summary>
    public bool AdjustedPrices { get; init; }

    /// <summary>Whether the provider supports intraday bar data.</summary>
    public bool Intraday { get; init; }

    /// <summary>Whether the provider includes dividend data.</summary>
    public bool Dividends { get; init; }

    /// <summary>Whether the provider includes split data.</summary>
    public bool Splits { get; init; }

    /// <summary>Whether the provider supports historical quote (NBBO) data.</summary>
    public bool Quotes { get; init; }

    /// <summary>Whether the provider supports historical trade data.</summary>
    public bool Trades { get; init; }

    /// <summary>Whether the provider supports historical auction data.</summary>
    public bool Auctions { get; init; }

    /// <summary>Market regions/countries supported (e.g., "US", "UK", "DE").</summary>
    public IReadOnlyList<string> SupportedMarkets { get; init; } = new[] { "US" };

    /// <summary>Default capabilities: no special features, US market only.</summary>
    public static HistoricalDataCapabilities None { get; } = new();

    /// <summary>Basic bars-only provider with adjusted prices and corporate actions.</summary>
    public static HistoricalDataCapabilities BarsOnly { get; } = new()
    {
        AdjustedPrices = true,
        Dividends = true,
        Splits = true
    };

    /// <summary>Full-featured provider supporting all data types.</summary>
    public static HistoricalDataCapabilities FullFeatured { get; } = new()
    {
        AdjustedPrices = true,
        Intraday = true,
        Dividends = true,
        Splits = true,
        Quotes = true,
        Trades = true,
        Auctions = true
    };

    /// <summary>Create capabilities with custom markets.</summary>
    public HistoricalDataCapabilities WithMarkets(params string[] markets) =>
        this with { SupportedMarkets = markets };

    /// <summary>Check if provider supports a specific market.</summary>
    public bool SupportsMarket(string market) =>
        SupportedMarkets.Contains(market, StringComparer.OrdinalIgnoreCase);

    /// <summary>Check if provider has any tick-level data capabilities.</summary>
    public bool HasTickData => Quotes || Trades || Auctions;

    /// <summary>Check if provider has corporate action data.</summary>
    public bool HasCorporateActions => Dividends || Splits;
}
