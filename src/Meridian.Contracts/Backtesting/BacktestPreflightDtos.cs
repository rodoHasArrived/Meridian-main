namespace Meridian.Contracts.Backtesting;

public sealed record BacktestPreflightRequestDto(
    DateOnly From,
    DateOnly To,
    string DataRoot,
    IReadOnlyList<string>? Symbols = null);

public sealed record BacktestPreflightReportV2Dto(
    bool IsReadyToRun,
    bool HasWarnings,
    IReadOnlyList<BacktestPreflightCheckResultDto> Checks,
    long TotalDurationMs,
    DateTimeOffset CheckedAt,
    string? SummaryMessage = null);

public sealed record BacktestPreflightCheckResultDto(
    string Name,
    BacktestPreflightCheckStatusDto Status,
    string Message,
    string? Remediation = null,
    IReadOnlyDictionary<string, string>? Details = null);

public enum BacktestPreflightCheckStatusDto : byte
{
    Passed = 0,
    Warning = 1,
    Failed = 2,
}
