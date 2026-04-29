namespace Meridian.Backtesting.Sdk;

/// <summary>
/// Stage-focused telemetry emitted with each <see cref="BacktestProgressEvent"/>.
/// </summary>
public sealed record BacktestStageTelemetryDto(
    BacktestStage Stage,
    TimeSpan StageElapsed,
    TimeSpan TotalElapsed,
    string? StageMessage = null);
