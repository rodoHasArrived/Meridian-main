using Meridian.Contracts.Workstation;
using Meridian.Strategies.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace Meridian.Ui.Shared.Endpoints;

/// <summary>
/// Workstation session bootstrap payload for the API surface: builds the typed session DTO
/// (latest strategy-run digest, workspace summaries) and its display-name / role / environment /
/// workspace mapping helpers. Split out of the WorkstationEndpoints core partial as a
/// behavior-preserving relocation; the inline session route lambda and the shared BuildRunDigest
/// helper remain in core (reached across the partial).
/// </summary>
public static partial class WorkstationEndpoints
{
    // PR-03: returns typed DTO instead of anonymous object
    private static async Task<WorkstationSessionPayload> BuildSessionPayloadAsync(HttpContext context)
    {
        var readService = context.RequestServices.GetService<StrategyRunReadService>();
        if (readService is null)
        {
            return new WorkstationSessionPayload(
                DisplayName: "Meridian Operator",
                Role: "Strategy Lead",
                Environment: "paper",
                ActiveWorkspace: "strategy",
                CommandCount: 6,
                LatestRun: null,
                WorkspaceSummary: new WorkstationSessionWorkspaceSummary(0, 0, 0, 0, 0));
        }

        var runs = (await readService.GetRunsAsync(ct: context.RequestAborted).ConfigureAwait(false)).ToArray();
        var latest = runs.FirstOrDefault();
        var latestDetail = latest is null
            ? null
            : await readService.GetRunDetailAsync(latest.RunId, context.RequestAborted).ConfigureAwait(false);
        var activeRuns = runs.Count(static run => run.Status is StrategyRunStatus.Running or StrategyRunStatus.Paused);
        var reviewRuns = runs.Count(static run => run.Promotion?.RequiresReview == true || run.Status is StrategyRunStatus.Failed or StrategyRunStatus.Cancelled);

        return new WorkstationSessionPayload(
            DisplayName: BuildDisplayName(latest),
            Role: BuildRole(latest),
            Environment: MapEnvironment(latest),
            ActiveWorkspace: MapWorkspace(latest),
            CommandCount: Math.Max(6, runs.Length + activeRuns + reviewRuns),
            LatestRun: latest is null ? null : BuildRunDigest(latest, latestDetail),
            WorkspaceSummary: new WorkstationSessionWorkspaceSummary(
                TotalRuns: runs.Length,
                ActiveRuns: activeRuns,
                ReviewRuns: reviewRuns,
                LedgerCoverage: runs.Count(static run => !string.IsNullOrWhiteSpace(run.LedgerReference)),
                PortfolioCoverage: runs.Count(static run => !string.IsNullOrWhiteSpace(run.PortfolioId))));
    }

    private static string BuildDisplayName(StrategyRunSummary? latest)
        => latest is null ? "Meridian Operator" : $"{latest.StrategyName} Desk";

    private static string BuildRole(StrategyRunSummary? latest)
        => latest is null
            ? "Strategy Lead"
            : latest.Mode == StrategyRunMode.Live
                ? "Live Operations"
                : "Strategy Lead";

    private static string MapEnvironment(StrategyRunSummary? latest)
        => latest?.Mode switch
        {
            StrategyRunMode.Live => "live",
            StrategyRunMode.Paper => "paper",
            StrategyRunMode.Backtest => "research",
            _ => "paper"
        };

    private static string MapWorkspace(StrategyRunSummary? latest)
        => latest?.Promotion?.State switch
        {
            StrategyRunPromotionState.LiveManaged => "accounting",
            StrategyRunPromotionState.CandidateForLive => "trading",
            StrategyRunPromotionState.CandidateForPaper => "strategy",
            _ => latest?.Mode == StrategyRunMode.Live ? "trading" : "strategy"
        };
}
