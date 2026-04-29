using System.Diagnostics;
using Meridian.Contracts.Backtesting;
using Meridian.Contracts.Services;

namespace Meridian.Application.Backtesting;

/// <summary>
/// Default implementation of the backtest trust gate.
/// </summary>
public sealed class BacktestPreflightService : IBacktestPreflightService
{
    public Task<BacktestPreflightReportV2Dto> RunAsync(BacktestPreflightRequestDto request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ct.ThrowIfCancellationRequested();

        var sw = Stopwatch.StartNew();
        var checks = new List<BacktestPreflightCheckResultDto>
        {
            ValidateDateRange(request.From, request.To),
            ValidateDataRoot(request.DataRoot),
            ValidateSymbols(request.Symbols),
        };

        sw.Stop();

        var hasFailures = checks.Any(c => c.Status == BacktestPreflightCheckStatusDto.Failed);
        var hasWarnings = checks.Any(c => c.Status == BacktestPreflightCheckStatusDto.Warning);
        var summary = hasFailures
            ? "Backtest preflight failed"
            : hasWarnings
                ? "Backtest preflight passed with warnings"
                : "Backtest preflight passed";

        return Task.FromResult(new BacktestPreflightReportV2Dto(
            IsReadyToRun: !hasFailures,
            HasWarnings: hasWarnings,
            Checks: checks,
            TotalDurationMs: sw.ElapsedMilliseconds,
            CheckedAt: DateTimeOffset.UtcNow,
            SummaryMessage: summary));
    }

    private static BacktestPreflightCheckResultDto ValidateDateRange(DateOnly from, DateOnly to)
    {
        if (from > to)
        {
            return new BacktestPreflightCheckResultDto(
                Name: "Date Range",
                Status: BacktestPreflightCheckStatusDto.Failed,
                Message: $"Start date {from:yyyy-MM-dd} is after end date {to:yyyy-MM-dd}",
                Remediation: "Set a From date that is on or before the To date.");
        }

        return new BacktestPreflightCheckResultDto(
            Name: "Date Range",
            Status: BacktestPreflightCheckStatusDto.Passed,
            Message: "Date range is valid.");
    }

    private static BacktestPreflightCheckResultDto ValidateDataRoot(string dataRoot)
    {
        if (string.IsNullOrWhiteSpace(dataRoot))
        {
            return new BacktestPreflightCheckResultDto(
                Name: "Data Root",
                Status: BacktestPreflightCheckStatusDto.Failed,
                Message: "Data root is required.",
                Remediation: "Provide a readable data root path.");
        }

        if (!Directory.Exists(dataRoot))
        {
            return new BacktestPreflightCheckResultDto(
                Name: "Data Root",
                Status: BacktestPreflightCheckStatusDto.Failed,
                Message: $"Data root '{dataRoot}' does not exist.",
                Remediation: "Create the directory or select an existing data root.");
        }

        return new BacktestPreflightCheckResultDto(
            Name: "Data Root",
            Status: BacktestPreflightCheckStatusDto.Passed,
            Message: "Data root exists and is readable.");
    }

    private static BacktestPreflightCheckResultDto ValidateSymbols(IReadOnlyList<string>? symbols)
    {
        if (symbols is null || symbols.Count == 0)
        {
            return new BacktestPreflightCheckResultDto(
                Name: "Symbol Scope",
                Status: BacktestPreflightCheckStatusDto.Warning,
                Message: "No symbols were specified. The engine will infer symbols from available data.",
                Remediation: "Specify symbols to constrain replay scope and reduce run time.");
        }

        var duplicates = symbols
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .Select(s => s.Trim().ToUpperInvariant())
            .GroupBy(s => s)
            .Where(g => g.Count() > 1)
            .Select(g => g.Key)
            .OrderBy(s => s)
            .ToArray();

        if (duplicates.Length > 0)
        {
            return new BacktestPreflightCheckResultDto(
                Name: "Symbol Scope",
                Status: BacktestPreflightCheckStatusDto.Warning,
                Message: "Duplicate symbols were detected.",
                Remediation: "Remove duplicate symbols to avoid redundant processing.",
                Details: new Dictionary<string, string>
                {
                    ["duplicates"] = string.Join(",", duplicates)
                });
        }

        return new BacktestPreflightCheckResultDto(
            Name: "Symbol Scope",
            Status: BacktestPreflightCheckStatusDto.Passed,
            Message: "Symbol list is valid.");
    }
}
