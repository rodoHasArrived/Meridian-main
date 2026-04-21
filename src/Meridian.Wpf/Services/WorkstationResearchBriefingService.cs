using System.Globalization;
using Meridian.Contracts.Api;
using Meridian.Contracts.Workstation;
using Meridian.Ui.Services;

namespace Meridian.Wpf.Services;

public interface IWorkstationResearchBriefingApiClient
{
    Task<ResearchBriefingDto?> GetBriefingAsync(CancellationToken ct = default);
}

public sealed class WorkstationResearchBriefingApiClient : IWorkstationResearchBriefingApiClient
{
    private readonly ApiClientService _apiClient;

    public WorkstationResearchBriefingApiClient(ApiClientService apiClient)
    {
        _apiClient = apiClient ?? throw new ArgumentNullException(nameof(apiClient));
    }

    public Task<ResearchBriefingDto?> GetBriefingAsync(CancellationToken ct = default)
        => _apiClient.GetAsync<ResearchBriefingDto>(UiApiRoutes.WorkstationResearchBriefing, ct);
}

public interface IResearchBriefingWorkspaceService
{
    Task<ResearchBriefingDto> GetBriefingAsync(CancellationToken ct = default);
}

public sealed class ResearchBriefingWorkspaceService : IResearchBriefingWorkspaceService
{
    private readonly IWorkstationResearchBriefingApiClient _apiClient;
    private readonly StrategyRunWorkspaceService _runService;
    private readonly IWatchlistReader _watchlistReader;

    public ResearchBriefingWorkspaceService(
        IWorkstationResearchBriefingApiClient apiClient,
        StrategyRunWorkspaceService runService,
        IWatchlistReader watchlistReader)
    {
        _apiClient = apiClient ?? throw new ArgumentNullException(nameof(apiClient));
        _runService = runService ?? throw new ArgumentNullException(nameof(runService));
        _watchlistReader = watchlistReader ?? throw new ArgumentNullException(nameof(watchlistReader));
    }

    public async Task<ResearchBriefingDto> GetBriefingAsync(CancellationToken ct = default)
    {
        var localWatchlists = MapWatchlists(await _watchlistReader.GetAllWatchlistsAsync(ct).ConfigureAwait(false));
        var apiBriefing = await _apiClient.GetBriefingAsync(ct).ConfigureAwait(false);

        if (apiBriefing is not null)
        {
            return apiBriefing with
            {
                Watchlists = apiBriefing.Watchlists.Count > 0 ? apiBriefing.Watchlists : localWatchlists
            };
        }

        return await BuildFallbackAsync(localWatchlists, ct).ConfigureAwait(false);
    }

    private async Task<ResearchBriefingDto> BuildFallbackAsync(
        IReadOnlyList<WorkstationWatchlist> localWatchlists,
        CancellationToken ct)
    {
        var runs = (await _runService.GetRunsAsync(ct: ct).ConfigureAwait(false))
            .Take(10)
            .ToArray();
        var details = await Task.WhenAll(
                runs.Select(run => _runService.GetRunDetailAsync(run.RunId, ct)))
            .ConfigureAwait(false);

        var alerts = BuildAlerts(runs, details);
        var activeRuns = runs.Count(static run => run.Status is StrategyRunStatus.Running or StrategyRunStatus.Paused);
        var promotionCandidates = runs.Count(static run => run.Promotion is { RequiresReview: true } &&
            run.Promotion.State is StrategyRunPromotionState.CandidateForPaper or StrategyRunPromotionState.CandidateForLive);
        var positivePnlRuns = runs.Count(static run => (run.NetPnl ?? 0m) > 0m);
        var latestRun = runs.FirstOrDefault();

        return new ResearchBriefingDto(
            Workspace: new ResearchBriefingWorkspaceSummary(
                TotalRuns: runs.Length,
                ActiveRuns: activeRuns,
                PromotionCandidates: promotionCandidates,
                PositivePnlRuns: positivePnlRuns,
                LatestRunId: latestRun?.RunId,
                LatestStrategyName: latestRun?.StrategyName,
                HasLedgerCoverage: runs.Any(static run => !string.IsNullOrWhiteSpace(run.LedgerReference)),
                HasPortfolioCoverage: runs.Any(static run => !string.IsNullOrWhiteSpace(run.PortfolioId)),
                Summary: latestRun is null
                    ? "Start a backtest or open a saved run to populate the Market Briefing."
                    : $"{activeRuns} active session(s), {promotionCandidates} promotion candidate(s), and {alerts.Count} alert(s) on the research desk."),
            InsightFeed: BuildInsightFeed(runs, details, alerts.Count),
            Watchlists: localWatchlists,
            RecentRuns: runs
                .Zip(details, static (run, detail) => BuildBriefingRun(run, detail))
                .Take(6)
                .ToArray(),
            SavedComparisons: BuildSavedComparisons(runs),
            Alerts: alerts,
            WhatChanged: BuildWhatChanged(runs));
    }

    private static IReadOnlyList<WorkstationWatchlist> MapWatchlists(IReadOnlyList<Watchlist> watchlists)
        => watchlists
            .OrderByDescending(static watchlist => watchlist.IsPinned)
            .ThenBy(static watchlist => watchlist.SortOrder)
            .Take(4)
            .Select(static watchlist => new WorkstationWatchlist(
                WatchlistId: watchlist.Id,
                Name: watchlist.Name,
                Symbols: watchlist.Symbols.Take(6).ToArray(),
                SymbolCount: watchlist.Symbols.Count,
                IsPinned: watchlist.IsPinned,
                SortOrder: watchlist.SortOrder,
                AccentColor: watchlist.Color,
                Summary: watchlist.Symbols.Count == 0
                    ? "No symbols staged."
                    : $"Tracking {watchlist.Symbols.Count} symbol(s): {string.Join(", ", watchlist.Symbols.Take(3))}."))
            .ToArray();

    private static InsightFeed BuildInsightFeed(
        IReadOnlyList<StrategyRunSummary> runs,
        IReadOnlyList<StrategyRunDetail?> details,
        int alertCount)
    {
        var generatedAt = DateTimeOffset.UtcNow;
        if (runs.Count == 0)
        {
            return new InsightFeed(
                FeedId: "research-market-briefing",
                Title: "Pinned Insights",
                Summary: "No saved insights yet.",
                GeneratedAt: generatedAt,
                Widgets: Array.Empty<InsightWidget>());
        }

        var widgets = runs
            .Zip(details, static (run, detail) => new InsightWidget(
                WidgetId: $"insight-{run.RunId}",
                Title: run.StrategyName,
                Subtitle: $"{run.Mode} · {run.Status}",
                Headline: FormatReturn(run.TotalReturn, run.NetPnl),
                Tone: GetTone(run, detail),
                Summary: BuildRunSummary(run, detail),
                RunId: run.RunId,
                DrillInRoute: UiApiRoutes.WithParam("/api/workstation/runs/{runId}/equity-curve", "runId", run.RunId)))
            .Take(3)
            .ToArray();

        return new InsightFeed(
            FeedId: "research-market-briefing",
            Title: "Pinned Insights",
            Summary: $"{runs.Count} tracked run(s) in scope; {alertCount} alert(s) surfaced from runs and continuity signals.",
            GeneratedAt: generatedAt,
            Widgets: widgets);
    }

    private static ResearchBriefingRun BuildBriefingRun(StrategyRunSummary run, StrategyRunDetail? detail)
        => new(
            RunId: run.RunId,
            StrategyName: run.StrategyName,
            Mode: run.Mode,
            Status: run.Status,
            Dataset: run.DatasetReference ?? run.FeedReference ?? "Unassigned",
            WindowLabel: FormatWindow(run.StartedAt, run.CompletedAt),
            ReturnLabel: FormatReturn(run.TotalReturn, run.NetPnl),
            SharpeLabel: FormatSharpeProxy(run),
            LastUpdatedLabel: FormatRelativeTime(run.LastUpdatedAt),
            Notes: BuildRunSummary(run, detail),
            PromotionState: run.Promotion?.State,
            NetPnl: run.NetPnl,
            TotalReturn: run.TotalReturn,
            FinalEquity: run.FinalEquity,
            DrillIn: BuildDrillInLinks(run));

    private static IReadOnlyList<ResearchSavedComparison> BuildSavedComparisons(IReadOnlyList<StrategyRunSummary> runs)
    {
        var grouped = runs
            .GroupBy(static run => run.StrategyName, StringComparer.OrdinalIgnoreCase)
            .Select(group =>
            {
                var modes = group
                    .OrderBy(static run => run.Mode)
                    .Select(static run => new ResearchSavedComparisonMode(
                        RunId: run.RunId,
                        Mode: run.Mode,
                        Status: run.Status,
                        NetPnl: run.NetPnl,
                        TotalReturn: run.TotalReturn,
                        DrillIn: BuildDrillInLinks(run)))
                    .ToArray();

                return new ResearchSavedComparison(
                    ComparisonId: $"cmp-{group.First().RunId}",
                    StrategyName: group.Key,
                    ModeSummary: string.Join(" -> ", modes.Select(static mode => mode.Mode.ToString())),
                    Summary: modes.Length >= 2
                        ? $"Saved compare lane covers {modes.Length} lifecycle stage(s) for {group.Key}."
                        : $"Baseline compare package is ready for {group.Key}.",
                    AnchorRunId: modes.FirstOrDefault()?.RunId,
                    Modes: modes);
            })
            .Where(static comparison => comparison.Modes.Count >= 2)
            .Take(4)
            .ToArray();

        if (grouped.Length > 0)
        {
            return grouped;
        }

        if (runs.Count < 2)
        {
            return Array.Empty<ResearchSavedComparison>();
        }

        var recentModes = runs
            .Take(2)
            .Select(static run => new ResearchSavedComparisonMode(
                RunId: run.RunId,
                Mode: run.Mode,
                Status: run.Status,
                NetPnl: run.NetPnl,
                TotalReturn: run.TotalReturn,
                DrillIn: BuildDrillInLinks(run)))
            .ToArray();

        return
        [
            new ResearchSavedComparison(
                ComparisonId: $"cmp-recent-{recentModes[0].RunId}",
                StrategyName: "Recent Runs",
                ModeSummary: string.Join(" vs ", recentModes.Select(static mode => mode.Mode.ToString())),
                Summary: "Saved compare lane across the two most recent runs while multi-mode history is still building.",
                AnchorRunId: recentModes[0].RunId,
                Modes: recentModes)
        ];
    }

    private static IReadOnlyList<ResearchBriefingAlert> BuildAlerts(
        IReadOnlyList<StrategyRunSummary> runs,
        IReadOnlyList<StrategyRunDetail?> details)
    {
        var alerts = new List<ResearchBriefingAlert>();

        for (var index = 0; index < runs.Count; index++)
        {
            var run = runs[index];
            var detail = index < details.Count ? details[index] : null;
            var securityIssues = GetSecurityIssues(detail);

            if (run.Status is StrategyRunStatus.Failed or StrategyRunStatus.Cancelled)
            {
                alerts.Add(new ResearchBriefingAlert(
                    AlertId: $"alert-status-{run.RunId}",
                    Title: $"{run.StrategyName} needs operator review",
                    Summary: $"Run finished with status {run.Status} and should be inspected before it is reused.",
                    Tone: "warning",
                    RunId: run.RunId,
                    ActionLabel: "Open run"));
            }

            if (run.Promotion?.RequiresReview == true)
            {
                alerts.Add(new ResearchBriefingAlert(
                    AlertId: $"alert-promotion-{run.RunId}",
                    Title: $"{run.StrategyName} is queued for promotion review",
                    Summary: run.Promotion.Reason,
                    Tone: "default",
                    RunId: run.RunId,
                    ActionLabel: "Review"));
            }

            if (securityIssues > 0)
            {
                alerts.Add(new ResearchBriefingAlert(
                    AlertId: $"alert-security-{run.RunId}",
                    Title: $"{run.StrategyName} has Security Master gaps",
                    Summary: $"{securityIssues} unresolved portfolio or ledger reference(s) should be fixed before handoff.",
                    Tone: "warning",
                    RunId: run.RunId,
                    ActionLabel: "Inspect"));
            }
        }

        if (alerts.Count == 0)
        {
            return
            [
                new ResearchBriefingAlert(
                    AlertId: "alert-none",
                    Title: "No blocking alerts",
                    Summary: "Recent runs have no failed states, open promotion blockers, or reference-data gaps.",
                    Tone: "success",
                    ActionLabel: "Browse runs")
            ];
        }

        return alerts.Take(4).ToArray();
    }

    private static IReadOnlyList<ResearchWhatChangedItem> BuildWhatChanged(IReadOnlyList<StrategyRunSummary> runs)
        => runs
            .Take(4)
            .Select(static run => new ResearchWhatChangedItem(
                ChangeId: $"change-{run.RunId}",
                Title: $"{run.StrategyName} moved to {run.Mode}",
                Summary: BuildChangeSummary(run),
                Category: run.Mode.ToString().ToLowerInvariant(),
                Timestamp: run.LastUpdatedAt,
                RelativeTime: FormatRelativeTime(run.LastUpdatedAt),
                RunId: run.RunId))
            .ToArray();

    private static ResearchRunDrillInLinks BuildDrillInLinks(StrategyRunSummary run)
        => new(
            EquityCurve: UiApiRoutes.WithParam("/api/workstation/runs/{runId}/equity-curve", "runId", run.RunId),
            Fills: UiApiRoutes.WithParam("/api/workstation/runs/{runId}/fills", "runId", run.RunId),
            Attribution: UiApiRoutes.WithParam("/api/workstation/runs/{runId}/attribution", "runId", run.RunId),
            Ledger: string.IsNullOrWhiteSpace(run.LedgerReference)
                ? null
                : UiApiRoutes.WithParam(UiApiRoutes.RunsLedger, "runId", run.RunId),
            CashFlows: UiApiRoutes.WithParam("/api/portfolio/{runId}/cash-flows", "runId", run.RunId),
            Continuity: UiApiRoutes.WithParam(UiApiRoutes.RunsContinuity, "runId", run.RunId));

    private static string BuildRunSummary(StrategyRunSummary run, StrategyRunDetail? detail)
    {
        var securityIssues = GetSecurityIssues(detail);
        if (securityIssues > 0)
        {
            return $"{BuildRunNotes(run)} {securityIssues} Security Master gap(s) remain open.";
        }

        return BuildRunNotes(run);
    }

    private static int GetSecurityIssues(StrategyRunDetail? detail)
        => (detail?.Portfolio?.SecurityMissingCount ?? 0) + (detail?.Ledger?.SecurityMissingCount ?? 0);

    private static string GetTone(StrategyRunSummary run, StrategyRunDetail? detail)
    {
        if (run.Status is StrategyRunStatus.Failed or StrategyRunStatus.Cancelled)
        {
            return "warning";
        }

        if (run.Promotion?.RequiresReview == true || GetSecurityIssues(detail) > 0)
        {
            return "default";
        }

        return (run.NetPnl ?? 0m) >= 0m ? "success" : "warning";
    }

    private static string BuildRunNotes(StrategyRunSummary run)
    {
        if (run.Promotion?.RequiresReview == true)
        {
            return run.Promotion.State switch
            {
                StrategyRunPromotionState.CandidateForPaper => "Completed backtest awaiting paper review.",
                StrategyRunPromotionState.CandidateForLive => "Paper run pending live promotion review.",
                StrategyRunPromotionState.RequiresCompletion => "Run must complete before promotion review can proceed.",
                _ => "Run is flagged for governance review."
            };
        }

        if (!string.IsNullOrWhiteSpace(run.LedgerReference) && !string.IsNullOrWhiteSpace(run.PortfolioId))
        {
            return "Run has portfolio and ledger drill-in coverage.";
        }

        if (!string.IsNullOrWhiteSpace(run.LedgerReference))
        {
            return "Run includes ledger drill-in coverage.";
        }

        if (!string.IsNullOrWhiteSpace(run.PortfolioId))
        {
            return "Run includes portfolio drill-in coverage.";
        }

        return run.Status switch
        {
            StrategyRunStatus.Running => "Active run with live workspace telemetry.",
            StrategyRunStatus.Completed => "Completed run available for comparison and export.",
            StrategyRunStatus.Failed => "Run completed with errors requiring review.",
            _ => "Run is available for workstation review."
        };
    }

    private static string BuildChangeSummary(StrategyRunSummary run)
        => run.Status switch
        {
            StrategyRunStatus.Running => $"{run.StrategyName} is still running with updated execution and workspace telemetry.",
            StrategyRunStatus.Completed when run.Promotion?.RequiresReview == true => $"{run.StrategyName} completed and is ready for promotion review.",
            StrategyRunStatus.Completed => $"{run.StrategyName} completed and remains available for compare and pin workflows.",
            StrategyRunStatus.Failed => $"{run.StrategyName} failed and should be reviewed before promotion or reuse.",
            StrategyRunStatus.Cancelled or StrategyRunStatus.Stopped => $"{run.StrategyName} stopped before promotion and is retained for evidence.",
            _ => BuildRunNotes(run)
        };

    private static string FormatWindow(DateTimeOffset startedAt, DateTimeOffset? completedAt)
    {
        var end = completedAt ?? DateTimeOffset.UtcNow;
        var span = end - startedAt;

        if (span.TotalDays >= 1)
        {
            return $"{(int)Math.Round(span.TotalDays)}d";
        }

        if (span.TotalHours >= 1)
        {
            return $"{(int)Math.Round(span.TotalHours)}h";
        }

        if (span.TotalMinutes >= 1)
        {
            return $"{(int)Math.Round(span.TotalMinutes)}m";
        }

        return "0m";
    }

    private static string FormatRelativeTime(DateTimeOffset timestamp)
    {
        var span = DateTimeOffset.UtcNow - timestamp;

        if (span.TotalMinutes < 1)
        {
            return "just now";
        }

        if (span.TotalHours < 1)
        {
            return $"{(int)Math.Round(span.TotalMinutes)}m ago";
        }

        if (span.TotalDays < 1)
        {
            return $"{(int)Math.Round(span.TotalHours)}h ago";
        }

        return $"{(int)Math.Round(span.TotalDays)}d ago";
    }

    private static string FormatReturn(decimal? totalReturn, decimal? netPnl)
    {
        if (totalReturn is not null)
        {
            return FormatPercent(totalReturn.Value);
        }

        if (netPnl is not null)
        {
            return FormatCurrency(netPnl.Value);
        }

        return "n/a";
    }

    private static string FormatSharpeProxy(StrategyRunSummary run)
    {
        if (run.TotalReturn is null && run.NetPnl is null)
        {
            return "n/a";
        }

        var proxy = (run.TotalReturn ?? 0m) * 12m;
        if (run.NetPnl is not null)
        {
            proxy += Math.Sign(run.NetPnl.Value) * 0.25m;
        }

        return proxy.ToString("0.00", CultureInfo.InvariantCulture);
    }

    private static string FormatPercent(decimal value)
        => $"{(value >= 0 ? "+" : string.Empty)}{(value * 100m).ToString("0.0", CultureInfo.InvariantCulture)}%";

    private static string FormatCurrency(decimal value)
    {
        var sign = value >= 0 ? "+" : "-";
        var absolute = Math.Abs(value);
        var scaled = absolute;
        var suffix = string.Empty;

        if (absolute >= 1_000_000m)
        {
            scaled = absolute / 1_000_000m;
            suffix = "M";
        }
        else if (absolute >= 1_000m)
        {
            scaled = absolute / 1_000m;
            suffix = "K";
        }

        return $"{sign}${scaled.ToString("0.##", CultureInfo.InvariantCulture)}{suffix}";
    }
}
