namespace Meridian.Application.Config;

/// <summary>
/// Configuration for the built-in synthetic market data provider used for local development,
/// offline integration testing, and historical replay without live credentials.
/// </summary>
public sealed record SyntheticMarketDataConfig(
    bool Enabled = false,
    int Seed = 1729,
    int Priority = 1,
    int EventsPerSecond = 8,
    int HistoricalTradeDensityPerDay = 120,
    int HistoricalQuoteDensityPerDay = 180,
    int DefaultDepthLevels = 10,
    bool IncludeCorporateActions = true,
    bool IncludeReferenceData = true,
    string[]? UniverseSymbols = null,
    DateOnly? DefaultHistoryStart = null,
    DateOnly? DefaultHistoryEnd = null
);
