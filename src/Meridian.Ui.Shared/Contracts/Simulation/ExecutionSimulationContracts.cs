namespace Meridian.Ui.Shared.Contracts;

public sealed record ExecutionSimulationSummaryDto(
    IReadOnlyList<string> Symbols,
    string? FromDate,
    string? ToDate,
    bool DryRun,
    int EventCount,
    DateTimeOffset GeneratedAtUtc,
    string OutputDirectory);
