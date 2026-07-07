using Meridian.Contracts.Api;
using Meridian.Contracts.Workstation;

namespace Meridian.Ui.Shared.Endpoints;

/// <summary>
/// Strategy run briefing helpers for the workstation API surface: run digests, run and
/// strategy cards, insight feeds, saved comparisons, briefing alerts, what-changed items,
/// drill-in links, timeline/mode-comparison cards, and run-mode parsing. Split out of the
/// WorkstationEndpoints core partial. The shared ResolveModeVariant helper stays in core.
/// </summary>
public static partial class WorkstationEndpoints
{
    // PR-03: returns typed DTO
    private static WorkstationRunDigest BuildRunDigest(StrategyRunSummary run, StrategyRunDetail? detail)
    {
        return new WorkstationRunDigest(
            RunId: run.RunId,
            StrategyName: run.StrategyName,
            Mode: run.Mode.ToString().ToLowerInvariant(),
            Status: run.Status.ToString(),
            LastUpdated: FormatRelativeTime(run.LastUpdatedAt),
            HasLedger: !string.IsNullOrWhiteSpace(run.LedgerReference),
            HasPortfolio: !string.IsNullOrWhiteSpace(run.PortfolioId),
            SecurityCoverage: BuildSecurityCoverage(detail));
    }

    private static WorkstationStrategyRunCard BuildStrategyRunCard(StrategyRunSummary run, StrategyRunDetail? detail)
    {
        return new WorkstationStrategyRunCard(
            Id: run.RunId,
            StrategyName: run.StrategyName,
            Engine: run.Engine.ToString(),
            Mode: run.Mode.ToString().ToLowerInvariant(),
            Status: run.Status.ToString(),
            Dataset: run.DatasetReference ?? run.FeedReference ?? "Unassigned",
            Window: FormatWindow(run.StartedAt, run.CompletedAt),
            Pnl: FormatReturn(run.TotalReturn, run.NetPnl),
            Sharpe: FormatSharpeProxy(run),
            LastUpdated: FormatRelativeTime(run.LastUpdatedAt),
            Notes: BuildRunNotes(run),
            PromotionState: run.Promotion?.State.ToString(),
            LedgerReference: run.LedgerReference,
            PortfolioId: run.PortfolioId,
            NetPnl: run.NetPnl,
            TotalReturn: run.TotalReturn,
            FinalEquity: run.FinalEquity,
            SecurityCoverage: BuildSecurityCoverage(detail),
            DrillIn: BuildRunDrillInLinks(run));
    }

    private static InsightFeed BuildBriefingInsightFeed(
        IReadOnlyList<StrategyRunSummary> runs,
        IReadOnlyList<StrategyRunDetail?> details,
        int alertCount)
    {
        var generatedAt = DateTimeOffset.UtcNow;
        if (runs.Count == 0)
        {
            return new InsightFeed(
                FeedId: "strategy-market-briefing",
                Title: "Pinned Insights",
                Summary: "No saved charts or run insights yet.",
                GeneratedAt: generatedAt,
                Widgets: Array.Empty<InsightWidget>());
        }

        var widgets = runs
            .Zip(details, static (run, detail) => new InsightWidget(
                WidgetId: $"insight-{run.RunId}",
                Title: run.StrategyName,
                Subtitle: $"{run.Mode} · {run.Status}",
                Headline: FormatReturn(run.TotalReturn, run.NetPnl),
                Tone: GetInsightTone(run, detail),
                Summary: BuildInsightSummary(run, detail),
                RunId: run.RunId,
                DrillInRoute: RunRoute(UiApiRoutes.RunsEquityCurve, run.RunId)))
            .Take(3)
            .ToArray();

        return new InsightFeed(
            FeedId: "strategy-market-briefing",
            Title: "Pinned Insights",
            Summary: $"{runs.Count} tracked run(s) in briefing scope; {alertCount} alert(s) require attention.",
            GeneratedAt: generatedAt,
            Widgets: widgets);
    }

    private static StrategyBriefingRun BuildBriefingRun(StrategyRunSummary run, StrategyRunDetail? detail)
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
            Notes: BuildInsightSummary(run, detail),
            PromotionState: run.Promotion?.State,
            NetPnl: run.NetPnl,
            TotalReturn: run.TotalReturn,
            FinalEquity: run.FinalEquity,
            DrillIn: BuildStrategyDrillInLinks(run));

    private static IReadOnlyList<StrategySavedComparison> BuildSavedComparisons(IReadOnlyList<StrategyRunSummary> runs)
    {
        var groupedComparisons = runs
            .GroupBy(static run => run.StrategyName, StringComparer.OrdinalIgnoreCase)
            .Select(group =>
            {
                var modes = group
                    .OrderBy(static run => run.Mode)
                    .Select(static run => new StrategySavedComparisonMode(
                        RunId: run.RunId,
                        Mode: run.Mode,
                        Status: run.Status,
                        NetPnl: run.NetPnl,
                        TotalReturn: run.TotalReturn,
                        DrillIn: BuildStrategyDrillInLinks(run)))
                    .ToArray();

                return new StrategySavedComparison(
                    ComparisonId: $"cmp-{group.First().RunId}",
                    StrategyName: group.Key,
                    ModeSummary: string.Join(" -> ", modes.Select(static mode => mode.Mode.ToString())),
                    Summary: BuildComparisonSummary(group.Key, modes),
                    AnchorRunId: modes.FirstOrDefault()?.RunId,
                    Modes: modes);
            })
            .Where(static comparison => comparison.Modes.Count >= 2)
            .Take(4)
            .ToArray();

        if (groupedComparisons.Length > 0)
        {
            return groupedComparisons;
        }

        if (runs.Count < 2)
        {
            return Array.Empty<StrategySavedComparison>();
        }

        var syntheticModes = runs
            .Take(2)
            .Select(static run => new StrategySavedComparisonMode(
                RunId: run.RunId,
                Mode: run.Mode,
                Status: run.Status,
                NetPnl: run.NetPnl,
                TotalReturn: run.TotalReturn,
                DrillIn: BuildStrategyDrillInLinks(run)))
            .ToArray();

        return
        [
            new StrategySavedComparison(
                ComparisonId: $"cmp-recent-{syntheticModes[0].RunId}",
                StrategyName: "Recent Runs",
                ModeSummary: string.Join(" vs ", syntheticModes.Select(static mode => mode.Mode.ToString())),
                Summary: "Saved compare lane across the two most recent runs while multi-mode history is still building.",
                AnchorRunId: syntheticModes[0].RunId,
                Modes: syntheticModes)
        ];
    }

    private static IReadOnlyList<StrategyBriefingAlert> BuildBriefingAlerts(
        IReadOnlyList<StrategyRunSummary> runs,
        IReadOnlyList<StrategyRunDetail?> details)
    {
        var alerts = new List<StrategyBriefingAlert>();

        for (var index = 0; index < runs.Count; index++)
        {
            var run = runs[index];
            var detail = index < details.Count ? details[index] : null;
            var coverageIssueCount = GetSecurityCoverageIssueCount(detail);

            if (run.Status is StrategyRunStatus.Failed or StrategyRunStatus.Cancelled)
            {
                alerts.Add(new StrategyBriefingAlert(
                    AlertId: $"alert-status-{run.RunId}",
                    Title: $"{run.StrategyName} needs operator review",
                    Summary: $"Run finished with status {run.Status} and should be investigated before it is reused.",
                    Tone: "warning",
                    RunId: run.RunId,
                    ActionLabel: "Open run"));
            }

            if (run.Promotion?.RequiresReview == true)
            {
                alerts.Add(new StrategyBriefingAlert(
                    AlertId: $"alert-promotion-{run.RunId}",
                    Title: $"{run.StrategyName} is queued for promotion review",
                    Summary: run.Promotion.Reason,
                    Tone: "default",
                    RunId: run.RunId,
                    ActionLabel: "Review promotion"));
            }

            if (coverageIssueCount > 0)
            {
                alerts.Add(new StrategyBriefingAlert(
                    AlertId: $"alert-security-{run.RunId}",
                    Title: $"{run.StrategyName} has Security Master gaps",
                    Summary: $"{coverageIssueCount} unresolved portfolio or ledger reference(s) should be fixed before handoff.",
                    Tone: "warning",
                    RunId: run.RunId,
                    ActionLabel: "Inspect continuity"));
            }
        }

        if (alerts.Count == 0)
        {
            return
            [
                new StrategyBriefingAlert(
                    AlertId: "alert-none",
                    Title: "No blocking alerts",
                    Summary: "Recent runs have no failed states, open promotion blockers, or Security Master gaps.",
                    Tone: "success",
                    ActionLabel: "Browse runs")
            ];
        }

        return alerts
            .Take(4)
            .ToArray();
    }

    private static IReadOnlyList<StrategyWhatChangedItem> BuildWhatChangedItems(IReadOnlyList<StrategyRunSummary> runs)
        => runs
            .Take(4)
            .Select(static run => new StrategyWhatChangedItem(
                ChangeId: $"change-{run.RunId}",
                Title: $"{run.StrategyName} moved to {run.Mode}",
                Summary: BuildChangeSummary(run),
                Category: run.Mode.ToString().ToLowerInvariant(),
                Timestamp: run.LastUpdatedAt,
                RelativeTime: FormatRelativeTime(run.LastUpdatedAt),
                RunId: run.RunId))
            .ToArray();

    private static string BuildInsightSummary(StrategyRunSummary run, StrategyRunDetail? detail)
    {
        var coverageIssueCount = GetSecurityCoverageIssueCount(detail);
        if (coverageIssueCount > 0)
        {
            return $"{BuildRunNotes(run)} {coverageIssueCount} Security Master gap(s) remain open.";
        }

        return BuildRunNotes(run);
    }

    private static string BuildComparisonSummary(
        string strategyName,
        IReadOnlyList<StrategySavedComparisonMode> modes)
    {
        if (modes.Count == 0)
        {
            return $"No comparison history saved for {strategyName}.";
        }

        if (modes.Count == 1)
        {
            return $"Baseline comparison package is ready for {strategyName}.";
        }

        return $"Saved compare lane covers {modes.Count} lifecycle stage(s) for {strategyName}.";
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

    private static string GetInsightTone(StrategyRunSummary run, StrategyRunDetail? detail)
    {
        if (run.Status is StrategyRunStatus.Failed or StrategyRunStatus.Cancelled)
        {
            return "warning";
        }

        if (run.Promotion?.RequiresReview == true || GetSecurityCoverageIssueCount(detail) > 0)
        {
            return "default";
        }

        return (run.NetPnl ?? 0m) >= 0m ? "success" : "warning";
    }

    private static int GetSecurityCoverageIssueCount(StrategyRunDetail? detail)
        => (detail?.Portfolio?.SecurityMissingCount ?? 0) + (detail?.Ledger?.SecurityMissingCount ?? 0);

    private static StrategyRunDrillInLinks BuildStrategyDrillInLinks(StrategyRunSummary run)
        => new(
            EquityCurve: RunRoute(UiApiRoutes.RunsEquityCurve, run.RunId),
            Fills: RunRoute(UiApiRoutes.RunsFills, run.RunId),
            Attribution: RunRoute(UiApiRoutes.RunsAttribution, run.RunId),
            Ledger: string.IsNullOrWhiteSpace(run.LedgerReference) ? null : RunRoute(UiApiRoutes.RunsLedger, run.RunId),
            CashFlows: RunRoute(UiApiRoutes.PortfolioCashFlows, run.RunId),
            Continuity: RunRoute(UiApiRoutes.RunsContinuity, run.RunId));

    // PR-03: returns typed DTO
    private static WorkstationTimelineCard BuildTimelineCard(StrategyRunSummary run) =>
        new WorkstationTimelineCard(
            RunId: run.RunId,
            StrategyName: run.StrategyName,
            Mode: run.Mode.ToString().ToLowerInvariant(),
            Status: run.Status.ToString(),
            StartedAt: run.StartedAt,
            CompletedAt: run.CompletedAt,
            LastUpdatedAt: run.LastUpdatedAt,
            TotalReturn: run.TotalReturn);

    // PR-03: returns typed comparison groups
    private static IReadOnlyList<WorkstationModeComparisonGroup> BuildModeComparisons(IReadOnlyList<StrategyRunSummary> runs)
    {
        return runs
            .GroupBy(static run => run.StrategyName, StringComparer.OrdinalIgnoreCase)
            .Select(group =>
            {
                var modes = group
                    .OrderBy(static run => run.Mode)
                    .Select(static run => new WorkstationModeComparisonRun(
                        RunId: run.RunId,
                        Mode: run.Mode.ToString().ToLowerInvariant(),
                        Status: run.Status.ToString(),
                        NetPnl: run.NetPnl,
                        TotalReturn: run.TotalReturn,
                        DrillIn: BuildRunDrillInLinks(run)))
                    .ToArray();
                return new WorkstationModeComparisonGroup(group.Key, modes);
            })
            .Where(static group => group.Modes.Count > 0)
            .ToArray();
    }

    private static WorkstationRunDrillInLinks BuildRunDrillInLinks(StrategyRunSummary run) => new(
        EquityCurve: RunRoute(UiApiRoutes.RunsEquityCurve, run.RunId),
        Fills: RunRoute(UiApiRoutes.RunsFills, run.RunId),
        Attribution: RunRoute(UiApiRoutes.RunsAttribution, run.RunId),
        Ledger: string.IsNullOrWhiteSpace(run.LedgerReference) ? null : RunRoute(UiApiRoutes.RunsLedger, run.RunId),
        CashFlows: RunRoute(UiApiRoutes.PortfolioCashFlows, run.RunId),
        Continuity: RunRoute(UiApiRoutes.RunsContinuity, run.RunId),
        Comparison: UiApiRoutes.RunsCompare);

    private static string RunRoute(string routeTemplate, string runId)
        => UiApiRoutes.WithParam(routeTemplate, "runId", runId);

    private static IReadOnlyList<StrategyRunMode>? ParseModes(string? mode)
    {
        if (string.IsNullOrWhiteSpace(mode))
        {
            return null;
        }

        var parsed = mode
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(static token => Enum.TryParse<StrategyRunMode>(token, true, out var modeValue)
                ? (StrategyRunMode?)modeValue
                : null)
            .Where(static item => item.HasValue)
            .Select(static item => item!.Value)
            .Distinct()
            .ToArray();

        return parsed.Length == 0 ? null : parsed;
    }

    private static IReadOnlyList<StrategyRunMode>? ParseModes(IReadOnlyList<string>? modes)
    {
        if (modes is not { Count: > 0 })
        {
            return null;
        }

        var parsed = modes
            .Select(static token => Enum.TryParse<StrategyRunMode>(token, true, out var modeValue)
                ? (StrategyRunMode?)modeValue
                : null)
            .Where(static item => item.HasValue)
            .Select(static item => item!.Value)
            .Distinct()
            .ToArray();

        return parsed.Length == 0 ? null : parsed;
    }
}
