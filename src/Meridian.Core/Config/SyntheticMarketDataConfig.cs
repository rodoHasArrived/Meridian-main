namespace Meridian.Core.Config;

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
    DateOnly? DefaultHistoryEnd = null,
    SyntheticScenarioConfig? Scenario = null
);

public sealed record SyntheticScenarioConfig(
    bool Enabled = false,
    int ThrottleEveryNCalls = 0,
    int TimeoutEveryNCalls = 0,
    int DegradeEveryNEvents = 0,
    int ReplayBarsLimit = 0,
    int BackfillBarsLimit = 0,
    int ThrottleDelayMs = 150,
    bool ApplyToHistorical = true,
    bool ApplyToStreaming = true
);
