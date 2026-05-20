using System.Diagnostics;
using Meridian.Application.SecurityMaster;
using Meridian.Contracts.Backtesting;
using Meridian.Contracts.SecurityMaster;
using Meridian.Contracts.Services;

namespace Meridian.Application.Backtesting;

/// <summary>
/// Default implementation of the backtest trust gate.
/// </summary>
public sealed class BacktestPreflightService : IBacktestPreflightService
{
    private static readonly string[] KnownReplayDataTypes = ["bars", "quotes", "book", "trades"];

    private readonly ISecurityValidationGateService? _securityValidationGate;

    public BacktestPreflightService(ISecurityValidationGateService? securityValidationGate = null)
    {
        _securityValidationGate = securityValidationGate;
    }

    public async Task<BacktestPreflightReportV2Dto> RunAsync(BacktestPreflightRequestDto request, CancellationToken ct = default)
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

        if (Directory.Exists(request.DataRoot))
        {
            checks.AddRange(ValidateReplayCoverage(request));
        }

        if (_securityValidationGate is not null)
        {
            checks.Add(await ValidateSecurityMasterAsync(request, ct).ConfigureAwait(false));
        }

        sw.Stop();

        var hasFailures = checks.Any(c => c.Status == BacktestPreflightCheckStatusDto.Failed);
        var hasWarnings = checks.Any(c => c.Status == BacktestPreflightCheckStatusDto.Warning);
        var summary = hasFailures
            ? "Backtest preflight failed"
            : hasWarnings
                ? "Backtest preflight passed with warnings"
                : "Backtest preflight passed";

        return new BacktestPreflightReportV2Dto(
            IsReadyToRun: !hasFailures,
            HasWarnings: hasWarnings,
            Checks: checks,
            TotalDurationMs: sw.ElapsedMilliseconds,
            CheckedAt: DateTimeOffset.UtcNow,
            SummaryMessage: summary);
    }

    private async Task<BacktestPreflightCheckResultDto> ValidateSecurityMasterAsync(
        BacktestPreflightRequestDto request,
        CancellationToken ct)
    {
        var normalizedSymbols = NormalizeSymbols(request.Symbols);
        if (normalizedSymbols.Length == 0)
        {
            return new BacktestPreflightCheckResultDto(
                Name: "Security Master Validation",
                Status: BacktestPreflightCheckStatusDto.Warning,
                Message: "Security Master validation was skipped because no symbols were specified.",
                Remediation: "Specify symbols so StrategyRun preflight can validate canonical instrument identity.");
        }

        var results = new List<SecurityValidationGateResultDto>(normalizedSymbols.Length);
        foreach (var symbol in normalizedSymbols)
        {
            ct.ThrowIfCancellationRequested();
            results.Add(await _securityValidationGate!
                .ValidateSymbolAsync(
                    symbol,
                    SecurityValidationWorkflowDto.StrategyRunPreflight,
                    workflowReference: $"{request.From:yyyy-MM-dd}:{request.To:yyyy-MM-dd}",
                    actor: "strategy-run-preflight",
                    persistSnapshot: false,
                    ct)
                .ConfigureAwait(false));
        }

        var blocked = results
            .Where(static result => result.IsBlocked)
            .Select(static result => result.Symbol ?? result.SecurityId?.ToString() ?? "unknown")
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(static symbol => symbol, StringComparer.Ordinal)
            .ToArray();
        var warnings = results
            .Where(static result => !result.IsBlocked && result.Report.Issues.Count > 0)
            .Select(static result => result.Symbol ?? result.SecurityId?.ToString() ?? "unknown")
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(static symbol => symbol, StringComparer.Ordinal)
            .ToArray();
        var issueCodes = results
            .SelectMany(static result => result.Report.Issues.Select(static issue => issue.Code))
            .Distinct(StringComparer.Ordinal)
            .OrderBy(static code => code, StringComparer.Ordinal)
            .ToArray();

        if (blocked.Length > 0)
        {
            return new BacktestPreflightCheckResultDto(
                Name: "Security Master Validation",
                Status: BacktestPreflightCheckStatusDto.Failed,
                Message: $"Security Master validation blocked {blocked.Length} strategy input symbol(s).",
                Remediation: "Resolve blocking Security Master validation issues before replaying the StrategyRun.",
                Details: new Dictionary<string, string>
                {
                    ["blockedSymbols"] = string.Join(",", blocked),
                    ["issueCodes"] = string.Join(",", issueCodes)
                });
        }

        if (warnings.Length > 0)
        {
            return new BacktestPreflightCheckResultDto(
                Name: "Security Master Validation",
                Status: BacktestPreflightCheckStatusDto.Warning,
                Message: $"Security Master validation found warning issues for {warnings.Length} strategy input symbol(s).",
                Remediation: "Review Security Master warnings before relying on governed replay outputs.",
                Details: new Dictionary<string, string>
                {
                    ["warningSymbols"] = string.Join(",", warnings),
                    ["issueCodes"] = string.Join(",", issueCodes)
                });
        }

        return new BacktestPreflightCheckResultDto(
            Name: "Security Master Validation",
            Status: BacktestPreflightCheckStatusDto.Passed,
            Message: "Requested strategy input symbols passed Security Master validation.");
    }

    private static IEnumerable<BacktestPreflightCheckResultDto> ValidateReplayCoverage(BacktestPreflightRequestDto request)
    {
        var normalizedSymbols = (request.Symbols ?? [])
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .Select(s => s.Trim().ToUpperInvariant())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (normalizedSymbols.Length == 0)
        {
            yield break;
        }

        var requiredTypes = ResolveRequiredTypes(request.RequiredReplayDataTypes, request.RequiredExecutionModel);
        var bySymbolMonth = IndexReplayData(request.DataRoot, normalizedSymbols, request.From, request.To);

        var gaps = new List<string>();
        var orderBookMissing = new List<string>();
        foreach (var symbol in normalizedSymbols)
        {
            foreach (var month in EnumerateMonths(request.From, request.To))
            {
                var key = (symbol, month);
                bySymbolMonth.TryGetValue(key, out var typesPresent);
                typesPresent ??= new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                foreach (var requiredType in requiredTypes)
                {
                    if (typesPresent.Contains(requiredType))
                        continue;

                    gaps.Add($"{symbol}:{month:yyyy-MM}:{requiredType}");
                    if (requiredType.Equals("book", StringComparison.OrdinalIgnoreCase) &&
                        string.Equals(request.RequiredExecutionModel, "OrderBook", StringComparison.OrdinalIgnoreCase))
                    {
                        orderBookMissing.Add($"{symbol}:{month:yyyy-MM}");
                    }
                }
            }
        }

        if (orderBookMissing.Count > 0)
        {
            yield return new BacktestPreflightCheckResultDto(
                Name: "Execution Model Compatibility",
                Status: BacktestPreflightCheckStatusDto.Failed,
                Message: "OrderBook execution was requested but Level 2 replay data is missing.",
                Remediation: "Provide order-book replay files for all requested symbol/month buckets or switch execution model.",
                Details: new Dictionary<string, string>
                {
                    ["executionModel"] = request.RequiredExecutionModel ?? "",
                    ["missingL2Buckets"] = string.Join(",", orderBookMissing.OrderBy(x => x, StringComparer.Ordinal))
                });
        }
        else if (!string.IsNullOrWhiteSpace(request.RequiredExecutionModel))
        {
            yield return new BacktestPreflightCheckResultDto(
                Name: "Execution Model Compatibility",
                Status: BacktestPreflightCheckStatusDto.Passed,
                Message: $"Execution model '{request.RequiredExecutionModel}' is compatible with replay data coverage.");
        }

        if (gaps.Count > 0)
        {
            yield return new BacktestPreflightCheckResultDto(
                Name: "Replay Coverage",
                Status: BacktestPreflightCheckStatusDto.Warning,
                Message: "Replay coverage has gaps by symbol/month for requested data types.",
                Remediation: "Backfill missing data types for each symbol/month before running high-trust simulations.",
                Details: new Dictionary<string, string>
                {
                    ["requiredTypes"] = string.Join(",", requiredTypes),
                    ["missingBuckets"] = string.Join(",", gaps.OrderBy(x => x, StringComparer.Ordinal))
                });
        }
        else
        {
            yield return new BacktestPreflightCheckResultDto(
                Name: "Replay Coverage",
                Status: BacktestPreflightCheckStatusDto.Passed,
                Message: "Replay data coverage includes requested symbol/month buckets and data types.");
        }
    }

    private static Dictionary<(string Symbol, DateOnly Month), HashSet<string>> IndexReplayData(string dataRoot, IReadOnlyList<string> symbols, DateOnly from, DateOnly to)
    {
        var symbolSet = new HashSet<string>(symbols, StringComparer.OrdinalIgnoreCase);
        var dict = new Dictionary<(string Symbol, DateOnly Month), HashSet<string>>();

        foreach (var file in Directory.EnumerateFiles(dataRoot, "*.jsonl*", SearchOption.AllDirectories))
        {
            var name = Path.GetFileNameWithoutExtension(Path.GetFileNameWithoutExtension(file));
            var tokens = name.Split(['_', '.'], StringSplitOptions.RemoveEmptyEntries);
            if (tokens.Length < 3)
                continue;

            var symbol = tokens[0].Trim().ToUpperInvariant();
            if (!symbolSet.Contains(symbol))
                continue;

            var type = tokens[1].Trim().ToLowerInvariant();
            if (!KnownReplayDataTypes.Contains(type))
                continue;

            if (!DateOnly.TryParse(tokens[2], out var date))
                continue;

            if (date < from || date > to)
                continue;

            var month = new DateOnly(date.Year, date.Month, 1);
            var key = (symbol, month);
            if (!dict.TryGetValue(key, out var types))
            {
                types = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                dict[key] = types;
            }

            types.Add(type);
        }

        return dict;
    }

    private static string[] ResolveRequiredTypes(IReadOnlyList<string>? requestedTypes, string? executionModel)
    {
        var resolved = (requestedTypes ?? ["bars", "quotes", "trades"])
            .Select(x => x?.Trim().ToLowerInvariant())
            .Where(x => !string.IsNullOrWhiteSpace(x) && KnownReplayDataTypes.Contains(x!))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (resolved.Count == 0)
            resolved.AddRange(["bars", "quotes", "trades"]);

        if (string.Equals(executionModel, "OrderBook", StringComparison.OrdinalIgnoreCase) &&
            !resolved.Contains("book", StringComparer.OrdinalIgnoreCase))
        {
            resolved.Add("book");
        }

        return resolved.ToArray();
    }

    private static string[] NormalizeSymbols(IReadOnlyList<string>? symbols)
        => (symbols ?? [])
            .Where(static symbol => !string.IsNullOrWhiteSpace(symbol))
            .Select(static symbol => symbol.Trim().ToUpperInvariant())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(static symbol => symbol, StringComparer.Ordinal)
            .ToArray();

    private static IEnumerable<DateOnly> EnumerateMonths(DateOnly from, DateOnly to)
    {
        var cursor = new DateOnly(from.Year, from.Month, 1);
        var end = new DateOnly(to.Year, to.Month, 1);
        while (cursor <= end)
        {
            yield return cursor;
            cursor = cursor.AddMonths(1);
        }
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
