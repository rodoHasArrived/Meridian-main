namespace Meridian.Backtesting.Sdk;

/// <summary>Coverage observed from the exact inputs consumed by a native replay.</summary>
public sealed record BacktestInputCoverage(
    decimal RequiredSessionFraction,
    IReadOnlyList<BacktestInstrumentCoverage> Instruments,
    IReadOnlyList<BacktestDataRevision> Revisions);

public sealed record BacktestInstrumentCoverage(
    string Instrument,
    string Status,
    string? Calendar,
    int ObservedSessions,
    int? ExpectedSessions,
    IReadOnlyList<DateOnly> MissingSessions);

public sealed record BacktestDataRevision(string RevisionId, string ContentHash, string ServingProvider, string Instrument, string Granularity);
