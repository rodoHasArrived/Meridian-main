using System.Globalization;
using Meridian.Contracts.Workstation;
using Meridian.Strategies.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace Meridian.Ui.Shared.Endpoints;

/// <summary>
/// Strategy workspace payload and compatibility briefing projections for the workstation API.
/// </summary>
public static partial class WorkstationEndpoints
{
    // PR-03: returns typed DTO instead of anonymous object.
    // Returns null when the strategy run read service is not registered so the route can
    // respond 503 instead of serving fabricated fallback data.
    private static async Task<WorkstationStrategyPayload?> BuildStrategyPayloadAsync(HttpContext context)
    {
        var readService = context.RequestServices.GetService<StrategyRunReadService>();
        if (readService is null)
        {
            return null;
        }

        var scope = ResolveStrategyRunReadScope(context);
        var runs = (await readService
                .GetRunsAsync(new StrategyRunHistoryQuery(Limit: 6), scope, context.RequestAborted)
                .ConfigureAwait(false))
            .ToArray();
        var runDetails = await Task.WhenAll(
                runs.Select(run => readService.GetRunDetailAsync(run.RunId, scope, context.RequestAborted)))
            .ConfigureAwait(false);

        if (runs.Length == 0)
        {
            return new WorkstationStrategyPayload(
                Metrics:
                [
                    new WorkstationMetricCard("active-runs", "Active Runs", "0", "0%", "success"),
                    new WorkstationMetricCard("queued-runs", "Queued Promotions", "0", "0%", "default"),
                    new WorkstationMetricCard("review-runs", "Needs Review", "0", "0%", "warning"),
                    new WorkstationMetricCard("winning-runs", "Positive P&L", "0", "0%", "default")
                ],
                Runs: Array.Empty<WorkstationStrategyRunCard>(),
                Comparisons: Array.Empty<WorkstationModeComparisonGroup>(),
                Timeline: Array.Empty<WorkstationTimelineCard>(),
                Workspace: new WorkstationStrategyWorkspaceSummary(0, null, null, false, false, 0),
                PlotTool: BuildStrategyPlotToolPayload(Array.Empty<StrategyRunSummary>(), selectedRunIds: Array.Empty<string>()));
        }

        var activeRuns = runs.Count(static run => run.Status is StrategyRunStatus.Running or StrategyRunStatus.Paused);
        var queuedPromotions = runs.Count(static run => run.Promotion is { RequiresReview: true } &&
            run.Promotion.State is StrategyRunPromotionState.CandidateForPaper or StrategyRunPromotionState.CandidateForLive);
        var reviewRuns = runs.Count(static run => run.Promotion?.RequiresReview == true || run.Status is StrategyRunStatus.Failed or StrategyRunStatus.Cancelled);
        var winningRuns = runs.Count(static run => (run.NetPnl ?? 0m) > 0m);
        var latestRun = runs[0];

        return new WorkstationStrategyPayload(
            Metrics:
            [
                new WorkstationMetricCard("active-runs", "Active Runs", activeRuns.ToString(CultureInfo.InvariantCulture), activeRuns == 0 ? "0%" : $"+{activeRuns}", "success"),
                new WorkstationMetricCard("queued-runs", "Queued Promotions", queuedPromotions.ToString(CultureInfo.InvariantCulture), queuedPromotions == 0 ? "0%" : $"+{queuedPromotions}", "default"),
                new WorkstationMetricCard("review-runs", "Needs Review", reviewRuns.ToString(CultureInfo.InvariantCulture), reviewRuns == 0 ? "0%" : $"-{reviewRuns}", "warning"),
                new WorkstationMetricCard("winning-runs", "Positive P&L", winningRuns.ToString(CultureInfo.InvariantCulture), winningRuns == 0 ? "0%" : $"+{winningRuns}", "default")
            ],
            Runs: runs
                .Zip(runDetails, static (run, detail) => BuildStrategyRunCard(run, detail))
                .ToArray(),
            Comparisons: BuildModeComparisons(runs),
            Timeline: runs.Select(BuildTimelineCard).ToArray(),
            Workspace: new WorkstationStrategyWorkspaceSummary(
                TotalRuns: runs.Length,
                LatestRunId: latestRun.RunId,
                LatestStrategyName: latestRun.StrategyName,
                HasLedgerCoverage: runs.Any(static run => !string.IsNullOrWhiteSpace(run.LedgerReference)),
                HasPortfolioCoverage: runs.Any(static run => !string.IsNullOrWhiteSpace(run.PortfolioId)),
                PromotionCandidates: queuedPromotions),
            PlotTool: BuildStrategyPlotToolPayload(runs, selectedRunIds: Array.Empty<string>()));
    }

    // Returns null when the strategy run read service is not registered so the route can
    // respond 503 instead of serving a fabricated briefing.
    private static async Task<StrategyBriefingDto?> BuildStrategyBriefingAsync(HttpContext context)
    {
        var readService = context.RequestServices.GetService<StrategyRunReadService>();
        if (readService is null)
        {
            return null;
        }

        var scope = ResolveStrategyRunReadScope(context);
        var runs = (await readService
                .GetRunsAsync(new StrategyRunHistoryQuery(Limit: 10), scope, context.RequestAborted)
                .ConfigureAwait(false))
            .ToArray();
        var details = await Task.WhenAll(
                runs.Select(run => readService.GetRunDetailAsync(run.RunId, scope, context.RequestAborted)))
            .ConfigureAwait(false);

        return BuildStrategyBriefingFromRuns(runs, details);
    }

    private static StrategyBriefingDto BuildStrategyBriefingFromRuns(
        IReadOnlyList<StrategyRunSummary> runs,
        IReadOnlyList<StrategyRunDetail?> details)
    {
        var activeRuns = runs.Count(static run => run.Status is StrategyRunStatus.Running or StrategyRunStatus.Paused);
        var promotionCandidates = runs.Count(static run => run.Promotion is { RequiresReview: true } &&
            run.Promotion.State is StrategyRunPromotionState.CandidateForPaper or StrategyRunPromotionState.CandidateForLive);
        var positivePnlRuns = runs.Count(static run => (run.NetPnl ?? 0m) > 0m);
        var latestRun = runs.FirstOrDefault();
        var alertItems = BuildBriefingAlerts(runs, details);

        return new StrategyBriefingDto(
            Workspace: new StrategyBriefingWorkspaceSummary(
                TotalRuns: runs.Count,
                ActiveRuns: activeRuns,
                PromotionCandidates: promotionCandidates,
                PositivePnlRuns: positivePnlRuns,
                LatestRunId: latestRun?.RunId,
                LatestStrategyName: latestRun?.StrategyName,
                HasLedgerCoverage: runs.Any(static run => !string.IsNullOrWhiteSpace(run.LedgerReference)),
                HasPortfolioCoverage: runs.Any(static run => !string.IsNullOrWhiteSpace(run.PortfolioId)),
                Summary: latestRun is null
                    ? "Start a backtest or restore a saved run to populate the Market Briefing."
                    : $"{activeRuns} active Strategy session(s), {promotionCandidates} promotion candidate(s), and {alertItems.Count} alert(s) on the desk."),
            InsightFeed: BuildBriefingInsightFeed(runs, details, alertItems.Count),
            Watchlists: Array.Empty<WorkstationWatchlist>(),
            RecentRuns: runs
                .Zip(details, static (run, detail) => BuildBriefingRun(run, detail))
                .Take(6)
                .ToArray(),
            SavedComparisons: BuildSavedComparisons(runs),
            Alerts: alertItems,
            WhatChanged: BuildWhatChangedItems(runs));
    }

    private static ResearchBriefingDto ToResearchBriefingDto(StrategyBriefingDto briefing)
        => new(
            Workspace: new ResearchBriefingWorkspaceSummary(
                TotalRuns: briefing.Workspace.TotalRuns,
                ActiveRuns: briefing.Workspace.ActiveRuns,
                PromotionCandidates: briefing.Workspace.PromotionCandidates,
                PositivePnlRuns: briefing.Workspace.PositivePnlRuns,
                LatestRunId: briefing.Workspace.LatestRunId,
                LatestStrategyName: briefing.Workspace.LatestStrategyName,
                HasLedgerCoverage: briefing.Workspace.HasLedgerCoverage,
                HasPortfolioCoverage: briefing.Workspace.HasPortfolioCoverage,
                Summary: briefing.Workspace.Summary),
            InsightFeed: briefing.InsightFeed,
            Watchlists: briefing.Watchlists,
            RecentRuns: briefing.RecentRuns
                .Select(static run => new ResearchBriefingRun(
                    RunId: run.RunId,
                    StrategyName: run.StrategyName,
                    Mode: run.Mode,
                    Status: run.Status,
                    Dataset: run.Dataset,
                    WindowLabel: run.WindowLabel,
                    ReturnLabel: run.ReturnLabel,
                    SharpeLabel: run.SharpeLabel,
                    LastUpdatedLabel: run.LastUpdatedLabel,
                    Notes: run.Notes,
                    PromotionState: run.PromotionState,
                    NetPnl: run.NetPnl,
                    TotalReturn: run.TotalReturn,
                    FinalEquity: run.FinalEquity,
                    DrillIn: ToResearchDrillInLinks(run.DrillIn)))
                .ToArray(),
            SavedComparisons: briefing.SavedComparisons
                .Select(static comparison => new ResearchSavedComparison(
                    ComparisonId: comparison.ComparisonId,
                    StrategyName: comparison.StrategyName,
                    ModeSummary: comparison.ModeSummary,
                    Summary: comparison.Summary,
                    AnchorRunId: comparison.AnchorRunId,
                    Modes: comparison.Modes
                        .Select(static mode => new ResearchSavedComparisonMode(
                            RunId: mode.RunId,
                            Mode: mode.Mode,
                            Status: mode.Status,
                            NetPnl: mode.NetPnl,
                            TotalReturn: mode.TotalReturn,
                            DrillIn: ToResearchDrillInLinks(mode.DrillIn)))
                        .ToArray()))
                .ToArray(),
            Alerts: briefing.Alerts
                .Select(static alert => new ResearchBriefingAlert(
                    AlertId: alert.AlertId,
                    Title: alert.Title,
                    Summary: alert.Summary,
                    Tone: alert.Tone,
                    RunId: alert.RunId,
                    ActionLabel: alert.ActionLabel))
                .ToArray(),
            WhatChanged: briefing.WhatChanged
                .Select(static item => new ResearchWhatChangedItem(
                    ChangeId: item.ChangeId,
                    Title: item.Title,
                    Summary: item.Summary,
                    Category: item.Category,
                    Timestamp: item.Timestamp,
                    RelativeTime: item.RelativeTime,
                    RunId: item.RunId))
                .ToArray());

    private static ResearchRunDrillInLinks ToResearchDrillInLinks(StrategyRunDrillInLinks links)
        => new(
            EquityCurve: links.EquityCurve,
            Fills: links.Fills,
            Attribution: links.Attribution,
            Ledger: links.Ledger,
            CashFlows: links.CashFlows,
            Continuity: links.Continuity);
}
