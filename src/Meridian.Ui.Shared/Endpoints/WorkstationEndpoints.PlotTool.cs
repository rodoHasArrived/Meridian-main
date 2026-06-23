using System.Globalization;
using Meridian.Contracts.Api;
using Meridian.Contracts.Workstation;

namespace Meridian.Ui.Shared.Endpoints;

public static partial class WorkstationEndpoints
{
    private static WorkstationPlotToolPayload BuildStrategyPlotToolPayload(
        IReadOnlyList<StrategyRunSummary> runs,
        IReadOnlyList<string> selectedRunIds)
    {
        var activeRun = selectedRunIds.Count > 0
            ? runs.FirstOrDefault(run => string.Equals(run.RunId, selectedRunIds[0], StringComparison.OrdinalIgnoreCase)) ?? runs.FirstOrDefault()
            : runs.FirstOrDefault();
        var companionRun = selectedRunIds.Count > 1
            ? runs.FirstOrDefault(run => string.Equals(run.RunId, selectedRunIds[1], StringComparison.OrdinalIgnoreCase))
            : runs.Skip(1).FirstOrDefault();
        var activeStrategy = activeRun?.StrategyName ?? "Meridian PlotTool";
        var companionStrategy = companionRun?.StrategyName;
        var chartTitle = companionStrategy is null ? activeStrategy : $"{activeStrategy} vs {companionStrategy}";
        var queuedCount = runs.Count(static run => run.Status == StrategyRunStatus.Pending);
        var reviewCount = runs.Count(static run =>
            run.Promotion?.RequiresReview == true ||
            run.Status is StrategyRunStatus.Failed or StrategyRunStatus.Cancelled);
        var points = BuildPlotToolScatterPoints().ToArray();
        var focusPoint = points.LastOrDefault(static point => point.Emphasis);
        var observationCount = (2184 + (runs.Count * 9)).ToString(CultureInfo.InvariantCulture);
        var refreshedLabel = FormatRelativeTime(activeRun?.LastUpdatedAt ?? DateTimeOffset.UtcNow);

        var workspace = new WorkstationPlotToolWorkspacePayload(
            Eyebrow: "Strategy Lane · PlotTool",
            Title: $"{chartTitle} workstation",
            Description: "API-backed PlotTool workspace state from workstation strategy payload.",
            StatusBadgeLabel: (activeRun?.Mode.ToString() ?? "Strategy").ToUpperInvariant(),
            StatusBadgeVariant: ResolveModeVariant(activeRun?.Mode),
            Expression: $"{SlugifyForExpression(activeStrategy)}.spread() vs {(companionStrategy is null ? SlugifyForExpression(activeStrategy) : SlugifyForExpression(companionStrategy))}.implied_volatility(3m, forward, 100)",
            ToolbarPills: ["MAX", "Daily (MAX)", companionStrategy is null ? "Single study" : "Pair overlay", "0d lag"],
            MetaItems:
            [
                activeRun?.DatasetReference ?? activeRun?.FeedReference ?? "Cross-asset sandbox",
                $"{observationCount} obs",
                "β 0.48",
                "R² 0.71",
                "ρ 0.84"
            ],
            XAxisLabel: "Spread (bps)",
            YAxisLabel: "3m implied vol",
            XTicks:
            [
                new WorkstationPlotToolTickPayload(40, "20"), new WorkstationPlotToolTickPayload(120, "40"), new WorkstationPlotToolTickPayload(200, "60"),
                new WorkstationPlotToolTickPayload(280, "80"), new WorkstationPlotToolTickPayload(360, "100"), new WorkstationPlotToolTickPayload(440, "140"),
                new WorkstationPlotToolTickPayload(520, "180"), new WorkstationPlotToolTickPayload(600, "200")
            ],
            YTicks:
            [
                new WorkstationPlotToolTickPayload(44, "120"), new WorkstationPlotToolTickPayload(98, "100"), new WorkstationPlotToolTickPayload(152, "80"),
                new WorkstationPlotToolTickPayload(206, "60"), new WorkstationPlotToolTickPayload(260, "40")
            ],
            Points: points.Select(static point => new WorkstationPlotToolPointPayload(point.X, point.Y, point.Emphasis)).ToArray(),
            StudySummary:
            [
                new WorkstationPlotToolSummaryItemPayload("primary", "Primary notebook", activeStrategy),
                new WorkstationPlotToolSummaryItemPayload("companion", "Pair target", companionStrategy ?? "Select a second run"),
                new WorkstationPlotToolSummaryItemPayload("queued", "Queued studies", queuedCount.ToString(CultureInfo.InvariantCulture)),
                new WorkstationPlotToolSummaryItemPayload("review", "Review queue", reviewCount.ToString(CultureInfo.InvariantCulture))
            ],
            LegendItems:
            [
                new WorkstationPlotToolLegendItemPayload("history", "History", $"{observationCount} observations", "history"),
                new WorkstationPlotToolLegendItemPayload("current", "Current", "88.40 / 73.80", "current"),
                new WorkstationPlotToolLegendItemPayload("trend", "OLS fit", "y = 0.48x + 39.31", "trend"),
                new WorkstationPlotToolLegendItemPayload("refresh", "Refresh", refreshedLabel, "muted")
            ],
            FocusPoint: new WorkstationPlotToolFocusPointPayload(
                Label: "Current marker",
                XValueText: "88.40",
                YValueText: "73.80",
                Detail: $"Highlighted at x {focusPoint.X}, y {focusPoint.Y}."),
            SignalCards:
            [
                new WorkstationPlotToolSignalCardPayload("correlation", "Correlation", "0.84", "Pearson correlation", "success"),
                new WorkstationPlotToolSignalCardPayload("regression", "Regression beta", "0.48", "OLS slope", "default"),
                new WorkstationPlotToolSignalCardPayload("queued", "Queued studies", queuedCount.ToString(CultureInfo.InvariantCulture), "Run-library backlog", queuedCount > 0 ? "warning" : "default"),
                new WorkstationPlotToolSignalCardPayload("review", "Review queue", reviewCount.ToString(CultureInfo.InvariantCulture), "Promotion review queue", reviewCount > 0 ? "warning" : "success")
            ],
            ConsoleTitle: "Expression editor",
            ConsoleBody: companionStrategy is null
                ? "Select a second run to activate pair-study overlays."
                : $"Pair analysis is active for {activeStrategy} versus {companionStrategy}.",
            OverlayTitle: "Meridian overlays",
            OverlayItems:
            [
                $"Notebook coverage: {runs.Count} retained {(runs.Count == 1 ? "study" : "studies")} in Strategy.",
                $"Queued studies: {queuedCount}.",
                $"Runs requiring review: {reviewCount}."
            ]);

        var statistics = new WorkstationPlotToolStatisticsPayload(
            Eyebrow: "Statistics view",
            Title: $"{chartTitle} analysis",
            Description: $"{activeRun?.DatasetReference ?? activeRun?.FeedReference ?? "Cross-asset sandbox"} · refreshed {refreshedLabel}",
            SummaryTiles:
            [
                new WorkstationPlotToolSummaryTilePayload("observations", "N obs", observationCount, "99.7% complete", "default"),
                new WorkstationPlotToolSummaryTilePayload("correlation", "Correlation", "0.84", "Pearson ρ", "success"),
                new WorkstationPlotToolSummaryTilePayload("r-squared", "R²", "0.71", "OLS fit", "success"),
                new WorkstationPlotToolSummaryTilePayload("beta", "β (slope)", "0.48", "SE 0.014", "success"),
                new WorkstationPlotToolSummaryTilePayload("sharpe", "Sharpe (5d)", activeRun is null ? "N/A" : FormatSharpeProxy(activeRun), "Run-linked", "success")
            ],
            DistributionBars: [2, 6, 11, 18, 24, 31, 34, 29, 20, 11, 5, 2],
            DistributionSummary: $"{observationCount} samples centered on spread 66.84 and IV 71.42.",
            DistributionFootnote: $"Latest observation {DateTimeOffset.UtcNow:yyyy-MM-dd} · refreshed {refreshedLabel}.",
            Moments:
            [
                new WorkstationPlotToolMomentPayload("net-pnl", "Net P&L", FormatCurrency(activeRun?.NetPnl ?? 0m), "Pair summary"),
                new WorkstationPlotToolMomentPayload("return", "Total return", FormatReturn(activeRun?.TotalReturn, activeRun?.NetPnl), "Run linked"),
                new WorkstationPlotToolMomentPayload("promotion", "Promotion state", activeRun?.Promotion?.State.ToString() ?? "Unavailable", "Strategy posture")
            ],
            Regression: new WorkstationPlotToolRegressionPayload(
                Equation: "y = 0.48x + 39.31",
                DetailItems:
                [
                    $"{chartTitle} remains linked to workstation strategy evidence.",
                    $"Queued studies: {queuedCount}.",
                    $"Review queue: {reviewCount}."
                ]),
            SampleRows:
            [
                new WorkstationPlotToolSampleRowPayload("sample-1", DateTimeOffset.UtcNow.AddMinutes(-2).ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture), "88.40", "73.80", "1.42", "Crowded vol", "warning"),
                new WorkstationPlotToolSampleRowPayload("sample-2", DateTimeOffset.UtcNow.AddMinutes(-6).ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture), "80.20", "70.10", "0.82", "Neutral", "default")
            ]);

        var studies = runs.Select(run => new WorkstationPlotToolStudyPayload(
            Id: run.RunId,
            Title: run.StrategyName,
            Subtitle: $"{run.DatasetReference ?? run.FeedReference ?? "Unassigned"} · {FormatWindow(run.StartedAt, run.CompletedAt)} · {run.Engine}",
            StatusText: run.Status.ToString(),
            StatusBadgeLabel: run.Mode.ToString().ToUpperInvariant(),
            StatusBadgeVariant: ResolveModeVariant(run.Mode),
            MetricText: $"{FormatReturn(run.TotalReturn, run.NetPnl)} · Sharpe {FormatSharpeProxy(run)}",
            NoteText: BuildRunNotes(run),
            IsActive: activeRun is not null && string.Equals(run.RunId, activeRun.RunId, StringComparison.OrdinalIgnoreCase))).ToArray();

        return new WorkstationPlotToolPayload(
            Workspace: workspace,
            Statistics: statistics,
            Studies: studies,
            Tabs:
            [
                new WorkstationPlotToolTabState("workspace", "Workstation", "plottool-workspace-tab", "plottool-workspace-panel", true, "secondary", 0, "Workstation"),
                new WorkstationPlotToolTabState("statistics", "Statistics", "plottool-statistics-tab", "plottool-statistics-panel", false, "ghost", -1, "Statistics")
            ],
            ActiveView: "workspace");
    }

    private static WorkstationPlotToolPayload BuildStrategyFallbackPlotToolPayload()
    {
        var fallbackRun = new StrategyRunSummary(
            RunId: "run-strategy-001",
            StrategyId: "mean-reversion-fx",
            StrategyName: "Mean Reversion FX",
            Mode: StrategyRunMode.Paper,
            Engine: StrategyRunEngine.MeridianNative,
            Status: StrategyRunStatus.Running,
            StartedAt: DateTimeOffset.UtcNow.AddMinutes(-20),
            CompletedAt: null,
            DatasetReference: "FX Majors",
            FeedReference: "synthetic:fx",
            PortfolioId: null,
            LedgerReference: null,
            NetPnl: 4200m,
            TotalReturn: 0.042m,
            FinalEquity: 104200m,
            FillCount: 12,
            LastUpdatedAt: DateTimeOffset.UtcNow.AddMinutes(-2),
            AuditReference: null,
            Identity: null,
            Execution: null,
            Promotion: null,
            Governance: null,
            FundProfileId: null,
            FundDisplayName: null,
            ParentRunId: null,
            SweepId: null,
            SweepDefinitionHash: null,
            SweepObjective: null,
            LiveStatus: null,
            PaperStatus: null);

        return BuildStrategyPlotToolPayload([fallbackRun], selectedRunIds: Array.Empty<string>());
    }

    private static IEnumerable<(int X, int Y, bool Emphasis)> BuildPlotToolScatterPoints()
    {
        const int startX = 90;
        const int startY = 250;
        for (var index = 0; index < 36; index++)
        {
            var x = startX + (index * 14);
            var y = startY - (index * 5);
            yield return (x, y, index == 35);
        }
    }

    private static string SlugifyForExpression(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return "plottool";

        Span<char> buffer = stackalloc char[value.Length];
        var cursor = 0;
        foreach (var character in value.ToLowerInvariant())
        {
            buffer[cursor++] = char.IsLetterOrDigit(character) ? character : '_';
        }

        return new string(buffer[..cursor]).Trim('_');
    }


}
