using Meridian.Contracts.Api;
using Meridian.Contracts.Backfill;

namespace Meridian.Ui.Services;

/// <summary>
/// Shared, UI-neutral projection of typed backfill progress and remediation history.
/// </summary>
public static class BackfillPresentationService
{
    public static IReadOnlyList<BackfillSymbolProgressPresentation> BuildSymbolProgress(
        BackfillRunProgressResponse? response)
    {
        if (response?.ProviderProgress?.Symbols is not { Count: > 0 } symbols)
            return [];

        var attempts = response.ProviderProgress.RecentProviderAttempts ?? [];
        return symbols.Values
            .Select(symbol =>
            {
                var latestAttempt = attempts
                    .Where(attempt => string.Equals(
                        attempt.Symbol,
                        symbol.Symbol,
                        StringComparison.OrdinalIgnoreCase))
                    .OrderByDescending(static attempt => attempt.ObservedAt)
                    .FirstOrDefault();
                var state = ResolveLiveState(symbol, response.IsActive);
                var percent = symbol.IsCompleted
                    ? 100d
                    : Math.Clamp(symbol.PercentComplete, 0d, 100d);

                return new BackfillSymbolProgressPresentation(
                    Symbol: symbol.Symbol,
                    RangeStart: symbol.RangeStart,
                    RangeEnd: symbol.RangeEnd,
                    RangeText: FormatRange(symbol.RangeStart, symbol.RangeEnd),
                    CurrentProvider: string.IsNullOrWhiteSpace(symbol.CurrentProvider)
                        ? "Awaiting provider"
                        : symbol.CurrentProvider,
                    ProviderAttempt: symbol.ProviderAttempt,
                    FallbackAttemptText: FormatProviderAttempt(symbol.ProviderAttempt),
                    RetryRound: symbol.RetryRound,
                    RetryText: symbol.RetryRound <= 0 ? "Initial try" : $"Retry {symbol.RetryRound}",
                    PercentComplete: percent,
                    ProgressText: $"{percent:N1}%",
                    BarsText: latestAttempt is null ? "--" : $"{latestAttempt.BarsDownloaded:N0} bars",
                    LiveState: state,
                    LiveStateSort: ResolveLiveStateSort(state),
                    LastUpdatedAt: symbol.LastUpdatedAt ?? latestAttempt?.ObservedAt,
                    LastUpdatedText: FormatTimestamp(symbol.LastUpdatedAt ?? latestAttempt?.ObservedAt),
                    Error: symbol.Error ?? latestAttempt?.Error);
            })
            .OrderBy(static row => row.LiveStateSort)
            .ThenBy(static row => row.Symbol, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public static IReadOnlyList<BackfillRemediationQueuePresentation> BuildRemediationQueue(
        BackfillExecutionHistoryResponse? response)
    {
        if (response?.Executions is not { Length: > 0 } executions)
            return [];

        var defaultProvider = NormalizeProvider(response.AutoRemediation?.DefaultProvider);
        return executions
            .Where(IsRemediation)
            .Select(execution =>
            {
                var sla = execution.AutoRemediationSla;
                var statusSort = sla is null ? 5 : ResolveSlaStatusSort(sla.Status);
                var tierSort = sla?.Tier switch
                {
                    BackfillRemediationSlaTierDto.SameBusinessDay => 0,
                    BackfillRemediationSlaTierDto.Standard => 1,
                    _ => 2
                };
                return new BackfillRemediationQueuePresentation(
                    ExecutionId: execution.Id,
                    SymbolsText: execution.Symbols.Length == 0
                        ? $"{execution.SymbolsProcessed:N0} symbol(s)"
                        : string.Join(", ", execution.Symbols),
                    Provider: NormalizeProvider(string.IsNullOrWhiteSpace(sla?.Provider)
                        ? defaultProvider
                        : sla.Provider),
                    RangeText: FormatRange(execution.FromDate, execution.ToDate),
                    SlaTier: sla?.Tier,
                    SlaTierText: FormatTier(sla?.Tier),
                    SlaTierSort: tierSort,
                    DueAtUtc: sla?.DueAtUtc,
                    DeadlineText: sla is null ? "Not recorded" : $"{sla.DueAtUtc:u}",
                    SlaStatus: sla?.Status,
                    SlaStatusText: sla?.Status.ToString() ?? "Not recorded",
                    SlaStatusSort: statusSort,
                    Outcome: execution.AutoRemediationLastOutcome ?? execution.Status,
                    AttemptText: execution.AutoRemediationAttemptCount <= 0
                        ? "Attempt not recorded"
                        : $"Attempt {execution.AutoRemediationAttemptCount}",
                    TriggerReason: execution.AutoRemediationTriggerReason ?? "Reason not recorded",
                    IsCompatibilityDerived: sla?.IsCompatibilityDerived == true,
                    Error: execution.ErrorMessage);
            })
            .OrderBy(static row => row.SlaStatusSort)
            .ThenBy(static row => row.DueAtUtc ?? DateTimeOffset.MaxValue)
            .ThenBy(static row => row.SymbolsText, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static bool IsRemediation(BackfillExecution execution) =>
        string.Equals(execution.Trigger, "AutoRemediation", StringComparison.OrdinalIgnoreCase) ||
        execution.AutoRemediationSla is not null ||
        !string.IsNullOrWhiteSpace(execution.AutoRemediationTriggerReason);

    private static string ResolveLiveState(BackfillProviderSymbolProgressDto symbol, bool runIsActive)
    {
        if (symbol.IsFailed)
            return "Failed";
        if (symbol.IsCompleted)
            return "Completed";
        if (symbol.IsSkipped)
            return "Skipped";
        if (!string.IsNullOrWhiteSpace(symbol.CurrentStatus))
            return symbol.CurrentStatus;
        return runIsActive ? "Queued" : "Idle";
    }

    private static int ResolveLiveStateSort(string state) => state.ToLowerInvariant() switch
    {
        "failed" => 0,
        "running" or "downloading" or "requesting" => 1,
        "queued" or "pending" => 2,
        "skipped" => 3,
        "completed" => 4,
        _ => 5
    };

    private static int ResolveSlaStatusSort(BackfillRemediationSlaStatusDto status) => status switch
    {
        BackfillRemediationSlaStatusDto.Overdue => 0,
        BackfillRemediationSlaStatusDto.Failed => 1,
        BackfillRemediationSlaStatusDto.DueSoon => 2,
        BackfillRemediationSlaStatusDto.Open => 3,
        BackfillRemediationSlaStatusDto.Completed => 4,
        _ => 5
    };

    private static string FormatProviderAttempt(int attempt) => attempt switch
    {
        <= 0 => "Awaiting attempt",
        1 => "Primary · attempt 1",
        _ => $"Fallback · attempt {attempt}"
    };

    private static string FormatTier(BackfillRemediationSlaTierDto? tier) => tier switch
    {
        BackfillRemediationSlaTierDto.SameBusinessDay => "Same business day",
        BackfillRemediationSlaTierDto.Standard => "Standard",
        _ => "Not recorded"
    };

    private static string FormatRange(DateOnly? from, DateOnly? to) => (from, to) switch
    {
        ({ } start, { } end) => $"{start:yyyy-MM-dd} — {end:yyyy-MM-dd}",
        ({ } start, null) => $"From {start:yyyy-MM-dd}",
        (null, { } end) => $"Through {end:yyyy-MM-dd}",
        _ => "Range unavailable"
    };

    private static string FormatTimestamp(DateTimeOffset? timestamp) =>
        timestamp.HasValue ? timestamp.Value.ToLocalTime().ToString("g") : "--";

    private static string NormalizeProvider(string? provider) =>
        string.IsNullOrWhiteSpace(provider) ? "stooq" : provider.Trim().ToLowerInvariant();
}

public sealed record BackfillSymbolProgressPresentation(
    string Symbol,
    DateOnly? RangeStart,
    DateOnly? RangeEnd,
    string RangeText,
    string CurrentProvider,
    int ProviderAttempt,
    string FallbackAttemptText,
    int RetryRound,
    string RetryText,
    double PercentComplete,
    string ProgressText,
    string BarsText,
    string LiveState,
    int LiveStateSort,
    DateTimeOffset? LastUpdatedAt,
    string LastUpdatedText,
    string? Error);

public sealed record BackfillRemediationQueuePresentation(
    string ExecutionId,
    string SymbolsText,
    string Provider,
    string RangeText,
    BackfillRemediationSlaTierDto? SlaTier,
    string SlaTierText,
    int SlaTierSort,
    DateTimeOffset? DueAtUtc,
    string DeadlineText,
    BackfillRemediationSlaStatusDto? SlaStatus,
    string SlaStatusText,
    int SlaStatusSort,
    string Outcome,
    string AttemptText,
    string TriggerReason,
    bool IsCompatibilityDerived,
    string? Error);
