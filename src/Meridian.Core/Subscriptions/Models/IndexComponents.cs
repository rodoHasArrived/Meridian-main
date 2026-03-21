namespace Meridian.Application.Subscriptions.Models;

/// <summary>
/// Components of a market index.
/// </summary>
public sealed record IndexComponents(
    string IndexId,

    string Name,

    IndexComponent[] Components,

    DateTimeOffset LastUpdated,

    string Source
);

/// <summary>
/// Individual component of an index.
/// </summary>
public sealed record IndexComponent(
    string Symbol,

    string Name,

    decimal? Weight = null,

    string? Sector = null
);

/// <summary>
/// Available indices for auto-subscription.
/// </summary>
public static class KnownIndices
{
    public static readonly IndexDefinition SP500 = new("SPX", "S&P 500", "Large-cap US equities");
    public static readonly IndexDefinition Nasdaq100 = new("NDX", "NASDAQ 100", "Top 100 NASDAQ non-financial stocks");
    public static readonly IndexDefinition DowJones = new("DJI", "Dow Jones Industrial Average", "30 blue-chip US stocks");
    public static readonly IndexDefinition Russell2000 = new("RUT", "Russell 2000", "Small-cap US equities");
    public static readonly IndexDefinition SP400 = new("MID", "S&P 400 MidCap", "Mid-cap US equities");

    public static IReadOnlyList<IndexDefinition> All => new[]
    {
        SP500, Nasdaq100, DowJones, Russell2000, SP400
    };
}

/// <summary>
/// Definition of an available index.
/// </summary>
public sealed record IndexDefinition(
    string Id,
    string Name,
    string Description
);

/// <summary>
/// Request to auto-subscribe to index components.
/// </summary>
public sealed record IndexSubscribeRequest(
    string IndexId,

    int? MaxComponents = null,

    TemplateSubscriptionDefaults? Defaults = null,

    bool ReplaceExisting = false,

    string[]? FilterSectors = null
);

/// <summary>
/// Result of an index subscription operation.
/// </summary>
public sealed record IndexSubscribeResult(
    string IndexId,
    int ComponentsSubscribed,
    int ComponentsSkipped,
    string[] SubscribedSymbols,
    string[] SkippedSymbols,
    string? Message = null
);
