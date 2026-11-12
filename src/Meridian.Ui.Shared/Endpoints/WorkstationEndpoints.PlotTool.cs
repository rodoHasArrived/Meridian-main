using System.Globalization;
using Meridian.Contracts.Workstation;

namespace Meridian.Ui.Shared.Endpoints;

public static partial class WorkstationEndpoints
{
    // Run metadata is real. Statistical observations have no retained source in this adapter.
    // Never fabricate coefficients, observation dates, scatter points or distributions here.
    private static WorkstationPlotToolPayload BuildStrategyPlotToolPayload(
        IReadOnlyList<StrategyRunSummary> runs, IReadOnlyList<string> selectedRunIds)
    {
        var activeRun = selectedRunIds.Count > 0
            ? runs.FirstOrDefault(run => string.Equals(run.RunId, selectedRunIds[0], StringComparison.Ordinal)) ?? runs.FirstOrDefault()
            : runs.FirstOrDefault();
        var queued = runs.Count(run => run.Status == StrategyRunStatus.Pending);
        var review = runs.Count(run => run.Promotion?.RequiresReview == true || run.Status is StrategyRunStatus.Failed or StrategyRunStatus.Cancelled);
        const string missing = "No retained statistical observations are connected. Run metadata remains available; scatter, regression and distributions are unavailable.";
        var workspace = new WorkstationPlotToolWorkspacePayload(
            Eyebrow: "Strategy Lane · PlotTool", Title: "Retained strategy studies", Description: missing,
            StatusBadgeLabel: "ANALYTICS UNAVAILABLE", StatusBadgeVariant: "outline",
            Expression: "Statistical observations unavailable", ToolbarPills: ["Retained runs", "No observations"],
            MetaItems: ["Source: retained run catalog", "Statistical observations unavailable"],
            XAxisLabel: "Spread", YAxisLabel: "Implied volatility", XTicks: [], YTicks: [], Points: [],
            StudySummary: [new("queued", "Queued studies", queued.ToString(CultureInfo.InvariantCulture)), new("review", "Review queue", review.ToString(CultureInfo.InvariantCulture))],
            LegendItems: [], FocusPoint: new("No retained marker", "Unavailable", "Unavailable", missing), SignalCards: [],
            ConsoleTitle: "Analysis source", ConsoleBody: missing, OverlayTitle: "Retained context", OverlayItems: []);
        var statistics = new WorkstationPlotToolStatisticsPayload(
            Eyebrow: "Statistics view", Title: "Statistical evidence unavailable", Description: missing,
            SummaryTiles: [], DistributionBars: [], DistributionSummary: missing,
            DistributionFootnote: "No observation timestamp has been supplied.",
            Moments: activeRun is null ? [] : [
                new("net-pnl", "Retained net P&L", activeRun.NetPnl?.ToString(CultureInfo.InvariantCulture) ?? "Unavailable", "Run denomination not supplied"),
                new("promotion", "Promotion state", activeRun.Promotion?.State.ToString() ?? "Unavailable", "Retained strategy state")],
            Regression: new("Unavailable — no retained regression observations", [missing]), SampleRows: []);
        var studies = runs.Select(run => new WorkstationPlotToolStudyPayload(
            Id: run.RunId, Title: run.StrategyName,
            Subtitle: $"{run.DatasetReference ?? run.FeedReference ?? "Unassigned"} · {FormatWindow(run.StartedAt, run.CompletedAt)} · {run.Engine}",
            StatusText: run.Status.ToString(), StatusBadgeLabel: run.Mode.ToString().ToUpperInvariant(), StatusBadgeVariant: ResolveModeVariant(run.Mode),
            MetricText: run.NetPnl.HasValue ? $"Net P&L {run.NetPnl.Value.ToString(CultureInfo.InvariantCulture)} · denomination unavailable" : "P&L unavailable",
            NoteText: BuildRunNotes(run), IsActive: run.RunId == activeRun?.RunId)).ToArray();
        return new WorkstationPlotToolPayload(workspace, statistics, studies,
            [new("workspace", "Workstation", "plottool-workspace-tab", "plottool-workspace-panel", true, "secondary", 0, "Workstation"),
             new("statistics", "Statistics", "plottool-statistics-tab", "plottool-statistics-panel", false, "ghost", -1, "Statistics")],
            AnalyticsEvidenceStatus: "Unavailable");
    }
}
